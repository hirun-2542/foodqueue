using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 14 — คิวออเดอร์ของเจ้าของร้าน ผ่าน seam 1 (แอปจริง + Postgres จริง)
/// หน้าจอ <c>/owner/orders</c> อ่านสองชุดจาก <c>GET /api/orders</c> ของ ticket 11 โดยไม่มี endpoint ใหม่:
/// - คิว = ค่าเริ่มต้น (Pending/Preparing/Ready ของรอบที่ยัง Open) — ชุดเดียวกับจอครัว
/// - "เสร็จแล้ววันนี้" = <c>?status=Ready,Cancelled&amp;date={วันนี้ตามเวลาไทย}</c>
/// และเลื่อนสถานะ/ยกเลิกด้วยบัญชีเจ้าของร้านผ่านเส้นทางเดียวกับครัว (ticket 12)
/// </summary>
[Collection(ApiCollection.Name)]
public class OwnerQueueTests(PostgresFixture postgres)
{
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

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static int IdOf(JsonElement order) => order.GetProperty("id").GetInt32();

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string url) =>
        (await OkAsync(await client.GetAsync(url))).EnumerateArray().ToList();

    /// <summary>Rank มี unique index ร่วมกับเทสต์อื่นในคอลเลกชัน จึงสร้างผ่านฐานข้อมูลด้วย rank ที่ยังว่าง</summary>
    private static Task<int> SeedPackageAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var package = new Package
            {
                Name = $"P{Guid.NewGuid():N}"[..12],
                Rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r)),
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private static async Task<int> CreateBuffetItemAsync(HttpClient owner)
    {
        var category = await CreatedAsync(await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") }));
        var item = await CreatedAsync(await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = UniqueName("หมู"),
            categoryId = category.GetProperty("id").GetInt32(),
            kind = "Buffet",
            minPackageRank = 1,
            price = 0m,
            isAvailable = true,
        }));
        return item.GetProperty("id").GetInt32();
    }

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private sealed record OpenedSession(int Id, string Token);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner)
    {
        var table = await CreatedAsync(await owner.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("Q"), seats = 4, zone = "A" }));
        var packageId = await SeedPackageAsync(app);
        var session = await CreatedAsync(await owner.PostAsJsonAsync("/api/sessions",
            new { tableId = table.GetProperty("id").GetInt32(), packageId, adultCount = 2, childCount = 0 }));
        var id = session.GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token);
    }

    /// <summary>
    /// หนึ่งรอบ หนึ่งที่ — ค่าเพดานต่อเมนูอยู่ใน Settings ที่เทสต์อื่นในคอลเลกชันเดียวกันปรับได้ จึงไม่พึ่งจำนวนที่มากกว่า 1
    /// </summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, int menuItemId)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders",
            new { items = new[] { new { menuItemId, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSessionId == session.Id)
            .OrderByDescending(o => o.Number).Select(o => o.Id).FirstAsync());
    }

    private static async Task<JsonElement> AdvanceAsync(HttpClient owner, int orderId, string status) =>
        await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status }));

    [Fact]
    public async Task The_owner_works_the_queue_with_their_own_account_and_finished_rounds_move_to_today_list()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var itemId = await CreateBuffetItemAsync(owner);
        var session = await OpenSessionAsync(app, owner);

        var pending = await SubmitAsync(app, session, itemId);
        var preparing = await SubmitAsync(app, session, itemId);
        var ready = await SubmitAsync(app, session, itemId);
        var cancelled = await SubmitAsync(app, session, itemId);

        // เริ่มทำ / พร้อมเสิร์ฟ ด้วยบัญชีเจ้าของร้าน (เส้นทางเดียวกับจอครัว)
        Assert.Equal("Preparing", (await AdvanceAsync(owner, preparing, "Preparing")).GetProperty("status").GetString());
        await AdvanceAsync(owner, ready, "Preparing");
        Assert.Equal("Ready", (await AdvanceAsync(owner, ready, "Ready")).GetProperty("status").GetString());

        // ยกเลิกพร้อมเหตุผลด้วยบัญชีเจ้าของร้าน
        var cancel = await OkAsync(await owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel",
            new { reason = "วัตถุดิบหมด: กุ้งขาว" }));
        Assert.Equal("Cancelled", cancel.GetProperty("status").GetString());

        var createdAt = await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.Id == cancelled).Select(o => o.CreatedAt).SingleAsync());
        var today = Bkk.DateOf(createdAt).ToString("yyyy-MM-dd");
        var todayUrl = $"/api/orders?status=Ready,Cancelled&date={today}";

        // คิว: รอรับ / กำลังทำ / พร้อมเสิร์ฟ ของโต๊ะที่ยังไม่ปิดบิล — ไม่มีรอบที่ยกเลิกแล้ว
        var queue = (await ListAsync(owner, "/api/orders")).Where(o => o.GetProperty("sessionId").GetInt32() == session.Id)
            .ToDictionary(IdOf, o => o.GetProperty("status").GetString());
        Assert.Equal(new Dictionary<int, string?>
        {
            [pending] = "Pending",
            [preparing] = "Preparing",
            [ready] = "Ready",
        }, queue);

        // เสร็จแล้ววันนี้: พร้อมเสิร์ฟ + ยกเลิก (พร้อมเหตุผล) เท่านั้น
        var done = (await ListAsync(owner, todayUrl)).Where(o => o.GetProperty("sessionId").GetInt32() == session.Id)
            .ToDictionary(IdOf);
        Assert.Equal([ready, cancelled], done.Keys.Order().ToArray());
        Assert.Equal("Ready", done[ready].GetProperty("status").GetString());
        Assert.Equal("วัตถุดิบหมด: กุ้งขาว", done[cancelled].GetProperty("cancelReason").GetString());

        // โต๊ะชำระเงินแล้ว (ticket 18 — ตั้งด้วยข้อมูล): รอบหลุดจากคิว แต่ยังอยู่ในรายการของวันนี้
        await app.WithDbAsync(db => db.TableSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SessionStatus.Paid)
            .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow)));

        Assert.DoesNotContain(await ListAsync(owner, "/api/orders"),
            o => o.GetProperty("sessionId").GetInt32() == session.Id);
        Assert.Equal([ready, cancelled], (await ListAsync(owner, todayUrl))
            .Where(o => o.GetProperty("sessionId").GetInt32() == session.Id).Select(IdOf).Order().ToArray());
    }
}
