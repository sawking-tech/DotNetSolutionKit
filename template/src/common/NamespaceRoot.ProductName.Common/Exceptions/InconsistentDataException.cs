namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Occurs when data is incomplete or inconsistent
/// </summary>
/// <param name="message"></param>
public class InconsistentDataException(string? message, string? parameterName = null) : BusinessLogicException(message)
{
    public string? ParameterName => parameterName;

    /// <summary>
    /// Throws exception if value is empty
    /// </summary>
    /// <exception cref="InconsistentDataException"></exception>
    public static void ThrowIfEmpty(string name, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            throw new InconsistentDataException($"{name} cannot contain empty values", name);
        }
    }

    /// <summary>
    /// Throws exception if value is empty
    /// </summary>
    /// <exception cref="InconsistentDataException"></exception>
    public static void ThrowIfEmpty<T>(string name, T? data)
    {
        object? defaultVal = default(T);

        var underlyingType = Nullable.GetUnderlyingType(typeof(T));
        if (underlyingType != null)
        {
            defaultVal = Activator.CreateInstance(underlyingType);
        }

        if (data == null || data.Equals(defaultVal))
        {
            throw new InconsistentDataException($"{name} cannot contain empty values", name);
        }
    }

    /// <summary>
    /// Throws exception if value is null
    /// </summary>
    /// <exception cref="InconsistentDataException"></exception>
    public static void ThrowIfNull<T>(string name, T? data)
    {
        if (data == null)
        {
            throw new InconsistentDataException($"{name} cannot contain null values", name);
        }
    }

    /// <summary>
    /// Checks if string is a currency code or at least resembles one
    /// </summary>
    public static void ThrowIfNotCurrencyCode(string name, string? data)
    {
        ThrowIfEmpty(name, data);

        if (data!.Length != 3)
        {
            throw new InconsistentDataException($"{name} must contain currency code");
        }
    }

    /// <summary>
    /// Checks if value is in range from 0 to 100
    /// </summary>
    public static void ThrowIfNotPercentWhenNotNull(string name, decimal? data)
    {
        if (data is < 0 || data is > 100)
        {
            throw new InconsistentDataException($"{name} must be in range from 0 to 100");
        }
    }

    /// <summary>
    /// Checks if string doesn't exceed length
    /// </summary>
    public static void ThrowIfStringToLong(string name, string? data, int expectedLength)
    {
        if (data?.Length > expectedLength)
        {
            throw new InconsistentDataException($"{name} exceeds maximum length. Length {data.Length}, maximum {expectedLength}");
        }
    }
}
