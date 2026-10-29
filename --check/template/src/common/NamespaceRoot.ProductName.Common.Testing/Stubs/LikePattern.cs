using System.Text;
using System.Text.RegularExpressions;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

/// <summary>
/// A SQL <c>LIKE</c> pattern as a .NET regular expression, for the in-memory stand-ins of the search.
/// </summary>
public static class LikePattern
{
    /// <summary>
    /// <c>%</c> is any run of characters, <c>_</c> one character, and <paramref name="escape"/> followed by a
    /// character is that character as it is. The pattern is anchored at both ends, as LIKE is.
    /// </summary>
    public static string ToRegex(string likePattern, char escape)
    {
        var regex = new StringBuilder("^");
        for (var i = 0; i < likePattern.Length; i++)
        {
            var c = likePattern[i];
            if (c == escape && i + 1 < likePattern.Length)
                regex.Append(Regex.Escape(likePattern[++i].ToString()));
            else if (c == '%')
                regex.Append(".*");
            else if (c == '_')
                regex.Append('.');
            else
                regex.Append(Regex.Escape(c.ToString()));
        }

        return regex.Append('$').ToString();
    }
}
