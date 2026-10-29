using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using NamespaceRoot.ProductName.Common.Attributes;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Decides which properties may not have their values recorded, and what stands in for them.
/// </summary>
/// <remarks>
/// <para>
/// Shared by both capture paths on purpose. The change tracker and the set-based snapshot read
/// values through completely different mechanisms — one walks <c>EntityEntry.Properties</c>, the
/// other pulls EF getters over rows the tracker never saw — so a redaction rule implemented in one
/// of them is simply absent from the other. A password hash is equally a password hash whichever
/// way the row was written.
/// </para>
/// <para>
/// Driven by <see cref="AuditRedactAttribute"/> alone: a property is secret because its owner said
/// so, not because its name looked suspicious to a regex. Name heuristics belong in the guard test
/// that makes someone add the attribute, not in the code that decides what reaches the journal.
/// </para>
/// </remarks>
public static class AuditRedaction
{
    private static readonly ConcurrentDictionary<Type, HashSet<string>> Cache = new();

    /// <summary>
    /// Keyed HMAC over a redacted value, so two snapshots can be compared without either one
    /// carrying the secret.
    /// </summary>
    /// <remarks>
    /// Random per process and never persisted. A bare placeholder on both sides would make every
    /// secret compare equal, and the diff would then report that a rotated token had not moved —
    /// the one thing the journal exists to record. A plain hash would instead be checkable against
    /// a dictionary by anyone holding the journal; keyed with a secret this process alone knows, it
    /// is comparable only within the pair of snapshots that produced it.
    /// </remarks>
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>Property names on <paramref name="clrType"/> whose values must not be recorded.</summary>
    public static HashSet<string> ResolveFor(Type clrType) =>
        Cache.GetOrAdd(clrType, static type =>
        {
            var redacted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in type.GetProperties())
            {
                if (property.GetCustomAttribute<AuditRedactAttribute>(inherit: false) is not null)
                    redacted.Add(property.Name);
            }

            return redacted;
        });

    /// <summary>
    /// What a redacted value is recorded as: the placeholder, plus a fingerprint that changes when
    /// the value does.
    /// </summary>
    /// <remarks>
    /// Null stays null — "the secret was not set" is not itself a secret, and collapsing it into a
    /// fingerprint would make an unset password indistinguishable from a set one.
    /// </remarks>
    public static string? Fingerprint(string? value)
    {
        if (value is null) return null;

        var digest = HMACSHA256.HashData(FingerprintKey, System.Text.Encoding.UTF8.GetBytes(value));

        return $"{AuditRedactAttribute.Placeholder}:{Convert.ToHexString(digest.AsSpan(0, 4)).ToLowerInvariant()}";
    }
}
