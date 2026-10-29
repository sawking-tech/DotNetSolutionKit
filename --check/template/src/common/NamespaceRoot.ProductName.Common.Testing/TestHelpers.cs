using System.Reflection;
using NamespaceRoot.ProductName.Common.Domain;

namespace NamespaceRoot.ProductName.Common.Tests;

public static class TestHelpers
{
    /// <summary>
    /// Sets the ID of an entity via reflection, handling both properties and backing fields.
    /// </summary>
    public static void SetEntityId<T>(Entity<T> entity, T id)
    {
        var idProperty = typeof(Entity<T>).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
        if (idProperty != null && idProperty.CanWrite)
        {
            idProperty.SetValue(entity, id);
        }
        else
        {
            var field = typeof(Entity<T>).GetField($"<Id>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(entity, id);
        }
    }
}
