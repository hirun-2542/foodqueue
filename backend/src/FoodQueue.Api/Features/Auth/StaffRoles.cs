namespace FoodQueue.Api.Features.Auth;

/// <summary>ชื่อ role ของพนักงานร้าน มีสองบทบาทเท่านั้น: เจ้าของร้านและครัว</summary>
public static class StaffRoles
{
    public const string Owner = "Owner";
    public const string Kitchen = "Kitchen";

    public static readonly string[] All = [Owner, Kitchen];
}

/// <summary>
/// ค่าคงที่ของบัญชีเริ่มต้นจาก config (DemoSeed) — บอทเดโมและ endpoint demo ใช้ชุดเดียวกันนี้
/// จึงไม่ต้องประดิษฐ์ชื่อ role หรือธรรมเนียมรหัสผ่านขึ้นใหม่
/// </summary>
public static class SeedAccountKeys
{
    public const string OwnerEmail = "DemoSeed:OwnerEmail";
    public const string KitchenEmail = "DemoSeed:KitchenEmail";
    public const string Password = "DemoSeed:Password";

    /// <summary>ป้ายชื่อที่แสดงบนจอเมื่อบัญชีนั้นไม่มีชื่อบุคคล</summary>
    public const string OwnerDisplayName = "เจ้าของร้าน";
    public const string KitchenDisplayName = "ครัว";
}
