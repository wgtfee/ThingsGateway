using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThingsGateway.Admin.Application;

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

        // In final Centralized mode the native cookie is still the Blazor business
        // session, but its login challenge must start from IAM. Shadow intentionally
        // keeps the original local login page available for side-by-side verification.
        var webSsoEnabled = App.Configuration.GetValue<bool>("IndustrialIamWeb:Enabled");
        var centralizedAuthorization = string.Equals(
            App.Configuration["Security:Authorization:Mode"],
            "Centralized",
            StringComparison.OrdinalIgnoreCase);
        if (webSsoEnabled && centralizedAuthorization)
        {
            var basePath = App.Configuration["IndustrialIamWeb:ExternalBasePath"]?.Trim() ?? "/thinggateway";
            if (!basePath.StartsWith('/')) basePath = "/" + basePath;
            basePath = basePath.TrimEnd('/');

            services.PostConfigure<CookieAuthenticationOptions>(ClaimConst.Scheme, options =>
            {
                options.LoginPath = basePath + "/auth/iam/signin";
                options.AccessDeniedPath = basePath + "/auth/iam/signin";
            });
        }
    }
}
