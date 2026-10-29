using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

/// <summary>
/// A setting that changes while the service runs: <see cref="Current"/> is the last valid value of its
/// section, read again whenever the configuration changes - a file edited, a secret store read again.
/// </summary>
/// <typeparam name="T">The settings class, validated by its data annotations.</typeparam>
/// <remarks>
/// Registered with <see cref="OptionsServiceCollectionExtensions.AddReloadableOptions{T}"/>. A setting
/// read through <c>AddValidatedOptions</c> is fixed for the life of the process instead, which is right
/// for what is built from it once: a connection pool, a signing key, a job server.
/// </remarks>
public interface IReloadable<out T> where T : class
{
    T Current { get; }
}

/// <summary>
/// Binds the section again on every change and keeps the result only when it is valid: a value that fails
/// its annotations is logged and dropped, and the service goes on with the last valid one: a typo in a
/// file fails no request.
/// </summary>
internal sealed class Reloadable<T> : IReloadable<T>, IDisposable where T : class, new()
{
    private readonly IConfigurationSection _section;
    private readonly ILogger _logger;
    private readonly IDisposable _subscription;
    private volatile T _current;

    public Reloadable(IConfigurationSection section, ILogger<Reloadable<T>> logger)
    {
        _section = section;
        _logger = logger;
        _current = Bind(out var errors) ?? throw new ValidationException(
            $"{section.Path} is not valid: {string.Join("; ", errors)}");
        _subscription = ChangeToken.OnChange(section.GetReloadToken, Rebind);
    }

    public T Current => _current;

    public void Dispose() => _subscription.Dispose();

    private void Rebind()
    {
        if (Bind(out var errors) is { } next)
        {
            _current = next;
            return;
        }

        _logger.LogWarning("The new value of {Section} is not valid and was not taken: {Errors}",
            _section.Path, string.Join("; ", errors));
    }

    private T? Bind(out List<string> errors)
    {
        var value = new T();
        _section.Bind(value);
        var results = new List<ValidationResult>();
        var valid = Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
        errors = results.Select(r => r.ErrorMessage ?? string.Join(", ", r.MemberNames)).ToList();
        return valid ? value : null;
    }
}
