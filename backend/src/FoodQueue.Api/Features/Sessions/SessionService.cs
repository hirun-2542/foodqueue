using System.Security.Cryptography;
using FoodQueue.Api.Billing;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// Read model ของ Table Session — จุดเดียวที่ทุกฟีเจอร์อ่าน/สร้างรอบการนั่ง
/// (ticket 08 ภาพรวม · 16 รายละเอียดบิล · 18 การชำระเงิน · 21 ย้ายโต๊ะ · 22 รวมบิล อ่านผ่านที่นี่)
/// จึงเป็นเมธอดที่พูดถึง "รอบการนั่ง" ไม่ใช่รูปทรงของ endpoint
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// เปิดโต๊ะว่างหนึ่งรอบ: snapshot แพ็กเกจ ตั้ง <c>EndsAt = OpenedAt + DurationMinutes</c> สร้าง token
    /// แล้วส่ง <c>sessionChanged</c> หลังบันทึกสำเร็จ — โต๊ะที่มี session Open อยู่แล้วได้
    /// <c>TABLE_OCCUPIED</c> โดย partial unique index ในฐานข้อมูลเป็นตัวตัดสินสุดท้าย
    ///
    /// <paramref name="isDemoBot"/> ใช้ทำเครื่องหมายรอบที่ Demo Bot เปิด (endpoint demo ส่ง true)
    /// เพื่อให้บอทแตะเฉพาะรอบของตัวเองและไม่รบกวนโต๊ะที่คนจริงเปิด (ticket 24/25)
    /// </summary>
    Task<SessionDetailDto> OpenAsync(int tableId, int packageId, int adultCount, int childCount,
        CancellationToken ct, bool isDemoBot = false);

    /// <summary>session หรือ <c>SESSION_NOT_FOUND</c> (404) พร้อม Table/Orders/Adjustments/Payment ที่โหลดแล้ว</summary>
    Task<TableSession> RequireAsync(int sessionId, CancellationToken ct);

    /// <summary>
    /// โหลด session จาก token — token ที่ไม่พบคืน null ให้ caller ตัดสินใจรหัสข้อผิดพลาดเอง
    /// ไม่กรองด้วย <c>Status == Open</c>: token ที่ปิดแล้วยังเป็นแถวที่มีอยู่ ผู้เรียกจึงแยกได้ระหว่าง
    /// "ไม่พบ" (<c>SESSION_NOT_FOUND</c>) กับ "ปิดแล้ว" (<c>SESSION_CLOSED</c>) — งานของหน้าลูกค้า (ticket 09)
    /// พร้อม <c>Table</c>/<c>Package</c> ติดมาให้แมปเป็น <c>CustomerSessionDto</c> ได้ในครั้งเดียว
    /// </summary>
    Task<TableSession?> FindByTokenAsync(string token, CancellationToken ct = default);

    /// <summary>รายละเอียดพร้อมบิล (รวม session ที่ถูกรวมเข้าบิลนี้แล้ว) หรือ <c>SESSION_NOT_FOUND</c></summary>
    Task<SessionDetailDto> GetDetailAsync(int sessionId, CancellationToken ct);

    /// <summary>แปลง session ที่โหลดมาแล้วเป็น <see cref="SessionDetailDto"/> (โหลดบิลและ session ที่รวมอยู่ให้เอง)</summary>
    Task<SessionDetailDto> MapDetailAsync(TableSession session, CancellationToken ct);

    /// <summary>บิลของ session นี้ คิด SC/VAT ครั้งเดียวจากผลรวมของทุก session ที่รวมเข้าด้วยกัน</summary>
    Task<BillDto> ComputeBillAsync(TableSession session, CancellationToken ct);

    /// <summary>ประวัติ/รายการรอบการนั่ง กรองตามสถานะและวันตามเวลาไทย (ไม่ระบุ = ทั้งหมด เรียงใหม่สุดก่อน)</summary>
    Task<IReadOnlyList<SessionSummaryDto>> ListAsync(SessionStatus? status, DateOnly? date, CancellationToken ct);

    /// <summary>ยกเลิกรอบที่เปิดผิด: ได้เฉพาะ session Open ที่ไม่มีออเดอร์ที่ไม่ถูกยกเลิก แล้วส่ง <c>sessionClosed</c></summary>
    Task<SessionDetailDto> VoidAsync(int sessionId, CancellationToken ct);

    /// <summary>URL ที่ให้ลูกค้าสแกน — <c>{App:PublicBaseUrl}/s/{token}</c></summary>
    string QrUrlFor(TableSession session);

    /// <summary>รูปย่อที่ส่งออกทาง realtime</summary>
    SessionSummaryDto MapSummary(TableSession session);
}

