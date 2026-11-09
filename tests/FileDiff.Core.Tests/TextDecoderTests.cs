using System.Text;
using FileDiff.Core.Diffing;

namespace FileDiff.Core.Tests;

public class TextDecoderTests
{
    [Fact]
    public void Plain_utf8_is_decoded()
    {
        var decoded = TextDecoder.Decode(Encoding.UTF8.GetBytes("hello\nworld"));

        Assert.Equal("UTF-8", decoded.EncodingName);
        Assert.Equal(["hello", "world"], decoded.Lines);
        Assert.Equal("LF", decoded.LineEnding);
    }

    [Fact]
    public void Utf8_bom_is_stripped_and_reported()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("café")).ToArray();

        var decoded = TextDecoder.Decode(bytes);

        Assert.Equal("UTF-8 (BOM)", decoded.EncodingName);
        Assert.Equal("café", decoded.Text);
    }

    [Fact]
    public void Utf16_both_endians_are_detected()
    {
        var littleEndian = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("ab")).ToArray();
        var bigEndian = Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes("ab")).ToArray();

        Assert.Equal("UTF-16 LE", TextDecoder.Decode(littleEndian).EncodingName);
        Assert.Equal("UTF-16 BE", TextDecoder.Decode(bigEndian).EncodingName);
        Assert.Equal("ab", TextDecoder.Decode(littleEndian).Text);
        Assert.Equal("ab", TextDecoder.Decode(bigEndian).Text);
    }

    [Fact]
    public void Invalid_utf8_falls_back_losslessly()
    {
        // 0xFF is never valid UTF-8. Latin-1 maps every byte to one character, so a diff of such
        // a file is still byte-accurate.
        byte[] bytes = [0x48, 0x69, 0xFF, 0xFE, 0x21];

        var decoded = TextDecoder.Decode(bytes);

        Assert.Contains("non-UTF-8", decoded.EncodingName);
        Assert.Equal(5, decoded.Text.Length);
        Assert.Equal((char)0xFF, decoded.Text[2]);
    }

    [Theory]
    [InlineData("a\r\nb\r\n", "CRLF")]
    [InlineData("a\nb\n", "LF")]
    [InlineData("a\rb\r", "CR")]
    [InlineData("a\r\nb\n", "Mixed")]
    [InlineData("no terminator", null)]
    public void Line_ending_style_is_detected(string text, string? expected)
    {
        Assert.Equal(expected, TextDecoder.DetectLineEnding(text));
    }

    [Fact]
    public void Trailing_terminator_does_not_create_a_phantom_line()
    {
        Assert.Equal(["a", "b"], TextDecoder.SplitLines("a\nb\n"));
        Assert.Equal(["a", "b"], TextDecoder.SplitLines("a\r\nb"));
        Assert.Equal([string.Empty, "a"], TextDecoder.SplitLines("\na"));
    }

    [Fact]
    public void Empty_content_has_no_lines()
    {
        var decoded = TextDecoder.Decode([]);

        Assert.Empty(decoded.Lines);
        Assert.Null(decoded.LineEnding);
    }

    [Fact]
    public void Crlf_and_lf_versions_of_the_same_text_share_identical_lines()
    {
        var windows = TextDecoder.Decode(Encoding.UTF8.GetBytes("alpha\r\nbeta\r\n"));
        var unix = TextDecoder.Decode(Encoding.UTF8.GetBytes("alpha\nbeta\n"));

        // This is why a line-ending switch does not report every line as modified.
        Assert.Equal(windows.Lines, unix.Lines);
        Assert.NotEqual(windows.LineEnding, unix.LineEnding);
    }
}
