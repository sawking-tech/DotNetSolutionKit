namespace NamespaceRoot.ProductName.Common.Exceptions;

/// <summary>
/// Occurs when rate limit is exceeded
/// </summary>
public class RateLimitException(string? message) : Exception(message)
{
    /// <summary>Number of seconds after which the caller may retry.</summary>
    public int? RetryAfterSeconds { get; init; }

    public RateLimitException(int limit, TimeSpan period)
        : this($"Rate limit exceeded. Maximum {limit} requests per {period.TotalSeconds} seconds.")
    {
    }

    public RateLimitException(string resource, int limit, TimeSpan period)
        : this($"Rate limit exceeded for {resource}. Maximum {limit} requests per {period.TotalSeconds} seconds.")
    {
    }

    /// <summary>Creates a rate-limit exception carrying an HTTP Retry-After delay.</summary>
    public RateLimitException(string resource, int limit, TimeSpan period, TimeSpan retryAfter)
        : this(resource, limit, period)
    {
        RetryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    }
}
