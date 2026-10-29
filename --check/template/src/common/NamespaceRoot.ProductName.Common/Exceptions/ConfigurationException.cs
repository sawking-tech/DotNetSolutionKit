namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Represents an exception that occurs due to invalid or missing service configuration.
/// </summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}
