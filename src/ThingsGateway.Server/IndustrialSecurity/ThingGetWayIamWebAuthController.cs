using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Industrial.Security.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ThingsGateway.Admin.Application;
using ThingsGateway.DB;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// Server-mediated Authorization Code + PKCE bridge for the Blazor/cookie based
/// ThingsGateway web application. IAM authenticates the human identity; after an explicit
/// IAM -> local user binding, the existing ThingsGateway AuthService creates the native
/// cookie/session so role, organization and device data-scope behavior stays unchanged.
/// </summary>
[ApiController]
[Route("auth/iam")]
public sealed class ThingGetWayIamWebAuthController : ControllerBase
{
    private const string PkceCookieName = "thinggateway_iam_pkce";
    private static readonly TimeSpan PkceLifetime = TimeSpan.FromMinutes(10);

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _clients;
    private readonly IShadowUserResolver _bindings;
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;
    private readonly IDataProtector _protector;

    public ThingGetWayIamWebAuthController(
        IConfiguration configuration,
        IHttpClientFactory clients,
        IShadowUserResolver bindings,
        IAuthService auth,
        ICurrentUser currentUser,
        IDataProtectionProvider dataProtection)
    {
        _configuration = configuration;
        _clients = clients;
        _bindings = bindings;
        _auth = auth;
        _currentUser = currentUser;
        _protector = dataProtection.CreateProtector("ThingsGateway.IAM.Web.PKCE.v1");
    }

    /// <summary>
    /// Small same-origin bootstrap page. Credentials are posted by the browser directly
    /// to Gateway/IAM /account/login; ThingsGateway never receives or stores the IAM password.
    /// </summary>
    [AllowAnonymous]
    [NonUnify]
    [HttpGet("signin")]
    public IActionResult SignInPage([FromQuery] string? returnUrl = null)
    {
        if (!IsEnabled()) return NotFound();

        var safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var loginPath = ExternalBasePath() + "/auth/iam/login?returnUrl=" + Uri.EscapeDataString(safeReturnUrl);
        var html = $$"""
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width,initial-scale=1" />
  <title>ThingsGateway 统一身份认证</title>
  <style>
    body{font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f5f7fa;margin:0;display:grid;place-items:center;min-height:100vh}
    .card{width:min(380px,calc(100vw - 40px));background:#fff;border-radius:12px;padding:28px;box-shadow:0 12px 36px rgba(0,0,0,.10)}
    h2{margin:0 0 20px;font-size:20px}.hint{color:#667085;font-size:13px;margin-bottom:18px}
    label{display:block;margin:12px 0 6px;font-size:13px}input{box-sizing:border-box;width:100%;padding:10px 12px;border:1px solid #d0d5dd;border-radius:7px}
    button,.continue{box-sizing:border-box;width:100%;margin-top:14px;padding:11px;border:0;border-radius:7px;font-weight:600;cursor:pointer;text-align:center;text-decoration:none;display:block}
    button{background:#1677ff;color:#fff}.continue{background:#eef4ff;color:#175cd3}.divider{text-align:center;color:#98a2b3;font-size:12px;margin-top:14px}
    #error{min-height:20px;margin-top:12px;color:#d92d20;font-size:13px}
  </style>
</head>
<body>
  <main class="card">
    <h2>ThingsGateway 统一身份认证</h2>
    <div class="hint">使用平台 IAM 账号登录。本页不会把 IAM 密码发送给 ThingsGateway。</div>
    <a class="continue" href="{{loginPath}}">已有 IAM 会话，直接继续</a>
    <div class="divider">或重新验证平台账号</div>
    <form id="loginForm">
      <label for="userName">IAM 账号</label>
      <input id="userName" autocomplete="username" required />
      <label for="password">IAM 密码</label>
      <input id="password" type="password" autocomplete="current-password" required />
      <label for="tenant">租户（可选）</label>
      <input id="tenant" autocomplete="organization" />
      <button type="submit">统一身份登录</button>
      <div id="error"></div>
    </form>
  </main>
<script>
const form=document.getElementById('loginForm');
const error=document.getElementById('error');
form.addEventListener('submit', async (event) => {
  event.preventDefault(); error.textContent='';
  const payload={
    userName:document.getElementById('userName').value,
    password:document.getElementById('password').value,
    tenant:document.getElementById('tenant').value || null
  };
  try {
    const response=await fetch('/account/login',{
      method:'POST',credentials:'include',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)
    });
    if(!response.ok){ error.textContent='IAM 账号或密码错误，或账号已被禁用。'; return; }
    window.location.replace('{{loginPath}}');
  } catch(e) { error.textContent='无法连接统一身份服务。'; }
});
</script>
</body>
</html>
""";
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Starts the Authorization Code + PKCE flow after the IAM browser session exists.</summary>
    [AllowAnonymous]
    [NonUnify]
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        if (!IsEnabled()) return NotFound();

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var safeReturnUrl = NormalizeReturnUrl(returnUrl);
        var payload = new PkcePayload(state, verifier, safeReturnUrl, DateTimeOffset.UtcNow);
        var protectedPayload = _protector.Protect(JsonSerializer.Serialize(payload));

        Response.Cookies.Append(PkceCookieName, protectedPayload, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = ExternalBasePath() + "/auth/iam",
            MaxAge = PkceLifetime,
            IsEssential = true
        });

