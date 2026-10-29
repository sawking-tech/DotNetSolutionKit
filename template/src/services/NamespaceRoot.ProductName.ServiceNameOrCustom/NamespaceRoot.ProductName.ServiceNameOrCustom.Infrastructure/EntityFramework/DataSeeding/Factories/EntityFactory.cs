using System.Reflection;
using NamespaceRoot.ProductName.Common.Domain;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework.DataSeeding.Factories;

public static class EntityFactory
{
    private static readonly SystemSeedContext SystemContext = new();


    public static void SetEntityId<T>(Entity<T> entity, T id)
    {
        var idProperty = typeof(Entity<T>).GetProperty("Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty);

        if (idProperty != null && idProperty.CanWrite)
        {
            idProperty.SetValue(entity, id);
        }
        else
        {
            var field = typeof(Entity<T>).GetField("<Id>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(entity, id);
        }
    }
}
