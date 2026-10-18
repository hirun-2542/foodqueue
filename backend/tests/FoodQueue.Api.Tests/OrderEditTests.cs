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
/// Ticket 17 — เจ้าของร้านแก้ออเดอร์ (ลดจำนวน/นำรายการออกพร้อมเหตุผล) ผ่าน seam 1 (แอปจริง + Postgres จริง)
/// <c>POST /api/orders/{id}/edit</c> <c>{ reason, items: [{ itemId, quantity }] }</c> โดย quantity คือจำนวนใหม่ (0 = นำออก)
/// ผลที่ตรวจ: HTTP + <c>code</c>, ออเดอร์ที่คืนมา, บิลจาก <c>GET /api/sessions/{id}</c>, รายการที่ลูกค้าเห็น,
/// และ event ที่การเชื่อมต่อ SignalR จริงได้รับ
/// </summary>
[Collection(ApiCollection.Name)]
public class OrderEditTests(PostgresFixture postgres)
{
    private const decimal DrinkPrice = 45m;

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

    private static Task<HttpClient> OwnerClientAsync(TestApp app) => LoginAsync(app, "DemoSeed:OwnerEmail");

    private static Task<HttpClient> KitchenClientAsync(TestApp app) => LoginAsync(app, "DemoSeed:KitchenEmail");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private sealed record Menu(int MeatId, int DrinkId);

