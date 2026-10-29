using FluentValidation;
using NamespaceRoot.ProductName.Common.Text;

namespace NamespaceRoot.ProductName.Common.Web.Validation;

/// <summary>
/// Reusable FluentValidation rules that block raw HTML markup and control characters
/// in plain-text fields. Prevents stored markup from leaking into contexts where
/// escaping is not guaranteed (email templates, PDFs, CSV exports, third-party APIs).
/// </summary>
public static class SafeTextRules
{
    private const string Message = "'{PropertyName}' must not contain HTML tags or control characters.";

    /// <summary>
    /// Rejects strings that contain HTML angle brackets or control characters
    /// other than tab (U+0009), line feed (U+000A), and carriage return (U+000D).
    /// Null and empty values pass.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeSafeText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(BeSafeText).WithMessage(Message);

    /// <summary>
    /// Normalises first, then applies <see cref="MustBeSafeText{T}"/> to the result. Use for text a
    /// person types and another person reads.
    /// </summary>
    /// <remarks>
    /// The order is the point. <c>&lt;scr</c> + U+200B + <c>ipt&gt;</c> contains no tag while the
    /// zero-width character sits in it, so a check on the raw value passes - and the string becomes
    /// a tag as soon as anything strips that character. Normalising before the check means the rule
    /// sees what the browser will. Format characters are also invisible to
    /// <see cref="char.IsControl(char)"/>, so nothing else in the chain would catch them.
    /// </remarks>
    public static IRuleBuilderOptions<T, string?> MustBeSafeNormalizedText<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => BeSafeText(PlainText.Normalize(v))).WithMessage(Message);

    /// <summary>
    /// Length limit applied to the NORMALISED value, so padding a field with zero-width characters
    /// cannot smuggle it past the stored width.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeWithinNormalizedLength<T>(
        this IRuleBuilder<T, string?> rule, int maxLength) =>
        rule.Must(v => (PlainText.Normalize(v)?.Length ?? 0) <= maxLength)
            .WithMessage($"'{{PropertyName}}' must be at most {maxLength} characters.");

    private static bool BeSafeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        foreach (var ch in value)
        {
            if (ch == '<' || ch == '>')
                return false;

            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
                return false;
        }

        return true;
    }
}
