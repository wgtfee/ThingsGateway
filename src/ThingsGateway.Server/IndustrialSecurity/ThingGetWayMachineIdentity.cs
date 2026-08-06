using System.Net.Http;
using System.Text.Json;
using Furion;
using Industrial.Security.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ThingsGateway.Server.IndustrialSecurity;

public sealed record ThingGetWayMachineIdentitySnapshot(
    bool Enabled,
    bool Authenticated,
    string ClientId,
    string Scope,
    DateTimeOffset? TokenExpiresAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError);

public interface IThingGetWayMachineTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    ThingGetWayMachineIdentitySnapshot GetSnapshot();
}

internal sealed class ThingGetWayMachineIdentityState
{
    private readonly object _sync = new();
    private ThingGetWayMachineIdentitySnapshot _snapshot = new(
        false,
        false,
        IndustrialServiceClientIds.ThingsGateway,
        IndustrialSecurityScopes.IotGatewayConnect,
        null,
        null,
        null);

    public ThingGetWayMachineIdentitySnapshot Snapshot
    {
        get { lock (_sync) return _snapshot; }
    }

    public void Set(ThingGetWayMachineIdentitySnapshot snapshot)
    {
        lock (_sync) _snapshot = snapshot;
    }
}

/// <summary>
/// Cached client-credentials provider for low-frequency platform/control-plane calls.
/// PLC polling, protocol drivers and telemetry loops must never call IAM per sample.
/// </summary>
internal sealed class ThingGetWayMachineTokenProvider(
    IConfiguration configuration,
    IHttpClientFactory clients,
    ThingGetWayMachineIdentityState state,
    ILogger<ThingGetWayMachineTokenProvider> logger) : IThingGetWayMachineTokenProvider
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiresAt;

    public ThingGetWayMachineIdentitySnapshot GetSnapshot() => state.Snapshot;

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var section = configuration.GetSection("IndustrialMachineIdentity");
        var enabled = section.GetValue<bool>("Enabled");
        var authority = (section["Authority"] ?? configuration["Security:Central:Authority"] ?? "http://localhost:5100").TrimEnd('/');
        var clientId = section["ClientId"] ?? IndustrialServiceClientIds.ThingsGateway;
        var clientSecret = section["ClientSecret"]
            ?? configuration["Security:ResourceSync:ClientSecret"]
            ?? string.Empty;
        var scope = section["Scope"] ?? IndustrialSecurityScopes.IotGatewayConnect;

        if (!enabled)
        {
            state.Set(new(false, false, clientId, scope, null, null, null));
            throw new InvalidOperationException("IndustrialMachineIdentity is disabled.");
        }
        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            state.Set(new(true, false, clientId, scope, null, null, "ClientSecret is empty."));
            throw new InvalidOperationException("IndustrialMachineIdentity:ClientSecret (or Security:ResourceSync:ClientSecret) is required when machine identity is enabled.");
        }

        if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
            return _accessToken;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) && _expiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
                return _accessToken;

            using var response = await clients.CreateClient("ThingsGateway.MachineIdentity").PostAsync(
                $"{authority}/connect/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope
                }),
                cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"IAM token endpoint returned {(int)response.StatusCode}: {payload}");

            using var document = JsonDocument.Parse(payload);
            var token = document.RootElement.GetProperty("access_token").GetString();
            var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expires)
                ? expires.GetInt32()
                : 300;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("IAM token response does not contain access_token.");

            _accessToken = token;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn));
            var now = DateTimeOffset.UtcNow;
            state.Set(new(true, true, clientId, scope, _expiresAt, now, null));
            logger.LogInformation(
                "ThingsGateway machine identity acquired. ClientId={ClientId}; Scope={Scope}; ExpiresAt={ExpiresAt}",
                clientId,
                scope,
                _expiresAt);
            return token;
        }
        catch (Exception ex)
        {
            var previous = state.Snapshot;
            state.Set(previous with { Enabled = true, Authenticated = false, LastError = ex.Message });
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <summary>
/// Warms/renews the machine token out of band. Failure is deliberately non-fatal so
/// edge acquisition continues when IAM or the upstream network is unavailable.
/// </summary>
internal sealed class ThingGetWayMachineIdentityWorker(
    IConfiguration configuration,
    IThingGetWayMachineTokenProvider tokens,
    ILogger<ThingGetWayMachineIdentityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("IndustrialMachineIdentity:Enabled"))
            return;

        var checkSeconds = Math.Clamp(
            configuration.GetValue<int?>("IndustrialMachineIdentity:RefreshCheckSeconds") ?? 60,
            30,
            3600);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _ = await tokens.GetAccessTokenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ThingsGateway machine identity refresh failed. Device acquisition remains active and the worker will retry.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(checkSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

/// <summary>Registers the machine-identity control plane without touching gateway runtime pipelines.</summary>
[AppStartup(-99998)]
public sealed class ThingGetWayMachineIdentityStartup : AppStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHttpClient("ThingsGateway.MachineIdentity");
        services.AddSingleton<ThingGetWayMachineIdentityState>();
        services.AddSingleton<IThingGetWayMachineTokenProvider, ThingGetWayMachineTokenProvider>();
        services.AddHostedService<ThingGetWayMachineIdentityWorker>();
    }
}

[ApiController]
[Route("api/platform/machine-identity")]
[Authorize]
public sealed class ThingGetWayMachineIdentityController(IThingGetWayMachineTokenProvider tokens) : ControllerBase
{
    [HttpGet("status")]
    public ActionResult<ThingGetWayMachineIdentitySnapshot> Status() => Ok(tokens.GetSnapshot());
}