    /// <summary>เนื้อบุฟเฟ่ต์ (ราคา 0 ในบิล) และเครื่องดื่ม à la carte ราคา 45 ผ่านเส้นทางจริงของ ticket 05</summary>
    private static async Task<Menu> CreateMenuAsync(HttpClient owner)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        var meat = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = UniqueName("หมู"), categoryId, kind = "Buffet", minPackageRank = 1, price = 0m, isAvailable = true,
        });
        Assert.Equal(HttpStatusCode.Created, meat.StatusCode);

        var drink = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = UniqueName("ชาไทย"), categoryId, kind = "AlaCarte", price = DrinkPrice,
        });
        Assert.Equal(HttpStatusCode.Created, drink.StatusCode);

        return new Menu((await JsonAsync(meat)).GetProperty("id").GetInt32(),
            (await JsonAsync(drink)).GetProperty("id").GetInt32());
    }

    private sealed record OpenedSession(int Id, string Token);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner)
    {
        var table = await owner.PostAsJsonAsync("/api/tables", new { name = UniqueName("E"), seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();
        var packageId = await app.WithDbAsync(async db =>
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

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = 2, childCount = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token);
    }

    private static object Line(int menuItemId, int quantity) => new { menuItemId, quantity };

    /// <summary>ลูกค้าส่งออเดอร์ด้วย token แล้วคืนออเดอร์ตามที่เจ้าของร้านเห็นใน <c>GET /api/orders</c></summary>
    private static async Task<JsonElement> SubmitAsync(TestApp app, HttpClient owner, OpenedSession session,
        params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var board = await JsonAsync(await owner.GetAsync("/api/orders"));
        return board.EnumerateArray()
            .Where(o => o.GetProperty("sessionId").GetInt32() == session.Id)
            .OrderByDescending(o => o.GetProperty("number").GetInt32())
            .First();
    }

    /// <summary>id ของบรรทัดในออเดอร์ (จาก <c>OrderDto.items[].id</c>) ตามเมนู</summary>
    private static int ItemId(JsonElement order, int menuItemId) =>
        order.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("menuItemId").GetInt32() == menuItemId)
            .GetProperty("id").GetInt32();

    private static object Change(int itemId, int quantity) => new { itemId, quantity };

    private static Task<HttpResponseMessage> EditAsync(HttpClient client, int orderId, string? reason,
        params object[] items) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/edit", new { reason, items });

    private static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, int orderId, string status) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status });

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
    }

    private static async Task<JsonElement> BillAsync(HttpClient owner, int sessionId) =>
        (await OkAsync(await owner.GetAsync($"/api/sessions/{sessionId}"))).GetProperty("bill");

    private static (int MenuItemId, int Quantity)[] Lines(JsonElement order) =>
        order.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("menuItemId").GetInt32(), i.GetProperty("quantity").GetInt32()))
            .ToArray();

    /// <summary>รายการรวมที่ลูกค้าเห็นในแท็บออเดอร์ (ไม่มีสถานะ ไม่มียอด)</summary>
    private static async Task<(int MenuItemId, int Quantity)[]> CustomerLinesAsync(TestApp app, string token)
    {
        using var customer = app.CreateClient();
        var body = await OkAsync(await customer.GetAsync($"/api/s/{token}/orders"));
        return body.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("menuItemId").GetInt32(), i.GetProperty("quantity").GetInt32()))
            .ToArray();
    }

    // ------------------------------------------------------------------ แก้ได้: Pending / Preparing

    [Fact]
    public async Task Pending_and_Preparing_orders_can_be_reduced_or_lose_a_line_and_the_bill_drops_at_once()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, session, Line(menu.MeatId, 3), Line(menu.DrinkId, 2));
        var orderId = IdOf(order);
        var before = await BillAsync(owner, session.Id);
        Assert.Equal(2 * DrinkPrice, before.GetProperty("alaCarteTotal").GetDecimal());

        // Pending: ชาไทย 2 → 1 — สถานะคงเดิม เหตุผลถูกเก็บ บิลลดลง 45 ทันที
        var reduced = await OkAsync(await EditAsync(owner, orderId, "  ลูกค้าเอาออก ",
            Change(ItemId(order, menu.DrinkId), 1)));
        Assert.Equal("Pending", reduced.GetProperty("status").GetString());
        Assert.Equal("ลูกค้าเอาออก", reduced.GetProperty("editReason").GetString());
        Assert.Equal([(menu.MeatId, 3), (menu.DrinkId, 1)], Lines(reduced));
        var afterReduce = await BillAsync(owner, session.Id);
        Assert.Equal(DrinkPrice, afterReduce.GetProperty("alaCarteTotal").GetDecimal());
        Assert.Equal(before.GetProperty("subtotal").GetDecimal() - DrinkPrice,
            afterReduce.GetProperty("subtotal").GetDecimal());
        Assert.True(afterReduce.GetProperty("total").GetDecimal() < before.GetProperty("total").GetDecimal());

        // Preparing: นำเนื้อออกทั้งบรรทัด — เหลือแค่ชาไทย สถานะยังกำลังทำ
        await OkAsync(await AdvanceAsync(kitchen, orderId, "Preparing"));
        var removed = await OkAsync(await EditAsync(owner, orderId, "วัตถุดิบหมด",
            Change(ItemId(order, menu.MeatId), 0)));
        Assert.Equal("Preparing", removed.GetProperty("status").GetString());
        Assert.Equal("วัตถุดิบหมด", removed.GetProperty("editReason").GetString());
        Assert.Equal([(menu.DrinkId, 1)], Lines(removed));

        // ลูกค้าเห็นรายการที่เหลือ ครัวเห็นการ์ดเดียวกัน (GET /api/orders)
        Assert.Equal([(menu.DrinkId, 1)], await CustomerLinesAsync(app, session.Token));
        var board = await OkAsync(await kitchen.GetAsync("/api/orders"));
        var card = board.EnumerateArray().Single(o => IdOf(o) == orderId);
        Assert.Equal([(menu.DrinkId, 1)], Lines(card));
        Assert.Equal("วัตถุดิบหมด", card.GetProperty("editReason").GetString());
    }

    [Fact]
    public async Task Removing_every_line_cancels_the_whole_order_with_the_reason_and_the_bill_drops()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, session, Line(menu.MeatId, 2), Line(menu.DrinkId, 1));
        var orderId = IdOf(order);
        var before = await BillAsync(owner, session.Id);

        var cancelled = await OkAsync(await EditAsync(owner, orderId, "สั่งผิด",
            Change(ItemId(order, menu.MeatId), 0), Change(ItemId(order, menu.DrinkId), 0)));

        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("สั่งผิด", cancelled.GetProperty("cancelReason").GetString());
        var after = await BillAsync(owner, session.Id);
        Assert.Equal(0m, after.GetProperty("alaCarteTotal").GetDecimal());
        Assert.Equal(before.GetProperty("subtotal").GetDecimal() - DrinkPrice,
            after.GetProperty("subtotal").GetDecimal());
        Assert.Empty(await CustomerLinesAsync(app, session.Token));

        // ยกเลิกแล้วแก้ต่อไม่ได้
        await AssertRefusedAsync(await EditAsync(owner, orderId, "สั่งผิด", Change(ItemId(order, menu.MeatId), 1)),
            HttpStatusCode.Conflict, ErrorCodes.OrderNotEditable);
    }

    // ------------------------------------------------------------------ ปฏิเสธ

    [Fact]
    public async Task Ready_and_Cancelled_orders_answer_ORDER_NOT_EDITABLE_and_stay_as_they_were()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);

        var ready = await SubmitAsync(app, owner, session, Line(menu.DrinkId, 2));
        await OkAsync(await AdvanceAsync(kitchen, IdOf(ready), "Preparing"));
        await OkAsync(await AdvanceAsync(kitchen, IdOf(ready), "Ready"));
        await AssertRefusedAsync(await EditAsync(owner, IdOf(ready), "ลูกค้าเอาออก",
            Change(ItemId(ready, menu.DrinkId), 1)), HttpStatusCode.Conflict, ErrorCodes.OrderNotEditable);

        var cancelled = await SubmitAsync(app, owner, session, Line(menu.DrinkId, 2));
        Assert.Equal(HttpStatusCode.OK, (await kitchen.PostAsJsonAsync($"/api/orders/{IdOf(cancelled)}/cancel",
            new { reason = "วัตถุดิบหมด" })).StatusCode);
        await AssertRefusedAsync(await EditAsync(owner, IdOf(cancelled), "ลูกค้าเอาออก",
            Change(ItemId(cancelled, menu.DrinkId), 1)), HttpStatusCode.Conflict, ErrorCodes.OrderNotEditable);

        var stored = await app.WithDbAsync(db => db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == IdOf(ready) || i.OrderId == IdOf(cancelled))
            .Select(i => i.Quantity).ToListAsync());
        Assert.Equal([2, 2], stored);
        Assert.Equal(DrinkPrice * 2, (await BillAsync(owner, session.Id)).GetProperty("alaCarteTotal").GetDecimal());
    }

    [Fact]
    public async Task Increasing_a_quantity_adding_a_line_or_changing_nothing_is_refused()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, session, Line(menu.DrinkId, 2));
        var other = await SubmitAsync(app, owner, session, Line(menu.MeatId, 1));
        var drink = ItemId(order, menu.DrinkId);

        // เพิ่มจำนวน
        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), "ลูกค้าขอเพิ่ม", Change(drink, 3)),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        // เพิ่มรายการ = บรรทัดที่ไม่ได้อยู่ในออเดอร์นี้
        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), "ลูกค้าขอเพิ่ม",
            Change(ItemId(other, menu.MeatId), 1)), HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        // ไม่ได้ลดอะไรเลย / ไม่มีรายการ / จำนวนติดลบ
        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), "ลูกค้าเอาออก", Change(drink, 2)),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), "ลูกค้าเอาออก"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), "ลูกค้าเอาออก", Change(drink, -1)),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        var stored = await app.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleAsync(o => o.Id == IdOf(order)));
        Assert.Equal(OrderStatus.Pending, stored.Status);
        Assert.Null(stored.EditReason);
        Assert.Equal([2], stored.Items.Select(i => i.Quantity));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_reason_is_refused(string? reason)
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, session, Line(menu.DrinkId, 2));

        await AssertRefusedAsync(await EditAsync(owner, IdOf(order), reason, Change(ItemId(order, menu.DrinkId), 1)),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(2, await app.WithDbAsync(db => db.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == IdOf(order)).Select(i => i.Quantity).SingleAsync()));
    }

    [Fact]
    public async Task Only_the_owner_may_edit_and_an_unknown_order_is_404()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, session, Line(menu.DrinkId, 2));
        var change = Change(ItemId(order, menu.DrinkId), 1);

        Assert.Equal(HttpStatusCode.Forbidden, (await EditAsync(kitchen, IdOf(order), "ลูกค้าเอาออก", change)).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await EditAsync(anonymous, IdOf(order), "ลูกค้าเอาออก", change)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await EditAsync(owner, int.MaxValue, "ลูกค้าเอาออก", change)).StatusCode);
    }

    // ------------------------------------------------------------------ ใครได้รับอะไร (WebSocket จริง)

    [Fact]
    public async Task Over_the_hub_an_edit_reaches_kitchen_owner_and_the_table_but_not_another_table()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var table = await OpenSessionAsync(app, owner);
        var otherTable = await OpenSessionAsync(app, owner);
        var order = await SubmitAsync(app, owner, table, Line(menu.MeatId, 2), Line(menu.DrinkId, 2));
        var orderId = IdOf(order);

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var customer = await ConnectAsync(app, token: table.Token);
        await using var otherCustomer = await ConnectAsync(app, token: otherTable.Token);

        await OkAsync(await EditAsync(owner, orderId, "ลูกค้าเอาออก", Change(ItemId(order, menu.DrinkId), 0)));

        foreach (var live in new[] { kitchen, ownerLive, customer })
        {
            var edited = await live.Inbox.WaitForAsync("orderEdited", p => IdOf(p) == orderId, Within);
            Assert.Equal("Pending", edited.GetProperty("status").GetString());
            Assert.Equal([(menu.MeatId, 2)], Lines(edited));
        }

        await BarrierAsync(app, kitchen, ownerLive, customer, otherCustomer);
        Assert.Single(kitchen.Inbox.Of("orderEdited"));
        Assert.Single(ownerLive.Inbox.Of("orderEdited"));
        Assert.Single(customer.Inbox.Of("orderEdited"));
        Assert.All(otherCustomer.Inbox.Names(), name => Assert.Equal("menuItemChanged", name));
        Assert.Empty(customer.Inbox.Of("orderStatusChanged"));
    }
}
