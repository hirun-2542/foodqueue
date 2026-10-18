using System.Globalization;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Orders;

/// <summary>หนึ่งบรรทัดที่แก้: <c>ItemId</c> = แถว OrderItem ของออเดอร์นี้, <c>Quantity</c> = จำนวนใหม่ (0 = นำออก)</summary>
public sealed record OrderEditLine(int ItemId, int Quantity);

/// <summary>
/// แก้ออเดอร์ของเจ้าของร้าน (spec 57, ticket 17): ลดจำนวนหรือนำรายการออกจากออเดอร์ Pending/Preparing ของรอบที่ยัง
/// Open พร้อมเหตุผล — เพิ่มจำนวนหรือเพิ่มรายการไม่ได้; นำออกครบทุกบรรทัด = ยกเลิกทั้งออเดอร์ (เหตุผลเป็นเหตุผลที่ยกเลิก)
/// ส่วนการแก้บางบรรทัดไม่เปลี่ยนสถานะ; บิลมาจาก <c>BillCalculator</c> ตอนอ่านเสมอ จึงลดลงทันทีที่บันทึก
///
/// ล็อกแถวออเดอร์ (<c>FOR UPDATE</c>) ตลอด transaction: ครัวที่กดเลื่อนเป็น Ready พร้อมกันจะรอแถวนี้
/// (conditional update ของ <see cref="OrderWorkflowService"/>) — การแก้จึงเห็นสถานะล่าสุดเสมอ
/// event <c>orderEdited</c> (ครัว เจ้าของร้าน และรอบนั้น) ส่งหลัง commit เท่านั้น
/// </summary>
public sealed class OrderEditService(AppDbContext db, ISessionService sessions, ILiveNotifier notifier)
{
    /// <summary>ตรงกับ HasMaxLength(200) ของ Order.EditReason / Order.CancelReason</summary>
    public const int MaxReasonLength = 200;

    public static OrderEditService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        SessionService.From(services),
        services.GetRequiredService<ILiveNotifier>());

    /// <summary>
    /// แก้ตาม <paramref name="lines"/> (บรรทัดที่ไม่ได้ระบุคงเดิม) แล้วคืนออเดอร์หลังบันทึก; ไม่มีออเดอร์นี้ → null
    /// </summary>
    public async Task<OrderDto?> EditAsync(int orderId, IReadOnlyList<OrderEditLine>? lines, string? reason,
        CancellationToken ct)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0)
        {
            throw Invalid("ต้องระบุเหตุผลในการแก้ไข");
        }

        if (why.Length > MaxReasonLength)
        {
            throw Invalid($"เหตุผลยาวได้ไม่เกิน {MaxReasonLength} ตัวอักษร");
        }

        if (lines is null || lines.Count == 0)
        {
            throw Invalid("เลือกรายการที่จะลดจำนวนหรือนำออก");
        }

        if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count)
        {
            throw Invalid("ระบุรายการเดียวกันซ้ำในคำขอเดียวไม่ได้");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Orders\" WHERE \"Id\" = {orderId} FOR UPDATE", ct);

        var order = await db.Orders
            .Include(o => o.TableSession)
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return null;
        }

        if (order.TableSession!.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "โต๊ะนี้ปิดบิลแล้ว · แก้ออเดอร์ไม่ได้");
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Preparing))
        {
            throw new AppException(ErrorCodes.OrderNotEditable, ErrorCodes.StatusFor(ErrorCodes.OrderNotEditable),
                $"ออเดอร์นี้{StatusPhrase(order.Status)}แล้ว · แก้ไขได้เฉพาะออเดอร์ที่รอรับหรือกำลังทำ");
        }

        var wanted = new Dictionary<OrderItem, int>();
        foreach (var line in lines)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == line.ItemId)
                       ?? throw Invalid("ไม่พบรายการนี้ในออเดอร์ · แก้ไขได้เฉพาะรายการที่สั่งไว้ เพิ่มรายการใหม่ไม่ได้");
            if (line.Quantity < 0)
            {
                throw Invalid($"จำนวนของ “{item.NameSnapshot}” ต้องไม่ติดลบ");
            }

            if (line.Quantity > item.Quantity)
            {
                throw Invalid(
                    $"เพิ่มจำนวน “{item.NameSnapshot}” ไม่ได้ (สั่งไว้ {item.Quantity}) · แก้ไขได้เฉพาะลดจำนวนหรือนำรายการออก");
            }

            wanted[item] = line.Quantity;
        }

        if (wanted.All(w => w.Value == w.Key.Quantity))
        {
            throw Invalid("ไม่มีรายการที่ลดจำนวนหรือนำออก");
        }

        var now = DateTimeOffset.UtcNow;
        var remaining = order.Items.Sum(i => wanted.TryGetValue(i, out var q) ? q : i.Quantity);
        if (remaining == 0)
        {
            // นำออกครบทุกบรรทัด = ยกเลิกทั้งออเดอร์ — เก็บรายการไว้ให้ประวัติเห็นว่ายกเลิกอะไร (เหมือนการยกเลิกปกติ)
            order.Status = OrderStatus.Cancelled;
            order.CancelReason = why;
        }
        else
        {
            foreach (var (item, quantity) in wanted)
            {
                if (quantity == 0)
                {
                    order.Items.Remove(item);
                    db.OrderItems.Remove(item);
                }
                else
                {
                    item.Quantity = quantity;
                }
            }

            order.EditReason = why;
        }

        order.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // บิลของโต๊ะ (บิลรวมถ้ารอบนี้ถูกรวมเข้ารอบหลัก) ต้องไม่ติดลบ — ส่วนลดที่ใส่ไว้อาจมากกว่ายอดที่เหลือ
        var billSessionId = order.TableSession.MergedIntoSessionId ?? order.TableSessionId;
        var bill = await sessions.ComputeBillAsync(await sessions.RequireAsync(billSessionId, ct), ct);
        if (bill.Subtotal < 0m)
        {
            throw new AppException(ErrorCodes.NegativeSubtotal, ErrorCodes.StatusFor(ErrorCodes.NegativeSubtotal),
                $"นำรายการออกแล้วยอดก่อนค่าบริการจะติดลบ (-฿{(-bill.Subtotal).ToString("N2", CultureInfo.InvariantCulture)}) · ลบส่วนลดในบิลก่อน");
        }

        await transaction.CommitAsync(ct);

        var saved = await db.Orders.AsNoTracking()
            .Include(o => o.TableSession!).ThenInclude(s => s.Table)
            .Include(o => o.Items.OrderBy(i => i.Id))
            .AsSplitQuery()
            .SingleAsync(o => o.Id == orderId, ct);
        var dto = saved.ToDto();
        // รายการที่ลูกค้าเห็นเปลี่ยน: ครัว เจ้าของร้าน และรอบนั้น (กับรอบหลักถ้าถูกรวมบิล)
        await notifier.OrderEdited(dto, saved.TableSessionId, saved.TableSession!.MergedIntoSessionId);
        return dto;
    }

    private static string StatusPhrase(OrderStatus status) => status switch
    {
        OrderStatus.Ready => "พร้อมเสิร์ฟ",
        OrderStatus.Cancelled => "ถูกยกเลิก",
        _ => status.ToString(),
    };

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
