using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Seam 1 ของ ticket 04: แพ็กเกจบุฟเฟ่ต์ผ่าน HTTP ของแอปจริงบน Postgres จริง
/// ตรวจเฉพาะพฤติกรรมภายนอก (สถานะ, `code`, ข้อมูลที่คืน, แถวในฐานข้อมูล) ไม่ตรวจโครงสร้างภายใน
/// </summary>
[Collection(ApiCollection.Name)]
public class PackagesTests(PostgresFixture postgres)
{
    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task LoginOwnerAsync(TestApp app, HttpClient client) =>
        await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), ConfigValue(app, "DemoSeed:Password"));

    private static async Task LoginKitchenAsync(TestApp app, HttpClient client) =>
        await LoginAsync(client, ConfigValue(app, "DemoSeed:KitchenEmail"), ConfigValue(app, "DemoSeed:Password"));

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await ProblemAsync(response)).GetProperty("code").GetString()!;

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await ProblemAsync(response)).GetProperty("message").GetString()!;

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, object body) =>
        client.PostAsJsonAsync("/api/packages", body);

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, int id, object body) =>
        client.PutAsJsonAsync($"/api/packages/{id}", body);

    private static object Body(string name, int rank, decimal adult = 299m, decimal child = 149m,
        int minutes = 90, bool active = true) => new { name, rank, adultPrice = adult, childPrice = child, durationMinutes = minutes, isActive = active };

    /// <summary>ระดับว่างชุดแรก — ฐานข้อมูลถูกใช้ร่วมกันในคอลเลกชัน จึงไม่ hardcode rank ที่อาจถูกใช้ไปแล้ว</summary>
    private static async Task<List<int>> FreeRanksAsync(TestApp app, int count)
    {
        var taken = await app.WithDbAsync(db => db.Packages.Select(p => p.Rank).ToListAsync());
        return Enumerable.Range(1, 99).Where(r => !taken.Contains(r)).Take(count).ToList();
    }

    private static async Task<int> CreateAsync(HttpClient client, string name, int rank,
        decimal adult = 299m, decimal child = 149m, int minutes = 90)
    {
        var response = await PostAsync(client, Body(name, rank, adult, child, minutes));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement[]> ReadPackagesAsync(HttpClient client)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/packages"));
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    [Fact]
    public async Task Creating_three_tiers_lists_them_in_rank_order_with_their_prices_and_duration()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 3);

        var silver = await CreateAsync(client, "Silver", ranks[0], 299m, 149m, 90);
        var gold = await CreateAsync(client, "Gold", ranks[1], 399m, 199m, 90);
        var platinum = await CreateAsync(client, "Platinum", ranks[2], 549m, 279m, 120);

        var packages = await ReadPackagesAsync(client);
        var mine = packages.Where(p => p.GetProperty("id").GetInt32() is var id && (id == silver || id == gold || id == platinum)).ToArray();

        Assert.Equal(3, mine.Length);
        // เรียงตามระดับจากน้อยไปมาก และข้อมูลที่คืนตรงกับที่สร้าง
        Assert.Equal(new[] { "Silver", "Gold", "Platinum" }, mine.Select(p => p.GetProperty("name").GetString()).ToArray());
        Assert.Equal(new[] { ranks[0], ranks[1], ranks[2] }, mine.Select(p => p.GetProperty("rank").GetInt32()).ToArray());
        Assert.Equal(399m, mine[1].GetProperty("adultPrice").GetDecimal());
        Assert.Equal(199m, mine[1].GetProperty("childPrice").GetDecimal());
        Assert.Equal(120, mine[2].GetProperty("durationMinutes").GetInt32());
        Assert.True(mine[2].GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task A_duplicate_rank_on_create_is_409_validation_failed_with_a_thai_message_naming_the_other_package()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 1);
        await CreateAsync(client, "Silver", ranks[0]);

        var response = await PostAsync(client, Body("Gold", ranks[0], 399m, 199m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        Assert.Contains("ระดับนี้ซ้ำกับ Silver", message);
        // คำใบ้ต้องเป็นระดับว่างที่ถัดไปจริง ไม่ใช่เลขเดา
        var nextFree = await app.WithDbAsync(db => db.Packages.MaxAsync(p => p.Rank)) + 1;
        Assert.Contains($"เลือกระดับ {nextFree} แทน", message);
        Assert.True(message.Any(c => c >= '\u0e00' && c <= '\u0e7f'), $"ข้อความต้องเป็นภาษาไทย: {message}");
    }

    [Fact]
    public async Task A_duplicate_rank_on_update_is_409_and_keeps_the_stored_value()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 2);
        await CreateAsync(client, "Silver", ranks[0]);
        var goldId = await CreateAsync(client, "Gold", ranks[1], 399m, 199m);

        var response = await PutAsync(client, goldId, Body("Gold", ranks[0], 399m, 199m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        Assert.Contains("ระดับนี้ซ้ำกับ Silver", await ProblemMessageAsync(response));

        // แถวเดิมต้องไม่ถูกแก้ (rank ยังเป็นค่าเดิม)
        var rank = await app.WithDbAsync(db =>
            db.Packages.Where(p => p.Id == goldId).Select(p => p.Rank).SingleAsync());
        Assert.Equal(ranks[1], rank);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task A_rank_outside_1_to_99_is_400_validation_failed_with_a_thai_message(int rank)
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);

        var response = await PostAsync(client, Body("Silver", rank));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        Assert.Contains("ระดับ", await ProblemMessageAsync(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_name_is_400_validation_failed(string name)
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 1);

        var response = await PostAsync(client, Body(name, ranks[0]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        Assert.Contains("ชื่อแพ็กเกจ", await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task Prices_and_duration_outside_their_bounds_are_400_validation_failed_with_thai_messages()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 1);
        var before = await app.WithDbAsync(db => db.Packages.CountAsync());
        var name = $"ขอบเขต-{Guid.NewGuid():N}";

        var zeroAdult = await PostAsync(client, Body(name, ranks[0], adult: 0m));
        Assert.Equal(HttpStatusCode.BadRequest, zeroAdult.StatusCode);
        Assert.Contains("ราคาผู้ใหญ่", await ProblemMessageAsync(zeroAdult));

        var childOverAdult = await PostAsync(client, Body(name, ranks[0], adult: 299m, child: 300m));
        Assert.Equal(HttpStatusCode.BadRequest, childOverAdult.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(childOverAdult));
        Assert.Contains("ราคาเด็ก", await ProblemMessageAsync(childOverAdult));

        var negativeChild = await PostAsync(client, Body(name, ranks[0], child: -1m));
        Assert.Equal(HttpStatusCode.BadRequest, negativeChild.StatusCode);
        Assert.Contains("ราคาเด็ก", await ProblemMessageAsync(negativeChild));

        foreach (var minutes in new[] { 0, 601 })
        {
            var duration = await PostAsync(client, Body(name, ranks[0], minutes: minutes));
            Assert.Equal(HttpStatusCode.BadRequest, duration.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(duration));
            Assert.Contains("เวลา", await ProblemMessageAsync(duration));
        }

        // ไม่มีแถวใดถูกสร้างจากคำขอที่ผิด (นับเทียบก่อน/หลังเพราะฐานข้อมูลใช้ร่วมกันทั้งคอลเลกชัน)
        Assert.Equal(before, await app.WithDbAsync(db => db.Packages.CountAsync()));
    }

    [Fact]
    public async Task Updating_a_package_changes_its_values_and_disable_keeps_the_row_with_is_active_false()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 1);
        var id = await CreateAsync(client, "Silver", ranks[0]);

        var updated = await PutAsync(client, id, Body("Silver Plus", ranks[0], 349m, 169m, 120, active: false));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var doc = JsonDocument.Parse(await updated.Content.ReadAsStringAsync());
        Assert.Equal("Silver Plus", doc.RootElement.GetProperty("name").GetString());
        Assert.Equal(349m, doc.RootElement.GetProperty("adultPrice").GetDecimal());
        Assert.Equal(120, doc.RootElement.GetProperty("durationMinutes").GetInt32());
        Assert.False(doc.RootElement.GetProperty("isActive").GetBoolean());

        // ปิดใช้งานคือแก้ค่า ไม่ใช่ลบแถว: แถวยังอยู่และประวัติยังอ้างถึงได้
        var stored = await app.WithDbAsync(db => db.Packages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id));
        Assert.NotNull(stored);
        Assert.False(stored!.IsActive);
        Assert.Equal("Silver Plus", stored.Name);
    }

    [Fact]
    public async Task There_is_no_delete_route_for_packages()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);
        var ranks = await FreeRanksAsync(app, 1);
        var id = await CreateAsync(client, "Silver", ranks[0]);

        var onCollection = await client.DeleteAsync("/api/packages");
        var onItem = await client.DeleteAsync($"/api/packages/{id}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, onCollection.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, onItem.StatusCode);
        // และแถวยังอยู่จริง
        var stillThere = await app.WithDbAsync(db => db.Packages.AsNoTracking().AnyAsync(p => p.Id == id));
        Assert.True(stillThere);
    }

    [Fact]
    public async Task Updating_a_package_that_does_not_exist_is_404()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        await LoginOwnerAsync(app, client);

        var missing = await PutAsync(client, 999_999, Body("Ghost", 1));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Packages_endpoints_reject_anonymous_with_401_and_kitchen_with_403()
    {
        using var app = new TestApp(postgres);
        var anonymous = app.CreateClient();
        var ranks = await FreeRanksAsync(app, 1);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/packages")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsync(anonymous, Body("Silver", ranks[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await PutAsync(anonymous, 1, Body("Silver", ranks[0]))).StatusCode);

        var kitchen = app.CreateClient();
        await LoginKitchenAsync(app, kitchen);

        var list = await kitchen.GetAsync("/api/packages");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(kitchen, Body("Silver", ranks[0]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PutAsync(kitchen, 1, Body("Silver", ranks[0]))).StatusCode);
    }
}
