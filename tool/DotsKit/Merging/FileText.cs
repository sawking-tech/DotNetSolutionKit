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

        var bytes = File.ReadAllBytes(full);
        if (bytes.AsSpan().Contains((byte)0))
        {
            return new(string.Empty, false, bytes);
        }

        var bom = bytes.AsSpan().StartsWith(Utf8Mark);
        return new(Encoding.UTF8.GetString(bom ? bytes[Utf8Mark.Length..] : bytes), bom);
    }

    public byte[] ToBytes() => Raw ?? [.. Bom ? Utf8Mark : [], .. Encoding.UTF8.GetBytes(Text)];

    /// <summary>A file with a NUL byte in it is not text to merge.</summary>
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
