using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Customer;

/// <summary>
/// ฝั่งอ่านของลูกค้า: สแกน QR แล้วเห็นข้อมูลรอบของตัวเองกับเมนูที่แพ็กเกจของตนสั่งได้
/// ไม่มี auth — token 43 ตัวอักษรคือสิ่งที่ระบุรอบการนั่ง (spec 101) และทุกคำตอบจำกัดอยู่ที่รอบนั้น
///
/// กฎที่บังคับที่นี่ (spec: "เมนูที่ลูกค้าเห็น/สั่งได้"): Buffet Item ที่
/// <c>MinPackageRank &lt;= rank ของ session</c> + AlaCarte ทุกรายการ เฉพาะที่ <c>IsActive</c>
/// ส่วน <c>IsAvailable = false</c> ยังต้องส่งออกไป (พร้อมราคา) เพื่อให้จอแสดงแถวสีเทา + ป้าย "หมด"
/// การ "สั่งได้หรือไม่" เป็นการตัดสินของ ticket 10 ตอนรับออเดอร์
///
/// ข้อผิดพลาด: token ที่ไม่มี → <c>SESSION_NOT_FOUND</c>; รอบที่ Paid/Voided → <c>SESSION_CLOSED</c>
/// (ทั้งสองอย่างเป็น 404/409 ในสัญญา) รอบที่เลย EndsAt ยังอ่านได้ตามปกติ เพราะจอลูกค้าต้องรู้เวลา
/// จากเซิร์ฟเวอร์จึงจะขึ้น "หมดเวลาสั่งอาหาร" ได้ — การห้ามสั่งเป็นการตัดสินของ ticket 10
/// </summary>
public sealed class CustomerEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/s");

        group.MapGet("/{token}", GetSessionAsync);
        group.MapGet("/{token}/menu", GetMenuAsync);
    }

    private static async Task<IResult> GetSessionAsync(string token, HttpContext http, CancellationToken ct)
    {
        var session = await RequireOpenSessionAsync(token, http, ct);
        var settings = await SettingsAsync(http, ct);

        return Results.Ok(new CustomerSessionDto(
            session.Table?.Name ?? "",
            session.Table?.Zone,
            session.PackageNameSnapshot,
            session.PackageRankSnapshot,
            session.AdultCount,
            session.ChildCount,
            session.OpenedAt,
            session.EndsAt,
            settings.LastOrderWarningMinutes,
            settings.MaxQtyPerItemPerOrder,
            settings.MaxItemsPerOrder,
            DateTimeOffset.UtcNow,
            settings.BannerImagePath,
            settings.Name));
    }

    /// <summary>
    /// เมนูของรอบนี้: หมวดที่ไม่มีอะไรสั่งได้จะไม่ถูกส่งออกไปเป็นแท็บ (แท็บเปล่าทำให้ลูกค้าเสียเวลากด)
    /// ลำดับหมวด/เมนูใช้ SortOrder แล้วตัดด้วย Id เหมือนหน้าจอเจ้าของร้าน
    /// </summary>
    private static async Task<IResult> GetMenuAsync(string token, HttpContext http, CancellationToken ct)
    {
        var session = await RequireOpenSessionAsync(token, http, ct);
        var rank = session.PackageRankSnapshot;

        var categories = await http.RequestServices.GetRequiredService<AppDbContext>().Categories
            .AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.SortOrder,
                Items = c.Items
                    .Where(i => i.IsActive
                                && (i.Kind == MenuItemKind.AlaCarte
                                    || (i.Kind == MenuItemKind.Buffet
                                        && i.MinPackageRank != null
                                        && i.MinPackageRank <= rank)))
                    .OrderBy(i => i.SortOrder).ThenBy(i => i.Id)
                    .Select(i => new CustomerMenuItemDto(i.Id, i.Name, i.ImagePath, i.Kind.ToString(), i.Price,
                        i.MinPackageRank, i.IsAvailable))
                    .ToList(),
            })
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct);

        var visible = categories
            .Where(c => c.Items.Count > 0)
            .Select(c => new CustomerCategoryDto(c.Id, c.Name, c.SortOrder, c.Items))
            .ToList();

        return Results.Ok(new CustomerMenuDto(visible));
    }

    /// <summary>
    /// รอบของ token นี้ หรือปฏิเสธ: ไม่พบ → <c>SESSION_NOT_FOUND</c> (จอ "QR ไม่ถูกต้อง"),
    /// พบแต่ไม่ Open → <c>SESSION_CLOSED</c> (ปิดบิล/ยกเลิกแล้ว token ใช้ไม่ได้ — spec 29, 76)
    /// </summary>
    private static async Task<TableSession> RequireOpenSessionAsync(string token, HttpContext http,
        CancellationToken ct)
    {
        var session = await SessionService.From(http.RequestServices).FindByTokenAsync(token, ct);
        if (session is null)
        {
            throw new AppException(ErrorCodes.SessionNotFound, ErrorCodes.StatusFor(ErrorCodes.SessionNotFound),
                "ไม่พบ QR นี้ · กรุณาแจ้งพนักงานเพื่อรับ QR ใหม่");
        }

        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "โต๊ะนี้ปิดบิลแล้ว · QR นี้ใช้สั่งอาหารไม่ได้อีก");
        }

        return session;
    }

    private static async Task<RestaurantSettings> SettingsAsync(HttpContext http, CancellationToken ct) =>
        await http.RequestServices.GetRequiredService<AppDbContext>().Settings.AsNoTracking()
            .FirstOrDefaultAsync(ct) ?? new RestaurantSettings();
}