        var authorize = BuildUri(PublicAuthority() + "/connect/authorize", new Dictionary<string, string>
        {
            ["client_id"] = ClientId(),
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri(),
            ["scope"] = $"openid profile {IndustrialSecurityScopes.Platform}",
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        });
        return Redirect(authorize);
    }

    /// <summary>
    /// Exchanges the authorization code server-side, verifies IAM SystemAccess, resolves the
    /// explicit local-user binding, then enters the unchanged ThingsGateway native login path.
    /// </summary>
    [AllowAnonymous]
    [NonUnify]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled()) return NotFound();
        if (!string.IsNullOrWhiteSpace(error))
            return SsoError("IAM 授权失败：" + error);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
            return SsoError("IAM 回调缺少授权码或 state。");
        if (!TryReadPkce(out var pkce) || pkce is null)
            return SsoError("登录状态已失效，请重新发起统一身份认证。");
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(pkce.State),
                Encoding.UTF8.GetBytes(state)))
            return SsoError("IAM state 校验失败。");
        if (DateTimeOffset.UtcNow - pkce.CreatedAt > PkceLifetime)
            return SsoError("IAM PKCE 会话已过期，请重新登录。");

        DeletePkceCookie();

        var token = await ExchangeCodeAsync(code, pkce.Verifier, cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            return SsoError("IAM 授权码交换失败。");

        var me = await GetIamUserAsync(token.AccessToken, cancellationToken);
        if (me is null || string.IsNullOrWhiteSpace(me.Id))
            return SsoError("无法读取 IAM 用户身份。");

        if (!await HasSystemAccessAsync(token.AccessToken, cancellationToken))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "当前 IAM 用户没有 THINGSGATEWAY SystemAccess。"
            });

        var binding = await _bindings.ResolveAsync(me.Id, cancellationToken);
        if (binding is null || !long.TryParse(binding.LocalUserId, out var localUserId) || localUserId <= 0)
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "IAM 用户尚未绑定有效的 ThingsGateway 本地用户，请先由本地超级管理员完成绑定。",
                iamUserId = me.Id
            });

        var platformClaims = new List<Claim>
        {
            new(IndustrialClaimTypes.GlobalUserId, me.Id),
            new(IndustrialClaimTypes.IdentitySource, IdentitySource.Platform.ToString()),
            new(IndustrialClaimTypes.ProtectedPlatformAccessToken, token.AccessToken)
        };
        if (me.PermissionVersion > 0)
            platformClaims.Add(new Claim(IndustrialClaimTypes.PermissionVersion, me.PermissionVersion.ToString()));

        // Keep the native ThingsGateway business session strictly inside the IAM access-token
        // lifetime. A small safety window prevents a valid local cookie from outliving the
        // platform token used for SystemAccess/permission rechecks.
        var maxSessionMinutes = Math.Max(1, (Math.Max(60, token.ExpiresIn) - 120) / 60);
        await _auth.LoginTrustedLocalUserAsync(localUserId, platformClaims, maxSessionMinutes);
        return Redirect(pkce.ReturnUrl);
    }

    /// <summary>Logs out both the native ThingsGateway session and the IAM browser session.</summary>
    [Authorize]
    [NonUnify]
    [HttpGet("logout")]
    public async Task<IActionResult> Logout()
    {
        await _auth.LoginOutAsync();
        var home = ExternalBasePath() + "/";
        var html = $$"""
<!doctype html><html><head><meta charset="utf-8"><title>退出登录</title></head>
<body><script>
fetch('/account/logout',{method:'POST',credentials:'include'}).catch(()=>{}).finally(()=>window.location.replace('{{home}}'));
</script></body></html>
""";
        return Content(html, "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        enabled = IsEnabled(),
        authenticationMode = _configuration["Security:Authentication:Mode"] ?? "Local",
        authorizationMode = _configuration["Security:Authorization:Mode"] ?? "Local",
        shadowCentralAuthorization = _configuration.GetValue<bool>("Security:Central:ShadowCentralAuthorization"),
        requireSystemAccess = _configuration.GetValue<bool>("Security:Central:RequireSystemAccess"),
        clientId = ClientId(),
        redirectUri = RedirectUri()
    });

    /// <summary>
    /// Authenticated diagnostic used by rollout smoke tests. It exposes only identity metadata
    /// already present in the protected ThingsGateway cookie and never returns the IAM token.
    /// </summary>
    [Authorize]
    [HttpGet("session")]
    public IActionResult Session() => Ok(new
    {
        authenticated = _currentUser.IsAuthenticated,
        identitySource = _currentUser.Source.ToString(),
        globalUserId = _currentUser.GlobalUserId,
        localUserId = _currentUser.LocalUserId ?? User.FindFirst(ClaimConst.UserId)?.Value,
        userName = _currentUser.UserName,
        tenantId = _currentUser.TenantId,
        permissionVersion = _currentUser.PermissionVersion
    });

    private async Task<TokenExchangeResult?> ExchangeCodeAsync(
        string code,
        string verifier,
        CancellationToken cancellationToken)
    {
        var http = _clients.CreateClient();
        using var response = await http.PostAsync(
            InternalAuthority() + "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId(),
                ["code"] = code,
                ["redirect_uri"] = RedirectUri(),
                ["code_verifier"] = verifier
            }),
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("access_token", out var tokenElement)
            || string.IsNullOrWhiteSpace(tokenElement.GetString()))
            return null;
        var expiresIn = json.RootElement.TryGetProperty("expires_in", out var expiresElement)
            && expiresElement.TryGetInt32(out var parsed)
            ? parsed
            : 3600;
        return new TokenExchangeResult(tokenElement.GetString()!, expiresIn);
    }

    private async Task<IamMe?> GetIamUserAsync(string accessToken, CancellationToken cancellationToken)
    {
        var http = _clients.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, InternalAuthority() + "/api/iam/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<IamMe>(cancellationToken: cancellationToken);
    }

    private async Task<bool> HasSystemAccessAsync(string accessToken, CancellationToken cancellationToken)
    {
        var http = _clients.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            InternalAuthority() + "/api/iam/access/" + IndustrialSystemCodes.ThingsGateway);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return false;
        var value = await response.Content.ReadFromJsonAsync<SystemAccessResponse>(cancellationToken: cancellationToken);
        return value?.Allowed == true;
    }

    private bool TryReadPkce(out PkcePayload? payload)
    {
        payload = null;
        if (!Request.Cookies.TryGetValue(PkceCookieName, out var protectedPayload)
            || string.IsNullOrWhiteSpace(protectedPayload))
            return false;
        try
        {
            payload = JsonSerializer.Deserialize<PkcePayload>(_protector.Unprotect(protectedPayload));
            return payload is not null;
        }
        catch
        {
            return false;
        }
    }

    private void DeletePkceCookie() => Response.Cookies.Delete(PkceCookieName, new CookieOptions
    {
        Path = ExternalBasePath() + "/auth/iam"
    });

    private IActionResult SsoError(string message)
        => StatusCode(StatusCodes.Status401Unauthorized, new { error = message });

    private bool IsEnabled() => _configuration.GetValue<bool>("IndustrialIamWeb:Enabled");
    private string ClientId() => _configuration["IndustrialIamWeb:ClientId"]?.Trim()
        ?? IndustrialPublicClientIds.ThingsGatewayWeb;
    private string PublicAuthority() => (_configuration["IndustrialIamWeb:PublicAuthority"] ?? "http://localhost:5202").TrimEnd('/');
    private string InternalAuthority() => (_configuration["Security:Central:Authority"] ?? "http://localhost:5100").TrimEnd('/');
    private string RedirectUri() => _configuration["IndustrialIamWeb:RedirectUri"]?.Trim()
        ?? "http://localhost:5202/thinggateway/auth/iam/callback";
    private string ExternalBasePath()
    {
        var path = _configuration["IndustrialIamWeb:ExternalBasePath"]?.Trim() ?? "/thinggateway";
        if (!path.StartsWith('/')) path = "/" + path;
        return path.TrimEnd('/');
    }

    private string NormalizeReturnUrl(string? returnUrl)
    {
        var root = ExternalBasePath() + "/";
        if (string.IsNullOrWhiteSpace(returnUrl)) return root;
        return returnUrl.StartsWith(root, StringComparison.Ordinal) ? returnUrl : root;
    }

    private static string BuildUri(string baseUri, IReadOnlyDictionary<string, string> values)
        => baseUri + "?" + string.Join("&", values.Select(x =>
            Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record TokenExchangeResult(string AccessToken, int ExpiresIn);
    private sealed record PkcePayload(string State, string Verifier, string ReturnUrl, DateTimeOffset CreatedAt);
    private sealed record IamMe(string Id, string UserName, string DisplayName, string? Tenant, long PermissionVersion);
    private sealed record SystemAccessResponse(bool Allowed);
}
