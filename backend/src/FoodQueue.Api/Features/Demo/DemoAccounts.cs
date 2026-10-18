using FoodQueue.Api.Common;
using FoodQueue.Api.Features.Auth;

namespace FoodQueue.Api.Features.Demo;

/// <summary>
/// บัญชีพนักงานของ Demo — ใช้ <see cref="StaffAccountProvisioner"/> ชุดเดียวกับตอนสตาร์ท
/// (อีเมล/รหัสผ่านมาจาก config หมวด <c>DemoSeed</c>) จึงไม่มีธรรมเนียมบัญชีชุดที่สองในระบบ
/// </summary>
internal static class DemoAccounts
{
    /// <summary>บทบาทที่ขอมา (ไม่สนตัวพิมพ์เล็ก/ใหญ่) หรือ <c>VALIDATION_FAILED</c> พร้อมข้อความไทย</summary>
    public static string RequireRole(string? requested) =>
        StaffRoles.All.FirstOrDefault(r => string.Equals(r, requested?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
            $"บทบาทที่เข้าใช้ได้คือ {string.Join(" และ ", StaffRoles.All)}");

    /// <summary>อีเมลของ role นั้น</summary>
    public static string EmailFor(IConfiguration config, string role) => role switch
    {
        StaffRoles.Owner => Required(config, SeedAccountKeys.OwnerEmail),
        StaffRoles.Kitchen => Required(config, SeedAccountKeys.KitchenEmail),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "role ที่ไม่ใช่พนักงาน"),
    };

    public static string DisplayNameFor(string role) => role switch
    {
        StaffRoles.Owner => SeedAccountKeys.OwnerDisplayName,
        StaffRoles.Kitchen => SeedAccountKeys.KitchenDisplayName,
        _ => role,
    };

    /// <summary>
    /// สร้าง role และบัญชี demo ทั้งสองบทบาทถ้ายังไม่มี (เรียกซ้ำได้) — ตอนสตาร์ท
    /// <c>AuthStartupTask</c> ทำงานนี้อยู่แล้ว การเรียกที่นี่จึงเป็นการรับประกันว่าบัญชีที่ปุ่ม demo
    /// ต้องใช้มีอยู่จริง แม้ seeder ถูกเรียกจากที่อื่น
    /// </summary>
    public static async Task EnsureAsync(IServiceProvider services, IConfiguration config)
    {
        var password = Required(config, SeedAccountKeys.Password);
        using var scope = services.CreateScope();
        foreach (var role in StaffRoles.All)
        {
            await StaffAccountProvisioner.EnsureAsync(scope.ServiceProvider, EmailFor(config, role), password,
                DisplayNameFor(role), role);
        }
    }

    private static string Required(IConfiguration config, string key)
    {
        var value = config[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings — Demo ต้องมีบัญชีตัวอย่าง")
            : value;
    }
}
