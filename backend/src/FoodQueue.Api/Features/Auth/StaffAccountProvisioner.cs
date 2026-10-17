using FoodQueue.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace FoodQueue.Api.Features.Auth;

/// <summary>
/// สร้าง role และบัญชีพนักงานเมื่อยังไม่มี — ใช้ร่วมกันทั้งงานตอนสตาร์ทและงาน seed ของ Demo
/// จึงมีที่เดียวที่กำหนดธรรมเนียม "อีเมลเป็น UserName" และรหัสผ่านมาจาก config
/// </summary>
public static class StaffAccountProvisioner
{
    /// <summary>
    /// สร้าง role (ถ้าไม่มี) และบัญชี (ถ้าไม่มี) แล้วผูก role ให้ — เรียกซ้ำได้ ไม่สร้างซ้ำ
    /// <paramref name="scopedServices"/> ต้องเป็น scope ที่ resolve <see cref="UserManager{TUser}"/> ได้
    /// </summary>
    public static async Task EnsureAsync(IServiceProvider scopedServices, string email, string password,
        string displayName, string role)
    {
        var users = scopedServices.GetRequiredService<UserManager<AppUser>>();
        var roles = scopedServices.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roles.RoleExistsAsync(role))
        {
            var roleResult = await roles.CreateAsync(new IdentityRole(role));
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"สร้าง role {role} ไม่สำเร็จ: {Describe(roleResult)}");
            }
        }

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new AppUser { UserName = email, Email = email, DisplayName = displayName, EmailConfirmed = true };
            var createResult = await users.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"สร้างบัญชี {email} ไม่สำเร็จ: {Describe(createResult)}");
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            var addResult = await users.AddToRoleAsync(user, role);
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"ผูก role {role} กับ {email} ไม่สำเร็จ: {Describe(addResult)}");
            }
        }
    }

    private static string Describe(IdentityResult result) =>
        string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
}

/// <summary>
/// บัญชีเริ่มต้นของเจ้าของร้านและครัวมาจาก config (DemoSeed) — ตั้งรหัสผ่านครั้งเดียวทั้งระบบ
/// ถ้าค่าจำเป็นหายไปให้สตาร์ทไม่ผ่านเลย ดีกว่าเปิดระบบที่ล็อกอินไม่ได้
/// </summary>
public sealed class AuthStartupTask : IStartupTask
{
    public async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        var ownerEmail = Required(config, SeedAccountKeys.OwnerEmail);
        var kitchenEmail = Required(config, SeedAccountKeys.KitchenEmail);
        var password = Required(config, SeedAccountKeys.Password);

        using var scope = services.CreateScope();
        await StaffAccountProvisioner.EnsureAsync(scope.ServiceProvider, ownerEmail, password,
            SeedAccountKeys.OwnerDisplayName, StaffRoles.Owner);
        await StaffAccountProvisioner.EnsureAsync(scope.ServiceProvider, kitchenEmail, password,
            SeedAccountKeys.KitchenDisplayName, StaffRoles.Kitchen);
    }

    private static string Required(IConfiguration config, string key)
    {
        var value = config[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"ไม่ได้ตั้งค่า {key} ใน appsettings — ต้องมีเพื่อสร้างบัญชีเจ้าของร้านและครัวตอนสตาร์ท");
        }

        return value;
    }
}
