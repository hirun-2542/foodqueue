using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 06 — จัดการโต๊ะ (ชื่อ ที่นั่ง โซน ปิดใช้งาน) ผ่าน seam 1: แอปจริง + Postgres จริง
/// ทุกเทสต์ยืนยันพฤติกรรมภายนอก (สถานะ HTTP, `code`, ข้อมูลที่คืน, สถานะในฐานข้อมูลที่ผู้ใช้เห็น)
/// </summary>
[Collection(ApiCollection.Name)]
public class TablesTests(PostgresFixture postgres)
{
    private sealed record TableBody(int Id, string Name, int Seats, string? Zone, int SortOrder, bool IsActive);

    /// <summary>Package.Rank มี unique index — กันชนกันข้ามเทสต์ด้วยตัวนับที่เพิ่มขึ้นเรื่อย ๆ</summary>
    private static int NextRank = 900_000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static async Task<HttpClient> OwnerClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        var owner = app.Services.GetRequiredService<IConfiguration>()["DemoSeed:OwnerEmail"]!;
        var password = app.Services.GetRequiredService<IConfiguration>()["DemoSeed:Password"]!;
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = owner, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<HttpClient> KitchenClientAsync(TestApp app)
    {
        var client = app.CreateClient();
        var kitchen = app.Services.GetRequiredService<IConfiguration>()["DemoSeed:KitchenEmail"]!;
        var password = app.Services.GetRequiredService<IConfiguration>()["DemoSeed:Password"]!;
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = kitchen, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<TableBody> BodyAsync(HttpResponseMessage response)
    {
        var table = JsonSerializer.Deserialize<TableBody>(await response.Content.ReadAsStringAsync(), Json);
        Assert.NotNull(table);
        return table!;
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("message").GetString()!;
    }

    private static void AssertThai(string message)
    {
        Assert.True(message.Any(c => c >= '\u0e00' && c <= '\u0e7f'), $"ข้อความต้องเป็นภาษาไทย: {message}");
    }

    /// <summary>ไม่มี GET รายตัว: อ่านสถานะปัจจุบันของโต๊ะผ่านรายการ (สิ่งที่ผู้ใช้เห็นจริงบนผัง)</summary>
    private static async Task<TableBody> ReadBackAsync(HttpClient client, int id)
    {
        var response = await client.GetAsync("/api/tables");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var all = JsonSerializer.Deserialize<List<TableBody>>(await response.Content.ReadAsStringAsync(), Json)!;
        return Assert.Single(all, t => t.Id == id);
    }

    /// <summary>ชื่อโต๊ะในเทสต์ไม่ซ้ำกันเสมอ เพราะทุกเทสต์ในคอลเลกชันใช้ฐานข้อมูลเดียวกัน</summary>
    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    [Fact]
    public async Task Owner_creates_a_table_and_reads_it_back_from_the_list()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var name = UniqueName("T");

        var created = await client.PostAsJsonAsync("/api/tables",
            new { name = $"  {name}  ", seats = 6, zone = "A" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await BodyAsync(created);
        Assert.Equal(name, body.Name); // ตัดช่องว่างหัวท้าย
        Assert.Equal(6, body.Seats);
        Assert.Equal("A", body.Zone);
        Assert.True(body.IsActive);
        Assert.True(body.SortOrder > 0);
        Assert.Equal($"/api/tables/{body.Id}", created.Headers.Location?.ToString());

        var list = await client.GetAsync("/api/tables");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var all = JsonSerializer.Deserialize<List<TableBody>>(await list.Content.ReadAsStringAsync(), Json)!;
        Assert.Contains(all, t => t.Id == body.Id && t.Name == name && t.Seats == 6);
        // เรียงตามผัง: SortOrder จากน้อยไปมาก
        Assert.Equal(all.Select(t => t.SortOrder).Order(), all.Select(t => t.SortOrder));
    }

    [Fact]
    public async Task Owner_updates_name_seats_and_zone_and_the_change_is_visible_on_reload()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var created = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("U"), seats = 4, zone = "A" }));

        var renamed = UniqueName("V");
        var updated = await client.PutAsJsonAsync($"/api/tables/{created.Id}",
            new { name = renamed, seats = 12, zone = "B" });

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await BodyAsync(updated);
        Assert.Equal(created.Id, body.Id);
        Assert.Equal(renamed, body.Name);
        Assert.Equal(12, body.Seats);
        Assert.Equal("B", body.Zone);
        Assert.Equal(created.SortOrder, body.SortOrder);
        Assert.True(body.IsActive);

        var reread = await ReadBackAsync(client, created.Id);
        Assert.Equal(renamed, reread.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(21)]
    [InlineData(99)]
    public async Task Seats_outside_1_to_20_are_rejected_with_validation_failed(int seats)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("S"), seats, zone = "A" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task Seats_outside_the_range_are_also_rejected_when_updating()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var table = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("W"), seats = 4 }));

        foreach (var seats in new[] { 0, 21 })
        {
            var response = await client.PutAsJsonAsync($"/api/tables/{table.Id}",
                new { name = table.Name, seats });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
            AssertThai(await ProblemMessageAsync(response));
        }

        // ค่าที่เคยถูกต้องต้องไม่ถูกเขียนทับด้วยคำขอที่ล้มเหลว
        Assert.Equal(4, (await ReadBackAsync(client, table.Id)).Seats);
    }

    [Fact]
    public async Task The_boundary_seats_1_and_20_are_accepted()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var one = await client.PostAsJsonAsync("/api/tables", new { name = UniqueName("P"), seats = 1 });
        var twenty = await client.PostAsJsonAsync("/api/tables", new { name = UniqueName("Q"), seats = 20 });

        Assert.Equal(HttpStatusCode.Created, one.StatusCode);
        Assert.Equal(HttpStatusCode.Created, twenty.StatusCode);
        Assert.Equal(1, (await BodyAsync(one)).Seats);
        Assert.Equal(20, (await BodyAsync(twenty)).Seats);
    }

    [Fact]
    public async Task A_duplicate_table_name_is_rejected_with_a_thai_message_about_the_name_field()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var existing = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("D"), seats = 4 }));

        var response = await client.PostAsJsonAsync("/api/tables", new { name = existing.Name, seats = 4 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains("ชื่อโต๊ะ", message);

        // ฝั่งฐานข้อมูลต้องมีโต๊ะชื่อนั้นใบเดียว
        var count = await app.WithDbAsync(db => db.Tables.CountAsync(t => t.Name == existing.Name));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Two_concurrent_creates_with_the_same_name_leave_one_table_and_one_validation_error()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var name = UniqueName("C");

        // เช็คก่อนหน้าอาจมีคำขออื่นแทรก: unique index บน Table.Name ต้องกันได้ และต้องตอบเป็น
        // VALIDATION_FAILED พร้อมข้อความไทย ไม่ใช่ 500 จาก Postgres
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ =>
                client.PostAsJsonAsync("/api/tables", new { name, seats = 4 })));

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        var rejected = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(1, created);
        Assert.Single(rejected);
        Assert.Equal(HttpStatusCode.BadRequest, rejected[0].StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(rejected[0]));
        AssertThai(await ProblemMessageAsync(rejected[0]));
        Assert.Equal(1, await app.WithDbAsync(db => db.Tables.CountAsync(t => t.Name == name)));
    }

    [Fact]
    public async Task Renaming_a_table_onto_another_tables_name_is_rejected_but_keeping_its_own_name_is_fine()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var first = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("E"), seats = 4 }));
        var second = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("F"), seats = 4 }));

        var clash = await client.PutAsJsonAsync($"/api/tables/{second.Id}",
            new { name = first.Name, seats = 4 });
        Assert.Equal(HttpStatusCode.BadRequest, clash.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(clash));

        // ส่งชื่อเดิมของตัวเองกลับไปต้องไม่นับเป็นชื่อซ้ำ
        var sameName = await client.PutAsJsonAsync($"/api/tables/{second.Id}",
            new { name = second.Name, seats = 8 });
        Assert.Equal(HttpStatusCode.OK, sameName.StatusCode);
        Assert.Equal(8, (await BodyAsync(sameName)).Seats);
    }

    [Fact]
    public async Task Disabling_a_table_keeps_the_row_and_only_flips_is_active()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var name = UniqueName("G");
        var table = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name, seats = 4, zone = "B" }));

        var disabled = await client.PutAsJsonAsync($"/api/tables/{table.Id}",
            new { name, seats = 4, zone = "B", isActive = false });

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        var body = await BodyAsync(disabled);
        Assert.False(body.IsActive);
        Assert.Equal(table.Id, body.Id);

        // แถวยังอยู่ (soft disable ไม่ใช่การลบ) และยังเห็นได้ในรายการเพื่อเปิดกลับ
        var rows = await app.WithDbAsync(db => db.Tables.Where(t => t.Id == table.Id).ToListAsync());
        Assert.Single(rows);
        Assert.False(rows[0].IsActive);

        var listed = JsonSerializer.Deserialize<List<TableBody>>(
            await (await client.GetAsync("/api/tables")).Content.ReadAsStringAsync(), Json)!;
        Assert.Contains(listed, t => t.Id == table.Id && !t.IsActive);

        // เปิดกลับได้ด้วยคำขอเดียวกัน
        var enabled = await client.PutAsJsonAsync($"/api/tables/{table.Id}",
            new { name, seats = 4, zone = "B", isActive = true });
        Assert.True((await BodyAsync(enabled)).IsActive);
    }

    [Fact]
    public async Task A_table_cannot_be_disabled_while_it_has_an_open_session()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var name = UniqueName("H");
        var table = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name, seats = 4 }));

        // ยังไม่มีเส้นทางเปิดโต๊ะ (ticket 07) จึงใส่ session ลงฐานข้อมูลทดสอบตรง ๆ
        var packageId = await app.WithDbAsync(async db =>
        {
            var package = new Package
            {
                Name = $"P{Guid.NewGuid():N}"[..12],
                Rank = Interlocked.Increment(ref NextRank),
                AdultPrice = 399,
                ChildPrice = 199,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            var session = new TableSession
            {
                TableId = table.Id,
                Package = package,
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..11],
                AdultCount = 2,
                PackageNameSnapshot = package.Name,
                AdultPriceSnapshot = package.AdultPrice,
                ChildPriceSnapshot = package.ChildPrice,
                PackageRankSnapshot = package.Rank,
                OpenedAt = DateTimeOffset.UtcNow,
                EndsAt = DateTimeOffset.UtcNow.AddMinutes(90),
                Status = SessionStatus.Open,
            };
            db.TableSessions.Add(session);
            await db.SaveChangesAsync();
            return package.Id;
        });
        Assert.True(packageId > 0);

        var response = await client.PutAsJsonAsync($"/api/tables/{table.Id}",
            new { name, seats = 4, isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("TABLE_OCCUPIED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
        Assert.True((await ReadBackAsync(client, table.Id)).IsActive);

        // แก้ชื่อ/ที่นั่งของโต๊ะที่ถูกนั่งอยู่ยังทำได้ ปิดใช้งานอย่างเดียวที่ถูกห้าม
        var renamed = UniqueName("J");
        var edit = await client.PutAsJsonAsync($"/api/tables/{table.Id}", new { name = renamed, seats = 6 });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(renamed, (await BodyAsync(edit)).Name);
    }

    [Fact]
    public async Task There_is_no_route_that_deletes_a_table_row()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var table = await BodyAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("K"), seats = 4 }));

        var response = await client.DeleteAsync($"/api/tables/{table.Id}");

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"คาดว่า 404/405 แต่ได้ {(int)response.StatusCode}");
        Assert.Equal(1, await app.WithDbAsync(db => db.Tables.CountAsync(t => t.Id == table.Id)));
    }

    [Fact]
    public async Task Updating_a_table_that_does_not_exist_is_404()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.PutAsJsonAsync("/api/tables/987654321",
            new { name = UniqueName("L"), seats = 4 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Kitchen_is_forbidden_on_every_tables_endpoint_and_anonymous_is_401()
    {
        using var app = new TestApp(postgres);

        var owner = await OwnerClientAsync(app);
        var table = await BodyAsync(await owner.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("M"), seats = 4 }));
        var before = await app.WithDbAsync(db => db.Tables.CountAsync());

        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/tables")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/api/tables", new { name = UniqueName("N"), seats = 4 })).StatusCode);

        var kitchen = await KitchenClientAsync(app);
        var list = await kitchen.GetAsync("/api/tables");
        var create = await kitchen.PostAsJsonAsync("/api/tables", new { name = UniqueName("O"), seats = 4 });
        var update = await kitchen.PutAsJsonAsync($"/api/tables/{table.Id}",
            new { name = table.Name, seats = 8 });

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        Assert.Null(list.Headers.Location);
        Assert.Equal(before, await app.WithDbAsync(db => db.Tables.CountAsync()));
        Assert.Equal(4, (await ReadBackAsync(owner, table.Id)).Seats);
    }
}
