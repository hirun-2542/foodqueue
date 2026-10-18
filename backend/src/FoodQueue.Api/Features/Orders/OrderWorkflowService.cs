using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Orders;

/// <summary>
/// เวิร์กโฟลว์ของออเดอร์หลังส่งแล้ว: เลื่อนสถานะ Pending → Preparing → Ready (Ready คือขั้นสุดท้าย ไม่มี Served)
/// และยกเลิกพร้อมเหตุผล — จอครัว คิวของเจ้าของร้าน (ticket 14) และ Demo Bot (ticket 25) เรียกที่นี่ที่เดียว
/// </summary>
public interface IOrderWorkflowService
{
    /// <summary>
    /// เลื่อนออเดอร์ไป <paramref name="target"/> ได้ขั้นเดียวจากสถานะก่อนหน้าที่คาดไว้ (Preparing ← Pending,
    /// Ready ← Preparing) ด้วย conditional update; ถ้าแถวไม่ได้อยู่ในสถานะนั้นแล้ว (กดซ้ำ ข้ามขั้น อีกเครื่องเลื่อนไปก่อน)
    /// → <c>INVALID_STATUS_TRANSITION</c>. คืน null เมื่อไม่มีออเดอร์นี้
    /// </summary>
    Task<OrderDto?> ChangeStatusAsync(int orderId, OrderStatus target, CancellationToken ct);

    /// <summary>
    /// ยกเลิกออเดอร์ที่ Pending/Preparing/Ready ของรอบที่ยัง Open พร้อมเหตุผล 1–200 ตัวอักษร (ตัดช่องว่างหัวท้าย)
    /// ออเดอร์ที่ Cancelled แล้ว → <c>INVALID_STATUS_TRANSITION</c>; รอบที่ปิดบิลแล้ว → <c>SESSION_CLOSED</c>.
    /// คืน null เมื่อไม่มีออเดอร์นี้
    /// </summary>
    Task<OrderDto?> CancelAsync(int orderId, string? reason, CancellationToken ct);
}

/// <summary>
/// ตัวจริงของ <see cref="IOrderWorkflowService"/> — ไม่ลงทะเบียนใน DI เพราะ Program.cs ถูก freeze:
/// ผู้เรียกสร้างผ่าน <see cref="From"/> เหมือน <see cref="OrderService"/>
///
/// ทุกการเขียนเป็น UPDATE คำสั่งเดียวที่มีเงื่อนไขสถานะอยู่ใน WHERE: ถ้าสองเครื่องกดพร้อมกัน Postgres ให้คำสั่งที่สอง
/// รอแถวที่ถูกล็อกแล้วตรวจเงื่อนไขกับค่าที่เพิ่งบันทึก จึงได้ 0 แถว — ไม่มีการอ่านแล้วค่อยเขียนที่ชนกันได้
/// event ถูกส่งหลังคำสั่งนั้นสำเร็จเท่านั้น
/// </summary>
public sealed class OrderWorkflowService(AppDbContext db, ILiveNotifier notifier) : IOrderWorkflowService
{
    /// <summary>ตรงกับ HasMaxLength(200) ของ Order.CancelReason</summary>
    public const int MaxReasonLength = 200;

    public static OrderWorkflowService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        services.GetRequiredService<ILiveNotifier>());

    public async Task<OrderDto?> ChangeStatusAsync(int orderId, OrderStatus target, CancellationToken ct)
    {
        var expected = target switch
        {
            OrderStatus.Preparing => OrderStatus.Pending,
            OrderStatus.Ready => OrderStatus.Preparing,
            _ => throw InvalidTransition($"เปลี่ยนเป็น “{Label(target)}” ไม่ได้ · ครัวเลื่อนได้แค่ เริ่มทำ และ พร้อมเสิร์ฟ"),
        };

        var now = DateTimeOffset.UtcNow;
        var updated = await db.Orders
            .Where(o => o.Id == orderId && o.Status == expected)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, target)
                .SetProperty(o => o.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var current = await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => (OrderStatus?)o.Status)
                .FirstOrDefaultAsync(ct);
            if (current is null)
            {
                return null;
            }

            throw InvalidTransition(
                $"ออเดอร์นี้เป็น “{Label(current.Value)}” แล้ว · อาจถูกอัปเดตจากเครื่องอื่น เปลี่ยนเป็น “{Label(target)}” ไม่ได้");
        }

        var dto = (await LoadAsync(orderId, ct)).ToDto();
        // ลูกค้าไม่เห็นสถานะรายรอบ: ครัวและเจ้าของร้านเท่านั้น (LiveNotifier)
        await notifier.OrderStatusChanged(dto);
        return dto;
    }

    public async Task<OrderDto?> CancelAsync(int orderId, string? reason, CancellationToken ct)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0)
        {
            throw Invalid("ต้องระบุเหตุผลที่ยกเลิก");
        }

        if (why.Length > MaxReasonLength)
        {
            throw Invalid($"เหตุผลยาวได้ไม่เกิน {MaxReasonLength} ตัวอักษร");
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await db.Orders
            .Where(o => o.Id == orderId
                && o.Status != OrderStatus.Cancelled
                && o.TableSession!.Status == SessionStatus.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Cancelled)
                .SetProperty(o => o.CancelReason, why)
                .SetProperty(o => o.UpdatedAt, now), ct);

        if (updated == 0)
        {
            var found = await db.Orders.AsNoTracking()
                .Where(o => o.Id == orderId)
                .Select(o => new { o.Status })
                .FirstOrDefaultAsync(ct);
            if (found is null)
            {
                return null;
            }

            if (found.Status == OrderStatus.Cancelled)
            {
                throw InvalidTransition("ออเดอร์นี้ถูกยกเลิกไปแล้ว · อาจถูกอัปเดตจากเครื่องอื่น");
            }

            // รอบที่ชำระแล้วเหลือแต่ Ready: ยกเลิกตอนนี้จะทำให้บิลไม่ตรงกับใบเสร็จ
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "โต๊ะนี้ปิดบิลแล้ว · ยกเลิกออเดอร์ไม่ได้");
        }

        var order = await LoadAsync(orderId, ct);
        var dto = order.ToDto();
        // การยกเลิกเปลี่ยนรายการที่ลูกค้าเห็น: ครัว เจ้าของร้าน และรอบนั้น (กับรอบหลักถ้าถูกรวมบิล)
        await notifier.OrderEdited(dto, order.TableSessionId, order.TableSession!.MergedIntoSessionId);
        return dto;
    }

    /// <summary>ออเดอร์หลังบันทึกพร้อมรอบ/โต๊ะ/รายการ — ToDto ให้รูปเดียวกับรายการของจอครัวและ event realtime</summary>
    private async Task<Order> LoadAsync(int orderId, CancellationToken ct) =>
        await db.Orders.AsNoTracking()
            .Include(o => o.TableSession!).ThenInclude(s => s.Table)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .AsSplitQuery()
            .SingleAsync(o => o.Id == orderId, ct);

    /// <summary>ชื่อสถานะบนจอของพนักงาน (ตรงกับหัวคอลัมน์ของจอครัว)</summary>
    private static string Label(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "รอรับ",
        OrderStatus.Preparing => "กำลังทำ",
        OrderStatus.Ready => "พร้อมเสิร์ฟ",
        _ => "ยกเลิกแล้ว",
    };

    private static AppException InvalidTransition(string message) =>
        new(ErrorCodes.InvalidStatusTransition, ErrorCodes.StatusFor(ErrorCodes.InvalidStatusTransition), message);

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
