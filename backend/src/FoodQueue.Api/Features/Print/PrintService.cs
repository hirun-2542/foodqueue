using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Print;

/// <summary>เอกสารพิมพ์ 80mm สามแบบ — ไม่มีใบสั่งครัวโดยเจตนา (spec)</summary>
public enum PrintDocument { Qr, Prebill, Receipt }

/// <summary>
/// หนึ่งแถวของหน้าประวัติการชำระ (frame o15): หนึ่งใบเสร็จต่อแถว — บิลรวมเป็นแถวเดียวที่รอบหลัก
/// และนับคนรวมทุกรอบที่ถูกรวมเข้ามา
/// </summary>
public record PaidBillDto(int SessionId, string ReceiptNo, DateTimeOffset PaidAt, string TableName,
    string PackageName, int Guests, string Method, decimal Total);

/// <summary>
/// ข้อมูลพิมพ์ (ใบแจ้งยอด ใบเสร็จ QR slip) และประวัติบิลที่ชำระแล้ว อ่านผ่าน read model ของ
/// <see cref="SessionService"/> — บิลบนกระดาษจึงมาจากตัวคำนวณจุดเดียวกับหน้าจอเจ้าของร้าน
/// </summary>
public sealed class PrintService(AppDbContext db, ISessionService sessions)
{
    public static PrintService From(IServiceProvider services) =>
        new(services.GetRequiredService<AppDbContext>(), SessionService.From(services));

    /// <summary>
    /// <paramref name="document"/> = null คืนข้อมูลโดยไม่ตรวจเงื่อนไขของเอกสาร
    /// ใบเสร็จได้เฉพาะบิลที่ชำระแล้ว และ QR slip ได้เฉพาะรอบที่ยังเปิดอยู่ (QR ของรอบที่ปิดแล้วสั่งไม่ได้)
    /// ทั้งสองกรณีปฏิเสธด้วย <c>SESSION_CLOSED</c> (409) — รหัสสถานะ open/closed ของรอบที่ไม่เอื้อต่อเอกสารนั้น
    /// </summary>
    public async Task<PrintDataDto> GetAsync(int sessionId, PrintDocument? document, CancellationToken ct)
    {
        var session = await sessions.RequireAsync(sessionId, ct);

        // รอบที่ถูกรวมบิลแล้ว (ticket 22): บิลและใบเสร็จอยู่ที่รอบหลัก ส่วน QR slip ยังเป็นของรอบตัวเอง
        if (document is not PrintDocument.Qr && session.MergedIntoSessionId is { } primaryId)
        {
            session = await sessions.RequireAsync(primaryId, ct);
        }

        switch (document)
        {
            case PrintDocument.Receipt when session.Status != SessionStatus.Paid || session.Payment is null:
                throw Closed("บิลนี้ยังไม่ได้ชำระเงิน จึงพิมพ์ใบเสร็จไม่ได้");
            case PrintDocument.Qr when session.Status != SessionStatus.Open:
                throw Closed("ปิดบิลแล้ว QR ของรอบนี้ใช้สั่งอาหารไม่ได้อีก จึงไม่พิมพ์ QR slip");
        }

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RestaurantSettings();

        return new PrintDataDto(
            settings.ToDto(),
            session.Id,
            session.Table?.Name ?? "",
            session.PackageNameSnapshot,
            session.AdultCount,
            session.ChildCount,
            session.OpenedAt,
            session.EndsAt,
            await sessions.ComputeBillAsync(session, ct),
            session.Payment?.ToDto(),
            sessions.QrUrlFor(session));
    }

    /// <summary>บิลที่ชำระในวันนั้นตามเวลาไทย (00:00–24:00 กรุงเทพฯ) เรียงจากเวลาชำระล่าสุด</summary>
    public async Task<IReadOnlyList<PaidBillDto>> ListPaidAsync(DateOnly day, CancellationToken ct)
    {
        // Bkk.StartOfDay คืนเวลาไทย (+07:00) ซึ่ง Npgsql เขียนลง timestamptz ไม่ได้ ต้องเป็น UTC
        var start = Bkk.StartOfDay(day).ToUniversalTime();
        var end = start.AddDays(1);

        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.PaidAt >= start && p.PaidAt < end)
            .OrderByDescending(p => p.PaidAt).ThenByDescending(p => p.Id)
            .Select(p => new
            {
                p.TableSessionId,
                p.ReceiptNo,
                p.PaidAt,
                TableName = p.TableSession!.Table!.Name,
                p.TableSession.PackageNameSnapshot,
                Guests = p.TableSession.AdultCount + p.TableSession.ChildCount,
                p.Method,
                p.Total,
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.TableSessionId).ToList();
        var mergedGuests = await db.TableSessions.AsNoTracking()
            .Where(s => s.MergedIntoSessionId != null && ids.Contains(s.MergedIntoSessionId.Value))
            .GroupBy(s => s.MergedIntoSessionId!.Value)
            .Select(g => new { PrimaryId = g.Key, Guests = g.Sum(s => s.AdultCount + s.ChildCount) })
            .ToDictionaryAsync(g => g.PrimaryId, g => g.Guests, ct);

        return rows.Select(r => new PaidBillDto(
                r.TableSessionId,
                r.ReceiptNo,
                r.PaidAt,
                r.TableName,
                r.PackageNameSnapshot,
                r.Guests + mergedGuests.GetValueOrDefault(r.TableSessionId),
                r.Method.ToString(),
                r.Total))
            .ToList();
    }

    private static AppException Closed(string message) =>
        new(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed), message);
}
