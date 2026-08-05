using ThingsGateway.DB;

namespace ThingsGateway.Admin.Application;

/// <summary>IAM to ThingGetWay shadow mapping. No IAM password is copied.</summary>
[SugarTable("ThingGetWay_ShadowUser")]
[Tenant(SqlSugarConst.DB_Admin)]
public sealed class ThingGetWayShadowUserEntity
{
    [SugarColumn(IsPrimaryKey = true, Length = 36)] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [SugarColumn(Length = 100, IsNullable = false)] public string IamUserId { get; set; } = string.Empty;
    [SugarColumn(Length = 120, IsNullable = false)] public string LocalUserId { get; set; } = string.Empty;
    [SugarColumn(Length = 200, IsNullable = true)] public string? UserName { get; set; }
    [SugarColumn(Length = 200, IsNullable = true)] public string? DisplayName { get; set; }
    [SugarColumn(Length = 30, IsNullable = false)] public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
