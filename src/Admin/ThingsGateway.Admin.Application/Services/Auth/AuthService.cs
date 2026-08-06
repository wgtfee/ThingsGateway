//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

using System.Security.Claims;

using ThingsGateway.DataEncryption;
using ThingsGateway.FriendlyException;

namespace ThingsGateway.Admin.Application;

public class AuthService : IAuthService
{
    private readonly ISysDictService _configService;
    private readonly ISysResourceService _sysResourceService;
    private readonly IUserCenterService _userCenterService;
    private readonly ISysUserService _sysUserService;
    private readonly ISysOrgService _sysOrgService;
    private IStringLocalizer<AuthService> _localizer;
    private IVerificatInfoService _verificatInfoService;
    private IAppService _appService;

    public AuthService(ISysDictService configService, ISysResourceService sysResourceService,
        ISysUserService userService, IUserCenterService userCenterService,
         ISysOrgService sysOrgService,
        IVerificatInfoService verificatInfoService, IAppService appService,
        IStringLocalizer<AuthService> localizer)
    {
        _sysOrgService = sysOrgService;
        _configService = configService;
        _appService = appService;
        _sysUserService = userService;
        _sysResourceService = sysResourceService;
        _userCenterService = userCenterService;
        _localizer = localizer;
        _verificatInfoService = verificatInfoService;
    }

    public async Task<LoginOutput> LoginAsync(LoginInput input, bool isCookie = true)
    {
        if (string.Equals(
                App.Configuration["Security:Authorization:Mode"],
                "Centralized",
                StringComparison.OrdinalIgnoreCase))
        {
            throw Oops.Bah("系统已启用统一身份认证，请使用 IAM 登录。");
        }

        var appConfig = await _configService.GetAppConfigAsync().ConfigureAwait(false);

        if (!appConfig.WebsitePolicy.WebStatus
            && input.Account != RoleConst.SuperAdmin)
        {
            throw Oops.Bah(appConfig.WebsitePolicy.CloseTip);
        }

        string? password = input.Password;
        if (isCookie)
        {
            try
            {
                password = DESEncryption.Decrypt(input.Password);
            }
            catch (Exception)
            {
                throw Oops.Bah(_localizer["MustDesc"]);
            }
        }

        await BeforeLoginAsync(appConfig, input).ConfigureAwait(false);

        var userInfo = await _sysUserService.GetUserByAccountAsync(input.Account, input.TenantId).ConfigureAwait(false);
        if (userInfo == null)
            throw Oops.Bah(_localizer["UserNull", input.Account]);

        if (userInfo.Password != password)
            LoginError(appConfig.LoginPolicy, input.Account);

        return await ExecLogin(appConfig.LoginPolicy, input, userInfo, isCookie).ConfigureAwait(false);
    }

    public async Task<LoginOutput> LoginTrustedLocalUserAsync(
        long localUserId,
        IReadOnlyCollection<Claim>? additionalClaims = null,
        int? maxSessionMinutes = null)
    {
        if (localUserId <= 0)
            throw new ArgumentOutOfRangeException(nameof(localUserId));

        var appConfig = await _configService.GetAppConfigAsync().ConfigureAwait(false);
        var userInfo = await _sysUserService.GetUserByIdAsync(localUserId).ConfigureAwait(false);
        if (userInfo is null)
            throw Oops.Bah(_localizer["UserNull", localUserId.ToString()]);

        var tenantEnabled = App.GetOptions<TenantOptions>()?.Enable ?? false;
        var tenantId = tenantEnabled
            ? await _sysOrgService.GetTenantIdByOrgIdAsync(userInfo.OrgId).ConfigureAwait(false)
            : RoleConst.DefaultTenantId;

        var input = new LoginInput
        {
            Account = userInfo.Account,
            Password = string.Empty,
            TenantId = tenantId
        };

        return await ExecLogin(
            appConfig.LoginPolicy,
            input,
            userInfo,
            isCookie: true,
            additionalClaims,
            maxSessionMinutes).ConfigureAwait(false);
    }

