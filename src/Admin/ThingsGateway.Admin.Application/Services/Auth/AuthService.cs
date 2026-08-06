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

    /// <summary>
    /// 登录
    /// </summary>
    /// <param name="input">登录参数</param>
    /// <param name="isCookie">cookie方式登录</param>
    /// <returns>登录输出</returns>
    public async Task<LoginOutput> LoginAsync(LoginInput input, bool isCookie = true)
    {
        // Shadow deliberately keeps the native password flow for side-by-side verification.
        // Once authorization is fully Centralized, however, local credentials must no longer
        // mint either a browser cookie or a local API token.
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
        {
            LoginError(appConfig.LoginPolicy, input.Account);
        }
        var result = await ExecLogin(appConfig.LoginPolicy, input, userInfo, isCookie).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 使用已经由可信上游身份系统验证并显式绑定的本地用户执行原生 Cookie 登录。
    /// 该入口不验证本地密码，但仍复用原生登录生命周期，包括账号/组织/模块状态校验、
    /// 在线会话、单用户登录、登录时间更新以及原始 Claims 生成。
    /// </summary>
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

    /// <summary>
    /// 注销当前用户
    /// </summary>
    public async Task LoginOutAsync()
    {
        if (UserManager.VerificatId == 0)
            return;
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
    }

    #region 方法

    /// <summary>
    /// 登录之前执行的方法
    /// </summary>
    /// <param name="appConfig">配置</param>
    /// <param name="input">input</param>
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
                if (tenant != null)
                    input.TenantId = tenant.Id;
                else
                    input.TenantId = RoleConst.DefaultTenantId;
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

    /// <summary>
    /// 执行登录
    /// </summary>
    /// <param name="loginPolicy">登录策略</param>
    /// <param name="input">用户登录参数</param>
    /// <param name="sysUser">用户信息</param>
    /// <param name="isCookie">cookie方式登录</param>
    /// <param name="additionalClaims">可信服务端调用方附加的平台身份 Claim。</param>
    /// <param name="maxSessionMinutes">可选的可信上游令牌寿命上限。</param>
    /// <returns>登录输出结果</returns>
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
            #region Token

            accessToken = JWTEncryption.Encrypt(new Dictionary<string, object>
        {
            {
                ClaimConst.UserId, sysUser.Id
            },
            {
                ClaimConst.Account, sysUser.Account
            },
            {
                ClaimConst.SuperAdmin, sysUser.RoleIdList.Contains(RoleConst.SuperAdminRoleId)
            },
                                {
                ClaimConst.VerificatId, verificatId
            },
            {
                ClaimConst.OrgId, sysUser.OrgId
            },
             {
                ClaimConst.TenantId, input.TenantId
            }
        });
            refreshToken = JWTEncryption.GenerateRefreshToken(accessToken, expire * 2);
            App.HttpContext?.SigninToSwagger(accessToken);
            App.HttpContext?.SetTokensOfResponseHeaders(accessToken, refreshToken);

            #endregion Token
        }
        else
        {
            if (sysUser.ModuleList.Count == 0)
                throw Oops.Bah(_localizer["UserNoModule"]);
            var org = await _sysOrgService.GetSysOrgByIdAsync(sysUser.OrgId).ConfigureAwait(false);
            if (!org.Status) throw Oops.Bah(_localizer["OrgDisable"]);
            #region cookie

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

            #endregion cookie
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

    /// <summary>
    /// 登录错误反馈
    /// </summary>
    /// <param name="loginPolicy">登录策略</param>
    /// <param name="userName">用户名称</param>
    private void LoginError(LoginPolicy loginPolicy, string userName)
    {
        var key = CacheConst.Cache_LoginErrorCount + userName;
        App.CacheService.Increment(key, 1);
        App.CacheService.SetExpire(key, TimeSpan.FromMinutes(loginPolicy.ErrorResetTime));
        var errorCountCache = App.CacheService.Get<int>(key);
        throw Oops.Bah(_localizer["AuthErrorMax", loginPolicy.ErrorCount, loginPolicy.ErrorLockTime, errorCountCache]);
    }

    /// <summary>
    /// 从cache删除用户verificat
    /// </summary>
    /// <param name="loginEvent">登录事件参数</param>
    private void RemoveTokenFromCache(LoginEvent loginEvent)
    {
        _verificatInfoService.Delete(loginEvent.VerificatId);
    }

    /// <summary>
    /// 单用户登录通知用户下线
    /// </summary>
    /// <param name="userId">用户Id</param>
    private async Task SingleLogin(long userId)
    {
        var clientIds = _verificatInfoService.GetClientIdListByUserId(userId);
        await NoticeUtil.UserLoginOut(new UserLoginOutEvent
        {
            Message = _localizer["SingleLoginWarn"],
            ClientIds = clientIds,
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 登录事件
    /// </summary>
    /// <param name="loginEvent"></param>
    /// <returns></returns>
    private async Task UpdateUser(LoginEvent loginEvent)
    {
        var sysUser = loginEvent.SysUser;

        #region 登录/密码策略

        var key = CacheConst.Cache_LoginErrorCount + sysUser.Account;
        App.CacheService.Remove(key);

        var userToken = _verificatInfoService.GetOne(loginEvent.VerificatId);

        #endregion 登录/密码策略

        #region 重新赋值属性,设置本次登录信息为最新的信息

        sysUser.LastLoginIp = sysUser.LatestLoginIp;
        sysUser.LastLoginTime = sysUser.LatestLoginTime;
        sysUser.LatestLoginIp = loginEvent.Ip;
        sysUser.LatestLoginTime = loginEvent.DateTime;

        #endregion 重新赋值属性,设置本次登录信息为最新的信息

        using var db = DbContext.GetDB<SysUser>();
        if (await db.UpdateableT(sysUser).UpdateColumns(it => new
        {
            it.LastLoginIp,
            it.LastLoginTime,
            it.LatestLoginIp,
            it.LatestLoginTime,
        }).ExecuteCommandAsync().ConfigureAwait(false) > 0)
            App.CacheService.HashAdd(CacheConst.Cache_SysUser, sysUser.Id.ToString(), sysUser);
    }

    /// <summary>
    /// 写入用户verificat到cache
    /// </summary>
    /// <param name="loginPolicy">登录策略</param>
    /// <param name="loginEvent">登录事件参数</param>
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
        {
            await SingleLogin(loginEvent.SysUser.Id).ConfigureAwait(false);
        }

        _verificatInfoService.Add(verificatInfo);
    }

    #endregion 方法
}

/// <summary>
/// 登录事件参数
/// </summary>
public class LoginEvent
{
    /// <summary>
    /// 时间
    /// </summary>
    public DateTime DateTime = DateTime.Now;

    /// <summary>
    /// 过期时间
    /// </summary>
    public int Expire { get; set; }

    /// <summary>
    /// Ip地址
    /// </summary>
    public string? Ip { get; set; }

    /// <summary>
    /// 用户信息
    /// </summary>
    public SysUser SysUser { get; set; }

    /// <summary>
    /// VerificatId
    /// </summary>
    public long VerificatId { get; set; }

    /// <summary>
    /// 登录设备
    /// </summary>
    public string Device { get; set; }
}