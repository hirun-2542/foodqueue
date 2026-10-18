using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static FoodQueue.Api.Tests.LiveClient;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 13 — แท็บ "ออเดอร์" ของลูกค้าและ "หมด" แบบ real-time ผ่าน seam 1 (แอปจริง + Postgres จริง)
/// <c>GET /api/s/{token}/orders</c> คืนรายการรวมของโต๊ะ: จำนวนรวมต่อ (เมนู, หมายเหตุ) ไม่มีรอบ ไม่มีสถานะ ไม่มียอด
/// และการสลับ "หมด" ของเจ้าของร้านส่ง <c>menuItemChanged</c> ถึงการเชื่อมต่อของลูกค้าจริงผ่าน hub
/// </summary>
[Collection(ApiCollection.Name)]
public class CustomerOrdersTests(PostgresFixture postgres)
{
    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task<HttpClient> LoginAsync(TestApp app, string emailKey)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = ConfigValue(app, emailKey),
            password = ConfigValue(app, "DemoSeed:Password"),
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

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

    private sealed record Menu(int CategoryId, int MeatId, string MeatName, int DrinkId, string DrinkName);

    /// <summary>
    /// เนื้อบุฟเฟ่ต์ (มีรูป) และเครื่องดื่ม à la carte ราคา 45 ผ่านเส้นทางจริงของ ticket 05 — ลิมิตของร้านเป็นแถวเดียว
    /// ที่เทสต์อื่นในคอลเลกชันแก้ได้ จึงตั้งค่าเริ่มต้น (5 ต่อเมนู / 20 ต่อรอบ) ผ่าน endpoint จริงก่อนเสมอ
    /// </summary>
    private static async Task<Menu> CreateMenuAsync(TestApp app, HttpClient owner)
    {
        var settings = await owner.PutAsJsonAsync("/api/settings", new
        {
            name = "ชาบูเดโม่",
            address = "99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110",
            phone = "02-000-0000",
            serviceChargePercent = 10m,
            vatPercent = 7m,
            maxQtyPerItemPerOrder = 5,
            maxItemsPerOrder = 20,
            lastOrderWarningMinutes = 15,
        });
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);

        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        var meatName = UniqueName("หมู");
        var meat = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = meatName, categoryId, kind = "Buffet", minPackageRank = 1, price = 0m, isAvailable = true,
        });
        Assert.Equal(HttpStatusCode.Created, meat.StatusCode);
        var meatId = (await JsonAsync(meat)).GetProperty("id").GetInt32();

        var drinkName = UniqueName("โค้ก");
        var drink = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = drinkName, categoryId, kind = "AlaCarte", price = 45m,
        });
        Assert.Equal(HttpStatusCode.Created, drink.StatusCode);

        // รูปเป็นของ ticket 05 (อัปโหลด) — ที่นี่สนใจแค่ว่าแถวของลูกค้าพาพาธรูปเล็กไปด้วย
        await app.WithDbAsync(db => db.MenuItems.Where(m => m.Id == meatId)
            .ExecuteUpdateAsync(m => m.SetProperty(x => x.ImagePath, "/api/uploads/meat.jpg")));

        return new Menu(categoryId, meatId, meatName, (await JsonAsync(drink)).GetProperty("id").GetInt32(), drinkName);
    }

    private sealed record OpenedSession(int Id, string Token);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner)
    {
        var table = await owner.PostAsJsonAsync("/api/tables", new { name = UniqueName("T"), seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();
        var packageId = await SeedPackageAsync(app);

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = 2, childCount = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token);
    }

    /// <summary>มือถือเครื่องหนึ่งของโต๊ะส่งออเดอร์ด้วย token (ไม่มี auth) แล้วคืน id ของรอบที่เพิ่งบันทึก</summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, params object[] items)
    {
        using var phone = app.CreateClient();
        var response = await phone.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSessionId == session.Id)
            .OrderByDescending(o => o.Number).Select(o => o.Id).FirstAsync());
    }

    private static object Line(int menuItemId, int quantity, string? note = null) => new { menuItemId, quantity, note };

    private sealed record Row(int MenuItemId, string Name, string Kind, int Quantity, string? Note, string? ImagePath);

    private static async Task<JsonElement> TableOrdersJsonAsync(TestApp app, string token)
    {
        using var phone = app.CreateClient();
        var response = await phone.GetAsync($"/api/s/{token}/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    /// <summary>JSON ของ API ไม่เขียนค่า null — หมายเหตุ/รูปที่ไม่มีจึงไม่มีช่องนั้นเลย</summary>
    private static string? Optional(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static async Task<List<Row>> TableOrdersAsync(TestApp app, string token) =>
        (await TableOrdersJsonAsync(app, token)).GetProperty("items").EnumerateArray()
            .Select(i => new Row(
                i.GetProperty("menuItemId").GetInt32(),
                i.GetProperty("name").GetString()!,
                i.GetProperty("kind").GetString()!,
                i.GetProperty("quantity").GetInt32(),
                Optional(i, "note"),
                Optional(i, "imagePath")))
            .ToList();

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
    }

    // ------------------------------------------------------------------ รายการรวมของโต๊ะ

    [Fact]
    public async Task Two_phones_orders_of_the_same_item_sum_into_one_row_and_a_noted_line_keeps_its_note()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var menu = await CreateMenuAsync(app, owner);
        var table = await OpenSessionAsync(app, owner);

        // มือถือ A: หมู 2 + หมูไม่ติดมัน 1 + โค้ก 1 · มือถือ B (token เดียวกัน): หมู 1 + โค้ก 2
        await SubmitAsync(app, table, Line(menu.MeatId, 2), Line(menu.MeatId, 1, "ไม่ติดมัน"), Line(menu.DrinkId, 1));
        await SubmitAsync(app, table, Line(menu.MeatId, 1), Line(menu.DrinkId, 2));

        var rows = await TableOrdersAsync(app, table.Token);

        // เรียงตามที่สั่งครั้งแรก ไม่แยกรอบ
        Assert.Equal(
        [
            new Row(menu.MeatId, menu.MeatName, "Buffet", 3, null, "/api/uploads/meat.jpg"),
            new Row(menu.MeatId, menu.MeatName, "Buffet", 1, "ไม่ติดมัน", "/api/uploads/meat.jpg"),
            new Row(menu.DrinkId, menu.DrinkName, "AlaCarte", 3, null, null),
        ], rows);
    }

    [Fact]
    public async Task The_payload_carries_no_round_no_status_and_no_bill()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var menu = await CreateMenuAsync(app, owner);
        var table = await OpenSessionAsync(app, owner);
        await SubmitAsync(app, table, Line(menu.MeatId, 1, "ไม่เผ็ด"), Line(menu.DrinkId, 1));

        var body = await TableOrdersJsonAsync(app, table.Token);

        // สัญญาทั้งก้อน: มีแค่ items และแต่ละแถวมีแค่ช่องเหล่านี้ — ไม่มีเลขรอบ สถานะ ราคา หรือยอดใด ๆ
        Assert.Equal(["items"], body.EnumerateObject().Select(p => p.Name).ToArray());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        // แถวที่มีหมายเหตุและรูปแสดงครบทุกช่อง; แถวที่ไม่มีก็ไม่มีช่องอื่นแทรกเข้ามา
        Assert.Equal(["menuItemId", "name", "kind", "quantity", "note", "imagePath"],
            items[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(["menuItemId", "name", "kind", "quantity"],
            items[1].EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task A_cancelled_order_and_removed_lines_vanish_from_the_table_list()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var kitchen = await LoginAsync(app, "DemoSeed:KitchenEmail");
        var menu = await CreateMenuAsync(app, owner);
        var table = await OpenSessionAsync(app, owner);

        var kept = await SubmitAsync(app, table, Line(menu.MeatId, 2), Line(menu.MeatId, 1, "ไม่ติดมัน"));
        var cancelled = await SubmitAsync(app, table, Line(menu.MeatId, 4), Line(menu.DrinkId, 1));

        Assert.Equal(HttpStatusCode.OK,
            (await kitchen.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "วัตถุดิบหมด" })).StatusCode);

        var afterCancel = await TableOrdersAsync(app, table.Token);
        Assert.Equal([(menu.MeatId, 2, (string?)null), (menu.MeatId, 1, "ไม่ติดมัน")],
            afterCancel.Select(r => (r.MenuItemId, r.Quantity, r.Note)).ToArray());

        // การแก้ออเดอร์ (ticket 17) ทำได้สองแบบที่ลูกค้าต้องเห็นเหมือนกัน: บรรทัดถูกนำออก (ไม่มีแถว หรือจำนวนเหลือ 0)
        // ตั้งด้วยข้อมูลเพราะเส้นทางแก้ออเดอร์ยังไม่มี
        await app.WithDbAsync(async db =>
        {
            var lines = await db.OrderItems.Where(i => i.OrderId == kept).OrderBy(i => i.Id).ToListAsync();
            db.OrderItems.Remove(lines[1]);
            await db.SaveChangesAsync();
            return 0;
        });
        Assert.Equal([(menu.MeatId, 2, (string?)null)],
            (await TableOrdersAsync(app, table.Token)).Select(r => (r.MenuItemId, r.Quantity, r.Note)).ToArray());

        await app.WithDbAsync(db => db.OrderItems.Where(i => i.OrderId == kept)
            .ExecuteUpdateAsync(i => i.SetProperty(x => x.Quantity, 0)));
        Assert.Empty(await TableOrdersAsync(app, table.Token));
    }

    [Fact]
    public async Task A_token_sees_only_its_own_table_and_unknown_or_closed_tokens_are_refused()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var menu = await CreateMenuAsync(app, owner);
        var mine = await OpenSessionAsync(app, owner);
        var other = await OpenSessionAsync(app, owner);

        await SubmitAsync(app, mine, Line(menu.MeatId, 1));
        await SubmitAsync(app, other, Line(menu.DrinkId, 5));

        Assert.Equal([(menu.MeatId, 1)],
            (await TableOrdersAsync(app, mine.Token)).Select(r => (r.MenuItemId, r.Quantity)).ToArray());
        Assert.Equal([(menu.DrinkId, 5)],
            (await TableOrdersAsync(app, other.Token)).Select(r => (r.MenuItemId, r.Quantity)).ToArray());

        using var phone = app.CreateClient();
        await AssertRefusedAsync(await phone.GetAsync($"/api/s/{UniqueName("x")}/orders"),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        // การชำระเงินเป็นของ ticket 18 — ตั้งสถานะด้วยข้อมูล
        await app.WithDbAsync(db => db.TableSessions.Where(s => s.Id == mine.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Paid)
                .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow)));
        await AssertRefusedAsync(await phone.GetAsync($"/api/s/{mine.Token}/orders"),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
    }

    // ------------------------------------------------------------------ realtime

    [Fact]
    public async Task Phone_B_hears_phone_A_submit_and_reads_it_back_while_status_changes_never_reach_it()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var kitchen = await LoginAsync(app, "DemoSeed:KitchenEmail");
        var menu = await CreateMenuAsync(app, owner);
        var table = await OpenSessionAsync(app, owner);

        await using var phoneB = await ConnectAsync(app, token: table.Token);

        var orderId = await SubmitAsync(app, table, Line(menu.MeatId, 2));
        await phoneB.Inbox.WaitForAsync("orderCreated", p => IdOf(p) == orderId, Within);
        Assert.Equal([(menu.MeatId, 2)],
            (await TableOrdersAsync(app, table.Token)).Select(r => (r.MenuItemId, r.Quantity)).ToArray());

        Assert.Equal(HttpStatusCode.OK,
            (await kitchen.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status = "Preparing" })).StatusCode);
        await BarrierAsync(app, phoneB);
        Assert.Empty(phoneB.Inbox.Of("orderStatusChanged"));
    }

    [Fact]
    public async Task Flipping_availability_sends_menu_item_changed_to_the_menu_group()
    {
        using var app = new TestApp(postgres);
        var owner = await LoginAsync(app, "DemoSeed:OwnerEmail");
        var menu = await CreateMenuAsync(app, owner);
        var table = await OpenSessionAsync(app, owner);

        await using var customer = await ConnectAsync(app, token: table.Token);
        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));

        bool For(JsonElement p) => p.GetProperty("menuItemId").GetInt32() == menu.MeatId;

        var off = await owner.PatchAsJsonAsync($"/api/menu-items/{menu.MeatId}/availability", new { isAvailable = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        foreach (var live in new[] { customer, kitchen })
        {
            var seen = await live.Inbox.WaitForAsync("menuItemChanged",
                p => For(p) && !p.GetProperty("isAvailable").GetBoolean(), Within);
            Assert.True(seen.GetProperty("isActive").GetBoolean());
        }

        var on = await owner.PatchAsJsonAsync($"/api/menu-items/{menu.MeatId}/availability", new { isAvailable = true });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        await customer.Inbox.WaitForAsync("menuItemChanged", p => For(p) && p.GetProperty("isAvailable").GetBoolean(),
            Within);

        // แก้ทั้งรายการจากฟอร์ม: เปลี่ยนแค่ชื่อไม่ประกาศ, ปิดใช้งาน (IsActive) ประกาศ
        var rename = await owner.PutAsJsonAsync($"/api/menu-items/{menu.MeatId}", new
        {
            name = UniqueName("หมู"), categoryId = menu.CategoryId, kind = "Buffet", minPackageRank = 1, price = 0m,
        });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        var hide = await owner.PutAsJsonAsync($"/api/menu-items/{menu.MeatId}", new
        {
            name = UniqueName("หมู"), categoryId = menu.CategoryId, kind = "Buffet", minPackageRank = 1, price = 0m,
            isActive = false,
        });
        Assert.Equal(HttpStatusCode.OK, hide.StatusCode);
        await customer.Inbox.WaitForAsync("menuItemChanged", p => For(p) && !p.GetProperty("isActive").GetBoolean(),
            Within);
        await BarrierAsync(app, customer);

        // off, on, hide — การเปลี่ยนชื่ออย่างเดียวไม่ส่งอะไร
        Assert.Equal(3, customer.Inbox.Of("menuItemChanged").Count(For));
    }
}
