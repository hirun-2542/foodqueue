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
/// Seam 1 ของ ticket 03: HTTP ของแอปจริง + Postgres จริง (Testcontainers)
/// ยืนยันพฤติกรรมที่ผู้ใช้สังเกตได้เท่านั้น (สถานะ HTTP, `code`, ข้อมูลที่คืน, แถวในฐานข้อมูล, ไฟล์ที่ดึงกลับได้)
/// </summary>
[Collection(ApiCollection.Name)]
public class SettingsTests(PostgresFixture postgres)
{
    /// <summary>PNG 1×1 จริง 1 ไฟล์ — ใช้เป็น "ไฟล์ที่ถูกต้อง" โดยไม่ต้องพึ่งไฟล์ภายนอก</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task<HttpClient> OwnerClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ConfigValue(app, "DemoSeed:OwnerEmail"),
            password = ConfigValue(app, "DemoSeed:Password"),
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<HttpClient> KitchenClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ConfigValue(app, "DemoSeed:KitchenEmail"),
            password = ConfigValue(app, "DemoSeed:Password"),
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("code").GetString()!;

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await BodyAsync(response)).GetProperty("message").GetString()!;

    private static void AssertThai(string message)
    {
        Assert.Contains(message, c => c is >= '\u0e00' and <= '\u0e7f');
    }

    /// <summary>ค่าตั้งชุดที่ถูกต้องทั้งหมด — แต่ละเทสต์เปลี่ยนทีละฟิลด์เพื่อให้รู้ว่าฟิลด์ไหนถูกปฏิเสธ</summary>
    private static Dictionary<string, object?> ValidSettings() => new()
    {
        ["name"] = "ชาบูเดโม่",
        ["address"] = "99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110",
        ["phone"] = "02-000-0000",
        ["taxId"] = "0-1055-67000-00-0",
        ["promptPayId"] = "081-234-5678",
        ["serviceChargePercent"] = 10m,
        ["vatPercent"] = 7m,
        ["maxQtyPerItemPerOrder"] = 5,
        ["maxItemsPerOrder"] = 20,
        ["lastOrderWarningMinutes"] = 15,
    };

    private static Dictionary<string, object?> With(string field, object? value)
    {
        var settings = ValidSettings();
        settings[field] = value;
        return settings;
    }

    private static async Task<SettingsSnapshot> ReadSettingsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return SettingsSnapshot.From(await BodyAsync(response));
    }

    [Fact]
    public async Task Saved_settings_come_back_unchanged_from_a_reload_and_from_the_database()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var payload = ValidSettings();
        payload["serviceChargePercent"] = 10.5m;
        payload["vatPercent"] = 7.25m;
        payload["maxQtyPerItemPerOrder"] = 3;
        payload["maxItemsPerOrder"] = 42;
        payload["lastOrderWarningMinutes"] = 20;

        var put = await client.PutAsJsonAsync("/api/settings", payload);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var saved = SettingsSnapshot.From(await BodyAsync(put));

        var reloaded = await ReadSettingsAsync(client);

        Assert.Equal(saved, reloaded);
        Assert.Equal("ชาบูเดโม่", reloaded.Name);
        Assert.Equal("99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110", reloaded.Address);
        Assert.Equal("02-000-0000", reloaded.Phone);
        Assert.Equal("0-1055-67000-00-0", reloaded.TaxId);
        Assert.Equal("081-234-5678", reloaded.PromptPayId);
        Assert.Equal(10.5m, reloaded.ServiceChargePercent);
        Assert.Equal(7.25m, reloaded.VatPercent);
        Assert.Equal(3, reloaded.MaxQtyPerItemPerOrder);
        Assert.Equal(42, reloaded.MaxItemsPerOrder);
        Assert.Equal(20, reloaded.LastOrderWarningMinutes);

        // ร้านเดียว = แถวเดียว: การบันทึกไม่สร้างแถวใหม่
        var rows = await app.WithDbAsync(async db => await db.Settings.ToListAsync());
        Assert.Single(rows);
        Assert.Equal(reloaded.VatPercent, rows[0].VatPercent);
        Assert.Equal(reloaded.MaxItemsPerOrder, rows[0].MaxItemsPerOrder);
    }

    [Theory]
    [InlineData("serviceChargePercent", -1, "ค่าบริการ")]
    [InlineData("serviceChargePercent", 31, "ค่าบริการ")]
    [InlineData("vatPercent", -1, "VAT")]
    [InlineData("vatPercent", 30.01, "VAT")]
    [InlineData("maxQtyPerItemPerOrder", 0, "สูงสุดต่อเมนูต่อรอบ")]
    [InlineData("maxQtyPerItemPerOrder", 100, "สูงสุดต่อเมนูต่อรอบ")]
    [InlineData("maxItemsPerOrder", 0, "สูงสุดต่อรอบ")]
    [InlineData("maxItemsPerOrder", 100, "สูงสุดต่อรอบ")]
    [InlineData("lastOrderWarningMinutes", 0, "Last order")]
    [InlineData("lastOrderWarningMinutes", 100, "Last order")]
    public async Task Out_of_range_setting_is_400_validation_failed_with_a_thai_message_and_is_not_saved(
        string field, decimal value, string expectedLabel)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var before = await ReadSettingsAsync(client);

        var response = await client.PutAsJsonAsync("/api/settings", With(field, value));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains(expectedLabel, message);

        // ค่าที่ผิดต้องไม่ถูกบันทึกเลย
        Assert.Equal(before, await ReadSettingsAsync(client));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task Borderline_percentages_are_accepted(int percent)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var payload = With("serviceChargePercent", percent);
        payload["vatPercent"] = percent;
        var response = await client.PutAsJsonAsync("/api/settings", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((decimal)percent, SettingsSnapshot.From(await BodyAsync(response)).VatPercent);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    public async Task Borderline_limits_are_accepted(int limit)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var payload = With("maxQtyPerItemPerOrder", limit);
        payload["maxItemsPerOrder"] = limit;
        payload["lastOrderWarningMinutes"] = limit;
        var response = await client.PutAsJsonAsync("/api/settings", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = SettingsSnapshot.From(await BodyAsync(response));
        Assert.Equal(limit, saved.MaxQtyPerItemPerOrder);
        Assert.Equal(limit, saved.MaxItemsPerOrder);
        Assert.Equal(limit, saved.LastOrderWarningMinutes);
    }

    [Fact]
    public async Task Empty_shop_name_is_rejected_with_a_thai_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PutAsJsonAsync("/api/settings", With("name", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task Settings_are_401_without_login_and_403_for_the_kitchen_role()
    {
        using var app = new TestApp(postgres);

        var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync("/api/settings", ValidSettings())).StatusCode);

        var kitchen = await KitchenClientAsync(app);
        var get = await kitchen.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
        Assert.Null(get.Headers.Location);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kitchen.PutAsJsonAsync("/api/settings", ValidSettings())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kitchen.PostAsync("/api/settings/banner", PngUpload("banner.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kitchen.DeleteAsync("/api/settings/banner")).StatusCode);
    }

    [Fact]
    public async Task An_uploaded_banner_is_stored_returned_and_served_back_over_http()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PostAsync("/api/settings/banner", PngUpload("banner.png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var path = SettingsSnapshot.From(await BodyAsync(response)).BannerImagePath;
        Assert.NotNull(path);
        Assert.StartsWith("/api/uploads/banner-", path);
        Assert.EndsWith(".png", path);

        // พาธที่คืนต้องดึงกลับมาแสดงได้จริง
        var fetched = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Equal("image/png", fetched.Content.Headers.ContentType?.MediaType);
        Assert.Equal(OnePixelPng, await fetched.Content.ReadAsByteArrayAsync());

        // ค่าที่ดึงใหม่ต้องชี้ไฟล์เดิม
        Assert.Equal(path, (await ReadSettingsAsync(client)).BannerImagePath);

        var clear = await client.DeleteAsync("/api/settings/banner");
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null(SettingsSnapshot.From(await BodyAsync(clear)).BannerImagePath);
        Assert.Null((await ReadSettingsAsync(client)).BannerImagePath);
    }

    [Fact]
    public async Task Banner_upload_rejects_the_wrong_type_and_an_oversized_file_with_the_contract_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var before = (await ReadSettingsAsync(client)).BannerImagePath;

        // นามสกุลไม่ตรงกับชนิดไฟล์
        var wrongType = await client.PostAsync("/api/settings/banner", Upload("notes.txt", "text/plain",
            Encoding.UTF8.GetBytes("ไม่ใช่รูป")));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(wrongType));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(wrongType));

        // ชนิดถูกแต่เนื้อไฟล์ไม่ใช่รูป → ไม่ตรวจ magic byte ตามสัญญา (ตรวจชนิด + นามสกุล + ขนาด)
        var mismatchedExtension = await client.PostAsync("/api/settings/banner", Upload("photo.jpg", "image/png",
            OnePixelPng));
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedExtension.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(mismatchedExtension));

        // เกิน 2 MB
        var oversized = await client.PostAsync("/api/settings/banner", Upload("huge.jpg", "image/jpeg",
            new byte[2 * 1024 * 1024 + 1]));
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(oversized));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(oversized));

        // 2 MB พอดีถือว่าผ่าน (ขอบเขต)
        var boundary = await client.PostAsync("/api/settings/banner", Upload("exact.jpg", "image/jpeg",
            new byte[2 * 1024 * 1024]));
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);

        // ไฟล์ที่ถูกปฏิเสธต้องไม่ถูกบันทึก
        Assert.NotNull((await ReadSettingsAsync(client)).BannerImagePath);
        Assert.NotEqual(before, (await ReadSettingsAsync(client)).BannerImagePath);
    }

    [Fact]
    public async Task Banner_upload_without_a_chosen_file_is_rejected_with_the_contract_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        // A multipart body that carries no `file` part at all: the endpoint must answer with the same
        // Thai sentence as every other bad upload rather than a framework error the UI cannot read.
        var form = new MultipartFormDataContent { { new StringContent("x"), "not-a-file" } };

        var response = await client.PostAsync("/api/settings/banner", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        Assert.Equal("ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB", await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task Banner_path_cannot_be_forged_through_put()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var before = (await ReadSettingsAsync(client)).BannerImagePath;

        var payload = ValidSettings();
        payload["bannerImagePath"] = "http://example.com/evil.png";
        var response = await client.PutAsJsonAsync("/api/settings", payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, (await ReadSettingsAsync(client)).BannerImagePath);
    }

    private static MultipartFormDataContent Upload(string fileName, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static MultipartFormDataContent PngUpload(string fileName) =>
        Upload(fileName, "image/png", OnePixelPng);

    /// <summary>รูปทรงของ SettingsDto ที่เทสต์สนใจ — เทียบทั้งก้อนเพื่อจับ "ค่าที่ไม่ควรเปลี่ยน" ได้ด้วย</summary>
    private sealed record SettingsSnapshot(
        string Name, string Address, string Phone, string? TaxId, string? PromptPayId, string? BannerImagePath,
        decimal ServiceChargePercent, decimal VatPercent, int MaxQtyPerItemPerOrder, int MaxItemsPerOrder,
        int LastOrderWarningMinutes)
    {
        public static SettingsSnapshot From(JsonElement e) => new(
            e.GetProperty("name").GetString()!,
            e.GetProperty("address").GetString()!,
            e.GetProperty("phone").GetString()!,
            e.TryGetProperty("taxId", out var tax) ? tax.GetString() : null,
            e.TryGetProperty("promptPayId", out var pp) ? pp.GetString() : null,
            e.TryGetProperty("bannerImagePath", out var banner) ? banner.GetString() : null,
            e.GetProperty("serviceChargePercent").GetDecimal(),
            e.GetProperty("vatPercent").GetDecimal(),
            e.GetProperty("maxQtyPerItemPerOrder").GetInt32(),
            e.GetProperty("maxItemsPerOrder").GetInt32(),
            e.GetProperty("lastOrderWarningMinutes").GetInt32());
    }
}