    public async Task LoginOutAsync()
    {
        if (UserManager.VerificatId == 0)
            return;

        var httpContext = App.HttpContext;
        var platformSession = string.Equals(
            httpContext?.User.FindFirst("identity_source")?.Value,
            "Platform",
            StringComparison.OrdinalIgnoreCase);

        var verificatId = UserManager.VerificatId;
        var userinfo = await _sysUserService.GetUserByAccountAsync(UserManager.UserAccount, UserManager.TenantId).ConfigureAwait(false);
        if (userinfo != null)
        {
            var loginEvent = new LoginEvent
            {
                Ip = _appService.RemoteIpAddress,
                SysUser = userinfo,
                VerificatId = verificatId
            };
            RemoveTokenFromCache(loginEvent);
        }

        await _appService.LoginOutAsync().ConfigureAwait(false);

        // The IAM session cookie was originally set through the same Gateway host. Removing
        // it from the native logout response makes the existing Blazor logout UI a real SSO
        // logout without teaching the UI about IAM internals.
        if (platformSession && httpContext is not null)
        {
            var iamCookieName = App.Configuration["IndustrialIamWeb:IamSessionCookieName"]?.Trim();
            if (string.IsNullOrWhiteSpace(iamCookieName))
                iamCookieName = "industrial-iam-session";
            httpContext.Response.Cookies.Delete(iamCookieName, new CookieOptions { Path = "/" });
        }
    }

    private async Task BeforeLoginAsync(AppConfig appConfig, LoginInput input)
    {
        var tenantEnable = App.GetOptions<TenantOptions>()?.Enable ?? false;

        if (tenantEnable)
        {
            if (input.TenantId == null)
            {
                var origin = App.HttpContext.Request.Headers["Origin"].ToString();
                if (string.IsNullOrEmpty(origin))
                    origin = App.HttpContext.Request.Headers["Referer"].ToString();
                var domain = origin.Split("//")[1].Split(".")[0];
                var tenantList = await _sysOrgService.GetTenantListAsync().ConfigureAwait(false);
                var tenant = tenantList.FirstOrDefault(x => x.Code.Equals(domain, StringComparison.OrdinalIgnoreCase));
                input.TenantId = tenant?.Id ?? RoleConst.DefaultTenantId;
            }
        }
        else
        {
            input.TenantId = RoleConst.DefaultTenantId;
        }

        var key = CacheConst.Cache_LoginErrorCount + input.Account + input.TenantId;
        var errorCountCache = App.CacheService.Get<int>(key);
        if (errorCountCache >= appConfig.LoginPolicy.ErrorCount)
        {
            App.CacheService.SetExpire(key, TimeSpan.FromMinutes(appConfig.LoginPolicy.ErrorLockTime));
            throw Oops.Bah(_localizer["PasswordError", appConfig.LoginPolicy.ErrorLockTime]);
        }
    }

    private async Task<LoginOutput> ExecLogin(
        LoginPolicy loginPolicy,
        LoginInput input,
        SysUser sysUser,
        bool isCookie = true,
        IReadOnlyCollection<Claim>? additionalClaims = null,
        int? maxSessionMinutes = null)
    {
        if (sysUser.Status == false)
            throw Oops.Bah(_localizer["UserDisable", sysUser.Account]);

        var verificatId = CommonUtils.GetSingleId();
        var expire = maxSessionMinutes is > 0
            ? Math.Min(loginPolicy.VerificatExpireTime, maxSessionMinutes.Value)
            : loginPolicy.VerificatExpireTime;
        string accessToken = string.Empty;
        string refreshToken = string.Empty;

        if (!isCookie)
        {
            accessToken = JWTEncryption.Encrypt(new Dictionary<string, object>
            {
                { ClaimConst.UserId, sysUser.Id },
                { ClaimConst.Account, sysUser.Account },
                { ClaimConst.SuperAdmin, sysUser.RoleIdList.Contains(RoleConst.SuperAdminRoleId) },
                { ClaimConst.VerificatId, verificatId },
                { ClaimConst.OrgId, sysUser.OrgId },
                { ClaimConst.TenantId, input.TenantId }
            });
            refreshToken = JWTEncryption.GenerateRefreshToken(accessToken, expire * 2);
            App.HttpContext?.SigninToSwagger(accessToken);
            App.HttpContext?.SetTokensOfResponseHeaders(accessToken, refreshToken);
        }
        else
        {
            if (sysUser.ModuleList.Count == 0)
                throw Oops.Bah(_localizer["UserNoModule"]);
            var org = await _sysOrgService.GetSysOrgByIdAsync(sysUser.OrgId).ConfigureAwait(false);
            if (!org.Status)
                throw Oops.Bah(_localizer["OrgDisable"]);

            var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
            identity.AddClaim(new Claim(ClaimConst.VerificatId, verificatId.ToString()));
            identity.AddClaim(new Claim(ClaimConst.UserId, sysUser.Id.ToString()));
            identity.AddClaim(new Claim(ClaimConst.Account, sysUser.Account));
            identity.AddClaim(new Claim(ClaimConst.SuperAdmin, sysUser.RoleIdList.Contains(RoleConst.SuperAdminRoleId).ToString()));
            identity.AddClaim(new Claim(ClaimConst.OrgId, sysUser.OrgId.ToString()));
            identity.AddClaim(new Claim(ClaimConst.TenantId, input.TenantId?.ToString() ?? "0"));

            if (additionalClaims is not null)
            {
                foreach (var claim in additionalClaims)
                {
                    if (!identity.HasClaim(x => x.Type == claim.Type && x.Value == claim.Value))
                        identity.AddClaim(claim);
                }
            }

            await _appService.LoginAsync(identity, expire).ConfigureAwait(false);
        }

        var logingEvent = new LoginEvent
        {
            Ip = _appService.RemoteIpAddress,
            Device = _appService.UserAgent?.Platform ?? "Unknown",
            Expire = expire,
            SysUser = sysUser,
            VerificatId = verificatId
        };
        await WriteTokenToCache(loginPolicy, logingEvent).ConfigureAwait(false);
        await UpdateUser(logingEvent).ConfigureAwait(false);

        return new LoginOutput
        {
            VerificatId = verificatId,
            Account = sysUser.Account,
            Id = sysUser.Id,
            AccessToken = accessToken,
            RefreshToken = refreshToken
        };
    }

