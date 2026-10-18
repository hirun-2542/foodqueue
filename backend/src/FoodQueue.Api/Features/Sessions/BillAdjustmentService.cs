using System.Globalization;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// รายการปรับยอดของบิล (spec 60–62): เจ้าของร้านบวก/ลบยอดพร้อมเหตุผล และลบรายการที่ใส่ผิด
/// ทำได้เฉพาะ session Open (เลย EndsAt แล้วก็ยังปรับได้ — เวลาปิดแค่การสั่ง) และห้ามทำให้ subtotal ติดลบ
/// ตัวเลขบิลมาจาก <see cref="ISessionService.ComputeBillAsync"/> (คือ <c>BillCalculator</c> จุดเดียว)
/// แล้วส่ง <c>sessionChanged</c> หลังบันทึกสำเร็จเท่านั้น
/// </summary>
public sealed class BillAdjustmentService(AppDbContext db, ISessionService sessions, ILiveNotifier notifier)
{
    /// <summary>ความยาวสูงสุดของเหตุผล = คอลัมน์ <c>BillAdjustment.Description</c></summary>
    public const int DescriptionMaxLength = 100;

    /// <summary>ยอดปรับต่อรายการไม่เกินนี้ทั้งบวกและลบ — กันพิมพ์ศูนย์เกินโดยไม่ตั้งใจ</summary>
    public const decimal MaxAmount = 100_000m;

    public static BillAdjustmentService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        SessionService.From(services),
        services.GetRequiredService<ILiveNotifier>());

    /// <summary>เพิ่มรายการปรับยอดแล้วคืนรายละเอียด session พร้อมบิลใหม่</summary>
    public async Task<SessionDetailDto> AddAsync(int sessionId, string? description, decimal? amount,
        CancellationToken ct)
    {
        var why = (description ?? "").Trim();
        if (why.Length == 0 || why.Length > DescriptionMaxLength)
        {
            throw Invalid($"ระบุเหตุผลของการปรับยอด (1–{DescriptionMaxLength} ตัวอักษร)");
        }

        if (amount is not { } value || value == 0m)
        {
            throw Invalid("ระบุยอดที่ปรับ (บวกเพื่อเพิ่มยอด ลบเพื่อลดยอด) และต้องไม่เป็น 0");
        }

        if (value != decimal.Round(value, 2))
        {
            throw Invalid("ยอดที่ปรับใส่ทศนิยมได้ไม่เกิน 2 ตำแหน่ง");
        }

        if (Math.Abs(value) > MaxAmount)
        {
            throw Invalid($"ยอดที่ปรับต่อรายการต้องไม่เกิน ฿{MaxAmount:N0}");
        }

        return await ChangeAsync(sessionId, session =>
        {
            var adjustment = new BillAdjustment
            {
                TableSessionId = session.Id,
                Description = why,
                Amount = value,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            session.Adjustments.Add(adjustment);
            db.BillAdjustments.Add(adjustment);
        }, ct);
    }

    /// <summary>ลบรายการปรับยอดของ session นี้ (รายการของ session อื่นถือว่าไม่พบ) แล้วคืนรายละเอียดพร้อมบิลใหม่</summary>
    public Task<SessionDetailDto> RemoveAsync(int sessionId, int adjustmentId, CancellationToken ct) =>
        ChangeAsync(sessionId, session =>
        {
            var adjustment = session.Adjustments.FirstOrDefault(a => a.Id == adjustmentId)
                             ?? throw new AppException(ErrorCodes.ValidationFailed, StatusCodes.Status404NotFound,
                                 "ไม่พบรายการปรับยอดนี้ในบิลของโต๊ะนี้ (อาจถูกลบไปแล้ว)");
            session.Adjustments.Remove(adjustment);
            db.BillAdjustments.Remove(adjustment);
        }, ct);

    /// <summary>
    /// ล็อกแถว session (<c>FOR UPDATE</c>) ตลอด transaction: การปรับยอดสองเครื่องพร้อมกันจึงถูกตรวจ
    /// subtotal ทีละคำขอ ไม่ใช่ต่างคนต่างผ่านแล้วรวมกันติดลบ และการชำระเงินที่ปิดบิลไปก่อนจะถูกเห็นที่นี่
    /// </summary>
    private async Task<SessionDetailDto> ChangeAsync(int sessionId, Action<TableSession> change, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TableSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", ct);

        var session = await sessions.RequireAsync(sessionId, ct);
        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "บิลของโต๊ะนี้ปิดแล้ว ปรับยอดไม่ได้");
        }

        change(session);

        var bill = await sessions.ComputeBillAsync(session, ct);
        if (bill.Subtotal < 0m)
        {
            throw new AppException(ErrorCodes.NegativeSubtotal, ErrorCodes.StatusFor(ErrorCodes.NegativeSubtotal),
                $"ปรับยอดแล้วยอดก่อนค่าบริการจะติดลบ (-฿{(-bill.Subtotal).ToString("N2", CultureInfo.InvariantCulture)}) · ลดยอดได้ไม่เกินยอดของบิล");
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var detail = await sessions.MapDetailAsync(session, ct);
        await notifier.SessionChanged(sessions.MapSummary(session), session.Id);
        return detail;
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
