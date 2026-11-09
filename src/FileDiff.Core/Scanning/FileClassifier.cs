namespace FileDiff.Core.Scanning;

public enum TextHint
{
    Unknown,
    Text,
    Binary,
}

/// <summary>
/// Cheap, predictable text/binary decisions. Extensions decide the obvious cases so that
/// binary files are never buffered for a content diff; anything unknown falls back to the
/// byte sniffing rule that Git uses (a NUL byte in the first 8 KB means binary).
/// </summary>
public static class FileClassifier
{
    private const int SniffLength = 8000;

    /// <summary>Extensions that are always treated as binary and never captured for content diffs.</summary>
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tif", ".tiff", ".psd", ".raw",
        ".zip", ".7z", ".rar", ".gz", ".bz2", ".xz", ".tar", ".zst", ".lz4", ".jar", ".war", ".nupkg", ".msi", ".cab",
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".lib", ".obj", ".o", ".a", ".class", ".wasm", ".bin", ".dat",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".rtf",
        ".mp3", ".mp4", ".wav", ".flac", ".ogg", ".avi", ".mkv", ".mov", ".webm", ".m4a", ".wmv",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".ldb",
        ".iso", ".img", ".vhd", ".vhdx", ".dmg",
        ".pyc", ".pyd", ".nupkg",
    };

    /// <summary>Extensions that are always treated as text; used to decide whether to buffer content.</summary>
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".text", ".md", ".markdown", ".rst", ".adoc", ".log", ".csv", ".tsv", ".json", ".json5", ".jsonc",
        ".xml", ".xaml", ".xsl", ".xslt", ".html", ".htm", ".xhtml", ".svg", ".css", ".scss", ".sass", ".less", ".styl",
        ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".mts", ".cts", ".vue", ".svelte", ".astro",
        ".cs", ".csx", ".vb", ".fs", ".fsx", ".java", ".kt", ".kts", ".scala", ".groovy", ".go", ".rs", ".swift",
        ".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hxx", ".m", ".mm", ".mmd",
        ".py", ".pyi", ".pyw", ".rb", ".erb", ".php", ".pl", ".pm", ".lua", ".r", ".jl", ".dart", ".ex", ".exs", ".erl",
        ".sh", ".bash", ".zsh", ".fish", ".ps1", ".psm1", ".psd1", ".bat", ".cmd", ".nu",
        ".sql", ".graphql", ".gql", ".proto", ".thrift", ".tf", ".tfvars", ".hcl",
        ".yml", ".yaml", ".toml", ".ini", ".cfg", ".conf", ".config", ".properties", ".env", ".editorconfig", ".gitignore",
        ".gitattributes", ".dockerignore", ".eslintrc", ".prettierrc", ".npmrc", ".nvmrc",
        ".sln", ".csproj", ".vbproj", ".fsproj", ".props", ".targets", ".gradle", ".pom", ".mod", ".sum",
        ".tex", ".bib", ".org", ".diff", ".patch", ".po", ".pot", ".resx", ".manifest", ".plist",
    };

    /// <summary>File names (extensionless or unusual) that are known to be text.</summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dockerfile", "Makefile", "Gemfile", "Rakefile", "Procfile", "LICENSE", "LICENCE",
        "README", "CHANGELOG", "AUTHORS", "NOTICE", "Vagrantfile", "CMakeLists.txt", ".gitignore",
        ".editorconfig", ".gitattributes", ".dockerignore", "hosts", "requirements.txt",
    };

    public static TextHint Classify(string fileNameOrPath)
    {
        var name = Path.GetFileName(fileNameOrPath);
        if (TextFileNames.Contains(name))
        {
            return TextHint.Text;
        }

        var extension = Path.GetExtension(name);
        if (extension.Length == 0)
        {
            return TextHint.Unknown;
        }

        if (BinaryExtensions.Contains(extension))
        {
            return TextHint.Binary;
        }

        return TextExtensions.Contains(extension) ? TextHint.Text : TextHint.Unknown;
    }

    /// <summary>NUL-byte sniff over the leading bytes of a file.</summary>
    public static bool LooksBinary(ReadOnlySpan<byte> sample)
    {
        var limit = Math.Min(sample.Length, SniffLength);
        return sample[..limit].IndexOf((byte)0) >= 0;
    }
}
