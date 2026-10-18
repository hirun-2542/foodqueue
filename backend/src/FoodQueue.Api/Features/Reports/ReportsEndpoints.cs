using FoodQueue.Api.Billing;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Reports;

/// <summary>
/// รายงานยอดขายของเจ้าของร้าน: <c>GET /api/reports/summary?range=today|7d|30d</c> (OwnerOnly)
/// หน้า <c>/owner/reports</c> และแถบ KPI ของ <c>/owner</c> อ่านจากเส้นทางนี้เส้นเดียว ตัวเลขจึงไม่ขัดกัน
///
/// กติกา (spec user story 91–99 และ Testing Decisions):
/// <list type="bullet">
/// <item>หนึ่งบิล = หนึ่งแถว <see cref="Payment"/> ของ session ที่ <c>Paid</c>; ยอดคือ <c>Payment.Total</c>
/// ที่ตรึงไว้ตอนชำระ (บิลที่ชำระแล้วไม่ถูกคำนวณใหม่)</item>
/// <item>วันขายคือวันตามเวลาไทยของ <c>PaidAt</c>: <c>today</c> เริ่ม 00:00 ไทย, <c>7d</c>/<c>30d</c> รวมวันนี้</item>
/// <item>บิลรวม (ticket 22) นับเป็นหนึ่งบิล: ลูกค้ารวมทุก session ที่ถูกรวมเข้ามา และแพ็กเกจมาจาก session หลัก
/// (สมมติฐานที่ spec ตั้งไว้)</item>
/// <item>Top 10 นับทุกชนิดเมนูจากออเดอร์ที่ไม่ถูกยกเลิกของบิลเหล่านั้น (รวม session ที่ถูกรวมเข้าบิล)</item>
/// <item><c>avgPerGuest</c> = ยอดขาย ÷ ลูกค้า และเป็น 0 เมื่อไม่มีลูกค้า</item>
/// </list>
/// </summary>
public sealed class ReportsEndpoints : IEndpointModule
{
    private static readonly Dictionary<string, int> RangeDays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["today"] = 1,
        ["7d"] = 7,
        ["30d"] = 30,
    };

    public void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/api/reports/summary", SummaryAsync).RequireAuthorization("OwnerOnly");

    private static async Task<IResult> SummaryAsync([FromQuery] string? range, AppDbContext db, CancellationToken ct)
    {
        // ไม่ระบุช่วง = วันนี้ (ค่าเดียวกับที่หน้าภาพรวมใช้)
        var key = string.IsNullOrWhiteSpace(range) ? "today" : range.Trim();
        if (!RangeDays.TryGetValue(key, out var days))
        {
            throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "ช่วงรายงานต้องเป็น today, 7d หรือ 30d");
        }

        var today = Bkk.DateOf(DateTimeOffset.UtcNow);
        var firstDay = today.AddDays(1 - days);
        // Bkk.StartOfDay คืนเวลาไทย (+07:00) ซึ่งเขียนลง timestamptz ไม่ได้ ต้องเป็น UTC
        var start = Bkk.StartOfDay(firstDay).ToUniversalTime();
        var end = Bkk.StartOfDay(today.AddDays(1)).ToUniversalTime();

        var bills = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < end && p.TableSession!.Status == SessionStatus.Paid)
            .Select(p => new
            {
                p.TableSessionId,
                p.Method,
                p.Total,
                p.PaidAt,
                Package = p.TableSession!.PackageNameSnapshot,
                Rank = p.TableSession.PackageRankSnapshot,
                Guests = p.TableSession.AdultCount + p.TableSession.ChildCount,
            })
            .ToListAsync(ct);

        var billIds = bills.Select(b => b.TableSessionId).ToList();

        // session ที่ถูกรวมเข้าบิลเหล่านี้: ลูกค้าและออเดอร์ของมันเป็นของบิลหลัก
        var merged = await db.TableSessions.AsNoTracking()
            .Where(s => s.MergedIntoSessionId != null && billIds.Contains(s.MergedIntoSessionId.Value))
            .Select(s => new { s.Id, PrimaryId = s.MergedIntoSessionId!.Value, Guests = s.AdultCount + s.ChildCount })
            .ToListAsync(ct);
        var mergedGuests = merged.GroupBy(m => m.PrimaryId).ToDictionary(g => g.Key, g => g.Sum(m => m.Guests));

        var rows = bills
            .Select(b => new
            {
                b.Method,
                b.Total,
                Local = Bkk.ToLocal(b.PaidAt),
                b.Package,
                b.Rank,
                Guests = b.Guests + mergedGuests.GetValueOrDefault(b.TableSessionId),
            })
            .ToList();

        var revenue = rows.Sum(r => r.Total);
        var guests = rows.Sum(r => r.Guests);
        var avgPerGuest = guests == 0 ? 0m : BillCalculator.Round2(revenue / guests);

        var packageMix = rows
            .GroupBy(r => r.Package)
            .Select(g => new { Rank = g.Min(r => r.Rank), Row = new PackageMixRow(g.Key, g.Count(), g.Sum(r => r.Guests), g.Sum(r => r.Total)) })
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Row.Name, StringComparer.Ordinal)
            .Select(x => x.Row)
            .ToList();

        var paymentMix = rows
            .GroupBy(r => r.Method)
            .Select(g => new PaymentMixRow(g.Key.ToString(), g.Count(), g.Sum(r => r.Total)))
            .OrderByDescending(r => r.Total)
            .ThenBy(r => r.Method, StringComparer.Ordinal)
            .ToList();

        var byHour = rows.GroupBy(r => r.Local.Hour).ToDictionary(g => g.Key, g => g.Sum(r => r.Total));
        var hourly = Enumerable.Range(0, 24)
            .Select(h => new HourlyRow(h, byHour.GetValueOrDefault(h)))
            .ToList();

        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(r.Local.DateTime))
            .ToDictionary(g => g.Key, g => (Revenue: g.Sum(r => r.Total), Bills: g.Count()));
        var daily = Enumerable.Range(0, days)
            .Select(i => firstDay.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var v) ? new DailyRow(d, v.Revenue, v.Bills) : new DailyRow(d, 0m, 0))
            .ToList();

        var topItems = await TopItemsAsync(db, billIds.Concat(merged.Select(m => m.Id)).ToList(), ct);

        // "เทียบเมื่อวานเวลาเดียวกัน" ของแถบ KPI หน้าภาพรวม: ยอดของเมื่อวานถึงเวลาของวันนี้
        // มีความหมายเฉพาะช่วง "วันนี้" — ช่วง 7/30 วันเทียบแบบนี้ไม่ได้จึงเป็น null
        var previous = days == 1 ? await PreviousRevenueAsync(db, today, ct) : null;

        return Results.Ok(new ReportSummaryDto(revenue, rows.Count, guests, avgPerGuest, previous, packageMix,
            topItems, hourly, paymentMix, daily));
    }

    /// <summary>
    /// ยอดขายของเมื่อวานตั้งแต่ 00:00 ไทยถึงเวลาเดียวกันกับตอนนี้ หรือ null เมื่อยังไม่มีอะไรให้เทียบ
    /// (ยังไม่มีบิลเมื่อวาน/ยอดเมื่อวานเป็น 0 — ผู้เรียกแสดงบรรทัดเทียบไม่ได้ ไม่ใช่แสดง +100%)
    /// </summary>
    private static async Task<decimal?> PreviousRevenueAsync(AppDbContext db, DateOnly today, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = Bkk.StartOfDay(today).ToUniversalTime();
        if (now <= todayStart)
        {
            return null;
        }

        var yesterdayStart = Bkk.StartOfDay(today.AddDays(-1)).ToUniversalTime();
        var yesterdayCutoff = yesterdayStart + (now - todayStart);

        var total = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= yesterdayStart && p.PaidAt < yesterdayCutoff
                && p.TableSession!.Status == SessionStatus.Paid)
            .SumAsync(p => (decimal?)p.Total, ct);

        return total is > 0 ? total : null;
    }

    /// <summary>10 เมนูที่เสิร์ฟมากที่สุด (ทั้ง Buffet และ AlaCarte) จากออเดอร์ที่ไม่ถูกยกเลิกของ session ที่ให้มา</summary>
    private static async Task<IReadOnlyList<TopItemRow>> TopItemsAsync(AppDbContext db, List<int> sessionIds,
        CancellationToken ct)
    {
        if (sessionIds.Count == 0)
        {
            return [];
        }

        var totals = await db.OrderItems.AsNoTracking()
            .Where(i => sessionIds.Contains(i.Order!.TableSessionId) && i.Order.Status != OrderStatus.Cancelled)
            .GroupBy(i => i.NameSnapshot)
            .Select(g => new { Name = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToListAsync(ct);

        return totals
            .Where(t => t.Quantity > 0)
            .OrderByDescending(t => t.Quantity)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Take(10)
            .Select(t => new TopItemRow(t.Name, t.Quantity))
            .ToList();
    }
}
