using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Industrial.Security.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using ThingsGateway.Admin.Application;
using ThingsGateway.DB;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// Bridges ThingGetWay's existing cookie/JWT and role/menu permission model to
/// the platform security contract. The existing password and role tables remain
/// the source of truth in Local mode.
/// </summary>
public sealed class ThingGetWayCurrentUser(IHttpContextAccessor accessor, IConfiguration configuration) : ICurrentUser
{
    private ClaimsPrincipal Principal => accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
    private bool CentralizedAuthentication => string.Equals(
        configuration["Security:Authentication:Mode"],
        "Centralized",
        StringComparison.OrdinalIgnoreCase);

    public IdentitySource Source
    {
        get
        {
            if (Enum.TryParse<IdentitySource>(Find("identity_source"), true, out var source))
                return source;

            if (CentralizedAuthentication)
                return IdentitySource.Platform;

            return Find("global_user_id") is not null || (Find("sub") is not null && Find(ClaimConst.UserId) is null)
                ? IdentitySource.Platform
                : IdentitySource.Local;
        }
    }

    public string? LocalUserId => Find("local_user_id")
        ?? (Source == IdentitySource.Local
            ? Find(ClaimConst.UserId) ?? Find(ClaimTypes.NameIdentifier) ?? Find(ClaimTypes.Name)
            : null);

    public string? GlobalUserId => Find("global_user_id")
        ?? (Source == IdentitySource.Platform ? Find("sub") ?? Find(ClaimTypes.NameIdentifier) : null);

    public string? UserId => LocalUserId ?? GlobalUserId;

    public string? UserName => Find("preferred_username")
        ?? Find("username")
        ?? Find("name")
        ?? Find(ClaimConst.Account)
        ?? Find(ClaimTypes.Name)
        ?? UserId;

    public string? TenantId => Find("tenant_id") ?? Find("tenant") ?? Find(ClaimConst.TenantId);

    public IReadOnlyCollection<string> Roles => Principal.Claims
        .Where(c => c.Type == ClaimTypes.Role
            || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.Type, "roles", StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Value)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public long PermissionVersion => long.TryParse(
        Find("permission_version") ?? Find("PermissionVersion"),
        out var version) ? version : 0;

    public bool IsAuthenticated
    {
        get
        {
            if (Principal.Identity?.IsAuthenticated != true) return false;
            return Source == IdentitySource.Platform
                ? !string.IsNullOrWhiteSpace(GlobalUserId)
                : !string.IsNullOrWhiteSpace(LocalUserId);
        }
    }

