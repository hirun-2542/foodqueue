using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Uploads;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Settings;

/// <summary>
/// ตั้งค่าร้าน: ข้อมูลร้าน (ใช้บนใบแจ้งยอด ใบเสร็จ และ QR slip), เปอร์เซ็นต์ค่าบริการ/VAT,
/// ลิมิตการสั่งต่อรอบ และภาพแบนเนอร์หน้าลูกค้า — ทั้งหมดเป็นค่าชุดเดียวของร้าน (Settings แถวเดียว)
/// </summary>
public sealed class SettingsEndpoints : IEndpointModule
{
    /// <summary>ช่วงที่ยอมรับของทุกค่าตั้ง (spec ข้อ 65, 89, 90) — ใช้ทั้งฝั่งเซิร์ฟเวอร์และข้อความ help</summary>
    public const decimal MinPercent = 0m;
    public const decimal MaxPercent = 30m;
    public const int MinLimit = 1;
    public const int MaxLimit = 99;

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").RequireAuthorization("OwnerOnly");

        group.MapGet("", GetAsync);
        group.MapPut("", PutAsync);
        group.MapPost("/banner", UploadBannerAsync).DisableAntiforgery();
        group.MapDelete("/banner", ClearBannerAsync);
    }

    private static async Task<IResult> GetAsync(AppDbContext db, CancellationToken ct) =>
        Results.Ok((await LoadAsync(db, ct)).ToDto());

    /// <summary>
    /// แทนที่ค่าตั้งทั้งชุด (ฟอร์มเดียวทั้งหน้า) แล้วคืนค่าที่บันทึกจริง
    /// บิลที่ชำระแล้วไม่ถูกคำนวณใหม่เพราะการปิดบิลเก็บยอดไว้ที่ Payment แล้ว
    /// </summary>
    private static async Task<IResult> PutAsync(SettingsDto request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw Validation("กรอกชื่อร้าน");
        }

        if (name.Length > 100)
        {
            throw Validation("ชื่อร้านยาวเกิน 100 ตัวอักษร");
        }

        var address = request.Address?.Trim() ?? "";
        if (address.Length > 300)
        {
            throw Validation("ที่อยู่ยาวเกิน 300 ตัวอักษร");
        }

        var phone = request.Phone?.Trim() ?? "";
        if (phone.Length > 30)
        {
            throw Validation("เบอร์โทรยาวเกิน 30 ตัวอักษร");
        }

        var taxId = Trimmed(request.TaxId);
        if (taxId is { Length: > 30 })
        {
            throw Validation("เลขผู้เสียภาษียาวเกิน 30 ตัวอักษร");
        }

        var promptPayId = Trimmed(request.PromptPayId);
        if (promptPayId is { Length: > 30 })
        {
            throw Validation("PromptPay ID ยาวเกิน 30 ตัวอักษร");
        }

        ValidatePercent(request.ServiceChargePercent, "ค่าบริการ (%)");
        ValidatePercent(request.VatPercent, "VAT (%)");
        ValidateLimit(request.MaxQtyPerItemPerOrder, "สูงสุดต่อเมนูต่อรอบ");
        ValidateLimit(request.MaxItemsPerOrder, "สูงสุดต่อรอบ");
        ValidateLimit(request.LastOrderWarningMinutes, "เตือน Last order ก่อนหมดเวลา");

        var settings = await LoadAsync(db, ct);
        settings.Name = name;
        settings.Address = address;
        settings.Phone = phone;
        settings.TaxId = taxId;
        settings.PromptPayId = promptPayId;
        settings.ServiceChargePercent = request.ServiceChargePercent;
        settings.VatPercent = request.VatPercent;
        settings.MaxQtyPerItemPerOrder = request.MaxQtyPerItemPerOrder;
        settings.MaxItemsPerOrder = request.MaxItemsPerOrder;
        settings.LastOrderWarningMinutes = request.LastOrderWarningMinutes;
        await db.SaveChangesAsync(ct);

        return Results.Ok(settings.ToDto());
    }

    /// <summary>อัปโหลดแบนเนอร์ผ่าน store กลาง (ticket 05 ใช้ตัวเดียวกัน) — ได้พาธคืนไปให้ UI แสดงทันที</summary>
    private static async Task<IResult> UploadBannerAsync(IFormFile? file, AppDbContext db, HttpContext http,
        CancellationToken ct)
    {
        if (file is null)
        {
            throw UploadFailure();
        }

        var store = UploadStore.From(http.RequestServices);
        var path = await store.SaveAsync(file, "banner", ct);

        var settings = await LoadAsync(db, ct);
        var previous = settings.BannerImagePath;
        settings.BannerImagePath = path;
        await db.SaveChangesAsync(ct);
        store.Delete(previous);

        return Results.Ok(settings.ToDto());
    }

    /// <summary>กลับไปใช้แบนเนอร์เริ่มต้น แล้วลบไฟล์ที่เคยอัปโหลด</summary>
    private static async Task<IResult> ClearBannerAsync(AppDbContext db, HttpContext http, CancellationToken ct)
    {
        var settings = await LoadAsync(db, ct);
        var previous = settings.BannerImagePath;
        settings.BannerImagePath = null;
        await db.SaveChangesAsync(ct);
        UploadStore.From(http.RequestServices).Delete(previous);

        return Results.Ok(settings.ToDto());
    }

    /// <summary>
    /// Settings เป็นแถวเดียวที่ Program.cs สร้างให้ตอน migrate; ถ้าหายไปให้สร้างคืนด้วยรหัส 1
    /// (แบนเนอร์ว่าง = ใช้แบนเนอร์เริ่มต้นของ UI) เพื่อให้ทุกหน้าดึงค่าชุดเดียวกันได้เสมอ
    /// </summary>
    private static async Task<RestaurantSettings> LoadAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.Settings.FirstOrDefaultAsync(ct);
        if (settings is not null)
        {
            return settings;
        }

        settings = new RestaurantSettings
        {
            Id = 1,
            Name = "ร้านชาบู",
            Address = "",
            Phone = "",
        };
        db.Settings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    private static void ValidatePercent(decimal value, string label)
    {
        if (value < MinPercent || value > MaxPercent)
        {
            throw Validation($"{label} ต้องอยู่ระหว่าง 0–30");
        }
    }

    private static void ValidateLimit(int value, string label)
    {
        if (value < MinLimit || value > MaxLimit)
        {
            throw Validation($"{label} ต้องอยู่ระหว่าง 1–99");
        }
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AppException Validation(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);

    /// <summary>ข้อความเดียวกับสัญญา upload (ticket 05 แสดงข้อความเดียวกันนี้เมื่อไฟล์ไม่ผ่าน)</summary>
    private static AppException UploadFailure() =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
            UploadStore.InvalidFileMessage);
}