    private void LoginError(LoginPolicy loginPolicy, string userName)
    {
        var key = CacheConst.Cache_LoginErrorCount + userName;
        App.CacheService.Increment(key, 1);
        App.CacheService.SetExpire(key, TimeSpan.FromMinutes(loginPolicy.ErrorResetTime));
        var errorCountCache = App.CacheService.Get<int>(key);
        throw Oops.Bah(_localizer["AuthErrorMax", loginPolicy.ErrorCount, loginPolicy.ErrorLockTime, errorCountCache]);
    }

    private void RemoveTokenFromCache(LoginEvent loginEvent)
        => _verificatInfoService.Delete(loginEvent.VerificatId);

    private async Task SingleLogin(long userId)
    {
        var clientIds = _verificatInfoService.GetClientIdListByUserId(userId);
        await NoticeUtil.UserLoginOut(new UserLoginOutEvent
        {
            Message = _localizer["SingleLoginWarn"],
            ClientIds = clientIds,
        }).ConfigureAwait(false);
    }

    private async Task UpdateUser(LoginEvent loginEvent)
    {
        var sysUser = loginEvent.SysUser;
        var key = CacheConst.Cache_LoginErrorCount + sysUser.Account;
        App.CacheService.Remove(key);

        sysUser.LastLoginIp = sysUser.LatestLoginIp;
        sysUser.LastLoginTime = sysUser.LatestLoginTime;
        sysUser.LatestLoginIp = loginEvent.Ip;
        sysUser.LatestLoginTime = loginEvent.DateTime;

        using var db = DbContext.GetDB<SysUser>();
        if (await db.UpdateableT(sysUser).UpdateColumns(it => new
        {
            it.LastLoginIp,
            it.LastLoginTime,
            it.LatestLoginIp,
            it.LatestLoginTime,
        }).ExecuteCommandAsync().ConfigureAwait(false) > 0)
        {
            App.CacheService.HashAdd(CacheConst.Cache_SysUser, sysUser.Id.ToString(), sysUser);
        }
    }

    private async Task WriteTokenToCache(LoginPolicy loginPolicy, LoginEvent loginEvent)
    {
        var tokenTimeout = loginEvent.DateTime.AddMinutes(loginEvent.Expire);
        var verificatInfo = new VerificatInfo
        {
            Device = loginEvent.Device ?? "Unknown",
            Expire = loginEvent.Expire,
            VerificatTimeout = tokenTimeout,
            Id = loginEvent.VerificatId,
            UserId = loginEvent.SysUser.Id,
            LoginIp = loginEvent.Ip,
            LoginTime = loginEvent.DateTime
        };
        if (loginPolicy.SingleOpen)
            await SingleLogin(loginEvent.SysUser.Id).ConfigureAwait(false);

        _verificatInfoService.Add(verificatInfo);
    }
}

public class LoginEvent
{
    public DateTime DateTime = DateTime.Now;
    public int Expire { get; set; }
    public string? Ip { get; set; }
    public SysUser SysUser { get; set; }
    public long VerificatId { get; set; }
    public string Device { get; set; }
}
