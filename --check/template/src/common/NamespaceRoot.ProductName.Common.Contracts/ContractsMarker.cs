namespace NamespaceRoot.ProductName.Common.Contracts;

/// <summary>
/// Anchor for assembly scanning of the contracts layer.
/// </summary>
/// <remarks>
/// The bus needs the assembly that holds the messages, and that is no longer the assembly that holds
/// their interfaces: the abstractions live in the domain, which knows nothing about transport. Scanning
/// by the interface's assembly finds nothing here, and a command with no address registered fails only
/// when it is actually sent — with a broker up, which local runs used not to have.
/// </remarks>
public class ContractsMarker;
