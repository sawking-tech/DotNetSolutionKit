using NamespaceRoot.ProductName.Common.Tests.Audit;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Tests;

/// <summary>
/// The service's entities and repositories answer the audit questions: every entity is marked
/// <c>[Auditable]</c> or <c>[AuditIgnore]</c>, nothing that reads like a secret reaches the journal with
/// its value, and every write past the change tracker says what happens to the journal.
/// </summary>
public class AuditGuardTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(DomainMarker).Assembly;

    [Test]
    public void Every_entity_has_an_audit_decision() => AuditMarkerVerifier.VerifyNoUndecidedAggregates(Domain);

    [Test]
    public void Every_audit_marker_names_properties_that_exist() => AuditMarkerVerifier.VerifyAll(Domain);

    [Test]
    public void No_secret_reaches_the_journal_with_its_value() => AuditRedactionVerifier.VerifyAll(Domain);

    [Test]
    public void Every_set_based_write_says_what_happens_to_the_journal() =>
        SetBasedWriteVerifier.VerifyService("NamespaceRoot.ProductName.ServiceNameOrCustom");
}
