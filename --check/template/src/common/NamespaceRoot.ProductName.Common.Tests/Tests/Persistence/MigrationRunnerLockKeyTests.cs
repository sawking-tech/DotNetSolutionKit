using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Persistence;

[TestFixture]
public class MigrationRunnerLockKeyTests
{
    [Test]
    public void ComputeLockKey_SameInput_ProducesSameKey()
    {
        var key1 = MigrationRunner.ComputeLockKey("orders");
        var key2 = MigrationRunner.ComputeLockKey("orders");

        key2.ShouldBe(key1);
    }

    [Test]
    public void ComputeLockKey_DifferentSchemas_ProduceDifferentKeys()
    {
        var orders = MigrationRunner.ComputeLockKey("orders");
        var payments = MigrationRunner.ComputeLockKey("payments");
        var inventory = MigrationRunner.ComputeLockKey("inventory");
        var notifications = MigrationRunner.ComputeLockKey("notifications");
        var reports = MigrationRunner.ComputeLockKey("reports");

        var keys = new[] { orders, payments, inventory, notifications, reports };
        keys.Distinct().Count().ShouldBe(keys.Length);
    }

    [Test]
    public void ComputeLockKey_IsCaseSensitive()
    {
        var lower = MigrationRunner.ComputeLockKey("orders");
        var upper = MigrationRunner.ComputeLockKey("ORDERS");

        upper.ShouldNotBe(lower);
    }
}
