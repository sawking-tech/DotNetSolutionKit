namespace NamespaceRoot.ProductName.Common.Contracts.Messaging.Audit;

/// <summary>
/// Canonical action labels for audit entries. Stored as strings: the journal is append-only, so a
/// label written today must keep its meaning after any future refactoring of the code that emitted it.
/// </summary>
public static class AuditAction
{
    public const string Created = nameof(Created);
    public const string Updated = nameof(Updated);
    public const string Deleted = nameof(Deleted);
}