    private string? Find(string type) => Principal.Claims.FirstOrDefault(c =>
        string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Value;
}

public sealed class ThingGetWayIdentityProvider(ThingGetWayCurrentUser currentUser) : IIdentityProvider
{
    public CurrentIdentity GetCurrentIdentity() => new(
        currentUser.UserId,
        currentUser.UserName,
        currentUser.TenantId,
        currentUser.Source,
        currentUser.GlobalUserId,
        currentUser.Roles,
        currentUser.PermissionVersion,
        currentUser.IsAuthenticated,
        currentUser.LocalUserId);
}

public sealed class ThingGetWayLocalPermissionSource(
    IHttpContextAccessor accessor,
    ISysUserService users) : ILocalPermissionSource
{
    public async Task<bool> HasPermissionAsync(string userId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        if (string.Equals(principal.FindFirst(ClaimConst.SuperAdmin)?.Value, "true", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!long.TryParse(userId, out var localUserId))
            localUserId = long.TryParse(principal.FindFirst(ClaimConst.UserId)?.Value, out var claimUserId) ? claimUserId : 0;
        if (localUserId <= 0)
            return false;

        var user = await users.GetUserByIdAsync(localUserId).ConfigureAwait(false);
        if (user is null || !user.Status)
            return false;

        var mapped = ThingGetWayPermissionCodeMapper.MapToLocal(permissionCode);
        var granted = user.PermissionCodeList ?? [];
        return mapped.Any(required => granted.Any(actual =>
            string.Equals(actual, permissionCode, StringComparison.OrdinalIgnoreCase)
            || string.Equals(actual, required, StringComparison.OrdinalIgnoreCase)
            || actual.Trim('/').StartsWith(required.Trim('/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class ThingGetWayPermissionCodeMapper : IPermissionCodeMapper
{
    private static readonly IReadOnlyDictionary<string, string[]> Mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["thingsgateway.gateway.access"] = ["gateway"],
        ["thingsgateway.gateway.view"] = ["gateway/monitor", "openApi/runtimeInfo"],
        ["thingsgateway.gateway.control"] = ["gateway/monitor", "openApi/control"],
        ["thingsgateway.gateway.manage"] = ["gateway/system", "openApi/management"],
        ["thingsgateway.gateway.export"] = ["gateway/monitor", "api/gatewayExport"],
        ["thingsgateway.gateway.system"] = ["gateway/system"],
        ["thingsgateway.gateway.plugin"] = ["gateway/plugin"],
        ["thingsgateway.gateway.rules"] = ["gateway/rules"],
        ["thingsgateway.alarm.view"] = ["gateway/realalarm"],
        ["thingsgateway.management.users"] = ["admin/user"],
        ["thingsgateway.management.roles"] = ["admin/role"],
        ["thingsgateway.management.resources"] = ["admin/resource"],
        ["thingsgateway.management.organizations"] = ["admin/org"],
        ["thingsgateway.management.configuration"] = ["admin/config"],
        ["thingsgateway.management.dictionary"] = ["admin/dict"],
        ["thingsgateway.management.positions"] = ["admin/position"],
        ["thingsgateway.management.sessions"] = ["admin/session"],
        ["thingsgateway.management.operation-log"] = ["admin/oplog"],
        ["thingsgateway.management.user-center"] = ["usercenter"],
        ["thingsgateway.management.backend-log"] = ["gateway/backendlog"],
        ["thingsgateway.management.rpc-log"] = ["gateway/rpclog"]
    };

    private static readonly IReadOnlyDictionary<string, string> LegacyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["THINGGATEWAY.Gateway"] = "thingsgateway.gateway.access",
        ["THINGGATEWAY.Gateway.Page"] = "thingsgateway.gateway.view",
        ["THINGGATEWAY.Gateway.View"] = "thingsgateway.gateway.view",
        ["THINGGATEWAY.Gateway.Control"] = "thingsgateway.gateway.control",
        ["THINGGATEWAY.Gateway.Manage"] = "thingsgateway.gateway.manage",
        ["THINGGATEWAY.Gateway.Export"] = "thingsgateway.gateway.export",
        ["THINGGATEWAY.Gateway.System"] = "thingsgateway.gateway.system",
        ["THINGGATEWAY.Gateway.Plugin"] = "thingsgateway.gateway.plugin",
        ["THINGGATEWAY.Gateway.Rules"] = "thingsgateway.gateway.rules",
        ["THINGGATEWAY.Gateway.RealAlarm"] = "thingsgateway.alarm.view",
        ["THINGGATEWAY.Management.Users"] = "thingsgateway.management.users",
        ["THINGGATEWAY.Management.Roles"] = "thingsgateway.management.roles",
        ["THINGGATEWAY.Management.Resources"] = "thingsgateway.management.resources",
        ["THINGGATEWAY.Management.Organizations"] = "thingsgateway.management.organizations",
        ["THINGGATEWAY.Management.Configuration"] = "thingsgateway.management.configuration",
        ["THINGGATEWAY.Management.Dictionary"] = "thingsgateway.management.dictionary",
        ["THINGGATEWAY.Management.Positions"] = "thingsgateway.management.positions",
        ["THINGGATEWAY.Management.Sessions"] = "thingsgateway.management.sessions",
        ["THINGGATEWAY.Management.OperationLog"] = "thingsgateway.management.operation-log",
        ["THINGGATEWAY.Management.UserCenter"] = "thingsgateway.management.user-center",
        ["THINGGATEWAY.Management.BackendLog"] = "thingsgateway.management.backend-log",
        ["THINGGATEWAY.Management.RpcLog"] = "thingsgateway.management.rpc-log"
    };

    internal static IEnumerable<string> KnownCodes => Mappings.Keys;

    public PermissionMappingResult Map(string permissionCode)
    {
        var normalized = Normalize(permissionCode);
        if (normalized == "*") return new(normalized, true, ["*"]);
        return Mappings.TryGetValue(normalized, out var local)
            ? new(normalized, true, local, "ThingsGateway route/controller permission")
            : new(normalized, false, [], "Unknown ThingsGateway permission");
    }

    internal static IReadOnlyCollection<string> MapToLocal(string permissionCode)
    {
        var normalized = Normalize(permissionCode);
        return Mappings.TryGetValue(normalized, out var local) ? local : [permissionCode];
    }

    private static string Normalize(string? permissionCode)
    {
        var value = permissionCode?.Trim() ?? string.Empty;
        return LegacyAliases.TryGetValue(value, out var canonical) ? canonical : value.ToLowerInvariant();
    }
}

public sealed class ThingGetWayLocalPermissionProvider(
    IHttpContextAccessor accessor,
    ISysUserService users) : IUserPermissionProvider
{
    public async Task<UserPermissionSnapshot> GetPermissionsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (long.TryParse(userId, out var localUserId))
        {
            var user = await users.GetUserByIdAsync(localUserId).ConfigureAwait(false);
            if (user?.PermissionCodeList is not null)
                permissions.UnionWith(user.PermissionCodeList);
        }

        var principal = accessor.HttpContext?.User;
        if (string.Equals(principal?.FindFirst(ClaimConst.SuperAdmin)?.Value, "true", StringComparison.OrdinalIgnoreCase))
            permissions.Add("*");

        foreach (var mapping in ThingGetWayPermissionCodeMapper.KnownCodes)
        {
            if (await new ThingGetWayLocalPermissionSource(accessor, users).HasPermissionAsync(userId, mapping, cancellationToken))
                permissions.Add(mapping);
        }

        return new UserPermissionSnapshot(userId, 0, permissions);
    }
}

/// <summary>Shadow mapping for centralized IAM identities; it never stores a password.</summary>
public sealed class ThingGetWayShadowUserResolver(ISqlSugarClient db) : IShadowUserResolver
{
    private const string SystemCode = IndustrialSystemCodes.ThingsGateway;

    public Task<ShadowUserSnapshot?> ResolveAsync(string iamUserId, CancellationToken cancellationToken = default)
        => Task.FromResult(db.Queryable<ThingGetWayShadowUserEntity>().First(x => x.IamUserId == iamUserId) is { } row ? ToSnapshot(row) : null);

    public Task<ShadowUserSnapshot?> EnsureAsync(string iamUserId, string? userName, string? displayName, CancellationToken cancellationToken = default)
    {
        var row = db.Queryable<ThingGetWayShadowUserEntity>().First(x => x.IamUserId == iamUserId);
        if (row is null)
        {
            row = new ThingGetWayShadowUserEntity
            {
                IamUserId = iamUserId,
                LocalUserId = $"{SystemCode}:{iamUserId}",
                UserName = userName,
                DisplayName = displayName
            };
            db.Insertable(row).ExecuteCommand();
        }
        else
        {
            row.UserName = userName;
            row.DisplayName = displayName;
            row.UpdatedAt = DateTime.UtcNow;
            db.Updateable(row).ExecuteCommand();
        }

        return Task.FromResult<ShadowUserSnapshot?>(ToSnapshot(row));
    }

    private static ShadowUserSnapshot ToSnapshot(ThingGetWayShadowUserEntity row) => new(
        row.Id, SystemCode, row.LocalUserId, row.IamUserId, row.UserName, row.DisplayName,
        null, null, IdentitySource.Platform, row.Status, row.CreatedAt, row.UpdatedAt);
}
