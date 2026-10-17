using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FoodQueue.Api.Features.Packages;

/// <summary>
/// คำขอสร้าง/แก้แพ็กเกจ ชื่อเป็น nullable เพื่อให้ค่าที่หายไปถูกปฏิเสธด้วยข้อความไทยของเรา
/// ไม่ใช่ข้อความ deserialize ของ framework
/// </summary>
public sealed record PackageRequest(string? Name, int Rank, decimal AdultPrice, decimal ChildPrice,
    int DurationMinutes, bool? IsActive);

/// <summary>
/// แพ็กเกจบุฟเฟ่ต์: เพิ่ม แก้ และปิดใช้งาน — ไม่มี endpoint ลบโดยเจตนา เพราะ session snapshot
/// อ้างถึงแพ็กเกจเพื่อรักษาประวัติการขาย การปิดใช้งานคือส่ง <c>IsActive=false</c> ผ่าน PUT เดิม
/// </summary>
public sealed class PackagesEndpoints : IEndpointModule
{
    // spec ไม่ระบุขอบเขตของ rank/ราคา/เวลา จึงเลือกขอบเขตที่ร้านบุฟเฟ่ต์ใช้ได้จริง (ดูรายงาน ticket 04)
    private const int MinRank = 1;
    private const int MaxRank = 99;
    private const decimal MaxPrice = 100_000m;
    private const int MinDurationMinutes = 1;
    private const int MaxDurationMinutes = 600;

    public void Map(IEndpointRouteBuilder app)
    {
        // เขียนเส้นทางเต็มแทน MapGroup เพื่อให้เจตนาเรื่อง method ชัดเจน: /api/packages ไม่มี DELETE
        app.MapGet("/api/packages", ListAsync).RequireAuthorization("OwnerOnly");
        app.MapPost("/api/packages", CreateAsync).RequireAuthorization("OwnerOnly");
        app.MapPut("/api/packages/{id:int}", UpdateAsync).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var packages = await db.Packages.AsNoTracking().OrderBy(p => p.Rank).ToListAsync(ct);
        return Results.Ok(packages.Select(p => p.ToDto()).ToList());
    }

    private static async Task<IResult> CreateAsync(PackageRequest request, AppDbContext db, CancellationToken ct)
    {
        var values = await ValidateAsync(request, null, true, db, ct);

        var package = new Package();
        Apply(package, values);
        db.Packages.Add(package);
        await SaveAsync(db, values.Rank, null, ct);

        return Results.Created($"/api/packages/{package.Id}", package.ToDto());
    }

    private static async Task<IResult> UpdateAsync(int id, PackageRequest request, AppDbContext db,
        CancellationToken ct)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null)
        {
            // ไม่มีรหัสข้อผิดพลาดของแพ็กเกจในสัญญา จึงตอบ 404 เปล่า ๆ ดีกว่าใช้ ITEM_NOT_FOUND ของเมนูอาหาร
            return Results.NotFound();
        }

        var values = await ValidateAsync(request, package, package.IsActive, db, ct);
        Apply(package, values);
        await SaveAsync(db, request.Rank, package, ct);

        return Results.Ok(package.ToDto());
    }

    /// <summary>ค่าที่ผ่านการตรวจแล้ว พร้อมใช้กับเอนทิตี</summary>
    private readonly record struct PackageValues(string Name, int Rank, decimal AdultPrice, decimal ChildPrice,
        int DurationMinutes, bool IsActive);

    private static void Apply(Package package, PackageValues values)
    {
        package.Name = values.Name;
        package.Rank = values.Rank;
        package.AdultPrice = values.AdultPrice;
        package.ChildPrice = values.ChildPrice;
        package.DurationMinutes = values.DurationMinutes;
        package.IsActive = values.IsActive;
    }

    private static async Task<PackageValues> ValidateAsync(PackageRequest request, Package? editing,
        bool isActiveDefault, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw Invalid("กรุณากรอกชื่อแพ็กเกจ");
        }

        if (name.Length > 50)
        {
            throw Invalid("ชื่อแพ็กเกจยาวเกิน 50 ตัวอักษร");
        }

        if (request.Rank is < MinRank or > MaxRank)
        {
            throw Invalid($"ระดับต้องอยู่ระหว่าง {MinRank} ถึง {MaxRank}");
        }

        ValidatePrice(request.AdultPrice, "ราคาผู้ใหญ่");
        ValidatePrice(request.ChildPrice, "ราคาเด็ก");
        if (request.AdultPrice <= 0)
        {
            throw Invalid("ราคาผู้ใหญ่ต้องมากกว่า 0 บาท");
        }

        if (request.ChildPrice > request.AdultPrice)
        {
            throw Invalid("ราคาเด็กต้องไม่เกินราคาผู้ใหญ่");
        }

        if (request.DurationMinutes is < MinDurationMinutes or > MaxDurationMinutes)
        {
            throw Invalid($"เวลาต้องอยู่ระหว่าง {MinDurationMinutes} ถึง {MaxDurationMinutes} นาที");
        }

        var other = await db.Packages.AsNoTracking()
            .Where(p => p.Rank == request.Rank && (editing == null || p.Id != editing.Id))
            .FirstOrDefaultAsync(ct);
        if (other is not null)
        {
            throw await RankTakenAsync(db, request.Rank, editing, other.Name, ct);
        }

        return new PackageValues(name, request.Rank, request.AdultPrice, request.ChildPrice,
            request.DurationMinutes, request.IsActive ?? isActiveDefault);
    }

    private static void ValidatePrice(decimal price, string label)
    {
        if (price < 0)
        {
            throw Invalid($"{label}ต้องไม่ติดลบ");
        }

        if (price > MaxPrice)
        {
            throw Invalid($"{label}ต้องไม่เกิน {MaxPrice.ToString("0")} บาท");
        }

        if (decimal.Round(price, 2) != price)
        {
            throw Invalid($"{label}ใส่ทศนิยมได้ไม่เกิน 2 ตำแหน่ง");
        }
    }

    /// <summary>
    /// rank ซ้ำคือ 409 ตามสัญญา (plan Step 4) ทั้งที่รหัสยังเป็น `VALIDATION_FAILED` และข้อความบอก
    /// ระดับถัดไปที่ว่างให้เลือก แบบเดียวกับข้อความในกรอบ o11
    /// </summary>
    private static async Task<AppException> RankTakenAsync(AppDbContext db, int rank, Package? editing,
        string takenBy, CancellationToken ct)
    {
        var ranks = await db.Packages.AsNoTracking()
            .Where(p => editing == null || p.Id != editing.Id)
            .Select(p => p.Rank)
            .ToListAsync(ct);
        var suggestion = Math.Max(ranks.Count == 0 ? rank : ranks.Max(), rank) + 1;
        return new AppException(ErrorCodes.ValidationFailed, StatusCodes.Status409Conflict,
            $"ระดับนี้ซ้ำกับ {takenBy} · เลือกระดับ {suggestion} แทน");
    }

    /// <summary>
    /// unique index บน Rank เป็นตัวตัดสินสุดท้ายถ้ามีคำขอชนกัน — แปลงเป็นข้อความเดียวกับที่ตรวจล่วงหน้า
    /// </summary>
    private static async Task SaveAsync(AppDbContext db, int rank, Package? editing, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            var takenBy = await db.Packages.AsNoTracking()
                .Where(p => p.Rank == rank && (editing == null || p.Id != editing.Id))
                .Select(p => p.Name)
                .FirstOrDefaultAsync(ct) ?? "แพ็กเกจอื่น";
            throw await RankTakenAsync(db, rank, editing, takenBy, ct);
        }
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
