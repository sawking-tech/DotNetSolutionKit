namespace NamespaceRoot.ProductName.Common.Contracts.Responses;

public static class ErrorConstants
{
    // Default error messages
    public static class Messages
    {
        // General HTTP Messages
        public const string BadRequest = "The request is invalid.";
        public const string Unauthorized = "Authentication required. Please provide valid credentials.";
        public const string Forbidden = "Access denied. You don't have permission to access this resource.";
        public const string NotFound = "The requested resource was not found.";
        public const string Conflict = "The request conflicts with the current state of the server.";
        public const string ValidationFailed = "One or more validation errors occurred.";
        public const string TooManyRequests = "Too many requests. Please try again later.";
        public const string InternalServerError = "An internal server error occurred.";
        public const string ServiceUnavailable = "Service temporarily unavailable.";
        //  Auth & API Key Specific
        public const string NoToken = "Authentication token is missing. Please provide it in the 'Authorization' header.";
        public const string ApiKeyProcessingError = "An error occurred while processing the API key.";
        public const string InvalidApiKey = "The provided API key is invalid or has been revoked.";
        public const string NoApiKey = "API key is missing. Please provide it in the 'X-API-Key' header.";
        public const string InvalidToken = "The security token is invalid, expired, or tampered with.";
        // --- Request & Infrastructure ---
        public const string RequestCancelled = "The request was cancelled by the client or timed out.";
        public const string MalformedJson = "The request body contains invalid JSON format.";
        public const string ConcurrencyConflict = "The resource has been modified by another process. Please reload and try again.";
        public const string RateLimitExceeded = "API rate limit exceeded. Please slow down your requests.";
        public const string MethodNotAllowed = "The requested HTTP method is not supported for this endpoint.";
    }

    // Error codes
    public static class Codes
    {
        public const string BadRequest = "BAD_REQUEST";
        public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string NotFound = "NOT_FOUND";
        public const string Conflict = "CONFLICT";
        public const string ValidationError = "VALIDATION_ERROR";
        public const string RateLimit = "RATE_LIMIT";
        public const string InternalError = "INTERNAL_ERROR";
        public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
        public const string InsufficientPermissions = "INSUFFICIENT_PERMISSIONS";

        // codes for auth-specific scenarios
        public const string NoToken = "NO_TOKEN";
        public const string NoApiKey = "NO_API_KEY";
        public const string InvalidToken = "INVALID_TOKEN";
        public const string InvalidApiKey = "INVALID_API_KEY";
        public const string RequestCancelled = "REQUEST_CANCELLED";
    }
}
