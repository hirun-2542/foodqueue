using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Demo;

/// <summary>คำขอของ <c>POST /api/demo/login</c> — <c>{"role":"Owner"}</c> หรือ <c>{"role":"Kitchen"}</c></summary>
public sealed record DemoLoginRequest(string? Role);

/// <summary>คำตอบของ <c>POST /api/demo/customer-session</c>: token ของรอบที่ bot เปิดไว้ (ใช้เปิดหน้า <c>/s/{token}</c>)</summary>
public sealed record DemoCustomerSessionResponse(string Token);

/// <summary>
/// Endpoint ของ Demo Mode (spec stories 104, 105, 109):
/// <list type="bullet">
/// <item><c>GET /api/demo/status</c> — <c>{enabled}</c> ไม่ต้อง auth เพื่อให้หน้า login ตัดสินใจว่าจะขึ้นการ์ดไหม</item>
/// <item><c>POST /api/demo/login</c> — เข้าใช้เป็นเจ้าของร้าน/ครัวโดยไม่ต้องพิมพ์รหัสผ่าน</item>
/// <item><c>POST /api/demo/customer-session</c> — token ของรอบที่ bot เปิด ถ้าไม่มีก็เปิดรอบใหม่ (Gold, 2 ผู้ใหญ่)
/// บนโต๊ะว่าง; ถ้าไม่มีโต๊ะว่างเลยตอบ <c>TABLE_OCCUPIED</c></item>
/// </list>
///
/// เมื่อ <c>Demo:Enabled=false</c> endpoint ทั้งสามตอบ <b>404</b> (spec 109) — module ยังถูก map ตามปกติ
/// แต่ทุกตัวตรวจสวิตช์ตั้งแต่บรรทัดแรก จึงไม่มีทางหลุดไปใช้จริง และไม่มี seed/bot ตอนสตาร์ท (ดู <see cref="DemoSeeder"/>)
/// </summary>
public sealed class DemoEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/demo");

        group.MapGet("/status", Status);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/customer-session", CustomerSessionAsync);
    }

    private static IResult Status(IConfiguration config) =>
        DemoMode.IsEnabled(config)
            ? Results.Ok(new { enabled = true })
            : Results.NotFound();

    /// <summary>
    /// เข้าใช้ด้วยบัญชี demo ตาม role โดยไม่ตรวจรหัสผ่าน — ใช้ <c>SignInManager.SignInAsync</c> ตัวเดียวกับ
    /// <c>POST /api/auth/login</c> (หลังผ่านการตรวจรหัส) cookie จึงเชื่อถือได้เท่ากันทุกประการ
    /// </summary>
    private static async Task<IResult> LoginAsync(DemoLoginRequest? request, IConfiguration config,
        HttpContext http)
    {
        if (!DemoMode.IsEnabled(config))
        {
            return Results.NotFound();
        }

        var role = DemoAccounts.RequireRole(request?.Role);
        await DemoAccounts.EnsureAsync(http.RequestServices, config);

        var signIn = http.RequestServices.GetRequiredService<SignInManager<AppUser>>();
        var email = DemoAccounts.EmailFor(config, role);
        var user = await signIn.UserManager.FindByEmailAsync(email)
                   ?? throw new AppException(ErrorCodes.ValidationFailed,
                       ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                       "บัญชีตัวอย่างของ demo ยังไม่พร้อม · ลองใหม่หลังเซิร์ฟเวอร์สตาร์ทเสร็จ");

        await signIn.SignInAsync(user, isPersistent: true);
        return Results.NoContent();
    }

    /// <summary>
    /// Token ของรอบที่ bot เปิดอยู่ (ยังไม่หมดเวลา) — ถ้าไม่มีก็เปิดรอบใหม่บนโต๊ะว่างที่แรกตามผัง
    /// ตัวเลขของรอบใหม่คือกลาง ๆ ของแพ็กเกจที่ใช้งานอยู่ (Silver/Gold/Platinum → Gold) กับผู้ใหญ่ 2 ท่าน
    /// </summary>
    private static async Task<IResult> CustomerSessionAsync(IConfiguration config, HttpContext http,
        CancellationToken ct)
    {
        if (!DemoMode.IsEnabled(config))
        {
            return Results.NotFound();
        }

        var db = http.RequestServices.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;

        var existing = await db.TableSessions.AsNoTracking()
            .Where(s => s.IsDemoBot && s.Status == SessionStatus.Open && s.EndsAt > now)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return Results.Ok(new DemoCustomerSessionResponse(existing.Token));
        }

        var freeTable = await db.Tables.AsNoTracking()
            .Where(t => t.IsActive
                        && !db.TableSessions.Any(s => s.TableId == t.Id && s.Status == SessionStatus.Open))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);
        if (freeTable is null)
        {
            throw new AppException(ErrorCodes.TableOccupied, ErrorCodes.StatusFor(ErrorCodes.TableOccupied),
                "โต๊ะว่างไม่มีเลย · ปิดรอบของโต๊ะหนึ่งก่อนจึงจะเปิดมุมมองลูกค้าได้");
        }

        var packages = await db.Packages.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Rank)
            .ToListAsync(ct);
        var package = packages.Count == 0
            ? throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "ยังไม่มีแพ็กเกจในระบบ · เปิดมุมมองลูกค้าไม่ได้")
            : packages[packages.Count / 2];

        var detail = await SessionService.From(http.RequestServices)
            .OpenAsync(freeTable.Id, package.Id, adultCount: 2, childCount: 0, ct, isDemoBot: true);

        var token = await db.TableSessions.AsNoTracking()
            .Where(s => s.Id == detail.Id)
            .Select(s => s.Token)
            .SingleAsync(ct);

        return Results.Ok(new DemoCustomerSessionResponse(token));
    }
}
