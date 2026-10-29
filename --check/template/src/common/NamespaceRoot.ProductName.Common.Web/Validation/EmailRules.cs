using System.Text.RegularExpressions;
using FluentValidation;
using NamespaceRoot.ProductName.Common.Text;

namespace NamespaceRoot.ProductName.Common.Web.Validation;

/// <summary>
/// The address rule every stored email is validated against.
/// </summary>
/// <remarks>
/// Kept beside <see cref="SafeTextRules"/> because the two answer different questions and are easy
/// to confuse: "safe text" only rejects markup and control characters, so a value like
/// <c>javascript:alert(1)</c> passes it and still becomes a live <c>mailto:</c> link. An address
/// field must be validated as an address. Validators take the expression from here rather than each
/// inlining its own.
/// </remarks>
public static class EmailRules
{
    /// <summary>ASCII-only address shape.</summary>
    public const string EmailPattern = @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$";

    private const string Message = "Email must contain only ASCII letters, digits, and the characters @._+-";

    /// <summary>Applies <see cref="EmailPattern"/>. Null and empty values pass - pair with NotEmpty when the field is required.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeEmailAddress<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => IsAddress(v)).WithMessage(Message);

    /// <summary>
    /// Same rule applied to the NORMALISED value, so an address padded with zero-width characters is
    /// judged as it will finally be stored rather than as it arrived.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> MustBeNormalizedEmailAddress<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(v => IsAddress(PlainText.Normalize(v))).WithMessage(Message);

    private static bool IsAddress(string? value) =>
        string.IsNullOrEmpty(value) || Regex.IsMatch(value, EmailPattern);
}
