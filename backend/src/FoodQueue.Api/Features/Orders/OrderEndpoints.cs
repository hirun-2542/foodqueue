using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Orders;

/// <summary>
/// รายการออเดอร์ของพนักงาน (จอครัว ticket 11 และคิวของเจ้าของร้าน ticket 14) — <c>KitchenAccess</c>
/// เพราะเจ้าของร้านก็ทำงานบนจอครัวด้วยบัญชีของตัวเอง
///
/// <c>GET /api/orders?status=Pending,Preparing,Ready&amp;date=yyyy-MM-dd</c>
/// - <c>status</c> คั่นด้วยจุลภาค ไม่ระบุ = Pending, Preparing, Ready (บอร์ดครัว); Ready คือสถานะสุดท้าย ไม่มีขั้นถัดไป
/// - <c>date</c> ไม่ระบุ = "บอร์ดที่กำลังทำงาน": เฉพาะออเดอร์ของรอบที่ยัง Open ไม่ว่าเปิดมาตั้งแต่เมื่อไร
///   (Ready เป็นสถานะสุดท้าย จึงหลุดจากบอร์ดเมื่อโต๊ะชำระเงินหรือถูกยกเลิก)
///   ระบุ = ประวัติของวันนั้นตามเวลาไทย ทุกรอบไม่ว่าสถานะใด
/// เรียงตาม <c>CreatedAt</c> (เก่าสุดก่อน = คิวของครัว) และคืน <see cref="OrderDto"/> รูปเดียวกับ event realtime
/// </summary>
public sealed class OrderEndpoints : IEndpointModule
{
    private static readonly OrderStatus[] AllStatuses = Enum.GetValues<OrderStatus>();

    private static readonly OrderStatus[] BoardStatuses =
        [OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.Ready];

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/orders")
            .RequireAuthorization("KitchenAccess")
            .MapGet("", ListAsync);
    }

    private static async Task<IResult> ListAsync([FromQuery] string? status, [FromQuery] string? date,
        AppDbContext db, CancellationToken ct)
    {
        var statuses = ParseStatuses(status);
        var day = ParseDate(date);

        var query = db.Orders.AsNoTracking()
            .Include(o => o.TableSession!).ThenInclude(s => s.Table)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .Where(o => statuses.Contains(o.Status));

        if (day is { } wanted)
        {
            // Bkk.StartOfDay คืนเวลาไทย (+07:00) ซึ่งเขียนลง timestamptz ไม่ได้ ต้องเป็น UTC
            var start = Bkk.StartOfDay(wanted).ToUniversalTime();
            var end = start.AddDays(1);
            query = query.Where(o => o.CreatedAt >= start && o.CreatedAt < end);
        }
        else
        {
            query = query.Where(o => o.TableSession!.Status == SessionStatus.Open);
        }

        var orders = await query
            .OrderBy(o => o.CreatedAt)
            .ThenBy(o => o.Id)
            .AsSplitQuery()
            .ToListAsync(ct);

        return Results.Ok(orders.Select(o => o.ToDto()).ToList());
    }

    private static List<OrderStatus> ParseStatuses(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [.. BoardStatuses];
        }

        var parsed = new List<OrderStatus>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // ชื่อเท่านั้น (Enum.TryParse ยอมตัวเลขด้วย ซึ่งไม่ใช่สัญญาของ API)
            var match = AllStatuses.Where(s => string.Equals(s.ToString(), part, StringComparison.OrdinalIgnoreCase))
                .Select(s => (OrderStatus?)s)
                .FirstOrDefault();
            if (match is not { } known)
            {
                throw Invalid($"สถานะที่กรองได้คือ {string.Join(", ", AllStatuses)}");
            }

            parsed.Add(known);
        }

        return parsed.Count == 0 ? [.. BoardStatuses] : parsed.Distinct().ToList();
    }

    private static DateOnly? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", out var parsed)
            ? parsed
            : throw Invalid("วันที่ต้องอยู่ในรูปแบบ yyyy-MM-dd");
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
