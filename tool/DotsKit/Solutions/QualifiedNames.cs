using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotsKit.Solutions;

/// <summary>
/// A type named in code by its full name - <c>Acme.Shop.Common.Events.DomainEvent</c> as a type, in new, typeof, a
/// generic argument, an attribute or before a static member - found by the C# syntax and its namespace replaced in
/// place, so a type the template moved is named where it lives now. Only the syntax is read: no compilation, no
/// MSBuild; a plain using directive is left to the repair of usings and a namespace declaration to nobody, while an
/// alias (<c>using E = Ns.Type;</c>) and a static using name the type and are repaired here. A cref of an XML comment
/// and code under an inactive #if are not read.
/// </summary>
internal static class QualifiedNames
{
    private const string Global = "global::";

    /// <summary>Whether the text names <paramref name="type"/> by its namespace <paramref name="ns"/> anywhere a repair can change.</summary>
    public static bool Names(string text, string ns, string type) =>
        text.Contains(ns + "." + type, StringComparison.Ordinal) && Qualifiers(text, ns, type).Any();

    /// <summary>The text with every full name of <paramref name="type"/> in <paramref name="ns"/> naming it in <paramref name="with"/> instead.</summary>
    public static string Replace(string text, string ns, string type, string with)
    {
        var source = SourceText.From(text);
        var changes = Qualifiers(text, ns, type)
            .Select(q => new TextChange(q.Span, q.Text.StartsWith(Global, StringComparison.Ordinal) ? Global + with : with));
        return source.WithChanges(changes).ToString();
    }

    /// <summary>The namespace part of each full name of the type: the left of a qualified name or of a member access.</summary>
    private static IEnumerable<(TextSpan Span, string Text)> Qualifiers(string text, string ns, string type)
    {
        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        foreach (var node in root.DescendantNodes())
        {
            var (left, right) = node switch
            {
                QualifiedNameSyntax q => ((SyntaxNode)q.Left, q.Right.Identifier.Text),
                MemberAccessExpressionSyntax m => (m.Expression, m.Name.Identifier.Text),
                _ => (null, null),
            };
            if (left is not null && right == type && Unqualified(left.ToString()) == ns && !InUsingOrNamespaceName(node))
            {
                yield return (left.Span, left.ToString());
            }
        }
    }

    /// <summary>The name of a plain using directive or of a namespace declaration, which the repair of usings or nobody changes.</summary>
    private static bool InUsingOrNamespaceName(SyntaxNode node) =>
        node.Ancestors().OfType<UsingDirectiveSyntax>().Any(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None))
        || node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Any(n => n.Name.Span.Contains(node.Span));

    private static string Unqualified(string name) => name.StartsWith(Global, StringComparison.Ordinal) ? name[Global.Length..] : name;
}
