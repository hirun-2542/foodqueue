using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Overview;

/// <summary>
/// ภาพรวมผังโต๊ะ: ทุกโต๊ะที่ใช้งานอยู่พร้อมรอบการนั่งที่เปิดอยู่ของโต๊ะนั้น
/// ครัวเข้าได้ด้วย (<c>KitchenAccess</c>) เพราะครัวต้องรู้ว่าโต๊ะไหนมีลูกค้าและมีออเดอร์ค้าง
/// — ไม่มีข้อมูลการเงินที่ครัวไม่ควรเห็น นอกจากยอดบิลสะสมซึ่งเป็นตัวเลขเดียวกับที่หน้าจอเจ้าของเห็น
///
/// อยู่ที่โมดูลของตัวเอง (ไม่ใช่ <c>TablesEndpoints</c>) เพราะไฟล์นั้นเป็นงานแก้ไขผังของ ticket 06
/// และสัญญาตรึงเส้นทางนี้ไว้ว่า <c>GET /api/tables/overview</c>
///
/// ตัวเลขทุกตัวที่ขึ้นกับเวลามาจากการคำนวณที่เซิร์ฟเวอร์ ณ เวลาที่เรียก (<c>minutesUsed</c>) หรือจาก
/// ข้อมูลที่เก็บไว้ (<c>openedAt</c>/<c>endsAt</c>) — ไคลเอนต์ไม่ต้องเรียกนาฬิกาของตัวเองตอนเรนเดอร์
/// จึงไม่มีตัวนับถอยหลังบนจอไหนเลย
/// </summary>
public sealed class OverviewEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/tables")
            .RequireAuthorization("KitchenAccess")
            .MapGet("/overview", GetAsync);
    }

    private static async Task<IResult> GetAsync(AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // เฉพาะโต๊ะที่ยังใช้งานอยู่: โต๊ะที่ปิดใช้งานไม่ปรากฏบนผังและเปิดรอบไม่ได้อยู่แล้ว
        var tables = await db.Tables.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync(ct);

        var tableIds = tables.Select(t => t.Id).ToList();
        // รอบที่เลย EndsAt แล้วยัง Status Open จึงยังอยู่ในรายการนี้ (การเลยเวลาแค่ปิดการสั่งใหม่)
        var openSessions = await db.TableSessions.AsNoTracking()
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Adjustments)
            .Where(s => s.Status == SessionStatus.Open && tableIds.Contains(s.TableId))
            .ToListAsync(ct);
        var openByTable = openSessions.ToDictionary(s => s.TableId);

        var sessions = SessionService.From(http.RequestServices);

        var overview = new List<TableOverviewDto>(tables.Count);
        foreach (var table in tables)
        {
            if (!openByTable.TryGetValue(table.Id, out var session))
            {
                overview.Add(new TableOverviewDto(table.Id, table.Name, table.Zone, table.Seats, table.IsActive,
                    null, null, null, null, null, null, null, null, null));
                continue;
            }

            // บิลผ่าน seam เดียวกับทุกหน้าจอ (ticket 07) จึงรวม session ที่ถูกรวมเข้าบิลนี้ให้ด้วย
            var bill = await sessions.ComputeBillAsync(session, ct);

            overview.Add(new TableOverviewDto(
                table.Id,
                table.Name,
                table.Zone,
                table.Seats,
                table.IsActive,
                session.Id,
                session.PackageNameSnapshot,
                session.AdultCount,
                session.ChildCount,
                session.OpenedAt,
                session.EndsAt,
                MinutesUsed(session.OpenedAt, now),
                WaitingOrders(session),
                bill.Total));
        }

        return Results.Ok(overview);
    }

    /// <summary>
    /// ออเดอร์ที่ครัวยังไม่ได้ทำ: Ready เป็นปลายทางแล้ว (พนักงานนำไปเสิร์ฟเอง) และ Cancelled ไม่ถูกคิดเงิน
    /// นับเฉพาะออเดอร์ของรอบนี้ ไม่รวมของ session ที่ถูกรวมเข้าบิล (คนละโต๊ะ คนละงานของครัว)
    /// </summary>
    private static int WaitingOrders(TableSession session) =>
        session.Orders.Count(o => o.Status is not (OrderStatus.Ready or OrderStatus.Cancelled));

    /// <summary>นาทีที่ใช้ไปคิดจากนาฬิกาเซิร์ฟเวอร์ ณ เวลาที่เรียก และไม่ติดลบเมื่อนาฬิกาเพี้ยน</summary>
    private static int MinutesUsed(DateTimeOffset openedAt, DateTimeOffset now) =>
        (int)Math.Max(0, Math.Floor((now - openedAt).TotalMinutes));
}
