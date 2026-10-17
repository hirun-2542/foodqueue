using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Orders;

/// <summary>
/// หนึ่งบรรทัดที่ลูกค้าส่งมา: เมนู จำนวน และหมายเหตุอิสระต่อรายการ (≤100 ตัวอักษร ตามคอลัมน์ของตาราง)
/// บรรทัดที่ซ้ำเมนูเดียวกันแต่หมายเหตุต่างกันเป็นคนละบรรทัด แต่จำนวนของเมนูเดียวกันรวมกันต้องไม่เกินลิมิต
/// </summary>
public sealed record OrderLineRequest(int MenuItemId, int Quantity, string? Note);

/// <summary>
/// เส้นทางเขียนออเดอร์จุดเดียวของระบบ (spec: "เลขรอบออเดอร์ = max+1 ใน transaction เดียว") —
/// endpoint ของลูกค้าและ Demo Bot (ticket 25) เรียกที่นี่เพื่อให้กฎและ event เหมือนกันทุกทาง
/// เรียงการตรวจตามที่ spec กำหนดเป๊ะ และส่ง <c>orderCreated</c> หลังบันทึกสำเร็จเท่านั้น
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// session ของ token นี้ หรือ <c>SESSION_NOT_FOUND</c> (404) เมื่อไม่รู้จัก token
    /// และ <c>SESSION_CLOSED</c> (409) เมื่อรอบนั้น Paid/Voided แล้ว (การตรวจลำดับแรกของ spec)
    /// </summary>
    Task<TableSession> RequireByTokenAsync(string token, CancellationToken ct);

    /// <summary>
    /// สร้างออเดอร์หนึ่งรอบจาก session ที่โหลดมาแล้ว — ลำดับการตรวจในเมธอดนี้คือสัญญาของระบบ
    /// </summary>
    Task<OrderDto> CreateAsync(TableSession session, IReadOnlyList<OrderLineRequest>? lines, CancellationToken ct);
}

/// <summary>
/// ตัวจริงของ <see cref="IOrderService"/> — ข้อความไทยทุกข้อความออกจากที่นี่ที่เดียว
/// (หน้าลูกค้าแสดงรหัส <c>code</c> หรือข้อความนี้ได้ตรง ๆ) และไม่ลงทะเบียนใน DI เพราะ Program.cs ถูก freeze:
/// ผู้เรียกสร้างผ่าน <see cref="From"/> เหมือน <c>SessionService</c>
/// </summary>
public sealed class OrderService(AppDbContext db, ILiveNotifier notifier) : IOrderService
{
    /// <summary>ขอบเขตหมายเหตุต่อรายการ — ตรงกับ HasMaxLength(100) ของ OrderItem.Note</summary>
    public const int MaxNoteLength = 100;

