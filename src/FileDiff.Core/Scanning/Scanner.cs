using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using FileDiff.Core.Models;
using FileDiff.Core.Storage;
using FileDiff.Core.Util;

namespace FileDiff.Core.Scanning;

/// <summary>A file discovered by the directory walk, before its content is inspected.</summary>
public sealed record FileCandidate(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc);

/// <summary>
/// Walks a folder and produces a <see cref="ScanManifest"/> describing its exact state.
/// <para>
/// Two things make repeated scans fast: files whose size and timestamp are unchanged since the
/// previous snapshot keep their previous hash without being read at all, and content is stored
/// in the shared content-addressed pool so identical bytes are written only once, ever.
/// </para>
/// </summary>
public sealed class Scanner
{
    private const int BufferSize = 128 * 1024;
    private const int SampleSize = 8000;

    private readonly BlobStore _blobs;

    public Scanner(BlobStore blobs) => _blobs = blobs;

    /// <summary>
    /// Scans <see cref="ScanOptions.RootPath"/>. The returned manifest has no
    /// <see cref="ScanManifest.SnapshotId"/> yet — the caller assigns one when saving.
    /// </summary>
    public async Task<ScanManifest> ScanAsync(
        ScanOptions options,
        ScanManifest? previous,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var root = PathUtil.NormalizeRoot(options.RootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder not found: {root}");
        }

        var stopwatch = Stopwatch.StartNew();
        var warnings = new ConcurrentQueue<string>();

        progress?.Report(new ScanProgress(ScanPhase.Enumerating, 0, 0, root));
        var candidates = EnumerateFiles(root, options, warnings, cancellationToken);
        progress?.Report(new ScanProgress(
            ScanPhase.Enumerating,
            0,
            candidates.Count,
            $"found {candidates.Count} files"));

        var processed = new ProcessedFile?[candidates.Count];
        var done = 0;
        long blobBytesWritten = 0;
        var previousIndex = previous?.Index;

        var parallelOptions = new ParallelOptions
        {
            // Hashing is IO-bound (disk reads dominate), so use full CPU count rather than
            // half: more in-flight reads keep NVMe/SSD queues full. Capped to avoid
            // threadpool starvation on high-core machines.
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 4, 16),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            parallelOptions,
            async (index, token) =>
            {
                var candidate = candidates[index];
                var result = await ProcessFileAsync(root, candidate, options, previousIndex, warnings, token)
                    .ConfigureAwait(false);

                processed[index] = result;
                Interlocked.Add(ref blobBytesWritten, result.BlobBytesWritten);

                var completed = Interlocked.Increment(ref done);
                // Progress<T> marshals to the UI thread per report; batch to keep
                // large scans (100k+ files) from flooding the dispatcher.
                if (completed % 64 == 0 || completed == candidates.Count)
                {
                    progress?.Report(new ScanProgress(
                        ScanPhase.Hashing,
                        completed,
                        candidates.Count,
                        candidate.RelativePath));
                }
            }).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ScanProgress(ScanPhase.Finalizing, candidates.Count, candidates.Count, string.Empty));

        var files = processed
            .Where(entry => entry is not null)
            .Select(entry => entry!.Entry)
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        stopwatch.Stop();

        var manifest = new ScanManifest
        {
            RootPath = root,
            MachineName = Environment.MachineName,
            AppVersion = GetAppVersion(),
            FileCount = files.Count,
            TotalBytes = files.Sum(f => f.Size),
            BlobCount = files.Count(f => f.HasBlob),
            BlobBytes = blobBytesWritten,
            DurationSeconds = Math.Round(stopwatch.Elapsed.TotalSeconds, 3),
            ContentCaptured = options.CaptureContent,
            Files = files,
            Warnings = [.. warnings],
        };

        manifest.Options["includeHiddenFiles"] = options.IncludeHiddenFiles.ToString();
        manifest.Options["captureContent"] = options.CaptureContent.ToString();
        manifest.Options["maxTextFileBytes"] = options.MaxTextFileBytes.ToString();
        manifest.Options["ignorePatterns"] = string.Join(",", options.IgnorePatterns);

        progress?.Report(new ScanProgress(ScanPhase.Completed, files.Count, files.Count, string.Empty));
        return manifest;
    }

    private static string GetAppVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            return assembly.GetName().Version?.ToString() ?? "1.0.0";
        }
        catch (Exception)
        {
            return "1.0.0";
        }
    }

    /// <summary>
    /// Breadth/depth-first walk that survives unreadable folders, refuses to follow reparse
    /// points (so a junction can never drag the scan out of the chosen folder or loop forever)
    /// and applies the ignore rules to whole subtrees.
    /// </summary>
    private static List<FileCandidate> EnumerateFiles(
        string root,
        ScanOptions options,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        var matcher = options.BuildMatcher();
        var results = new List<FileCandidate>();
        var pending = new Stack<string>();
        pending.Push(string.Empty);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeDirectory = pending.Pop();
            var absoluteDirectory = relativeDirectory.Length == 0
                ? root
                : PathUtil.ToLocalPath(root, relativeDirectory);

            List<FileSystemInfo> children;
            try
            {
                children = [.. new DirectoryInfo(absoluteDirectory).EnumerateFileSystemInfos()];
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Enqueue($"Skipped (access denied): {Describe(relativeDirectory)}");
                continue;
            }
            catch (IOException exception)
            {
                warnings.Enqueue($"Skipped ({exception.GetType().Name}): {Describe(relativeDirectory)}");
                continue;
            }

            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = relativeDirectory.Length == 0
                    ? child.Name
                    : $"{relativeDirectory}/{child.Name}";

                FileAttributes attributes;
                try
                {
                    attributes = child.Attributes;
                }
                catch (UnauthorizedAccessException)
                {
                    warnings.Enqueue($"Skipped (access denied): {relativePath}");
                    continue;
                }
                catch (IOException)
                {
                    warnings.Enqueue($"Skipped (unreadable): {relativePath}");
                    continue;
                }

                if (!options.IncludeHiddenFiles
                    && (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                if (matcher.Matches(relativePath))
                {
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(relativePath);
                    continue;
                }

                if (child is not FileInfo fileInfo)
                {
                    continue;
                }

                try
                {
                    results.Add(new FileCandidate(
                        relativePath,
                        fileInfo.Length,
                        new DateTimeOffset(
                            DateTime.SpecifyKind(fileInfo.LastWriteTimeUtc, DateTimeKind.Utc),
                            TimeSpan.Zero)));
                }
                catch (IOException)
                {
                    warnings.Enqueue($"Skipped (unreadable): {relativePath}");
                }
            }
        }

        return results;
    }

    private static string Describe(string relativeDirectory) =>
        relativeDirectory.Length == 0 ? "<root>" : relativeDirectory;

    /// <summary>
    /// Produces the <see cref="FileEntry"/> for one file, reusing the previous snapshot's hash
    /// when size and timestamp prove the content cannot have changed.
    /// </summary>
    private async Task<ProcessedFile> ProcessFileAsync(
        string root,
        FileCandidate candidate,
        ScanOptions options,
        IReadOnlyDictionary<string, FileEntry>? previousIndex,
        ConcurrentQueue<string> warnings,
        CancellationToken cancellationToken)
    {
        if (previousIndex is not null
            && previousIndex.TryGetValue(candidate.RelativePath, out var reusable)
            && reusable.Hash.Length > 0
            && reusable.Size == candidate.Length
            && reusable.LastWriteTimeUtc.UtcDateTime == candidate.LastWriteTimeUtc.UtcDateTime)
        {
            return new ProcessedFile(reusable.Clone(), 0);
        }

        var absolutePath = PathUtil.ToLocalPath(root, candidate.RelativePath);
        var hint = FileClassifier.Classify(candidate.RelativePath);
        var bufferContent = options.CaptureContent
                            && hint != TextHint.Binary
                            && candidate.Length > 0
                            && candidate.Length <= options.MaxTextFileBytes;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var read = await ReadFileAsync(absolutePath, bufferContent, cancellationToken).ConfigureAwait(false);

                var entry = new FileEntry
                {
                    RelativePath = candidate.RelativePath,
                    Size = read.Length,
                    LastWriteTimeUtc = candidate.LastWriteTimeUtc,
                    Hash = read.Hash,
                    IsText = hint switch
                    {
                        TextHint.Text => true,
                        TextHint.Binary => false,
                        _ => !FileClassifier.LooksBinary(read.Sample),
                    },
                };

                long written = 0;
                if (entry.IsText && read.Content is { Length: > 0 })
                {
                    written = _blobs.Write(entry.Hash, read.Content);
                    entry.HasBlob = true;
                }

                return new ProcessedFile(entry, written);
            }
            catch (FileNotFoundException)
            {
                warnings.Enqueue($"Vanished during scan: {candidate.RelativePath}");
                return new ProcessedFile(Unreadable(candidate), 0);
            }
            catch (DirectoryNotFoundException)
            {
                warnings.Enqueue($"Folder vanished during scan: {candidate.RelativePath}");
                return new ProcessedFile(Unreadable(candidate), 0);
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Enqueue($"Skipped (access denied): {candidate.RelativePath}");
                return new ProcessedFile(Unreadable(candidate), 0);
            }
            catch (IOException) when (attempt == 0)
            {
                // Very often a transient lock from another process; one short retry usually wins.
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                warnings.Enqueue($"Unreadable: {candidate.RelativePath} ({exception.Message})");
                return new ProcessedFile(Unreadable(candidate), 0);
            }
        }
    }

    /// <summary>Entry for a file that exists but could not be read: an empty hash means "content unknown".</summary>
    private static FileEntry Unreadable(FileCandidate candidate) => new()
    {
        RelativePath = candidate.RelativePath,
        Size = candidate.Length,
        LastWriteTimeUtc = candidate.LastWriteTimeUtc,
        Hash = string.Empty,
        IsText = false,
        HasBlob = false,
    };

    private sealed record FileReadResult(long Length, string Hash, byte[]? Content, byte[] Sample);

    /// <summary>Single streaming pass: hash, sniff the leading bytes, and optionally keep the content for the blob pool.</summary>
    private static async Task<FileReadResult> ReadFileAsync(
        string path,
        bool bufferContent,
        CancellationToken cancellationToken)
    {
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using (stream)
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var sample = new byte[SampleSize];
            var sampleLength = 0;
            MemoryStream? content = bufferContent ? new MemoryStream() : null;

            try
            {
                long total = 0;
                int read;

                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                           .ConfigureAwait(false)) > 0)
                {
                    hasher.AppendData(buffer, 0, read);

                    if (sampleLength < SampleSize)
                    {
                        var take = Math.Min(SampleSize - sampleLength, read);
                        buffer.AsSpan(0, take).CopyTo(sample.AsSpan(sampleLength));
                        sampleLength += take;
                    }

                    content?.Write(buffer, 0, read);
                    total += read;
                }

                var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                return new FileReadResult(total, hash, content?.ToArray(), sample);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                content?.Dispose();
            }
        }
    }

    private sealed record ProcessedFile(FileEntry Entry, long BlobBytesWritten);
}
