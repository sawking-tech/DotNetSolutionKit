using Hangfire.Dashboard;
using Microsoft.AspNetCore.Http;
using NamespaceRoot.ProductName.Common.Infrastructure.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.Security.Filters;

public class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly string _user;
    private readonly string _password;

    public HangfireAdminAuthorizationFilter(IHangfireSettings settings)
    {
        _user = settings.DashboardUser;
        _password = settings.DashboardPassword;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (BasicCredentials.Match(httpContext.Request.Headers.Authorization.FirstOrDefault(), _user, _password))
            return true;

        SetChallengeResponse(httpContext);
        return false;
    }

    private static void SetChallengeResponse(HttpContext context)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers.Append("WWW-Authenticate", "Basic realm=\"Hangfire Dashboard\"");
    }
}