    public static OrderService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        services.GetRequiredService<ILiveNotifier>());

    public async Task<TableSession> RequireByTokenAsync(string token, CancellationToken ct)
    {
        var session = await db.TableSessions
            .Include(s => s.Table)
            .FirstOrDefaultAsync(s => s.Token == token, ct)
            ?? throw new AppException(ErrorCodes.SessionNotFound, ErrorCodes.StatusFor(ErrorCodes.SessionNotFound),
                "ไม่พบ QR นี้ · กรุณาแจ้งพนักงานเพื่อรับ QR ใหม่");

        if (session.Status != SessionStatus.Open)
        {
            throw Closed();
        }

        return session;
    }

    /// <summary>
    /// ลำดับการตรวจ (spec "กฎธุรกิจที่ต้องตรงตามนี้") — แต่ละข้อมีรหัสของตัวเอง:
    /// 1. session ยัง Open · 2. ยังไม่เลย <c>EndsAt</c> ตามเวลาของเซิร์ฟเวอร์ · 3. รายการไม่ว่าง ·
    /// 4. จำนวนต่อเมนู (รวมบรรทัดซ้ำ) · 5. จำนวนรวม · 6. เมนูมีอยู่และ active · 7. <c>IsAvailable</c> ·
    /// 8. เมนูบุฟเฟ่ต์อยู่ในสิทธิ์แพ็กเกจ; แล้วจึงบันทึกพร้อม snapshot และส่ง event
    /// </summary>
    public async Task<OrderDto> CreateAsync(TableSession session, IReadOnlyList<OrderLineRequest>? lines,
        CancellationToken ct)
    {
        if (session.Status != SessionStatus.Open)
        {
            throw Closed();
        }

        // เวลาที่ใช้ตัดสินคือเวลาของเซิร์ฟเวอร์เสมอ (ลูกค้าได้ offset ไปชดเชยนาฬิกามือถือ)
        var now = DateTimeOffset.UtcNow;
        if (now > session.EndsAt)
        {
            throw new AppException(ErrorCodes.SessionExpired, ErrorCodes.StatusFor(ErrorCodes.SessionExpired),
                "หมดเวลาสั่งอาหาร · กรุณาติดต่อพนักงาน");
        }

        // บรรทัดที่จำนวนต่ำกว่า 1 ไม่มีอะไรจะสั่ง จึงไม่นับเป็นรายการ
        var wanted = (lines ?? []).Where(l => l.Quantity > 0).ToList();
        if (wanted.Count == 0)
        {
            throw new AppException(ErrorCodes.EmptyOrder, ErrorCodes.StatusFor(ErrorCodes.EmptyOrder),
                "ยังไม่มีรายการในตะกร้า · เลือกเมนูก่อนส่งออเดอร์");
        }

        var settings = await SettingsAsync(ct);

        foreach (var same in wanted.GroupBy(l => l.MenuItemId))
        {
            if (same.Sum(l => l.Quantity) > settings.MaxQtyPerItemPerOrder)
            {
                throw new AppException(ErrorCodes.QtyLimitExceeded, ErrorCodes.StatusFor(ErrorCodes.QtyLimitExceeded),
                    $"สั่งได้สูงสุด {settings.MaxQtyPerItemPerOrder} ต่อเมนูต่อรอบ");
            }
        }

        if (wanted.Sum(l => l.Quantity) > settings.MaxItemsPerOrder)
        {
            throw new AppException(ErrorCodes.OrderItemsLimitExceeded,
                ErrorCodes.StatusFor(ErrorCodes.OrderItemsLimitExceeded),
                $"สั่งได้สูงสุด {settings.MaxItemsPerOrder} รายการต่อรอบ");
        }

        var ids = wanted.Select(l => l.MenuItemId).Distinct().ToList();
        var menu = await db.MenuItems.Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);

        foreach (var line in wanted)
        {
            if (!menu.TryGetValue(line.MenuItemId, out var item) || !item.IsActive)
            {
                throw new AppException(ErrorCodes.ItemNotFound, ErrorCodes.StatusFor(ErrorCodes.ItemNotFound),
                    "ไม่พบเมนูนี้แล้ว · กรุณาเลือกเมนูใหม่");
            }
        }

        foreach (var line in wanted)
        {
            var item = menu[line.MenuItemId];
            if (!item.IsAvailable)
            {
                throw new AppException(ErrorCodes.ItemUnavailable, ErrorCodes.StatusFor(ErrorCodes.ItemUnavailable),
                    $"“{item.Name}” หมดชั่วคราว · เอาออกจากตะกร้าก่อนส่งออเดอร์");
            }
        }

        foreach (var line in wanted)
        {
            var item = menu[line.MenuItemId];
            if (item.Kind == MenuItemKind.Buffet && (item.MinPackageRank ?? 0) > session.PackageRankSnapshot)
            {
                throw new AppException(ErrorCodes.ItemNotInPackage,
                    ErrorCodes.StatusFor(ErrorCodes.ItemNotInPackage),
                    $"“{item.Name}” ไม่อยู่ในแพ็กเกจ {session.PackageNameSnapshot} · สั่งไม่ได้");
            }
        }

        // ไม่อยู่ในลำดับการตรวจของ spec แต่ต้องกันก่อนถึงฐานข้อมูล: หมายเหตุยาวเกิน 100 จะทำให้บันทึกไม่ผ่าน
        foreach (var line in wanted)
        {
            if (NormalizeNote(line.Note) is { Length: > MaxNoteLength })
            {
                throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                    $"หมายเหตุของ “{menu[line.MenuItemId].Name}” ยาวเกิน {MaxNoteLength} ตัวอักษร");
            }
        }

        var order = await InsertAsync(session.Id, wanted, menu, now, ct);

        // ส่ง event หลังบันทึกสำเร็จเท่านั้น (notes/00-CONTRACTS.md) — ครัว/เจ้าของร้าน และ group ของ session นี้
        // ถ้ารอบนี้ถูกรวมเข้าบิลของรอบหลัก รอบหลักก็ต้องเห็นออเดอร์ใหม่ด้วย
        var dto = ToDto(session, order);
        await notifier.OrderCreated(dto, session.Id, session.MergedIntoSessionId);
        return dto;
    }

    /// <summary>
    /// บันทึกออเดอร์พร้อม snapshot ชื่อ/ชนิด/ราคา (บุฟเฟ่ต์ราคา 0) และ <c>Number = max+1</c> ใน transaction เดียว
    /// ถ้าอีกเครื่องส่งพร้อมกันแล้วได้เลขเดียวกัน (unique index) ทิ้งของที่ยังไม่บันทึกแล้วลองใหม่หนึ่งครั้ง
    /// </summary>
    private async Task<Order> InsertAsync(int sessionId, List<OrderLineRequest> wanted,
        Dictionary<int, MenuItem> menu, DateTimeOffset now, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var next = (await db.Orders
                .Where(o => o.TableSessionId == sessionId)
                .MaxAsync(o => (int?)o.Number, ct) ?? 0) + 1;

            var order = new Order
            {
                TableSessionId = sessionId,
                Number = next,
                Status = OrderStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                Items = wanted.Select(line => new OrderItem
                {
                    MenuItemId = line.MenuItemId,
                    NameSnapshot = menu[line.MenuItemId].Name,
                    KindSnapshot = menu[line.MenuItemId].Kind,
                    UnitPrice = menu[line.MenuItemId].Kind == MenuItemKind.Buffet ? 0m : menu[line.MenuItemId].Price,
                    Quantity = line.Quantity,
                    Note = NormalizeNote(line.Note),
                }).ToList(),
            };

            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return order;
            }
            catch (DbUpdateException ex) when (attempt == 0 && IsNumberConflict(ex))
            {
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>unique <c>IX_Orders_TableSessionId_Number</c> = มีคนได้เลขรอบนี้ไปก่อนแล้ว</summary>
    private static bool IsNumberConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" } pg &&
        pg.ConstraintName?.Contains("Orders", StringComparison.Ordinal) == true;

    /// <summary>แถวตั้งค่าเดียวของร้าน (Program.cs สร้างให้ตอน migrate); ไม่มีแถวก็ใช้ค่าเริ่มต้นแทนการล้ม</summary>
    private async Task<RestaurantSettings> SettingsAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RestaurantSettings();

    private static string? NormalizeNote(string? note) =>
        string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    private static OrderDto ToDto(TableSession session, Order order) => new(
        order.Id,
        session.Id,
        session.Table?.Name ?? "",
        order.Number,
        order.Status.ToString(),
        order.CancelReason,
        order.CreatedAt,
        order.UpdatedAt,
        order.Items.Select(i => new OrderItemDto(i.NameSnapshot, i.KindSnapshot.ToString(), i.UnitPrice,
            i.Quantity, i.Note)).ToList());

    private static AppException Closed() =>
        new(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
            "รอบนี้ปิดแล้ว · สั่งอาหารไม่ได้");
}
