using System.Text;

namespace FileDiff.Core.Diffing;

public sealed record DecodedText(
    string Text,
    string EncodingName,
    string? LineEnding,
    IReadOnlyList<string> Lines);

/// <summary>
/// Turns raw bytes into comparable lines.
/// <para>
/// Line terminators are stripped during decoding, so a file that only switched from CRLF to LF
/// does not report every single line as modified — the change is reported once, as a note.
/// </para>
/// </summary>
public static class TextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static DecodedText Decode(byte[] content)
    {
        var (text, encodingName) = DecodeText(content);
        var lineEnding = DetectLineEnding(text);
        return new DecodedText(text, encodingName, lineEnding, SplitLines(text));
    }

    private static (string Text, string EncodingName) DecodeText(byte[] content)
    {
        if (TryStrip(content, [0xEF, 0xBB, 0xBF], out var utf8Body))
        {
            return (Encoding.UTF8.GetString(utf8Body), "UTF-8 (BOM)");
        }

        if (TryStrip(content, [0xFF, 0xFE, 0x00, 0x00], out var utf32Le))
        {
            return (Encoding.UTF32.GetString(utf32Le), "UTF-32 LE");
        }

        if (TryStrip(content, [0x00, 0x00, 0xFE, 0xFF], out var utf32Be))
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: false).GetString(utf32Be), "UTF-32 BE");
        }

        if (TryStrip(content, [0xFF, 0xFE], out var utf16Le))
        {
            return (Encoding.Unicode.GetString(utf16Le), "UTF-16 LE");
        }

        if (TryStrip(content, [0xFE, 0xFF], out var utf16Be))
        {
            return (Encoding.BigEndianUnicode.GetString(utf16Be), "UTF-16 BE");
        }

        try
        {
            return (StrictUtf8.GetString(content), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            // Byte-for-byte lossless fallback: every byte maps to exactly one character,
            // so the diff is still accurate even when the encoding is not UTF-8.
            return (Encoding.Latin1.GetString(content), "non-UTF-8 (shown as Latin-1)");
        }
    }

    private static bool TryStrip(byte[] content, byte[] bom, out byte[] body)
    {
        if (content.Length >= bom.Length && content.AsSpan(0, bom.Length).SequenceEqual(bom))
        {
            body = content[bom.Length..];
            return true;
        }

        body = content;
        return false;
    }

    public static string? DetectLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);

        return kinds switch
        {
            0 => null,
            1 when crlf > 0 => "CRLF",
            1 when lf > 0 => "LF",
            1 => "CR",
            _ => "Mixed",
        };
    }

    /// <summary>Splits on CRLF, LF or CR. A trailing terminator never produces a phantom empty line.</summary>
    public static IReadOnlyList<string> SplitLines(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var lines = new List<string>(64);
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\r')
            {
                lines.Add(text[start..i]);
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }
}
