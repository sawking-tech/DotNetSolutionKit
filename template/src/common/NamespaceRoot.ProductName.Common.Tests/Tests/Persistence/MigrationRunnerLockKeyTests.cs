using Shouldly;
using NamespaceRoot.ProductName.Capabilities.Persistence;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Persistence;

[TestFixture]
public class MigrationRunnerLockKeyTests
{
    [Test]
    public void ComputeLockKey_SameInput_ProducesSameKey()
    {
        var key1 = MigrationLockKey.Compute("orders");
        var key2 = MigrationLockKey.Compute("orders");

        key2.ShouldBe(key1);
    }

    [Test]
    public void ComputeLockKey_DifferentSchemas_ProduceDifferentKeys()
    {
        var orders = MigrationLockKey.Compute("orders");
        var payments = MigrationLockKey.Compute("payments");
        var inventory = MigrationLockKey.Compute("inventory");
        var notifications = MigrationLockKey.Compute("notifications");
        var reports = MigrationLockKey.Compute("reports");

        var keys = new[] { orders, payments, inventory, notifications, reports };
        keys.Distinct().Count().ShouldBe(keys.Length);
    }

    [Test]
    public void ComputeLockKey_IsCaseSensitive()
    {
        var lower = MigrationLockKey.Compute("orders");
        var upper = MigrationLockKey.Compute("ORDERS");

        upper.ShouldNotBe(lower);
    }
}