/// <summary>
/// ตัวจริงของ <see cref="ISessionService"/> — ตัวเลขบิลมาจาก <see cref="BillCalculator"/> จุดเดียว
/// และ event realtime ส่งหลัง <c>SaveChanges</c> สำเร็จเท่านั้น (ตาม notes/00-CONTRACTS.md)
/// ไม่ลงทะเบียนใน DI เพราะ Program.cs ถูก freeze: ผู้เรียกสร้างผ่าน <see cref="From"/> เหมือน <c>UploadStore</c>
/// </summary>
public sealed class SessionService(AppDbContext db, IConfiguration config, ILiveNotifier notifier)
    : ISessionService
{
    /// <summary>256 บิต → base64url 43 ตัวอักษร (ไม่มี padding) ตาม spec</summary>
    public const int TokenLength = 43;

    private const int TokenBytes = 32;
    private const string DefaultPublicBaseUrl = "http://localhost:8080";

    public static SessionService From(IServiceProvider services) => new(
        services.GetRequiredService<AppDbContext>(),
        services.GetRequiredService<IConfiguration>(),
        services.GetRequiredService<ILiveNotifier>());

    public async Task<SessionDetailDto> OpenAsync(int tableId, int packageId, int adultCount, int childCount,
        CancellationToken ct, bool isDemoBot = false)
    {
        ValidateCounts(adultCount, childCount);

        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId, ct)
                    ?? throw Invalid("ไม่พบโต๊ะที่เลือก");
        if (!table.IsActive)
        {
            throw Invalid($"โต๊ะ {table.Name} ถูกปิดใช้งานอยู่ จึงเปิดโต๊ะไม่ได้");
        }

        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == packageId, ct)
                      ?? throw Invalid("ไม่พบแพ็กเกจที่เลือก");
        if (!package.IsActive)
        {
            throw Invalid($"แพ็กเกจ {package.Name} ถูกปิดใช้งานอยู่ เลือกไม่ได้");
        }

        var openedAt = DateTimeOffset.UtcNow;
        var session = new TableSession
        {
            Table = table,
            Package = package,
            TableId = table.Id,
            PackageId = package.Id,
            Token = NewToken(),
            AdultCount = adultCount,
            ChildCount = childCount,
            // snapshot ณ ตอนเปิด: แก้ราคา/ชื่อแพ็กเกจทีหลังต้องไม่กระทบรอบนี้และประวัติ
            PackageNameSnapshot = package.Name,
            AdultPriceSnapshot = package.AdultPrice,
            ChildPriceSnapshot = package.ChildPrice,
            PackageRankSnapshot = package.Rank,
            OpenedAt = openedAt,
            EndsAt = openedAt.AddMinutes(package.DurationMinutes),
            Status = SessionStatus.Open,
            IsDemoBot = isDemoBot,
        };

        db.TableSessions.Add(session);
        await SaveAsync(session, table.Name, ct);

        var detail = await MapDetailAsync(session, ct);
        await notifier.SessionChanged(MapSummary(session), session.Id);
        return detail;
    }

    public async Task<TableSession> RequireAsync(int sessionId, CancellationToken ct) =>
        await LoadAsync(sessionId, ct) ?? throw NotFound();

    /// <summary>
    /// ตามรอย (tracking) โดยเจตนา: ลูกค้าที่เปิดหน้าด้วย token อาจถูกเลื่อนสถานะ/แก้รายการในคำขอเดียวกัน
    /// ในอนาคต (ticket 10/13) และการอ่านตาราง/แพ็กเกจแนบมาก็เพื่อแมปเป็น DTO ได้โดยไม่ต้องยิงคำขอเพิ่ม
    /// </summary>
    public Task<TableSession?> FindByTokenAsync(string token, CancellationToken ct = default) =>
        db.TableSessions
            .Include(s => s.Table)
            .Include(s => s.Package)
            .FirstOrDefaultAsync(s => s.Token == token, ct);

    public async Task<SessionDetailDto> GetDetailAsync(int sessionId, CancellationToken ct) =>
        await MapDetailAsync(await RequireAsync(sessionId, ct), ct);

    public async Task<SessionDetailDto> MapDetailAsync(TableSession session, CancellationToken ct)
    {
        // session ที่ถูกรวมเข้าบิลนี้ ยังคงสั่งผ่าน QR เดิมได้จนกว่าจะชำระ (ticket 22)
        var merged = await db.TableSessions
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Adjustments)
            .Where(s => s.MergedIntoSessionId == session.Id)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var orders = session.Orders.Concat(merged.SelectMany(m => m.Orders))
            .OrderBy(o => o.Number)
            .ThenBy(o => o.Id)
            .ToList();
        var adjustments = session.Adjustments.Concat(merged.SelectMany(m => m.Adjustments)).ToList();
        var settings = await SettingsAsync(ct);

        return new SessionDetailDto(
            session.Id,
            session.TableId,
            session.Table?.Name ?? "",
            session.Table?.Zone,
            session.PackageNameSnapshot,
            session.PackageRankSnapshot,
            session.AdultCount,
            session.ChildCount,
            session.OpenedAt,
            session.EndsAt,
            session.Status.ToString(),
            QrUrlFor(session),
            session.IsDemoBot,
            session.MergedIntoSessionId,
            orders.Select(o => o.ToDto()).ToList(),
            adjustments.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Select(a => a.ToDto()).ToList(),
            BillCalculator.Compute(session, orders, adjustments, settings, merged),
            session.Payment?.ToDto());
    }

    public async Task<BillDto> ComputeBillAsync(TableSession session, CancellationToken ct)
    {
        var merged = await db.TableSessions
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Adjustments)
            .Where(s => s.MergedIntoSessionId == session.Id)
            .ToListAsync(ct);

        return BillCalculator.Compute(
            session,
            session.Orders.Concat(merged.SelectMany(m => m.Orders)),
            session.Adjustments.Concat(merged.SelectMany(m => m.Adjustments)),
            await SettingsAsync(ct),
            merged);
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> ListAsync(SessionStatus? status, DateOnly? date,
        CancellationToken ct)
    {
        var query = db.TableSessions.AsNoTracking().Include(s => s.Table).AsQueryable();
        if (status is { } wanted)
        {
            query = query.Where(s => s.Status == wanted);
        }

        if (date is { } day)
        {
            // วันของรอบ = วันที่ปิด (บิลที่ชำระแล้ว/ถูกยกเลิกในวันนั้น) ไม่งั้นวันที่เปิด
            // Bkk.StartOfDay คืนเวลาไทย (+07:00) ซึ่งเขียนลง timestamptz ไม่ได้ ต้องเป็น UTC
            var start = Bkk.StartOfDay(day).ToUniversalTime();
            var end = start.AddDays(1);
            query = query.Where(s => (s.ClosedAt ?? s.OpenedAt) >= start && (s.ClosedAt ?? s.OpenedAt) < end);
        }

        var sessions = await query.OrderByDescending(s => s.OpenedAt).ThenByDescending(s => s.Id).ToListAsync(ct);
        return sessions.Select(MapSummary).ToList();
    }

    public async Task<SessionDetailDto> VoidAsync(int sessionId, CancellationToken ct)
    {
        var session = await RequireAsync(sessionId, ct);
        if (session.Status != SessionStatus.Open)
        {
            throw new AppException(ErrorCodes.SessionClosed, ErrorCodes.StatusFor(ErrorCodes.SessionClosed),
                "รอบนี้ปิดไปแล้ว ยกเลิกไม่ได้");
        }

        // รอบที่ถูกรวมเข้าบิลอื่น: การยกเลิกรอบลูกจะทิ้งออเดอร์ของมันไว้นอกบิลหลักที่ยังคิดเงินอยู่ จึงยกเลิกที่นี่ไม่ได้
        if (session.MergedIntoSessionId is not null)
        {
            throw new AppException(ErrorCodes.SessionHasOrders, ErrorCodes.StatusFor(ErrorCodes.SessionHasOrders),
                "ยกเลิกโต๊ะไม่ได้ เพราะรอบนี้ถูกรวมเข้าบิลของอีกโต๊ะอยู่ · ยกเลิกรอบหลักแทน");
        }

        var hasOrders = await db.Orders
            .AnyAsync(o => o.TableSessionId == session.Id && o.Status != OrderStatus.Cancelled, ct);
        if (hasOrders)
        {
            throw new AppException(ErrorCodes.SessionHasOrders, ErrorCodes.StatusFor(ErrorCodes.SessionHasOrders),
                "ยกเลิกโต๊ะไม่ได้ เพราะมีออเดอร์แล้ว");
        }

        session.Status = SessionStatus.Voided;
        session.ClosedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var detail = await MapDetailAsync(session, ct);
        await notifier.SessionClosed(session.Id);
        return detail;
    }

    public string QrUrlFor(TableSession session)
    {
        var baseUrl = config["App:PublicBaseUrl"];
        return $"{(string.IsNullOrWhiteSpace(baseUrl) ? DefaultPublicBaseUrl : baseUrl.TrimEnd('/'))}/s/{session.Token}";
    }

    public SessionSummaryDto MapSummary(TableSession session) => session.ToSummaryDto();

    /// <summary>โหลดพร้อมทุกอย่างที่ read model ต้องใช้ (ตาราง ออเดอร์+รายการ รายการปรับยอด การชำระเงิน)</summary>
    private Task<TableSession?> LoadAsync(int sessionId, CancellationToken ct) =>
        db.TableSessions
            .Include(s => s.Table)
            .Include(s => s.Orders).ThenInclude(o => o.Items)
            .Include(s => s.Adjustments)
            .Include(s => s.Payment)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    private async Task<RestaurantSettings> SettingsAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RestaurantSettings();

    /// <summary>
    /// partial unique index บน TableId คือตัวตัดสินการเปิดพร้อมกัน: แปลง 23505 เป็น TABLE_OCCUPIED
    /// ส่วนการชนกันของ token (โอกาสน้อยมาก) ลองสร้างใหม่หนึ่งครั้งก่อนปล่อยผ่าน
    /// </summary>
    private async Task SaveAsync(TableSession session, string tableName, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" } pg)
            {
                if (pg.ConstraintName?.Contains("Token", StringComparison.Ordinal) == true && attempt == 0)
                {
                    session.Token = NewToken();
                    continue;
                }

                throw new AppException(ErrorCodes.TableOccupied, ErrorCodes.StatusFor(ErrorCodes.TableOccupied),
                    $"โต๊ะ {tableName} มีลูกค้านั่งอยู่แล้ว เปิดรอบซ้อนไม่ได้");
            }
        }
    }

    /// <summary>256 บิตสุ่มจาก CSPRNG แล้วเข้ารหัส base64url ไม่มี padding = 43 ตัวอักษรเสมอ</summary>
    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static void ValidateCounts(int adultCount, int childCount)
    {
        if (adultCount < 1)
        {
            throw Invalid("ต้องมีอย่างน้อย 1 ท่าน");
        }

        if (childCount < 0)
        {
            throw Invalid("จำนวนเด็กต้องไม่ติดลบ");
        }
    }

    private static AppException NotFound() =>
        new(ErrorCodes.SessionNotFound, ErrorCodes.StatusFor(ErrorCodes.SessionNotFound), "ไม่พบรอบการนั่งนี้");

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
