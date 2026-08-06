using System.Net.Http.Headers;
using Industrial.Security.Abstractions;
using Microsoft.AspNetCore.Http;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// For cookie-based ThingsGateway web sessions, forwards the IAM access token that is
/// stored inside the encrypted/HttpOnly native authentication ticket. Browser JavaScript
/// never receives the token. Existing Bearer headers always take precedence.
/// </summary>
public sealed class ThingGetWayPlatformTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = accessor.HttpContext?.User
                .FindFirst(IndustrialClaimTypes.ProtectedPlatformAccessToken)?.Value;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
