using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FoodQueue.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Seam 1 ของ ticket 05: HTTP ของแอปจริงบน Postgres จริง (Testcontainers)
/// ตรวจเฉพาะพฤติกรรมภายนอก (สถานะ HTTP, `code`, ข้อความไทย, ข้อมูลที่คืน, แถวในฐานข้อมูล, ไฟล์ที่ดึงกลับได้)
/// ตามกฎของเมนูใน spec 84–86 และการลบหมวดใน spec 83
/// </summary>
[Collection(ApiCollection.Name)]
public class MenuTests(PostgresFixture postgres)
{
    /// <summary>PNG 1×1 จริง — ใช้เป็น "ไฟล์ที่ถูกต้อง" โดยไม่ต้องพึ่งไฟล์ภายนอก</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpClient> OwnerClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), ConfigValue(app, "DemoSeed:Password"));
        return client;
    }

    private static async Task<HttpClient> KitchenClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        await LoginAsync(client, ConfigValue(app, "DemoSeed:KitchenEmail"), ConfigValue(app, "DemoSeed:Password"));
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("code").GetString()!;

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("message").GetString()!;

    private static void AssertThai(string message) =>
        Assert.Contains(message, c => c is >= '\u0e00' and <= '\u0e7f');

    /// <summary>ชื่อหมวด/เมนูไม่ซ้ำกับของเทสต์อื่นในคอลเลกชันเดียวกัน (ฐานข้อมูลถูกใช้ร่วมกัน)</summary>
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 9, 40)];

    private static async Task<int> CreateCategoryAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/categories", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateBuffetItemAsync(HttpClient client, int categoryId, string name,
        int minPackageRank = 1)
    {
        var response = await client.PostAsJsonAsync("/api/menu-items", new
        {
            name,
            categoryId,
            kind = "Buffet",
            minPackageRank,
            price = 0m,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt32();
    }

    private static object BuffetItem(int categoryId, string name, int? minPackageRank = 1, decimal price = 0m,
        string? description = null, bool? isAvailable = null, bool? isActive = null) => new
    {
        name,
        categoryId,
        description,
        kind = "Buffet",
        minPackageRank,
        price,
        isAvailable,
        isActive,
    };

    private static object AlaCarteItem(int categoryId, string name, decimal price = 15m, int? minPackageRank = null)
        => new
        {
            name,
            categoryId,
            kind = "AlaCarte",
            minPackageRank,
            price,
        };

    // ------------------------------------------------------------ หมวดหมู่

    [Fact]
    public async Task A_category_with_items_cannot_be_deleted_while_an_empty_one_can()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var busyName = Unique("หมู");
        var busy = await CreateCategoryAsync(client, busyName);
        _ = await CreateBuffetItemAsync(client, busy, "สันคอหมูสไลซ์");

        var refusal = await client.DeleteAsync($"/api/categories/{busy}");
        Assert.Equal(HttpStatusCode.Conflict, refusal.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(refusal));
        var message = await ProblemMessageAsync(refusal);
        AssertThai(message);
        Assert.Contains(busyName, message);
        Assert.Contains("1", message);

        // หมวดและเมนูยังอยู่ครบ — การปฏิเสธต้องไม่ทำข้อมูลหาย
        var categories = await ReadCategoriesAsync(client);
        Assert.Contains(categories, c => c.Name == busyName && c.ItemCount == 1);

        var empty = await CreateCategoryAsync(client, Unique("ของหวาน"));
        var deleted = await client.DeleteAsync($"/api/categories/{empty}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.DoesNotContain(await ReadCategoriesAsync(client), c => c.Id == empty);
        Assert.Equal(0, await app.WithDbAsync(db => db.Categories.CountAsync(c => c.Id == empty)));
    }

    [Fact]
    public async Task Category_list_counts_items_and_keeps_sort_order()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var first = await client.PostAsJsonAsync("/api/categories", new { name = Unique("ทะเล"), sortOrder = 41 });
        var second = await client.PostAsJsonAsync("/api/categories", new { name = Unique("ผัก"), sortOrder = 42 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var firstId = (await BodyAsync(first)).GetProperty("id").GetInt32();
        var secondId = (await BodyAsync(second)).GetProperty("id").GetInt32();
        _ = await CreateBuffetItemAsync(client, firstId, "กุ้งขาว", 2);

        var categories = await ReadCategoriesAsync(client);

        var ordered = categories.Where(c => c.Id == firstId || c.Id == secondId).ToArray();
        Assert.Equal([firstId, secondId], ordered.Select(c => c.Id).ToArray());
        Assert.Equal(1, ordered[0].ItemCount);
        Assert.Equal(0, ordered[1].ItemCount);

        var renamed = await client.PutAsJsonAsync($"/api/categories/{secondId}",
            new { name = Unique("ของหวาน"), sortOrder = 40 });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var afterRename = (await ReadCategoriesAsync(client)).Where(c => c.Id == secondId).Single();
        Assert.StartsWith("ของหวาน-", afterRename.Name);
        Assert.Equal(40, afterRename.SortOrder);
    }

    [Fact]
    public async Task An_empty_category_name_is_400_validation_failed_with_a_thai_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PostAsJsonAsync("/api/categories", new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    // ------------------------------------------------------------ กฎของชนิด/ราคา/แพ็กเกจขั้นต่ำ

    [Theory]
    [InlineData(1)]     // Buffet ที่มีราคา ≠ 0
    [InlineData(50)]
    public async Task A_buffet_item_with_a_price_other_than_zero_is_rejected(int price)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("หมู"));

        var response = await client.PostAsJsonAsync("/api/menu-items",
            BuffetItem(category, "สันคอหมูสไลซ์", price: price));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("บุฟเฟ่ต์", message);
        Assert.Equal(0, await app.WithDbAsync(db => db.MenuItems.CountAsync(m => m.CategoryId == category)));
    }

    [Fact]
    public async Task A_buffet_item_without_a_minimum_package_rank_is_rejected()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("น้ำซุป"));

        var response = await client.PostAsJsonAsync("/api/menu-items",
            BuffetItem(category, "น้ำซุปใส", minPackageRank: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("แพ็กเกจขั้นต่ำ", message);
    }

    [Fact]
    public async Task An_ala_carte_item_with_a_price_of_zero_is_rejected_with_its_kind_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("เครื่องดื่ม"));

        var response = await client.PostAsJsonAsync("/api/menu-items", AlaCarteItem(category, "น้ำเปล่า", 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("คิดเงินแยก", message);
        Assert.Equal(0, await app.WithDbAsync(db => db.MenuItems.CountAsync(m => m.CategoryId == category)));
    }

    /// <summary>ราคาติดลบถูกปฏิเสธด้วยกฎราคาทั่วไป ก่อนถึงกฎของชนิด (ลำดับเดียวกับที่ฟอร์มแสดง)</summary>
    [Fact]
    public async Task A_negative_price_is_rejected_for_both_kinds()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("เครื่องดื่ม"));

        var alaCarte = await client.PostAsJsonAsync("/api/menu-items", AlaCarteItem(category, "โค้ก", -5m));
        Assert.Equal(HttpStatusCode.BadRequest, alaCarte.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(alaCarte));
        Assert.StartsWith("ราคาต้องไม่ติดลบ", await ProblemMessageAsync(alaCarte));

        var buffet = await client.PostAsJsonAsync("/api/menu-items", BuffetItem(category, "เห็ดรวม", price: -1m));
        Assert.Equal(HttpStatusCode.BadRequest, buffet.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(buffet));
        Assert.StartsWith("ราคาต้องไม่ติดลบ", await ProblemMessageAsync(buffet));
    }

    [Fact]
    public async Task An_ala_carte_item_with_a_minimum_package_rank_is_rejected()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("เครื่องดื่ม"));

        var response = await client.PostAsJsonAsync("/api/menu-items",
            AlaCarteItem(category, "โค้ก", minPackageRank: 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("แพ็กเกจขั้นต่ำ", message);
    }

    [Fact]
    public async Task An_unknown_kind_is_rejected_with_a_thai_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ผัก"));

        var response = await client.PostAsJsonAsync("/api/menu-items",
            new { name = "ผักกาดขาว", categoryId = category, kind = "Set", minPackageRank = (int?)null, price = 20m });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    /// <summary>แก้เมนูก็ต้องผ่านกฎเดิม — เปลี่ยนราคาของ Buffet ให้ไม่ใช่ 0 ต้องถูกปฏิเสธ</summary>
    [Fact]
    public async Task Editing_a_buffet_item_into_a_paid_one_is_rejected_and_leaves_the_row_untouched()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("เนื้อวัว"));
        var item = await CreateBuffetItemAsync(client, category, "เนื้อวัวสไลซ์", 2);

        var response = await client.PutAsJsonAsync($"/api/menu-items/{item}",
            BuffetItem(category, "เนื้อวัวสไลซ์", 2, price: 120m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        Assert.StartsWith("เมนูบุฟเฟ่ต์", await ProblemMessageAsync(response));

        var stored = await app.WithDbAsync(db => db.MenuItems.SingleAsync(m => m.Id == item));
        Assert.Equal(0m, stored.Price);
        Assert.Equal(2, stored.MinPackageRank);
    }

    [Fact]
    public async Task Both_kinds_save_the_fields_the_spec_requires_and_the_kind_survives_a_reload()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var buffetCategory = await CreateCategoryAsync(client, Unique("ทะเล"));
        var drinkCategory = await CreateCategoryAsync(client, Unique("เครื่องดื่ม"));
        // The whole test collection shares one Postgres, so item names have to be unique per run.
        var buffetName = Unique("ริบอายสไลซ์");
        var alaCarteName = Unique("เบียร์สิงห์");

        var buffet = await client.PostAsJsonAsync("/api/menu-items", new
        {
            name = buffetName,
            categoryId = buffetCategory,
            description = "เนื้อริบอายออสเตรเลียสไลซ์บาง 3 มม.",
            kind = "Buffet",
            minPackageRank = 3,
            price = 0m,
        });
        Assert.Equal(HttpStatusCode.Created, buffet.StatusCode);

        var alaCarte = await client.PostAsJsonAsync("/api/menu-items",
            AlaCarteItem(drinkCategory, alaCarteName, 89m));
        Assert.Equal(HttpStatusCode.Created, alaCarte.StatusCode);

        var items = await ReadItemsAsync(client);
        var savedBuffet = items.Single(i => i.Name == buffetName);
        Assert.Equal("Buffet", savedBuffet.Kind);
        Assert.Equal(3, savedBuffet.MinPackageRank);
        Assert.Equal(0m, savedBuffet.Price);
        Assert.Equal(buffetCategory, savedBuffet.CategoryId);
        Assert.Equal("เนื้อริบอายออสเตรเลียสไลซ์บาง 3 มม.", savedBuffet.Description);
        Assert.True(savedBuffet.IsAvailable);

        var savedAlaCarte = items.Single(i => i.Name == alaCarteName);
        Assert.Equal("AlaCarte", savedAlaCarte.Kind);
        Assert.Null(savedAlaCarte.MinPackageRank);
        Assert.Equal(89m, savedAlaCarte.Price);

        // MinPackageRank ที่เก็บไว้คือค่าที่ ticket 09 ใช้ตัดสิน MinPackageRank <= rank
        var stored = await app.WithDbAsync(db => db.MenuItems.SingleAsync(m => m.Id == savedBuffet.Id));
        Assert.Equal(3, stored.MinPackageRank);
    }

    [Fact]
    public async Task A_package_rank_beyond_1_to_99_is_rejected()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ของหวาน"));

        var response = await client.PostAsJsonAsync("/api/menu-items",
            BuffetItem(category, "ไอศกรีมวานิลลา", minPackageRank: 100));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task A_menu_item_in_a_category_that_does_not_exist_is_rejected()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PostAsJsonAsync("/api/menu-items", BuffetItem(999_999, "เห็ดรวม"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("หมวดหมู่", message);
    }

    // ------------------------------------------------------------ สลับ "หมด"

    [Fact]
    public async Task Flipping_availability_changes_is_available_immediately_and_round_trips()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ทะเล"));
        var item = await CreateBuffetItemAsync(client, category, "กุ้งขาว", 2);

        var off = await client.PatchAsJsonAsync($"/api/menu-items/{item}/availability", new { isAvailable = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.False((await BodyAsync(off)).GetProperty("isAvailable").GetBoolean());
        Assert.False((await app.WithDbAsync(db => db.MenuItems.SingleAsync(m => m.Id == item))).IsAvailable);
        Assert.False((await ReadItemsAsync(client)).Single(i => i.Id == item).IsAvailable);

        var on = await client.PatchAsJsonAsync($"/api/menu-items/{item}/availability", new { isAvailable = true });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.True((await BodyAsync(on)).GetProperty("isAvailable").GetBoolean());
        Assert.True((await app.WithDbAsync(db => db.MenuItems.SingleAsync(m => m.Id == item))).IsAvailable);

        // สลับแล้วต้องแตะแค่ IsAvailable — IsActive และราคาไม่ขยับ
        Assert.True((await ReadItemsAsync(client)).Single(i => i.Id == item).IsActive);
    }

    [Fact]
    public async Task Availability_without_a_value_is_rejected_and_an_unknown_item_is_item_not_found()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ผัก"));
        var item = await CreateBuffetItemAsync(client, category, "ผักบุ้งจีน");

        var missing = await client.PatchAsJsonAsync($"/api/menu-items/{item}/availability", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(missing));
        AssertThai(await ProblemMessageAsync(missing));

        var unknown = await client.PatchAsJsonAsync("/api/menu-items/999999/availability", new { isAvailable = false });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("ITEM_NOT_FOUND", await ProblemCodeAsync(unknown));
        AssertThai(await ProblemMessageAsync(unknown));
    }

    // ------------------------------------------------------------ รูปเมนู

    [Fact]
    public async Task An_uploaded_menu_image_is_stored_returned_and_served_back_over_http()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("เนื้อวัว"));
        var item = await CreateBuffetItemAsync(client, category, "ริบอายสไลซ์", 3);

        var response = await client.PostAsync($"/api/menu-items/{item}/image", Upload("ribeye.png", "image/png",
            OnePixelPng));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = (await BodyAsync(response)).GetProperty("imagePath").GetString();
        Assert.NotNull(path);
        Assert.StartsWith("/api/uploads/menu-", path);
        Assert.EndsWith(".png", path);

        // พาธที่คืนต้องดึงกลับมาแสดงได้จริง (การ์ดเมนูของลูกค้าใช้พาธเดียวกัน)
        var fetched = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OnePixelPng, await fetched.Content.ReadAsByteArrayAsync());

        Assert.Equal(path, (await ReadItemsAsync(client)).Single(i => i.Id == item).ImagePath);

        var cleared = await client.DeleteAsync($"/api/menu-items/{item}/image");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Null((await ReadItemsAsync(client)).Single(i => i.Id == item).ImagePath);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_menu_image_upload_rejects_the_wrong_type_and_an_oversized_file_with_the_contract_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ของหวาน"));
        var item = await CreateBuffetItemAsync(client, category, "ไอศกรีมวานิลลา");

        var wrongType = await client.PostAsync($"/api/menu-items/{item}/image",
            Upload("notes.txt", "text/plain", Encoding.UTF8.GetBytes("ไม่ใช่รูป")));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(wrongType));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(wrongType));

        // ชนิดถูกแต่นามสกุลไม่ตรง
        var mismatched = await client.PostAsync($"/api/menu-items/{item}/image",
            Upload("photo.jpg", "image/png", OnePixelPng));
        Assert.Equal(HttpStatusCode.BadRequest, mismatched.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(mismatched));

        var oversized = await client.PostAsync($"/api/menu-items/{item}/image",
            Upload("huge.jpg", "image/jpeg", new byte[2 * 1024 * 1024 + 1]));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(oversized));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(oversized));

        // ไม่มีไฟล์มาเลย (multipart ที่ไม่มี part ชื่อ file)
        var empty = new MultipartFormDataContent { { new StringContent("x"), "not-a-file" } };
        var missing = await client.PostAsync($"/api/menu-items/{item}/image", empty);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(missing));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(missing));

        // ไฟล์ที่ถูกปฏิเสธต้องไม่ถูกบันทึก
        Assert.Null((await ReadItemsAsync(client)).Single(i => i.Id == item).ImagePath);

        // 2 MB พอดีถือว่าผ่าน (ขอบเขตเดียวกับแบนเนอร์ของ ticket 03)
        var boundary = await client.PostAsync($"/api/menu-items/{item}/image",
            Upload("exact.jpg", "image/jpeg", new byte[2 * 1024 * 1024]));
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        Assert.NotNull((await ReadItemsAsync(client)).Single(i => i.Id == item).ImagePath);
    }

    [Fact]
    public async Task Replacing_a_menu_image_deletes_the_previous_file()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var category = await CreateCategoryAsync(client, Unique("ผัก"));
        var item = await CreateBuffetItemAsync(client, category, "เห็ดรวม");

        var first = await client.PostAsync($"/api/menu-items/{item}/image", Upload("a.png", "image/png", OnePixelPng));
        var firstPath = (await BodyAsync(first)).GetProperty("imagePath").GetString()!;

        var second = await client.PostAsync($"/api/menu-items/{item}/image", Upload("b.png", "image/png", OnePixelPng));
        var secondPath = (await BodyAsync(second)).GetProperty("imagePath").GetString()!;

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal(secondPath, (await ReadItemsAsync(client)).Single(i => i.Id == item).ImagePath);
        // ไฟล์เก่าถูกเก็บกวาดหลังเขียนฐานข้อมูลสำเร็จ
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(firstPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(secondPath)).StatusCode);
    }

    // ------------------------------------------------------------ สิทธิ์

    [Fact]
    public async Task The_menu_endpoints_are_401_without_login_and_403_for_the_kitchen_role()
    {
        using var app = new TestApp(postgres);

        var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/menu-items")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/categories", new { name = "ทะเล" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/menu-items", AlaCarteItem(1, "โค้ก"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PatchAsJsonAsync("/api/menu-items/1/availability", new { isAvailable = false })).StatusCode);

        var kitchen = await KitchenClientAsync(app);
        var get = await kitchen.GetAsync("/api/menu-items");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Null(get.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync("/api/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kitchen.PostAsync("/api/menu-items/1/image", Upload("a.png", "image/png", OnePixelPng))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.DeleteAsync("/api/categories/1")).StatusCode);
    }

    // ------------------------------------------------------------ helpers

    private static MultipartFormDataContent Upload(string fileName, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static async Task<CategorySnapshot[]> ReadCategoriesAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/categories"));
        return doc.RootElement.EnumerateArray().Select(CategorySnapshot.From).ToArray();
    }

    private static async Task<ItemSnapshot[]> ReadItemsAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/menu-items"));
        return doc.RootElement.EnumerateArray().Select(ItemSnapshot.From).ToArray();
    }

    private readonly record struct CategorySnapshot(int Id, string Name, int SortOrder, int ItemCount)
    {
        public static CategorySnapshot From(JsonElement e) => new(
            e.GetProperty("id").GetInt32(),
            e.GetProperty("name").GetString()!,
            e.GetProperty("sortOrder").GetInt32(),
            e.GetProperty("itemCount").GetInt32());
    }

    /// <summary>รูปทรงของ MenuItemDto ที่เทสต์สนใจ — ตรงกับ Contracts/Dtos.cs</summary>
    private readonly record struct ItemSnapshot(int Id, int CategoryId, string CategoryName, string Name,
        string? Description, string? ImagePath, string Kind, int? MinPackageRank, decimal Price, bool IsAvailable,
        bool IsActive)
    {
        public static ItemSnapshot From(JsonElement e) => new(
            e.GetProperty("id").GetInt32(),
            e.GetProperty("categoryId").GetInt32(),
            e.GetProperty("categoryName").GetString()!,
            e.GetProperty("name").GetString()!,
            e.TryGetProperty("description", out var d) ? d.GetString() : null,
            e.TryGetProperty("imagePath", out var i) ? i.GetString() : null,
            e.GetProperty("kind").GetString()!,
            e.TryGetProperty("minPackageRank", out var r) ? r.GetInt32() : null,
            e.GetProperty("price").GetDecimal(),
            e.GetProperty("isAvailable").GetBoolean(),
            e.GetProperty("isActive").GetBoolean());
    }
}
