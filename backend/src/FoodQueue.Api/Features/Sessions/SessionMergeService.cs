using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Billing;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>หนึ่งโต๊ะในหน้าต่าง "รวมบิล" (frame o19): ยอดของบิลตัวเอง ณ ตอนนี้</summary>
public sealed record MergePreviewTableDto(int SessionId, string TableName, string PackageName, int Adults,
    int Children, DateTimeOffset OpenedAt, decimal Subtotal, decimal Total);

/// <summary>
/// คำตอบของหน้าต่าง "รวมบิล" ก่อนกดยืนยัน: โต๊ะหลัก, โต๊ะที่ถูกเลือกจะรวมอยู่แล้ว (<c>Mergeable</c>),
/// โต๊ะอื่นที่เลือกได้ (<c>Candidates</c>) และ <c>BillDto</c> ของบิลรวมที่ <c>BillCalculator</c> คำนวณให้
/// — ตัวเดียวกับที่ใช้ตอนอ่านบิลจริง หน้าจอจึงไม่คำนวณเงินเอง และตัวเลขที่เห็นก่อนกดยืนยันตรงกับบิลจริงเสมอ
/// </summary>
public sealed record MergePreviewDto(int PrimarySessionId, string PrimaryTableName, string PrimaryPackageName,
    int PrimaryAdults, int PrimaryChildren, DateTimeOffset PrimaryOpenedAt, decimal PrimarySubtotal,
    decimal PrimaryTotal, IReadOnlyList<MergePreviewTableDto> Mergeable,
    IReadOnlyList<MergePreviewTableDto> Candidates, BillDto Bill);

