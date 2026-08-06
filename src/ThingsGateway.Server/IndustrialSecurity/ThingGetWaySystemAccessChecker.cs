using System.Net.Http.Headers;
using System.Net.Http.Json;
using Industrial.Security.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// ThingsGateway's Blazor UI authenticates with its native cookie after IAM SSO. The shared
/// checker intentionally expects an inbound Bearer header, so this adapter preserves that
/// behavior for APIs while allowing the protected platform token from the native cookie for
/// server-side UI requests. Any failure remains fail-closed.
/// </summary>
public sealed class ThingGetWaySystemAccessChecker(
    IHttpContextAccessor accessor,
    IHttpClientFactory clients,
    ISystemAccessCache cache,
    IConfiguration configuration,
    ILogger<ThingGetWaySystemAccessChecker> logger) : ISystemAccessChecker
{
    public async Task<bool> HasAccessAsync(
        string iamUserId,
        string systemCode,
        CancellationToken cancellationToken = default)
    {
        var accessToken = ResolveAccessToken();
        if (string.IsNullOrWhiteSpace(accessToken))
            return false;

        if (await cache.GetAsync(iamUserId, systemCode, cancellationToken) is bool cached)
            return cached;

        var positiveSeconds = Math.Max(
            5,
            configuration.GetValue<int?>("Security:Central:SystemAccessCacheSeconds") ?? 30);
        var negativeSeconds = Math.Max(1, Math.Min(5, positiveSeconds));

        try
        {
            var authority = (configuration["Security:Central:Authority"] ?? "http://localhost:5100").TrimEnd('/');
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{authority}/api/iam/access/{Uri.EscapeDataString(systemCode)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await clients.CreateClient("Industrial.IAM").SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await cache.SetAsync(
                    iamUserId,
                    systemCode,
                    false,
                    TimeSpan.FromSeconds(negativeSeconds),
                    cancellationToken);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<SystemAccessResponse>(cancellationToken: cancellationToken);
            var allowed = result?.Allowed == true;
            await cache.SetAsync(
                iamUserId,
                systemCode,
                allowed,
                TimeSpan.FromSeconds(allowed ? positiveSeconds : negativeSeconds),
                cancellationToken);
            return allowed;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "IAM SystemAccess evaluation failed for ThingsGateway user {UserId}/{SystemCode}; denying request.",
                iamUserId,
                systemCode);
            await cache.SetAsync(
                iamUserId,
                systemCode,
                false,
                TimeSpan.FromSeconds(Math.Min(2, negativeSeconds)),
                cancellationToken);
            return false;
        }
    }

    private string? ResolveAccessToken()
    {
        var context = accessor.HttpContext;
        var raw = context?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(raw)
            && raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return raw[7..].Trim();
        }

        return context?.User.FindFirst(IndustrialClaimTypes.ProtectedPlatformAccessToken)?.Value;
    }

    private sealed record SystemAccessResponse(bool Allowed);
}
