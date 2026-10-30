using System.Text;

namespace DotsKit.Merging;

/// <summary>
/// A file's text with its byte order mark kept apart, so writing it back keeps the mark it had. A binary file is
/// carried as its bytes (<paramref name="Raw"/>) and never decoded, so it is written back exactly.
/// </summary>
internal sealed record FileText(string Text, bool Bom, byte[]? Raw = null)
{
    private static readonly byte[] Utf8Mark = [0xEF, 0xBB, 0xBF];

    public static FileText? Read(string? root, string relativePath)
    {
        if (root is null)
        {
            return null;
        }

        var full = Path.Combine(root, relativePath);
        if (!File.Exists(full))
        {
            return null;
        }

        // A committed symbolic link is carried as a link, never read: reading would put what it points at - a file
        // outside the solution - into a merged file, and a repair would write over the target.
        if (new FileInfo(full).ResolveLinkTarget(returnFinalTarget: false) is { } link)
        {
            return new(string.Empty, false, Encoding.UTF8.GetBytes(link.FullName));
        }

        var bytes = File.ReadAllBytes(full);
        var bom = bytes.AsSpan().StartsWith(Utf8Mark);
        return bytes.AsSpan().Contains((byte)0) || Decoded(bom ? bytes[Utf8Mark.Length..] : bytes) is not { } text
            ? new(string.Empty, false, bytes)
            : new(text, bom);
    }

    /// <summary>
    /// The text of UTF-8 bytes; null for bytes of another encoding (a file saved in Windows-1251), which are carried
    /// as they are: decoded as UTF-8 and written back, each of their letters would become a replacement character.
    /// </summary>
    private static string? Decoded(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public byte[] ToBytes() => Raw ?? [.. Bom ? Utf8Mark : [], .. Encoding.UTF8.GetBytes(Text)];

    /// <summary>A file with a NUL byte in it, not in UTF-8, or a symbolic link is not text to merge or repair.</summary>
    public bool IsBinary => Raw is not null;

    /// <summary>Equal as text, whatever the line endings: a checkout with autocrlf is not a change; a binary file by its bytes.</summary>
    public bool SameTextAs(FileText other) =>
        IsBinary || other.IsBinary
            ? IsBinary && other.IsBinary && Raw.AsSpan().SequenceEqual(other.Raw)
            : LineEndings.ToLf(Text) == LineEndings.ToLf(other.Text);
}

internal static class LineEndings
{
    private const string Lf = "\n";
    private const string CrLf = "\r\n";

    public static string ToLf(string text) => text.Replace(CrLf, Lf, StringComparison.Ordinal);

    /// <summary>The line ending a text uses: CRLF when it has any, LF otherwise.</summary>
    public static string Of(string text) => text.Contains(CrLf, StringComparison.Ordinal) ? CrLf : Lf;

    public static string Apply(string text, string newline) => newline == Lf ? ToLf(text) : ToLf(text).Replace(Lf, CrLf, StringComparison.Ordinal);
}
