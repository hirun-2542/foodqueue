using System.Globalization;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// ออเดอร์ที่ยังค้างในครัวและขวางการปิดบิล — ส่งไปกับ <c>ORDERS_IN_PROGRESS</c> ใน field <c>orders</c>
/// <c>TableName</c> มีความจำเป็นกับบิลรวม (ticket 22): เลขรอบซ้ำกันข้ามโต๊ะได้ เจ้าของร้านจึงต้องรู้ว่ารอบไหนของโต๊ะไหน
/// </summary>
public sealed record BlockingOrderDto(int Id, int Number, string Status, string TableName);

/// <summary>
/// การปิดบิล (spec "การปิดบิล", user stories 69, 70, 72, 74–77) — จุดเดียวที่ endpoint และ Demo bot (ticket 25)
/// เรียกใช้ ตรวจตามลำดับของ spec:
/// 1. session ต้อง Open — ไม่งั้น <c>ALREADY_PAID</c> (รวมถึงรอบที่ถูกยกเลิก: ไม่มีบิลให้รับเงินแล้ว)
/// 2. ไม่มีออเดอร์ Pending/Preparing — ไม่งั้น <c>ORDERS_IN_PROGRESS</c> พร้อมบอกว่ารอบไหนค้าง
///    (ออเดอร์ Ready ไม่ขวาง: spec override plan.md ที่เคยต้อง Served ก่อน)
/// 3. Cash ต้องรับเงิน ≥ total — ไม่งั้น <c>INSUFFICIENT_AMOUNT</c>; วิธีอื่นถือว่ารับเท่ากับ total พอดี
/// แล้วบันทึก Payment หนึ่งแถว (ยอดแยกส่วน ณ ตอนจ่าย) ตั้ง session เป็น Paid และส่ง <c>sessionChanged</c>
/// + <c>sessionClosed</c> หลัง commit — token ของรอบนี้สั่ง/ต่อ hub ไม่ได้อีกตั้งแต่ commit
///
/// เลย EndsAt ไม่เกี่ยว: เวลาปิดแค่การสั่ง บิลยังรับชำระได้เสมอ
/// ticket 19 เพิ่มหน้าจอ PromptPay บนเส้นทางเดียวกันนี้, ticket 22 ขยายไปยัง session ที่ถูกรวมบิล
/// </summary>
public sealed class PaymentService(AppDbContext db, ISessionService sessions, ILiveNotifier notifier)
{
    /// <summary>ขอบเขตของ <c>decimal(10,2)</c> ของคอลัมน์เงิน</summary>
    public const decimal MaxAmount = 99_999_999.99m;

