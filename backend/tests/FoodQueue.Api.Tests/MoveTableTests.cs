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
/// Ticket 21 — ย้ายโต๊ะผ่าน seam 1 (spec 54 + Testing Decisions "ย้ายโต๊ะ"): <c>POST /api/sessions/{id}/move</c>
/// ย้ายทั้ง session ไปโต๊ะ active ที่ว่าง (ไม่งั้น <c>TABLE_OCCUPIED</c>) โดย token/QR เดิมใช้ต่อได้ โต๊ะเดิมว่างทันที
/// ทุกข้อพิสูจน์จากสิ่งที่มองเห็นจากภายนอก: HTTP + code + ข้อมูลที่คืน + ภาพรวมโต๊ะ + จอครัว + event ที่แต่ละจอได้รับจริง
/// </summary>
[Collection(ApiCollection.Name)]
public class MoveTableTests(PostgresFixture postgres)
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

    private static Task<HttpClient> OwnerClientAsync(TestApp app) => LoginAsync(app, "DemoSeed:OwnerEmail");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..12];

    private sealed record CreatedTable(int Id, string Name);

    private static async Task<CreatedTable> CreateTableAsync(HttpClient owner)
    {
        var name = UniqueName("M");
        var response = await owner.PostAsJsonAsync("/api/tables", new { name, seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return new CreatedTable((await JsonAsync(response)).GetProperty("id").GetInt32(), name);
    }

    /// <summary>ฐานข้อมูลของ host นี้เป็นของเทสต์นี้คนเดียว (โคลนจาก template) จึงเลือก rank ว่างตัวแรกได้เลย</summary>
    private static Task<int> SeedPackageAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var package = new Package
            {
                Name = UniqueName("P"),
                Rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r)),
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private sealed record Menu(int MeatId, int DrinkId);

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
            name = UniqueName("ชา"), categoryId, kind = "AlaCarte", price = 45m,
        });
        Assert.Equal(HttpStatusCode.Created, drink.StatusCode);

        return new Menu((await JsonAsync(meat)).GetProperty("id").GetInt32(),
            (await JsonAsync(drink)).GetProperty("id").GetInt32());
    }

    private sealed record OpenedSession(int Id, string Token, CreatedTable Table);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int? packageId = null)
    {
        var table = await CreateTableAsync(owner);
        var response = await owner.PostAsJsonAsync("/api/sessions", new
        {
            tableId = table.Id, packageId = packageId ?? await SeedPackageAsync(app), adultCount = 2, childCount = 0,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, table);
    }

    private static object Line(int menuItemId, int quantity) => new { menuItemId, quantity };

    /// <summary>ลูกค้าส่งออเดอร์ด้วย token (ไม่มี auth) แล้วคืน id ของรอบที่เพิ่งบันทึก (คำตอบของลูกค้าไม่มี id)</summary>
    private static async Task<int> SubmitAsync(TestApp app, string token, params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{token}/orders", new { items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSession!.Token == token)
            .OrderByDescending(o => o.Number).Select(o => o.Id).FirstAsync());
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient owner, int sessionId, int tableId) =>
        owner.PostAsJsonAsync($"/api/sessions/{sessionId}/move", new { tableId });

    private static async Task<JsonElement> OverviewRowAsync(HttpClient owner, int tableId)
    {
        var overview = await OkAsync(await owner.GetAsync("/api/tables/overview"));
        return overview.EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == tableId);
    }

    /// <summary>โต๊ะว่าง = ไม่มี <c>sessionId</c> (ค่า null ไม่ถูกเขียนลง JSON)</summary>
    private static int? SessionIdOf(JsonElement row) =>
        row.TryGetProperty("sessionId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : null;

    // ------------------------------------------------------------------ ปฏิเสธ

    [Fact]
    public async Task Moving_onto_a_table_with_an_open_session_is_refused_and_nothing_moves()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);
        var moving = await OpenSessionAsync(app, owner, packageId);
        var seated = await OpenSessionAsync(app, owner, packageId);

        await AssertProblemAsync(await MoveAsync(owner, moving.Id, seated.Table.Id),
            HttpStatusCode.Conflict, ErrorCodes.TableOccupied);

        // ย้ายไปโต๊ะที่ตัวเองนั่งอยู่ก็คือโต๊ะที่มี session Open
        await AssertProblemAsync(await MoveAsync(owner, moving.Id, moving.Table.Id),
            HttpStatusCode.Conflict, ErrorCodes.TableOccupied);

        // โต๊ะที่ถูกปิดใช้งานไม่ใช่โต๊ะ active ที่ย้ายไปได้
        var disabled = await CreateTableAsync(owner);
        var off = await owner.PutAsJsonAsync($"/api/tables/{disabled.Id}",
            new { name = disabled.Name, seats = 4, zone = "A", sortOrder = 0, isActive = false });
        await OkAsync(off);
        await AssertProblemAsync(await MoveAsync(owner, moving.Id, disabled.Id),
            HttpStatusCode.Conflict, ErrorCodes.TableOccupied);

        // โต๊ะที่ไม่มีอยู่ / ไม่ระบุโต๊ะ = คำขอไม่ถูกต้อง
        await AssertProblemAsync(await MoveAsync(owner, moving.Id, 987_654),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertProblemAsync(await owner.PostAsJsonAsync($"/api/sessions/{moving.Id}/move", new { }),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        // session ไม่พบ
        var free = await CreateTableAsync(owner);
        await AssertProblemAsync(await MoveAsync(owner, 987_654, free.Id),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        // ไม่มีอะไรขยับ: ทั้งสองยังอยู่ที่โต๊ะเดิม
        Assert.Equal(moving.Id, SessionIdOf(await OverviewRowAsync(owner, moving.Table.Id)));
        Assert.Equal(seated.Id, SessionIdOf(await OverviewRowAsync(owner, seated.Table.Id)));
        Assert.Null(SessionIdOf(await OverviewRowAsync(owner, free.Id)));
    }

    [Fact]
    public async Task A_paid_or_voided_session_cannot_move()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);
        var paid = await OpenSessionAsync(app, owner, packageId);
        var voided = await OpenSessionAsync(app, owner, packageId);
        var free = await CreateTableAsync(owner);

        // การชำระเงินเป็นของ ticket 18 — ที่นี่ตั้งสถานะด้วยข้อมูลจึงไม่รอใคร
        await app.WithDbAsync(db => db.TableSessions.Where(s => s.Id == paid.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, SessionStatus.Paid)
            .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/sessions/{voided.Id}/void", null)).StatusCode);

        await AssertProblemAsync(await MoveAsync(owner, paid.Id, free.Id), HttpStatusCode.Conflict,
            ErrorCodes.SessionClosed);
        await AssertProblemAsync(await MoveAsync(owner, voided.Id, free.Id), HttpStatusCode.Conflict,
            ErrorCodes.SessionClosed);

        var history = await OkAsync(await owner.GetAsync($"/api/sessions/{paid.Id}"));
        Assert.Equal(paid.Table.Id, history.GetProperty("tableId").GetInt32());
        Assert.Null(SessionIdOf(await OverviewRowAsync(owner, free.Id)));
    }

    [Fact]
    public async Task Only_the_owner_may_move_a_table()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var session = await OpenSessionAsync(app, owner);
        var free = await CreateTableAsync(owner);

        var kitchen = await LoginAsync(app, "DemoSeed:KitchenEmail");
        Assert.Equal(HttpStatusCode.Forbidden, (await MoveAsync(kitchen, session.Id, free.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await MoveAsync(app.CreateClient(), session.Id, free.Id)).StatusCode);

        Assert.Equal(session.Id, SessionIdOf(await OverviewRowAsync(owner, session.Table.Id)));
    }

    // ------------------------------------------------------------------ ย้ายสำเร็จ

    [Fact]
    public async Task The_whole_session_moves_the_token_keeps_ordering_and_the_old_table_is_free_at_once()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session.Token, Line(menu.MeatId, 2), Line(menu.DrinkId, 1));
        await OkAsync(await owner.PostAsJsonAsync($"/api/sessions/{session.Id}/adjustments",
            new { description = "น้ำแข็งเพิ่ม", amount = 20m }));
        var target = await CreateTableAsync(owner);

        var before = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));

        var moved = await OkAsync(await MoveAsync(owner, session.Id, target.Id));

        // ทั้ง session ย้าย: id, token/QR, เวลา, ออเดอร์, รายการปรับยอด และบิลชุดเดิม เปลี่ยนแค่โต๊ะ
        Assert.Equal(session.Id, moved.GetProperty("id").GetInt32());
        Assert.Equal(target.Id, moved.GetProperty("tableId").GetInt32());
        Assert.Equal(target.Name, moved.GetProperty("tableName").GetString());
        Assert.Equal("Open", moved.GetProperty("status").GetString());
        foreach (var same in new[] { "qrUrl", "openedAt", "endsAt", "packageName" })
        {
            Assert.Equal(before.GetProperty(same).GetString(), moved.GetProperty(same).GetString());
        }

        Assert.EndsWith($"/s/{session.Token}", moved.GetProperty("qrUrl").GetString());
        Assert.Equal(before.GetProperty("orders").GetRawText().Replace(session.Table.Name, target.Name),
            moved.GetProperty("orders").GetRawText());
        Assert.Equal(before.GetProperty("adjustments").GetRawText(), moved.GetProperty("adjustments").GetRawText());
        Assert.Equal(before.GetProperty("bill").GetRawText(), moved.GetProperty("bill").GetRawText());

        // ผังโต๊ะ: โต๊ะเดิมว่างทันที โต๊ะใหม่มีรอบนี้พร้อมยอดเดิม
        var oldRow = await OverviewRowAsync(owner, session.Table.Id);
        var newRow = await OverviewRowAsync(owner, target.Id);
        Assert.Null(SessionIdOf(oldRow));
        Assert.Equal(session.Id, SessionIdOf(newRow));
        Assert.Equal(before.GetProperty("bill").GetProperty("total").GetDecimal(),
            newRow.GetProperty("runningTotal").GetDecimal());

        // partial unique index: โต๊ะเดิมเปิดรอบใหม่ได้ ส่วนโต๊ะใหม่เปิดซ้อนไม่ได้
        var packageId = await app.WithDbAsync(db =>
            db.TableSessions.Where(s => s.Id == session.Id).Select(s => s.PackageId).SingleAsync());
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/sessions",
            new { tableId = session.Table.Id, packageId, adultCount = 1, childCount = 0 })).StatusCode);
        await AssertProblemAsync(await owner.PostAsJsonAsync("/api/sessions",
                new { tableId = target.Id, packageId, adultCount = 1, childCount = 0 }),
            HttpStatusCode.Conflict, ErrorCodes.TableOccupied);

        // token เดิมยังใช้ได้: ลูกค้าเห็นชื่อโต๊ะใหม่ รายการเดิมของโต๊ะ และสั่งรอบถัดไปได้
        using var customer = app.CreateClient();
        var customerSession = await OkAsync(await customer.GetAsync($"/api/s/{session.Token}"));
        Assert.Equal(target.Name, customerSession.GetProperty("tableName").GetString());
        var nextOrder = await SubmitAsync(app, session.Token, Line(menu.MeatId, 1));
        Assert.NotEqual(orderId, nextOrder);

        // ครัวเห็นทั้งสองรอบภายใต้ชื่อโต๊ะใหม่ (และไม่เห็นชื่อเดิมอีก)
        var kitchen = await LoginAsync(app, "DemoSeed:KitchenEmail");
        var board = await OkAsync(await kitchen.GetAsync("/api/orders"));
        var mine = board.EnumerateArray().Where(o => o.GetProperty("sessionId").GetInt32() == session.Id).ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, o => Assert.Equal(target.Name, o.GetProperty("tableName").GetString()));

        var after = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));
        Assert.Equal(2, after.GetProperty("orders").GetArrayLength());
        Assert.Equal(1, after.GetProperty("adjustments").GetArrayLength());
    }

    [Fact]
    public async Task A_session_past_its_end_time_can_still_move()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var session = await OpenSessionAsync(app, owner);
        var target = await CreateTableAsync(owner);
        await app.WithDbAsync(db => db.TableSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.OpenedAt, DateTimeOffset.UtcNow.AddMinutes(-120))
            .SetProperty(x => x.EndsAt, DateTimeOffset.UtcNow.AddMinutes(-30))));

        var moved = await OkAsync(await MoveAsync(owner, session.Id, target.Id));

        Assert.Equal(target.Id, moved.GetProperty("tableId").GetInt32());
        Assert.Null(SessionIdOf(await OverviewRowAsync(owner, session.Table.Id)));
    }

    [Fact]
    public async Task Two_sessions_racing_for_the_same_free_table_one_wins_and_the_other_is_told_it_is_occupied()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);
        var first = await OpenSessionAsync(app, owner, packageId);
        var second = await OpenSessionAsync(app, owner, packageId);
        var target = await CreateTableAsync(owner);
        var otherOwner = await OwnerClientAsync(app);

        var responses = await Task.WhenAll(MoveAsync(owner, first.Id, target.Id), MoveAsync(otherOwner, second.Id, target.Id));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK);
        await AssertProblemAsync(loser, HttpStatusCode.Conflict, ErrorCodes.TableOccupied);

        var winner = SessionIdOf(await OverviewRowAsync(owner, target.Id));
        Assert.Contains(winner, new int?[] { first.Id, second.Id });
        var loserSession = winner == first.Id ? second : first;
        Assert.Equal(loserSession.Id, SessionIdOf(await OverviewRowAsync(owner, loserSession.Table.Id)));
    }

    // ------------------------------------------------------------------ realtime

    /// <summary>
    /// เจ้าของร้านและโต๊ะนั้นได้ <c>sessionChanged</c> ที่มีชื่อโต๊ะใหม่ (spec: owner/session เมื่อย้าย);
    /// ครัวไม่อยู่ใน group ที่ได้ sessionChanged จึงได้การ์ดของรอบที่ยังอยู่บนจอ (Pending/Preparing/Ready)
    /// ซ้ำอีกครั้งทาง <c>orderStatusChanged</c> (kitchen+owner เท่านั้น) พร้อมชื่อโต๊ะใหม่และสถานะเดิม
    /// ลูกค้าไม่ได้รับ <c>orderStatusChanged</c> และโต๊ะอื่นไม่ได้รับอะไรเลย
    /// </summary>
    [Fact]
    public async Task The_owner_and_the_table_hear_sessionChanged_and_the_kitchen_cards_take_the_new_table_name()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var packageId = await SeedPackageAsync(app);
        var session = await OpenSessionAsync(app, owner, packageId);
        var bystander = await OpenSessionAsync(app, owner, packageId);
        var pending = await SubmitAsync(app, session.Token, Line(menu.MeatId, 1));
        var ready = await SubmitAsync(app, session.Token, Line(menu.DrinkId, 1));
        var cancelled = await SubmitAsync(app, session.Token, Line(menu.DrinkId, 2));
        await app.WithDbAsync(async db =>
        {
            await db.Orders.Where(o => o.Id == ready).ExecuteUpdateAsync(o => o.SetProperty(x => x.Status, OrderStatus.Ready));
            return await db.Orders.Where(o => o.Id == cancelled).ExecuteUpdateAsync(o => o
                .SetProperty(x => x.Status, OrderStatus.Cancelled).SetProperty(x => x.CancelReason, "สั่งผิด"));
        });
        var target = await CreateTableAsync(owner);

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var customer = await ConnectAsync(app, token: session.Token);
        await using var otherTable = await ConnectAsync(app, token: bystander.Token);

        await OkAsync(await MoveAsync(owner, session.Id, target.Id));

        bool IsMoved(JsonElement s) =>
            s.GetProperty("id").GetInt32() == session.Id && s.GetProperty("tableName").GetString() == target.Name &&
            s.GetProperty("tableId").GetInt32() == target.Id;
        await ownerLive.Inbox.WaitForAsync("sessionChanged", IsMoved, Within);
        await customer.Inbox.WaitForAsync("sessionChanged", IsMoved, Within);

        foreach (var (id, status) in new[] { (pending, "Pending"), (ready, "Ready") })
        {
            var card = await kitchen.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == id, Within);
            Assert.Equal(target.Name, card.GetProperty("tableName").GetString());
            Assert.Equal(status, card.GetProperty("status").GetString());
            await ownerLive.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == id, Within);
        }

        await BarrierAsync(app, kitchen, ownerLive, customer, otherTable);

        // การ์ดที่ยกเลิกแล้วไม่อยู่บนจอครัว จึงไม่ถูกประกาศซ้ำ; เจ้าของร้านได้ครั้งเดียว (ไม่ซ้ำผ่านสอง group)
        Assert.DoesNotContain(kitchen.Inbox.Of("orderStatusChanged"), p => IdOf(p) == cancelled);
        Assert.Single(ownerLive.Inbox.Of("sessionChanged"), IsMoved);
        Assert.Empty(kitchen.Inbox.Of("sessionChanged"));
        Assert.Empty(customer.Inbox.Of("orderStatusChanged"));
        Assert.Empty(otherTable.Inbox.Of("sessionChanged"));
        Assert.Empty(otherTable.Inbox.Of("orderStatusChanged"));
    }
}
