using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Uploads;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Menu;

/// <summary>
/// คำขอสร้าง/แก้หมวดหมู่ ชื่อเป็น nullable เพื่อให้ค่าที่หายไปถูกปฏิเสธด้วยข้อความไทยของเรา
/// ไม่ใช่ข้อความ deserialize ของ framework
/// </summary>
public sealed record CategoryRequest(string? Name, int? SortOrder);

/// <summary>
/// คำขอสร้าง/แก้เมนู: <c>Kind</c> มาเป็นข้อความ (ไม่ใช่ enum) เพื่อให้ค่าที่ไม่รู้จักได้ข้อความไทย
/// และไม่ส่ง ImagePath มาเลย — รูปเปลี่ยนได้ทาง <c>POST/DELETE /api/menu-items/{id}/image</c> เท่านั้น
/// </summary>
public sealed record MenuItemRequest(string? Name, int CategoryId, string? Description, string? Kind,
    int? MinPackageRank, decimal Price, bool? IsAvailable, bool? IsActive, int? SortOrder);

/// <summary>คำขอสลับ "หมด" — ไม่มีค่า = ปฏิเสธ ไม่เดาเป็น true</summary>
public sealed record AvailabilityRequest(bool? IsAvailable);

/// <summary>
/// หมวดหมู่และเมนูของร้าน (spec 83–86): เจ้าของร้านเพิ่ม/แก้หมวด (ลบได้เฉพาะหมวดว่าง),
/// เพิ่ม/แก้เมนูโดยเลือกชนิด Buffet (มีแพ็กเกจขั้นต่ำ ราคา 0) หรือ AlaCarte (มีราคา ไม่มีแพ็กเกจขั้นต่ำ),
/// อัปโหลดรูปผ่าน store กลางของ ticket 03 และสลับ "หมด" ได้ทันที
///
/// เมื่อ IsAvailable/IsActive ของเมนูเปลี่ยน จะส่ง <c>menuItemChanged</c> ถึง group menu หลังบันทึกสำเร็จ
/// (ticket 13) เพื่อให้จอลูกค้าเปลี่ยนเป็นแถวสีเทา + ป้าย "หมด" ได้ทันทีโดยไม่ต้องรีเฟรช
/// </summary>
public sealed class MenuEndpoints : IEndpointModule
{
    // ขอบเขตเดียวกับคอลัมน์ใน AppDbContext (Name 50/80, Description 200) และขอบเขตราคาของแพ็กเกจ
    public const int MaxCategoryNameLength = 50;
    public const int MaxItemNameLength = 80;
    public const int MaxDescriptionLength = 200;
    public const decimal MaxPrice = 100_000m;
    public const int MinPackageRank = 1;
    public const int MaxPackageRank = 99;

    /// <summary>ไฟล์รูปเมนูใช้ข้อความเดียวกับสัญญา upload (ticket 03) — ไม่มีข้อความที่สองในระบบ</summary>
    public const string InvalidImageMessage = UploadStore.InvalidFileMessage;

    public void Map(IEndpointRouteBuilder app)
    {
        var categories = app.MapGroup("/api/categories").RequireAuthorization("OwnerOnly");
        categories.MapGet("", ListCategoriesAsync);
        categories.MapPost("", CreateCategoryAsync);
        categories.MapPut("/{id:int}", UpdateCategoryAsync);
        categories.MapDelete("/{id:int}", DeleteCategoryAsync);

        var items = app.MapGroup("/api/menu-items").RequireAuthorization("OwnerOnly");
        items.MapGet("", ListItemsAsync);
        items.MapPost("", CreateItemAsync);
        items.MapPut("/{id:int}", UpdateItemAsync);
        items.MapPatch("/{id:int}/availability", SetAvailabilityAsync);
        items.MapPost("/{id:int}/image", UploadImageAsync).DisableAntiforgery();
        items.MapDelete("/{id:int}/image", DeleteImageAsync);
    }

    // ---------------------------------------------------------------- หมวดหมู่

