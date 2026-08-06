using Microsoft.Extensions.DependencyInjection;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// Adds the ThingsGateway-only IAM token bridge after the shared Industrial.Security
/// services have registered the Industrial.IAM named client.
/// </summary>
[AppStartup(-99998)]
public sealed class ThingGetWayIamWebSsoStartup : AppStartup
{
    public void Configure(IServiceCollection services)
    {
        services.AddTransient<ThingGetWayPlatformTokenHandler>();
        services.AddHttpClient("Industrial.IAM")
            .AddHttpMessageHandler<ThingGetWayPlatformTokenHandler>();
    }
}
