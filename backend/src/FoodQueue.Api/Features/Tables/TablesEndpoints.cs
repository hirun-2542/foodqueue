using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Tables;

/// <summary>
/// คำขอสร้าง/แก้ไขโต๊ะ ใช้รูปเดียวกันทั้งสองทาง (PUT ส่งค่าครบชุดเสมอ)
/// <c>SortOrder</c> ไม่ระบุได้ = สร้างใหม่ต่อท้ายผัง, <c>IsActive</c> ไม่ระบุ = คงค่าเดิม
/// </summary>
public sealed record TableRequest(string? Name, int? Seats, string? Zone, int? SortOrder, bool? IsActive);

/// <summary>
/// จัดการโต๊ะ: เพิ่ม แก้ชื่อ/ที่นั่ง/โซน และปิดใช้งาน — งานของเจ้าของร้านเท่านั้น
/// ปิดใช้งานคือตั้ง <c>IsActive=false</c> ไม่มีการลบแถวทิ้ง จึงไม่มีเส้นทาง DELETE
/// (ครัวอ่านผังผ่าน <c>GET /api/tables/overview</c> ซึ่งเป็นของ ticket 08 ไม่ใช่ที่นี่)
/// </summary>
public sealed class TablesEndpoints : IEndpointModule
{
    public const int MinSeats = 1;
    public const int MaxSeats = 20;

    /// <summary>ตรงกับ HasMaxLength(20) ของ Name/Zone ใน AppDbContext — ตรวจที่นี่เพื่อให้ได้ข้อความไทย</summary>
    private const int NameMaxLength = 20;
    private const int ZoneMaxLength = 20;

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tables").RequireAuthorization("OwnerOnly");

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
    }

    /// <summary>ทุกโต๊ะของร้าน ทั้งที่ใช้งานอยู่และที่ปิดใช้งานไปแล้ว (ต้องเห็นเพื่อเปิดกลับได้)</summary>
    private static async Task<IResult> ListAsync(AppDbContext db)
    {
        var tables = await db.Tables.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();
        return Results.Ok(tables.Select(t => t.ToDto()).ToList());
    }

    private static async Task<IResult> CreateAsync(TableRequest request, AppDbContext db)
    {
        var name = ValidateName(request.Name);
        await EnsureNameIsFreeAsync(db, name, exceptId: null);
        var seats = ValidateSeats(request.Seats);
        var zone = ValidateZone(request.Zone);

        var lastSortOrder = await db.Tables.MaxAsync(t => (int?)t.SortOrder) ?? 0;
        var table = new Table
        {
            Name = name,
            Seats = seats,
            Zone = zone,
            SortOrder = request.SortOrder ?? lastSortOrder + 1,
            IsActive = request.IsActive ?? true,
        };

        db.Tables.Add(table);
        await SaveOrReportDuplicateNameAsync(db, name);

        return Results.Created($"/api/tables/{table.Id}", table.ToDto());
    }

    private static async Task<IResult> UpdateAsync(int id, TableRequest request, AppDbContext db)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == id);
        if (table is null)
        {
            // ไม่มีรหัสข้อผิดพลาดสำหรับ "ไม่พบโต๊ะ" ในชุด code ที่ตรึงไว้ (ErrorCodes)
            // จึงตอบ 404 เปล่า ๆ ให้ฝั่ง UI ใช้ข้อความมาตรฐานของสถานะ 404 แทนการยัดเป็น VALIDATION_FAILED
            return Results.NotFound();
        }

        var name = ValidateName(request.Name);
        await EnsureNameIsFreeAsync(db, name, exceptId: id);
        var seats = ValidateSeats(request.Seats);
        var zone = ValidateZone(request.Zone);

        // ปิดใช้งานโต๊ะที่มีลูกค้านั่งอยู่ไม่ได้ (spec: โต๊ะที่มี session Open) — บิลจะค้าง
        var disabling = request.IsActive == false && table.IsActive;
        if (disabling && await HasOpenSessionAsync(db, id))
        {
            throw new AppException(ErrorCodes.TableOccupied, ErrorCodes.StatusFor(ErrorCodes.TableOccupied),
                $"ปิดใช้งานโต๊ะ {table.Name} ไม่ได้ เพราะมีลูกค้านั่งอยู่");
        }

        table.Name = name;
        table.Seats = seats;
        table.Zone = zone;
        if (request.SortOrder is { } sortOrder)
        {
            table.SortOrder = sortOrder;
        }

        if (request.IsActive is { } isActive)
        {
            table.IsActive = isActive;
        }

        await SaveOrReportDuplicateNameAsync(db, name);

        return Results.Ok(table.ToDto());
    }

    /// <summary>
    /// unique index บน Table.Name เป็นตัวตัดสินสุดท้าย: การเช็คก่อนหน้าอาจมีคำขออื่นแทรกเข้ามาระหว่างนั้น
    /// (Postgres 23505) จึงแปลงเป็นข้อความไทยชุดเดียวกับที่ผู้ใช้เห็น ไม่ให้กลายเป็น 500
    /// </summary>
    private static async Task SaveOrReportDuplicateNameAsync(AppDbContext db, string name)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            throw Invalid($"ชื่อโต๊ะ {name} ซ้ำกับโต๊ะอื่น");
        }
    }

    private static Task<bool> HasOpenSessionAsync(AppDbContext db, int tableId) =>
        db.TableSessions.AnyAsync(s => s.TableId == tableId && s.Status == SessionStatus.Open);

    private static string ValidateName(string? raw)
    {
        var name = raw?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw Invalid("กรอกชื่อโต๊ะ");
        }

        if (name.Length > NameMaxLength)
        {
            throw Invalid($"ชื่อโต๊ะยาวเกิน {NameMaxLength} ตัวอักษร");
        }

        return name;
    }

    /// <summary>ชื่อซ้ำไม่ได้ (เทียบแบบไม่แยกตัวพิมพ์เล็ก–ใหญ่) — ข้อความชี้ที่ช่องชื่อโต๊ะ</summary>
    private static async Task EnsureNameIsFreeAsync(AppDbContext db, string name, int? exceptId)
    {
        var lowered = name.ToLower();
        var taken = await db.Tables.AnyAsync(t => t.Id != (exceptId ?? 0) && t.Name.ToLower() == lowered);
        if (taken)
        {
            throw Invalid($"ชื่อโต๊ะ {name} ซ้ำกับโต๊ะอื่น");
        }
    }

    private static int ValidateSeats(int? seats)
    {
        if (seats is not { } value)
        {
            throw Invalid("ระบุจำนวนที่นั่ง");
        }

        if (value < MinSeats || value > MaxSeats)
        {
            throw Invalid($"จำนวนที่นั่งต้องอยู่ระหว่าง {MinSeats}–{MaxSeats}");
        }

        return value;
    }

    private static string? ValidateZone(string? raw)
    {
        var zone = raw?.Trim();
        if (string.IsNullOrEmpty(zone))
        {
            return null;
        }

        if (zone.Length > ZoneMaxLength)
        {
            throw Invalid($"ชื่อโซนยาวเกิน {ZoneMaxLength} ตัวอักษร");
        }

        return zone;
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
