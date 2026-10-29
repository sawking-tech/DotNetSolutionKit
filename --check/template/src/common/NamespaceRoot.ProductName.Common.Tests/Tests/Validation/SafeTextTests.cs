using FluentValidation;
using NamespaceRoot.ProductName.Common.Text;
using NamespaceRoot.ProductName.Common.Web.Validation;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Validation;

/// <summary>
/// Text a person types and another reads: normalised first, then checked, so an invisible character
/// cannot hide markup from the check.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class SafeTextTests
{
    private const string ZeroWidthSpace = "​";
    private const string RightToLeftOverride = "‮";

    private sealed record Contact(string? Name, string? Email);

    private sealed class ContactValidator : AbstractValidator<Contact>
    {
        public ContactValidator()
        {
            RuleFor(c => c.Name).MustBeSafeNormalizedText().MustBeWithinNormalizedLength(10);
            RuleFor(c => c.Email).MustBeNormalizedEmailAddress();
        }
    }

    private static bool Valid(string? name = null, string? email = null) =>
        new ContactValidator().Validate(new Contact(name, email)).IsValid;

    [Test(Description = "Normalising trims, drops format characters and turns blank into null")]
    public void Should_Normalize()
    {
        PlainText.Normalize($"  An{ZeroWidthSpace}na  ").ShouldBe("Anna");
        PlainText.Normalize($"x{RightToLeftOverride}y").ShouldBe("xy");
        PlainText.Normalize("   ").ShouldBeNull();
        PlainText.Normalize(null).ShouldBeNull();
    }

    [Test(Description = "Plain text passes")]
    public void Should_Accept_PlainText() => Valid(name: "Anna Smith").ShouldBeTrue();

    [Test(Description = "Markup is refused")]
    public void Should_Refuse_Markup() => Valid(name: "<b>x</b>").ShouldBeFalse();

    [Test(Description = "Markup split by a zero-width character is refused: the check sees it normalised")]
    public void Should_Refuse_MarkupHiddenByAnInvisibleCharacter() =>
        Valid(name: $"<scr{ZeroWidthSpace}ipt>").ShouldBeFalse();

    [Test(Description = "A control character is refused; tab and line breaks are allowed")]
    public void Should_Refuse_ControlCharacters()
    {
        Valid(name: "a\u0007b").ShouldBeFalse();
        Valid(name: "a\tb\nc").ShouldBeTrue();
    }

    [Test(Description = "The length counts what will be stored, so invisible padding does not count")]
    public void Should_MeasureTheNormalizedLength()
    {
        Valid(name: "Anna" + string.Concat(Enumerable.Repeat(ZeroWidthSpace, 50))).ShouldBeTrue();
        Valid(name: "Anna Smith Jones").ShouldBeFalse();
    }

    [TestCase("anna@example.com", true, TestName = "An address passes")]
    [TestCase("javascript:alert(1)", false, TestName = "A script URL is not an address, though it holds no markup")]
    [TestCase("anna@example", false, TestName = "An address without a top-level domain is refused")]
    [TestCase("anna​@example.com", true, TestName = "An address padded with an invisible character is judged as stored")]
    public void Should_ValidateAddresses(string email, bool valid) =>
        Valid(email: email).ShouldBe(valid);
}
