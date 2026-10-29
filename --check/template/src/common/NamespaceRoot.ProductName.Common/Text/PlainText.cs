using System.Globalization;
using System.Text;

namespace NamespaceRoot.ProductName.Common.Text;

/// <summary>
/// Normalisation for plain text a person types and other people later read.
/// </summary>
public static class PlainText
{
    /// <summary>
    /// Trims, applies Unicode normalisation and strips format characters. Returns <c>null</c> for
    /// null, empty or whitespace-only input.
    /// </summary>
    /// <remarks>
    /// This runs BEFORE any safety rule, and the order is the whole point. A payload like
    /// <c>&lt;scr</c> + U+200B + <c>ipt&gt;</c> passes a check for markup while the zero-width
    /// character is still in it, and becomes a tag the moment something strips it. Normalising
    /// first means the safety rule sees the string in the shape the browser will.
    /// <para>
    /// Format characters (Unicode category Cf) are removed rather than rejected: zero-width spaces,
    /// the BOM and directional overrides such as U+202E carry no meaning in a name or a message but do
    /// change how it reads. <c>char.IsControl</c> does not cover this category, so nothing else in
    /// the validation chain would catch them.
    /// </para>
    /// </remarks>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var normalized = value.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Format) continue;
            builder.Append(ch);
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }
}
