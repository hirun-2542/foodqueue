using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// ย้ายโต๊ะ (spec 54, ticket 21): ย้ายทั้ง session Open ไปโต๊ะ active ที่ไม่มี session Open
/// ออเดอร์ รายการปรับยอด บิล และเวลาผูกกับ session ไม่ใช่โต๊ะ การย้ายจึงเปลี่ยนแค่ <c>TableId</c> —
/// token/QR, OpenedAt/EndsAt และทุกแถวลูกตามไปเอง โต๊ะเดิมว่างทันทีเมื่อ commit
/// </summary>
public sealed class SessionMoveService(AppDbContext db, ISessionService sessions, ILiveNotifier notifier)
{
    /// <summary>รอบที่ยังอยู่บนจอครัว — การ์ดเหล่านี้ต้องเปลี่ยนเป็นชื่อโต๊ะใหม่</summary>
    private static readonly OrderStatus[] BoardStatuses = [OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.Ready];

    public static SessionMoveService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        SessionService.From(services),
        services.GetRequiredService<ILiveNotifier>());

    /// <summary>
    /// ลำดับการตรวจ: session ต้องมีอยู่ (<c>SESSION_NOT_FOUND</c>) และยัง Open (<c>SESSION_CLOSED</c>);
    /// โต๊ะปลายทางต้องมีอยู่ (<c>VALIDATION_FAILED</c>) เป็นโต๊ะ active และไม่มี session Open — รวมถึงโต๊ะที่
    /// รอบนี้นั่งอยู่เอง — ไม่งั้น <c>TABLE_OCCUPIED</c>
    /// แถว session ถูกล็อก (<c>FOR UPDATE</c>) ตลอด transaction เหมือนการปรับยอด จึงไม่ย้ายรอบที่เพิ่งถูกชำระ
    /// ส่วนการแย่งโต๊ะว่างตัวเดียวกัน (สองการย้าย หรือย้ายกับเปิดโต๊ะ) partial unique index เป็นผู้ตัดสินสุดท้าย
    /// </summary>
    public async Task<SessionDetailDto> MoveAsync(int sessionId, int tableId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TableSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", ct);

        var session = await sessions.RequireAsync(sessionId, ct);
        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "รอบนี้ปิดไปแล้ว ย้ายโต๊ะไม่ได้");
        }

        var target = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId, ct)
                     ?? throw new AppException(ErrorCodes.ValidationFailed,
                         ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), "ไม่พบโต๊ะที่เลือก");
        if (target.Id == session.TableId)
        {
            throw Occupied($"รอบนี้นั่งอยู่ที่โต๊ะ {target.Name} อยู่แล้ว · เลือกโต๊ะว่างโต๊ะอื่น");
        }

        if (!target.IsActive)
        {
            throw Occupied($"โต๊ะ {target.Name} ถูกปิดใช้งานอยู่ ย้ายไปไม่ได้");
        }

        if (await db.TableSessions.AnyAsync(s => s.TableId == target.Id && s.Status == SessionStatus.Open, ct))
        {
            throw Occupied(OccupiedMessage(target.Name));
        }

        session.TableId = target.Id;
        session.Table = target;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // อีกคำขอหนึ่งเพิ่งเปิดหรือย้ายรอบเข้ามาที่โต๊ะนี้ระหว่างที่เราตรวจ
            throw Occupied(OccupiedMessage(target.Name));
        }

        await transaction.CommitAsync(ct);

        var detail = await sessions.MapDetailAsync(session, ct);
        await notifier.SessionChanged(sessions.MapSummary(session), session.Id);

        // ครัวไม่อยู่ใน group ที่ได้ sessionChanged (owner/session ตาม spec) — การ์ดที่ยังอยู่บนจอครัวจึงถูกประกาศ
        // ซ้ำทาง orderStatusChanged (kitchen+owner เท่านั้น ลูกค้าไม่ได้รับ) ด้วยสถานะเดิมและชื่อโต๊ะใหม่
        foreach (var order in session.Orders.Where(o => BoardStatuses.Contains(o.Status)).OrderBy(o => o.Number))
        {
            await notifier.OrderStatusChanged(order.ToDto());
        }

        return detail;
    }

    private static string OccupiedMessage(string tableName) => $"โต๊ะ {tableName} มีลูกค้าอยู่ ย้ายไม่ได้ · เลือกโต๊ะว่าง";

    private static AppException Occupied(string message) =>
        new(ErrorCodes.TableOccupied, ErrorCodes.StatusFor(ErrorCodes.TableOccupied), message);
}
