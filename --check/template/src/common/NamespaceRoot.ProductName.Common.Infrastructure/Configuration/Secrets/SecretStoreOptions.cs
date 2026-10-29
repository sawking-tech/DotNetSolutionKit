namespace NamespaceRoot.ProductName.Common.Infrastructure.Configuration.Secrets;

/// <summary>
/// What every secret store shares: which folders a service reads and whether it may start without them.
/// Each store adds where it lives and how a service signs in.
/// </summary>
/// <remarks>
/// The credentials themselves come from the environment, never from a file in the repository: the whole
/// point of the secret store is that the repository does not contain the values, and a repository holding
/// the key to the store would only move the problem one step.
/// </remarks>
public abstract class SecretStoreOptions
{
    /// <summary>
    /// The folder holding this service's own secrets, for example <c>/auth</c>.
    /// </summary>
    /// <remarks>
    /// Read after the shared folder, so a service can override a shared value without the shared value
    /// having to know which services exist.
    /// </remarks>
    public string ServicePath { get; set; } = string.Empty;

    /// <summary>
    /// The folder holding secrets every service needs.
    /// </summary>
    public abstract string SharedPath { get; set; }

    /// <summary>
    /// Whether a service may start when the store cannot be read.
    /// </summary>
    /// <remarks>
    /// False everywhere it matters: a service that starts without its secrets does not fail, it
    /// misbehaves - connecting to nothing, signing with an empty key - and the cause surfaces far from
    /// here. It is left configurable only so a developer can run locally without the store.
    /// </remarks>
    public bool Optional { get; set; }

    /// <summary>
    /// Whether the store is read at all. Off, the service takes every value from its files and
    /// environment: for a service generated with the store and run before one exists.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How often a running service reads the store again, in seconds; 0 reads it at startup only.
    /// </summary>
    /// <remarks>
    /// A value changed in the store reaches the settings read through <c>IOptionsMonitor</c> or
    /// <c>IReloadable</c> within this time; what was built from a value at startup, such as a connection
    /// pool, keeps the old one until a restart.
    /// </remarks>
    public int ReloadSeconds { get; set; } = 300;

    /// <summary>
    /// Whether enough is configured to read anything at all.
    /// </summary>
    public abstract bool IsConfigured { get; }
}
