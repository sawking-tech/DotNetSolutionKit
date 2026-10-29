using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// Checks an HTTP Basic <c>Authorization</c> header against one expected user and password.
/// </summary>
/// <remarks>
/// Both parts are compared in constant time and both are always compared, so the time an answer
/// takes does not tell an attacker how many leading characters were right, or whether the user name
/// was. Any malformed header is a mismatch, never an exception.
/// </remarks>
public static class BasicCredentials
{
    public static bool Match(string? authorizationHeader, string expectedUser, string expectedPassword)
    {
        if (string.IsNullOrEmpty(authorizationHeader)
            || !AuthenticationHeaderValue.TryParse(authorizationHeader, out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
            return false;

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return false;

        var userMatches = FixedTimeEquals(decoded[..separator], expectedUser);
        var passwordMatches = FixedTimeEquals(decoded[(separator + 1)..], expectedPassword);
        return userMatches & passwordMatches;
    }

    private static bool FixedTimeEquals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
}