    private static async Task<IResult> ListCategoriesAsync(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.SortOrder, ItemCount = c.Items.Count })
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            .ToListAsync(ct);
        return Results.Ok(categories.Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.ItemCount)).ToList());
    }

    private static async Task<IResult> CreateCategoryAsync(CategoryRequest request, AppDbContext db,
        CancellationToken ct)
    {
        var name = ValidateCategoryName(request.Name);
        var sortOrder = request.SortOrder ?? await NextCategorySortOrderAsync(db, ct);

        var category = new Category { Name = name, SortOrder = sortOrder };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/categories/{category.Id}", new CategoryDto(category.Id, category.Name,
            category.SortOrder, 0));
    }

    private static async Task<IResult> UpdateCategoryAsync(int id, CategoryRequest request, AppDbContext db,
        CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return Results.NotFound();
        }

        category.Name = ValidateCategoryName(request.Name);
        category.SortOrder = request.SortOrder ?? category.SortOrder;
        await db.SaveChangesAsync(ct);

        var itemCount = await db.MenuItems.CountAsync(m => m.CategoryId == id, ct);
        return Results.Ok(new CategoryDto(category.Id, category.Name, category.SortOrder, itemCount));
    }

    /// <summary>
    /// หมวดที่มีเมนูอยู่ลบไม่ได้ (spec 83) — ปฏิเสธพร้อมบอกจำนวนและทางแก้ ไม่ใช่ให้เมนูหายตามหมวด
    /// รหัสเป็น VALIDATION_FAILED ตามสัญญา (ไม่มีรหัสเฉพาะของหมวด) แต่สถานะ 409 แบบเดียวกับ rank ซ้ำของแพ็กเกจ
    /// </summary>
    private static async Task<IResult> DeleteCategoryAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return Results.NotFound();
        }

        var itemCount = await db.MenuItems.CountAsync(m => m.CategoryId == id, ct);
        if (itemCount > 0)
        {
            throw new AppException(ErrorCodes.ValidationFailed, StatusCodes.Status409Conflict,
                $"ลบหมวด “{category.Name}” ไม่ได้เพราะยังมีเมนูอยู่ในหมวด {itemCount} รายการ · ย้ายหรือลบเมนูก่อน");
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<int> NextCategorySortOrderAsync(AppDbContext db, CancellationToken ct)
    {
        var max = await db.Categories.AsNoTracking().Select(c => (int?)c.SortOrder).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    // ---------------------------------------------------------------- เมนู

    private static async Task<IResult> ListItemsAsync(AppDbContext db, CancellationToken ct)
    {
        var items = await db.MenuItems.AsNoTracking()
            .Include(m => m.Category)
            .OrderBy(m => m.Category!.SortOrder).ThenBy(m => m.SortOrder).ThenBy(m => m.Id)
            .ToListAsync(ct);
        return Results.Ok(items.Select(m => m.ToDto()).ToList());
    }

    private static async Task<IResult> CreateItemAsync(MenuItemRequest request, AppDbContext db,
        CancellationToken ct)
    {
        var values = await ValidateItemAsync(request, null, db, ct);

        var item = new MenuItem();
        Apply(item, values);
        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/menu-items/{item.Id}", item.ToDto());
    }

    private static async Task<IResult> UpdateItemAsync(int id, MenuItemRequest request, AppDbContext db,
        ILiveNotifier notifier, CancellationToken ct)
    {
        var item = await db.MenuItems.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw ItemMissing();

        var values = await ValidateItemAsync(request, item, db, ct);
        var flagsChanged = item.IsAvailable != values.IsAvailable || item.IsActive != values.IsActive;
        Apply(item, values);
        await db.SaveChangesAsync(ct);

        if (flagsChanged)
        {
            await notifier.MenuItemChanged(item.Id, item.IsAvailable, item.IsActive);
        }

        return Results.Ok(item.ToDto());
    }

    /// <summary>
    /// สลับ "หมด" ทันที (spec 86): เปลี่ยนเฉพาะ IsAvailable แล้วคืนเมนูที่บันทึกจริง
    /// UI ใช้ค่านี้เป็นคำตอบสุดท้ายหลัง optimistic update
    /// </summary>
    private static async Task<IResult> SetAvailabilityAsync(int id, AvailabilityRequest request, AppDbContext db,
        ILiveNotifier notifier, CancellationToken ct)
    {
        if (request.IsAvailable is null)
        {
            throw Invalid("ต้องระบุสถานะพร้อมขายเป็น true หรือ false");
        }

        var item = await db.MenuItems.Include(m => m.Category).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw ItemMissing();

        item.IsAvailable = request.IsAvailable.Value;
        await db.SaveChangesAsync(ct);
        await notifier.MenuItemChanged(item.Id, item.IsAvailable, item.IsActive);

        return Results.Ok(item.ToDto());
    }

    /// <summary>รูปเมนูผ่าน store กลางของ ticket 03 — ไฟล์เก่าถูกลบหลังเขียนฐานข้อมูลสำเร็จเท่านั้น</summary>
    private static async Task<IResult> UploadImageAsync(int id, IFormFile? file, AppDbContext db, HttpContext http,
        CancellationToken ct)
    {
        var item = await db.MenuItems.Include(m => m.Category).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw ItemMissing();

        if (file is null)
        {
            throw UploadFailure();
        }

        var store = UploadStore.From(http.RequestServices);
        var path = await store.SaveAsync(file, "menu", ct);

        var previous = item.ImagePath;
        item.ImagePath = path;
        await db.SaveChangesAsync(ct);
        store.Delete(previous);

        return Results.Ok(item.ToDto());
    }

    /// <summary>
    /// ลบรูปของเมนู (ปุ่ม "ลบรูป" ในกรอบ o10) — ไม่มี endpoint นี้ในสัญญา แต่เป็นคู่ของ
    /// <c>DELETE /api/settings/banner</c> ที่ ticket 03 ใช้อยู่: พาธรูปแก้ผ่าน PUT ไม่ได้
    /// </summary>
    private static async Task<IResult> DeleteImageAsync(int id, AppDbContext db, HttpContext http,
        CancellationToken ct)
    {
        var item = await db.MenuItems.Include(m => m.Category).FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw ItemMissing();

        var previous = item.ImagePath;
        item.ImagePath = null;
        await db.SaveChangesAsync(ct);
        UploadStore.From(http.RequestServices).Delete(previous);

        return Results.Ok(item.ToDto());
    }

    // ---------------------------------------------------------------- กฎของเมนู

    /// <summary>ค่าที่ผ่านการตรวจแล้ว พร้อมใช้กับเอนทิตี (หมวดหมู่ถูกโหลดมาแล้วเพื่อให้ DTO มีชื่อหมวด)</summary>
    private readonly record struct MenuItemValues(string Name, string? Description, Category Category,
        MenuItemKind Kind, int? MinPackageRank, decimal Price, bool IsAvailable, bool IsActive, int SortOrder);

    private static void Apply(MenuItem item, MenuItemValues values)
    {
        item.Name = values.Name;
        item.Description = values.Description;
        item.CategoryId = values.Category.Id;
        item.Category = values.Category;
        item.Kind = values.Kind;
        item.MinPackageRank = values.MinPackageRank;
        item.Price = values.Price;
        item.IsAvailable = values.IsAvailable;
        item.IsActive = values.IsActive;
        item.SortOrder = values.SortOrder;
    }

    private static async Task<MenuItemValues> ValidateItemAsync(MenuItemRequest request, MenuItem? editing,
        AppDbContext db, CancellationToken ct)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            throw Invalid("กรุณากรอกชื่อเมนู");
        }

        if (name.Length > MaxItemNameLength)
        {
            throw Invalid($"ชื่อเมนูยาวเกิน {MaxItemNameLength} ตัวอักษร");
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > MaxDescriptionLength })
        {
            throw Invalid($"คำอธิบายยาวเกิน {MaxDescriptionLength} ตัวอักษร");
        }

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == request.CategoryId, ct)
            ?? throw Invalid("ไม่พบหมวดหมู่ที่เลือก · เลือกหมวดหมู่ใหม่");

        // กฎตาม spec: Buffet ต้องมี MinPackageRank และราคา 0; AlaCarte ต้องไม่มี MinPackageRank และราคา > 0
        var kind = ParseKind(request.Kind);
        ValidatePrice(request.Price);

        int? minPackageRank;
        if (kind == MenuItemKind.Buffet)
        {
            if (request.MinPackageRank is null)
            {
                throw Invalid("เมนูบุฟเฟ่ต์ต้องเลือกแพ็กเกจขั้นต่ำ");
            }

            if (request.MinPackageRank is < MinPackageRank or > MaxPackageRank)
            {
                throw Invalid($"แพ็กเกจขั้นต่ำต้องอยู่ระหว่าง {MinPackageRank} ถึง {MaxPackageRank}");
            }

            if (request.Price != 0m)
            {
                throw Invalid("เมนูบุฟเฟ่ต์รวมอยู่ในราคาบุฟเฟ่ต์แล้ว ราคาต้องเป็น 0 บาท");
            }

            minPackageRank = request.MinPackageRank;
        }
        else
        {
            if (request.MinPackageRank is not null)
            {
                throw Invalid("เมนูคิดเงินแยกต้องไม่มีแพ็กเกจขั้นต่ำ");
            }

            if (request.Price <= 0m)
            {
                throw Invalid("เมนูคิดเงินแยกต้องมีราคามากกว่า 0 บาท");
            }

            minPackageRank = null;
        }

        var sortOrder = request.SortOrder ?? editing?.SortOrder
            ?? await NextItemSortOrderAsync(db, category.Id, ct);

        return new MenuItemValues(name, description, category, kind, minPackageRank, request.Price,
            request.IsAvailable ?? editing?.IsAvailable ?? true,
            request.IsActive ?? editing?.IsActive ?? true,
            sortOrder);
    }

    private static MenuItemKind ParseKind(string? kind) => kind?.Trim() switch
    {
        { } text when text.Equals(nameof(MenuItemKind.Buffet), StringComparison.OrdinalIgnoreCase) =>
            MenuItemKind.Buffet,
        { } text when text.Equals(nameof(MenuItemKind.AlaCarte), StringComparison.OrdinalIgnoreCase) =>
            MenuItemKind.AlaCarte,
        _ => throw Invalid("เลือกชนิดเมนูเป็นบุฟเฟ่ต์หรือคิดเงินแยก"),
    };

    private static void ValidatePrice(decimal price)
    {
        if (price < 0)
        {
            throw Invalid("ราคาต้องไม่ติดลบ");
        }

        if (price > MaxPrice)
        {
            throw Invalid($"ราคาต้องไม่เกิน {MaxPrice.ToString("0")} บาท");
        }

        if (decimal.Round(price, 2) != price)
        {
            throw Invalid("ราคาใส่ทศนิยมได้ไม่เกิน 2 ตำแหน่ง");
        }
    }

    private static async Task<int> NextItemSortOrderAsync(AppDbContext db, int categoryId, CancellationToken ct)
    {
        var max = await db.MenuItems.AsNoTracking()
            .Where(m => m.CategoryId == categoryId)
            .Select(m => (int?)m.SortOrder).MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    private static string ValidateCategoryName(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            throw Invalid("กรุณากรอกชื่อหมวดหมู่");
        }

        if (trimmed.Length > MaxCategoryNameLength)
        {
            throw Invalid($"ชื่อหมวดหมู่ยาวเกิน {MaxCategoryNameLength} ตัวอักษร");
        }

        return trimmed;
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);

    /// <summary>
    /// ใช้ <c>ITEM_NOT_FOUND</c> (รหัสในสัญญา) แทน 404 เปล่า ๆ เพื่อให้ UI แสดงข้อความไทยได้
    /// </summary>
    private static AppException ItemMissing() =>
        new(ErrorCodes.ItemNotFound, ErrorCodes.StatusFor(ErrorCodes.ItemNotFound), "ไม่พบเมนูที่ต้องการ");

    /// <summary>ข้อความเดียวกับสัญญา upload (เหมือน ticket 03) ทั้งกรณีไม่มีไฟล์และไฟล์ไม่ผ่านกฎ</summary>
    private static AppException UploadFailure() =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), InvalidImageMessage);
}