    public static PaymentService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        SessionService.From(services),
        services.GetRequiredService<ILiveNotifier>());

    /// <summary>
    /// รับชำระแล้วคืนรายละเอียด session ที่ปิดแล้วพร้อม <c>payment</c> (เลขใบเสร็จ รับเงิน เงินทอน)
    ///
    /// การกดจ่ายพร้อมกัน: แถว session ถูกล็อก <c>FOR UPDATE</c> ตลอด transaction คำขอที่สองจึงรอจนคำขอแรก commit
    /// แล้วเห็นว่า Paid → <c>ALREADY_PAID</c>; unique index ของ <c>Payment.TableSessionId</c> เป็นตัวตัดสินสุดท้าย
    /// เลขใบเสร็จดึงจาก sequence <c>receipt_no_seq</c> หลังผ่านทุกกฎแล้วเท่านั้น จึงไม่มีเลขซ้ำและไม่ถูกใช้ซ้ำ
    /// (sequence ไม่ย้อนแม้ transaction ล้ม — เลขอาจข้ามได้แต่ไม่ซ้ำ)
    /// </summary>
    public async Task<SessionDetailDto> PayAsync(int sessionId, PaymentMethod method, decimal? amountReceived,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"TableSessions\" WHERE \"Id\" = {sessionId} FOR UPDATE", ct);

        var session = await sessions.RequireAsync(sessionId, ct);

        // บิลรวม (ticket 22): ทุกรอบที่ถูกรวมเข้าบิลนี้ปิดพร้อมกันเมื่อบิลหลักถูกชำระ — ล็อกแถวของมันไว้ก่อน
        // เพื่อไม่ให้มีออเดอร์ใหม่หรือการรวมรอบที่กำลังจะปิดเข้ามาระหว่างนี้
        var merged = await db.TableSessions
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Where(s => s.MergedIntoSessionId == session.Id)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        foreach (var child in merged)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TableSessions\" WHERE \"Id\" = {child.Id} FOR UPDATE", ct);
        }

        // 1. ต้อง Open
        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.AlreadyPaid, ErrorCodes.StatusFor(ErrorCodes.AlreadyPaid),
                session.Status == SessionStatus.Paid
                    ? $"โต๊ะ {TableName(session)} ชำระเงินแล้ว{(session.Payment is { } paid ? $" · ใบเสร็จ {paid.ReceiptNo}" : "")} · รับชำระซ้ำไม่ได้"
                    : $"โต๊ะ {TableName(session)} ถูกยกเลิกไปแล้ว · ไม่มีบิลให้รับชำระ");
        }

        // 2. ต้องไม่ใช่รอบที่ถูกรวมเข้าบิลอื่น (ticket 22): รอบลูกยัง Open และยังสั่งผ่าน QR เดิมได้ แต่มันเป็นส่วนหนึ่ง
        //    ของบิลหลัก — ถ้าปิดที่นี่ บิลลูกจะคิดแค่ยอดของตัวเอง ขณะที่รอบหลักยังคิดออเดอร์ของมันอยู่ = คิดเงินซ้ำ
        if (session.MergedIntoSessionId is { } primaryId)
        {
            var primary = await db.TableSessions.AsNoTracking().Include(s => s.Table)
                .FirstOrDefaultAsync(s => s.Id == primaryId, ct);
            throw Invalid(
                $"โต๊ะ {TableName(session)} ถูกรวมเข้าบิลของโต๊ะ {primary?.Table?.Name ?? primaryId.ToString()} อยู่แล้ว · รับชำระที่บิลหลัก");
        }

        // 3. ไม่มีออเดอร์ที่ครัวยังทำไม่เสร็จ (Ready ไม่ขวาง) — ออเดอร์ของรอบที่ถูกรวมก็อยู่ในบิลนี้
        //    จึงขวางด้วย และถูกเรียกชื่อพร้อมโต๊ะของมันเพราะเลขรอบซ้ำกันข้ามโต๊ะได้
        var blocking = session.Orders
            .Select(o => (Order: o, Table: TableName(session)))
            .Concat(merged.SelectMany(child => child.Orders.Select(o => (Order: o, Table: TableName(child)))))
            .Where(x => x.Order.Status is OrderStatus.Pending or OrderStatus.Preparing)
            .OrderBy(x => x.Order.Id)
            .ToList();
        if (blocking.Count > 0)
        {
            var primaryTable = TableName(session);
            var rounds = string.Join(", ", blocking.Select(x =>
                $"{(x.Table == primaryTable ? "" : $"โต๊ะ {x.Table} ")}รอบที่ {x.Order.Number} " +
                $"({(x.Order.Status == OrderStatus.Pending ? "รอรับ" : "กำลังทำ")})"));
            throw new AppException(ErrorCodes.OrdersInProgress, ErrorCodes.StatusFor(ErrorCodes.OrdersInProgress),
                $"ยังปิดบิลไม่ได้ · มีออเดอร์ค้างในครัว: {rounds}",
                new Dictionary<string, object?>
                {
                    ["orders"] = blocking
                        .Select(x => new BlockingOrderDto(x.Order.Id, x.Order.Number, x.Order.Status.ToString(),
                            x.Table))
                        .ToList(),
                });
        }

        // 4. ยอดที่รับ
        var bill = await sessions.ComputeBillAsync(session, ct);
        var received = bill.Total;
        if (method == PaymentMethod.Cash)
        {
            if (amountReceived is not { } cash)
            {
                throw Invalid("กรอกจำนวนเงินที่รับมา");
            }

            if (cash < 0m || cash > MaxAmount || decimal.Round(cash, 2) != cash)
            {
                throw Invalid("จำนวนเงินที่รับมาต้องเป็นตัวเลขไม่ติดลบ ทศนิยมไม่เกิน 2 ตำแหน่ง");
            }

            if (cash < bill.Total)
            {
                throw new AppException(ErrorCodes.InsufficientAmount,
                    ErrorCodes.StatusFor(ErrorCodes.InsufficientAmount),
                    $"รับเงินมาไม่พอ · ยอดที่ต้องชำระ ฿{Baht(bill.Total)} ขาดอีก ฿{Baht(bill.Total - cash)}");
            }

            received = cash;
        }

        var paidAt = DateTimeOffset.UtcNow;
        var payment = new Payment
        {
            TableSessionId = session.Id,
            Method = method,
            BuffetTotal = bill.BuffetTotal,
            AlaCarteTotal = bill.AlaCarteTotal,
            AdjustmentTotal = bill.AdjustmentTotal,
            Subtotal = bill.Subtotal,
            ServiceCharge = bill.ServiceCharge,
            Vat = bill.Vat,
            Total = bill.Total,
            AmountReceived = received,
            Change = received - bill.Total,
            ReceiptNo = ReceiptNo(paidAt, await NextReceiptSequenceAsync(ct)),
            PaidAt = paidAt,
        };

        session.Payment = payment;
        session.Status = SessionStatus.Paid;
        session.ClosedAt = paidAt;

        // ทุกรอบที่ถูกรวมเข้าบิลนี้ปิดพร้อมกัน — โต๊ะว่างทั้งหมด และ token ของแต่ละรอบสั่งไม่ได้อีก
        // (ใบเสร็จใบเดียวอยู่ที่ session หลักเท่านั้น: Payment มี unique index บน TableSessionId)
        foreach (var child in merged)
        {
            child.Status = SessionStatus.Paid;
            child.ClosedAt = paidAt;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // ล็อกแถวข้างบนทำให้มาไม่ถึงตรงนี้ในทางปกติ — unique index คือตัวกันชั้นสุดท้าย
            throw new AppException(ErrorCodes.AlreadyPaid, ErrorCodes.StatusFor(ErrorCodes.AlreadyPaid),
                $"โต๊ะ {TableName(session)} ชำระเงินแล้ว · รับชำระซ้ำไม่ได้");
        }

        await transaction.CommitAsync(ct);

        var detail = await sessions.MapDetailAsync(session, ct);
        // sessionChanged ก่อน: sessionClosed ตัดการเชื่อมต่อของลูกค้ารอบนี้ (LiveNotifier)
        // ทุกรอบที่ถูกรวมปิดไปพร้อมกัน จึงประกาศของมันด้วย เพื่อให้จอที่เปิดรอบนั้นอยู่รู้ตัวทันที
        foreach (var child in merged)
        {
            await notifier.SessionChanged(sessions.MapSummary(child), child.Id);
            await notifier.SessionClosed(child.Id);
        }

        await notifier.SessionChanged(sessions.MapSummary(session), session.Id);
        await notifier.SessionClosed(session.Id);
        return detail;
    }

    /// <summary><c>R</c> + วันที่ตามเวลาไทย (yyyyMMdd ปี ค.ศ.) + <c>-</c> + ลำดับ 6 หลัก เช่น <c>R20261016-000047</c></summary>
    public static string ReceiptNo(DateTimeOffset paidAt, long sequence) =>
        $"R{Bkk.DateOf(paidAt).ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{sequence.ToString("D6", CultureInfo.InvariantCulture)}";

    private async Task<long> NextReceiptSequenceAsync(CancellationToken ct) =>
        await db.Database.SqlQuery<long>($"SELECT nextval('receipt_no_seq') AS \"Value\"").SingleAsync(ct);

    private static string TableName(TableSession session) => session.Table?.Name ?? "";

    private static string Baht(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
