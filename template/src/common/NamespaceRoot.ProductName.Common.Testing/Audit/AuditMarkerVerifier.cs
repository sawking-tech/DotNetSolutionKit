// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Reflection;
using NamespaceRoot.ProductName.Common.Attributes;
using NamespaceRoot.ProductName.Common.Domain;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Audit;

/// <summary>
/// Checks that every <see cref="AuditableAttribute"/> in a service assembly names properties that
/// actually exist and can carry what the journal expects.
/// </summary>
/// <remarks>
/// The interceptor reads both nominated properties by reflection and silently records null when it
/// cannot find one or the type does not fit. That failure mode is invisible: the journal keeps
/// working and quietly loses the display name, or — far worse — the subject, which is what makes a
/// system-initiated change visible to its tenant at all. A rename caught by <c>nameof</c> is not
/// the risk; changing a subject property's TYPE is, and only this check sees it.
/// </remarks>
public static class AuditMarkerVerifier
{
    public static void VerifyAll(Assembly assembly)
    {
        var marked = assembly.GetTypes()
            .Select(type => (Type: type, Marker: type.GetCustomAttribute<AuditableAttribute>(inherit: false)))
            .Where(x => x.Marker is not null)
            .ToList();

        foreach (var (type, marker) in marked)
        {
            marker!.Module.ShouldNotBeNullOrWhiteSpace($"{type.Name} has a blank audit module.");

            if (marker.DisplayProperty is { } display)
            {
                type.GetProperty(display).ShouldNotBeNull(
                    $"{type.Name} nominates '{display}' as its display property, which does not exist.");
            }

            if (marker.SubjectTenantProperty is not { } subject) continue;

            var property = type.GetProperty(subject);

            property.ShouldNotBeNull(
                $"{type.Name} nominates '{subject}' as its subject tenant, which does not exist. "
                + "Entries for it would be visible to the platform only.");

            var propertyType = Nullable.GetUnderlyingType(property!.PropertyType) ?? property.PropertyType;

            propertyType.ShouldBe(typeof(Guid),
                $"{type.Name}.{subject} is {property.PropertyType.Name}, but the subject tenant must be a Guid. "
                + "The interceptor reads it by reflection and would record no subject at all.");
        }
    }

    /// <summary>
    /// Checks that every persisted entity in the assembly has been decided about: either
    /// <see cref="AuditableAttribute"/> or an explicit <see cref="AuditIgnoreAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Coverage is opt-in, so a new entity is silently absent from the journal until someone notices
    /// — and nobody notices an entry that was never written. Failing here forces the choice at the
    /// moment the entity is added, while its author still knows whether its changes carry operator
    /// meaning.
    /// <para>
    /// Every persisted entity counts, not only aggregate roots. Restricting this to roots is exactly
    /// how child entities with prices and limits came to be unaudited in the product this check comes
    /// from: each hangs off a root and is a plain entity, so nothing asked about them, while the
    /// numbers that matter live precisely there.
    /// </para>
    /// <para>
    /// Auditing everything by default was rejected instead: the module, the display property and the
    /// subject tenant cannot be inferred from a type, and an entry with no subject is visible to
    /// the platform alone — half the journal would quietly become platform-only.
    /// </para>
    /// </remarks>
    public static void VerifyNoUndecidedAggregates(Assembly assembly)
    {
        var undecided = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsClass: true } && IsPersistedEntity(type))
            .Where(type => type.GetCustomAttribute<AuditableAttribute>(inherit: false) is null
                           && type.GetCustomAttribute<AuditIgnoreAttribute>(inherit: false) is null)
            .Select(type => type.Name)
            .OrderBy(name => name)
            .ToList();

        undecided.ShouldBeEmpty(
            $"{assembly.GetName().Name} has entities with no audit decision: {string.Join(", ", undecided)}. "
            + "Mark each with [Auditable(module)] or, if its changes carry no operator meaning, with "
            + "[AuditIgnore] and a comment saying why.");
    }

    private static bool IsPersistedEntity(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType) continue;

            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(AggregateRoot<>) || definition == typeof(Entity<>))
                return true;
        }

        return false;
    }
}
