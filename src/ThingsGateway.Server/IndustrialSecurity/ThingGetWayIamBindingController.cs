using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using ThingsGateway.Admin.Application;

namespace ThingsGateway.Server.IndustrialSecurity;

/// <summary>
/// Explicit IAM -> existing ThingGetWay user binding used during IamPrepare/Shadow.
/// It never creates a business user and never copies an IAM password. The existing
/// local user remains the source of roles, organizations and device data scope.
/// </summary>
[ApiController]
[Route("api/security/iam-bindings")]
[Authorize]
public sealed class ThingGetWayIamBindingController : ControllerBase
{
    private readonly ISqlSugarClient _db;
    private readonly ISysUserService _users;

    public ThingGetWayIamBindingController(ISqlSugarClient db, ISysUserService users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!IsLocalSuperAdmin()) return Forbid();

        var rows = _db.Queryable<ThingGetWayShadowUserEntity>()
            .OrderByDescending(x => x.UpdatedAt)
            .ToList()
            .Select(ToResponse);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<IActionResult> Bind([FromBody] ThingGetWayIamBindingRequest request)
    {
        if (!IsLocalSuperAdmin()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.IamUserId))
            return BadRequest(new { error = "IamUserId is required." });
        if (!long.TryParse(request.LocalUserId, out var localUserId) || localUserId <= 0)
            return BadRequest(new { error = "LocalUserId must be an existing numeric ThingGetWay user id." });

        var localUser = await _users.GetUserByIdAsync(localUserId).ConfigureAwait(false);
        if (localUser is null || !localUser.Status)
            return NotFound(new { error = "The local ThingGetWay user does not exist or is disabled." });

        var iamUserId = request.IamUserId.Trim();
        var localId = localUserId.ToString();
        var conflictingLocal = _db.Queryable<ThingGetWayShadowUserEntity>()
            .First(x => x.LocalUserId == localId && x.IamUserId != iamUserId);
        if (conflictingLocal is not null)
            return Conflict(new { error = "This local user is already bound to another IAM user." });

        var row = _db.Queryable<ThingGetWayShadowUserEntity>().First(x => x.IamUserId == iamUserId);
        if (row is null)
        {
            row = new ThingGetWayShadowUserEntity
            {
                IamUserId = iamUserId,
                LocalUserId = localId,
                UserName = request.UserName,
                DisplayName = request.DisplayName,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.Insertable(row).ExecuteCommand();
        }
        else
        {
            row.LocalUserId = localId;
            row.UserName = request.UserName ?? row.UserName;
            row.DisplayName = request.DisplayName ?? row.DisplayName;
            row.Status = "Active";
            row.UpdatedAt = DateTime.UtcNow;
            _db.Updateable(row).ExecuteCommand();
        }

        return Ok(ToResponse(row));
    }

    [HttpDelete("{iamUserId}")]
    public IActionResult Unbind(string iamUserId)
    {
        if (!IsLocalSuperAdmin()) return Forbid();
        var affected = _db.Deleteable<ThingGetWayShadowUserEntity>()
            .Where(x => x.IamUserId == iamUserId)
            .ExecuteCommand();
        return affected > 0 ? NoContent() : NotFound();
    }

    private bool IsLocalSuperAdmin()
    {
        if (User.Identity?.IsAuthenticated != true) return false;
        if (string.Equals(User.FindFirst("identity_source")?.Value, "Platform", StringComparison.OrdinalIgnoreCase))
            return false;
        return string.Equals(User.FindFirst(ClaimConst.SuperAdmin)?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static object ToResponse(ThingGetWayShadowUserEntity row) => new
    {
        row.IamUserId,
        row.LocalUserId,
        row.UserName,
        row.DisplayName,
        row.Status,
        row.CreatedAt,
        row.UpdatedAt
    };
}

public sealed record ThingGetWayIamBindingRequest(
    string IamUserId,
    string LocalUserId,
    string? UserName = null,
    string? DisplayName = null);
