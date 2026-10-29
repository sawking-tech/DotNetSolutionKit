using System.Reflection;
using NamespaceRoot.ProductName.Common.Attributes;
using NamespaceRoot.ProductName.Common.Domain;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Audit;

/// <summary>
/// Checks that no property whose name reads like a secret reaches the journal with its value
/// intact.
/// </summary>
/// <remarks>
/// <para>
/// Redaction itself is driven by <see cref="AuditRedactAttribute"/> — an owner declaring what is
/// secret, not a regex guessing. This check exists because that declaration is easy to forget: add
/// a <c>ResetPasswordToken</c> to an audited entity and it flows into the journal, out through the
/// CSV export, and into whatever the operator does with the file. Nothing fails, so nobody notices.
/// </para>
/// <para>
/// The name list is a tripwire, not a policy. A property it flags is not necessarily a secret — the
/// answer may well be "this is a public key id, leave it" — but the answer has to be given
/// deliberately, by adding <see cref="AuditRedactAttribute"/> or by excluding the property from the
/// journal entirely with <see cref="AuditIgnoreAttribute"/>.
/// </para>
/// </remarks>
public static class AuditRedactionVerifier
{
    /// <summary>
    /// Fragments that make a property worth a second look. Deliberately broad: a false positive
    /// costs one attribute, a false negative costs a leaked credential.
    /// </summary>
    private static readonly string[] SuspiciousFragments =
    [
        "password", "passphrase", "secret", "token", "apikey", "privatekey",
        "credential", "salt", "signature", "hash", "otp", "pin",
    ];

    public static void VerifyAll(Assembly assembly)
    {
        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes().Where(IsAuditedEntity))
        {
            var ignoredOnType = type.GetCustomAttribute<AuditIgnoreAttribute>(inherit: false) is not null;
            if (ignoredOnType) continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!LooksLikeSecret(property.Name) || !IsRecordedValue(property)) continue;

                var redacted = property.GetCustomAttribute<AuditRedactAttribute>(inherit: false) is not null;
                var ignored = property.GetCustomAttribute<AuditIgnoreAttribute>(inherit: false) is not null;

                if (redacted || ignored) continue;

                offenders.Add($"{type.Name}.{property.Name}");
            }
        }

        offenders.ShouldBeEmpty(
            "These properties read like secrets but their values would be written to the audit journal "
            + $"verbatim: {string.Join(", ", offenders)}. Mark each with [AuditRedact] to record the change "
            + "without the value, or with [AuditIgnore] to keep it out of the journal altogether.");
    }

    /// <summary>
    /// Entities whose changes are recorded — the only ones whose property values can leak this way.
    /// </summary>
    private static bool IsAuditedEntity(Type type)
        => !type.IsAbstract
           && type.GetCustomAttribute<AuditableAttribute>(inherit: false) is not null
           && IsPersistedEntity(type);

    private static bool IsPersistedEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType) continue;

            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(Entity<>) || definition == typeof(AggregateRoot<>)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the property's VALUE is something the journal writes down at all.
    /// </summary>
    /// <remarks>
    /// Only scalars are. Both capture paths record columns — the interceptor walks
    /// <c>EntityEntry.Properties</c>, the snapshot reader walks EF's mapped properties — and a
    /// navigation is in neither. Without this, <c>User.RefreshTokens</c> (a collection of related
    /// rows, never serialised into a diff) is flagged next to <c>User.PasswordHash</c>, and a guard
    /// that cries wolf gets an attribute slapped on it to shut it up.
    /// </remarks>
    private static bool IsRecordedValue(PropertyInfo property)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(Guid)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(byte[]);
    }

    private static bool LooksLikeSecret(string propertyName)
        => SuspiciousFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
