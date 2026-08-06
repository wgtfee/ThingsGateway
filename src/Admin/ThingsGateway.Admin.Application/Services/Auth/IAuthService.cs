//------------------------------------------------------------------------------
//  此代码版权声明为全文件覆盖，如有原作者特别声明，会在下方手动补充
//  此代码版权（除特别声明外的代码）归作者本人Diego所有
//  源代码使用协议遵循本仓库的开源协议及附加协议
//  Gitee源代码仓库：https://gitee.com/diego2098/ThingsGateway
//  Github源代码仓库：https://github.com/kimdiego2098/ThingsGateway
//  使用文档：https://thingsgateway.cn/
//  QQ群：605534569
//------------------------------------------------------------------------------

using System.Security.Claims;

namespace ThingsGateway.Admin.Application;

/// <summary>
/// 定义身份验证服务的接口
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// 用户登录
    /// </summary>
    /// <param name="input">登录参数</param>
    /// <param name="isCookie">是否使用 cookie 登录方式</param>
    /// <returns>登录输出</returns>
    Task<LoginOutput> LoginAsync(LoginInput input, bool isCookie = true);

    /// <summary>
    /// 使用已经由上游可信身份系统验证过的本地用户执行原生 Cookie 登录。
    /// 不验证本地密码，但仍执行本地账号、模块、组织状态检查，并复用原在线会话、
    /// 单用户登录、登录时间更新等完整登录生命周期。
    /// </summary>
    /// <param name="localUserId">已显式绑定的 ThingsGateway 本地用户 ID。</param>
    /// <param name="additionalClaims">仅由可信服务端调用方附加的平台身份 Claim。</param>
    Task<LoginOutput> LoginTrustedLocalUserAsync(
        long localUserId,
        IReadOnlyCollection<Claim>? additionalClaims = null);

    /// <summary>
    /// 注销当前用户
    /// </summary>
    Task LoginOutAsync();
}