/// <summary>
/// รวมบิล (spec 68, ticket 22): เลือก session หลักหนึ่งรอบแล้วนำ session ที่ยัง Open อื่นเข้ามารวมได้
/// แม้ครัวยังทำออเดอร์ไม่เสร็จ — รอบที่ถูกรวมยังเป็น session ของตัวเอง (token/QR เดิม ยังสั่งได้ตาม EndsAt
/// ของตัวเอง) แต่มันชี้มาที่ session หลักผ่าน <see cref="TableSession.MergedIntoSessionId"/> เส้นทางเดียว
/// ยอดของมันจึงถูกคิดรวมในบิลหลักด้วย <c>BillCalculator</c> ตัวเดิม (ตัวนั้นรับ <c>mergedSessions</c> อยู่แล้ว)
/// — ไม่มีตัวคำนวณที่สอง
///
/// กฎ (ticket 22):
/// <list type="bullet">
/// <item>รวมได้เฉพาะรอบที่ยัง Open — รอบที่ชำระแล้ว/ถูกยกเลิกแล้ว คือ <c>SESSION_CLOSED</c></item>
/// <item>รวมเข้าตัวเองไม่ได้, ระบุซ้ำไม่ได้, รอบที่ถูกรวมเข้าบิลอื่นแล้วรวมซ้ำไม่ได้ และรอบที่เป็นเจ้าของบิลรวมอยู่
/// ยกไปรวมกับรอบอื่นไม่ได้ (การรวมไม่ย้อนกลับ และตัวคำนวณดูแค่ชั้นเดียว — การซ้อนชั้นจะทำยอดหาย)</item>
/// <item>subtotal ของบิลรวม = ผลรวม subtotal ของทุกรอบ แล้วคิด SC/VAT ครั้งเดียว</item>
/// <item>ชำระครั้งเดียวที่ session หลัก: ทุกรอบกลายเป็น Paid พร้อมใบเสร็จใบเดียว และ token ทุกอันสั่งไม่ได้
/// (งานนี้อยู่ใน <see cref="PaymentService"/> ไม่ใช่ที่นี่)</item>
/// </list>
///
/// การล็อกแถว: ทุกแถวที่เกี่ยวข้องถูกล็อก <c>FOR UPDATE</c> ตามลำดับ id จากน้อยไปมากตลอด transaction
/// คำขอที่ไขว้กัน (A←B กับ B←A) จึงเข้าคิวแบบเดียวกันได้ ไม่เกิด deadlock และการชำระที่ล็อกแถวหลักอยู่
/// ก็ทำให้การรวมรอบนั้นต้องรอจนจบ
/// </summary>
public sealed class SessionMergeService(AppDbContext db, ISessionService sessions, ILiveNotifier notifier)
{
    public static SessionMergeService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        SessionService.From(services),
        services.GetRequiredService<ILiveNotifier>());

    /// <summary>
    /// ข้อมูลของหน้าต่าง "รวมบิล" (frame o19): โต๊ะหลักพร้อมยอดของตัวเอง, โต๊ะที่เลือกอยู่ (<c>Mergeable</c>),
    /// โต๊ะที่เลือกได้ที่เหลือ (<c>Candidates</c>) และบิลรวมตัวอย่างที่ <c>BillCalculator</c> คิดให้สำหรับ
    /// <paramref name="selected"/> ที่ส่งมา (null/ว่าง = ยังไม่ติ๊กอะไร) — การติ๊กเป็นของผู้ใช้เสมอ
    /// เพราะการรวมย้อนกลับไม่ได้ จึงไม่มีอะไรถูกเลือกไว้ก่อน
    ///
    /// เป็นการอ่านเท่านั้น: ไม่มีอะไรถูกเขียน และการกดยืนยันยังผ่านกฎทั้งหมดของ <see cref="MergeAsync"/> อีกครั้ง
    /// </summary>
    public async Task<MergePreviewDto> PreviewAsync(int sessionId, IReadOnlyList<int>? selected,
        CancellationToken ct)
    {
        var primary = await sessions.RequireAsync(sessionId, ct);
        if (primary.Status != SessionStatus.Open)
        {
            throw Invalid($"โต๊ะ {TableName(primary)} ปิดบิลไปแล้ว · รวมบิลไม่ได้");
        }

        // โต๊ะหลักเป็นลูกของบิลอื่นอยู่ = บิลของมันถูกรวมไปแล้ว ยอดที่คิดต่อจากนี้ไม่ใช่ของบิลจริง
        if (primary.MergedIntoSessionId is not null)
        {
            throw Invalid($"โต๊ะ {TableName(primary)} ถูกรวมเข้าบิลอื่นอยู่แล้ว · ชำระที่บิลหลัก ไม่ใช่รวมซ้ำ");
        }

        // โต๊ะที่ยังเข้าร่วมบิลได้จริง: ยัง Open, ไม่ได้เป็นลูกของบิลใด และไม่ได้เป็นเจ้าของบิลรวมอยู่
        // (การยกเจ้าของบิลไปรวมกับบิลอื่นถูกปฏิเสธตอนยืนยัน จึงไม่ควรเสนอเป็นตัวเลือกให้กดแล้วพลาด)
        var joinable = await db.TableSessions.AsNoTracking()
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Adjustments)
            .Where(s => s.Status == SessionStatus.Open && s.MergedIntoSessionId == null)
            .ToListAsync(ct);
        var owners = await db.TableSessions.AsNoTracking()
            .Where(s => s.MergedIntoSessionId != null)
            .Select(s => s.MergedIntoSessionId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var selectable = joinable.Where(s => s.Id != primary.Id && !owners.Contains(s.Id)).ToList();
        // ไม่มีอะไรถูกเลือกไว้ล่วงหน้าโดยเจตนา: การรวมไม่ย้อนกลับได้ การติ๊กต้องเป็นการตัดสินใจของเจ้าของร้าน
        var wanted = selected ?? [];
        var picked = selectable.Where(s => wanted.Contains(s.Id)).ToList();
        var others = selectable.Where(s => !wanted.Contains(s.Id)).ToList();

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RestaurantSettings();
        var bill = BillCalculator.Compute(
            primary,
            primary.Orders.Concat(picked.SelectMany(s => s.Orders)).ToList(),
            primary.Adjustments.Concat(picked.SelectMany(s => s.Adjustments)).ToList(),
            settings,
            picked);

        // ยอดต่อโต๊ะก็มาจากตัวคำนวณตัวเดียวกัน (บิลของรอบนั้นตามลำพัง) — ไม่มีเลขคณิตเงินที่สองในระบบ
        var own = joinable.ToDictionary(s => s.Id, s => BillCalculator.Compute(s, s.Orders, s.Adjustments, settings));

        return new MergePreviewDto(
            primary.Id,
            TableName(primary),
            primary.PackageNameSnapshot,
            primary.AdultCount,
            primary.ChildCount,
            primary.OpenedAt,
            own[primary.Id].Subtotal,
            own[primary.Id].Total,
            picked.OrderBy(s => s.Table?.Name).ThenBy(s => s.Id).Select(s => ToPreviewRow(s, own[s.Id])).ToList(),
            others.OrderBy(s => s.Table?.Name).ThenBy(s => s.Id).Select(s => ToPreviewRow(s, own[s.Id])).ToList(),
            bill);
    }

    private static MergePreviewTableDto ToPreviewRow(TableSession session, BillDto bill) => new(
        session.Id,
        session.Table?.Name ?? "",
        session.PackageNameSnapshot,
        session.AdultCount,
        session.ChildCount,
        session.OpenedAt,
        bill.Subtotal,
        bill.Total);

    /// <summary>
    /// รวม <paramref name="sessionIds"/> เข้าบิลของ <paramref name="sessionId"/> แล้วคืนรายละเอียดของ session หลัก
    /// พร้อมบิลรวมที่คำนวณใหม่ (รอบที่ถูกรวมและรอบหลักได้ <c>sessionChanged</c> หลัง commit)
    /// </summary>
    public async Task<SessionDetailDto> MergeAsync(int sessionId, IReadOnlyList<int>? sessionIds,
        CancellationToken ct)
    {
        if (sessionIds is null || sessionIds.Count == 0)
        {
            throw Invalid("เลือกอย่างน้อยหนึ่งโต๊ะที่จะรวมเข้าบิลนี้");
        }

        if (sessionIds.Any(id => id <= 0))
        {
            throw Invalid("รหัสรอบการนั่งต้องเป็นจำนวนเต็มบวก");
        }

        var targets = sessionIds.ToList();
        if (targets.Distinct().Count() != targets.Count)
        {
            throw Invalid("ระบุโต๊ะเดียวกันซ้ำในคำขอเดียวไม่ได้");
        }

        if (targets.Contains(sessionId))
        {
            throw Invalid("รวมบิลของโต๊ะนี้เข้ากับตัวเองไม่ได้ · เลือกโต๊ะอื่นที่ยังไม่ปิดบิล");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // ลำดับล็อกคงที่ (id น้อยไปมาก) ทั้งรอบหลักและรอบที่ถูกรวม — คำขอที่ไขว้กันจึงไม่ตายค้าง
        foreach (var id in targets.Append(sessionId).Distinct().Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"TableSessions\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        }

        var primary = await sessions.RequireAsync(sessionId, ct);
        await RequireJoinableAsync(primary, isPrimary: true, ct);

        var merged = new List<TableSession>(targets.Count);
        foreach (var id in targets)
        {
            var target = await db.TableSessions.Include(s => s.Table).FirstOrDefaultAsync(s => s.Id == id, ct)
                         ?? throw new AppException(ErrorCodes.SessionNotFound,
                             ErrorCodes.StatusFor(ErrorCodes.SessionNotFound),
                             "ไม่พบรอบการนั่งของโต๊ะที่เลือก · อาจเพิ่งปิดไป ลองใหม่อีกครั้ง");
            await RequireJoinableAsync(target, isPrimary: false, ct);
            merged.Add(target);
        }

        // ผูกทุกรอบเข้าบิลหลัก: สถานะยัง Open (ลูกค้าสั่งผ่าน QR เดิมได้) — มีแต่เจ้าของบิลที่รู้เรื่องนี้
        foreach (var target in merged)
        {
            target.MergedIntoSessionId = primary.Id;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var detail = await sessions.MapDetailAsync(primary, ct);

        // event หลัง commit เท่านั้น: เจ้าของร้านและรอบที่ถูกรวม (รอบที่ถูกรวมต้องบอกได้ว่าบิลไปรวมที่ไหน)
        foreach (var target in merged)
        {
            await notifier.SessionChanged(sessions.MapSummary(target), target.Id);
        }

        await notifier.SessionChanged(sessions.MapSummary(primary), primary.Id);
        return detail;
    }

    /// <summary>รอบที่เข้าร่วมบิลรวมได้ = ยัง Open, ไม่ได้เป็นลูกของบิลอื่น และไม่ใช่เจ้าของบิลรวมอยู่</summary>
    private async Task RequireJoinableAsync(TableSession session, bool isPrimary, CancellationToken ct)
    {
        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                session.Status == SessionStatus.Paid
                    ? $"โต๊ะ {TableName(session)} ชำระเงินแล้ว · รวมบิลไม่ได้"
                    : $"โต๊ะ {TableName(session)} ถูกยกเลิกไปแล้ว · รวมบิลไม่ได้");
        }

        if (session.MergedIntoSessionId is not null)
        {
            throw Invalid(isPrimary
                ? $"โต๊ะ {TableName(session)} ถูกรวมเข้าบิลอื่นอยู่แล้ว · ชำระที่บิลหลัก ไม่ใช่รวมซ้ำ"
                : $"โต๊ะ {TableName(session)} ถูกรวมเข้าบิลอื่นอยู่แล้ว · รวมซ้ำไม่ได้");
        }

        if (await db.TableSessions.AnyAsync(s => s.MergedIntoSessionId == session.Id, ct))
        {
            throw Invalid($"โต๊ะ {TableName(session)} เป็นเจ้าของบิลรวมอยู่ · ยกไปรวมกับบิลอื่นไม่ได้");
        }
    }

    private static string TableName(TableSession session) => session.Table?.Name ?? "";

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
