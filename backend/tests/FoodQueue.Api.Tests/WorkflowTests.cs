using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static FoodQueue.Api.Tests.LiveClient;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 12 — ครัวเลื่อนสถานะและยกเลิกออเดอร์ ผ่าน seam 1 (แอปจริง + Postgres จริง)
/// <c>POST /api/orders/{id}/status</c> รับสถานะปลายทาง (<c>Preparing</c> หรือ <c>Ready</c>) และ
/// <c>POST /api/orders/{id}/cancel</c> รับเหตุผล; ทั้งสองคืน <see cref="OrderDto"/> ที่อัปเดตแล้ว
///
/// event ถูกตรวจสองชั้น: <see cref="RecordingNotifier"/> (ระบบประกาศอะไร ด้วยปลายทางใด) และไคลเอนต์ SignalR จริง
/// ผ่าน WebSocket (<see cref="LiveClient"/>) ที่ดูว่าครัว เจ้าของร้าน และลูกค้าได้รับอะไรจริง
/// </summary>
[Collection(ApiCollection.Name)]
public class WorkflowTests(PostgresFixture postgres)
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

    private static Task<HttpClient> KitchenClientAsync(TestApp app) => LoginAsync(app, "DemoSeed:KitchenEmail");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

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

    private sealed record Menu(int MeatId, int DrinkId);

    /// <summary>เนื้อบุฟเฟ่ต์ rank 1 (ราคา 0 ในบิล) และเครื่องดื่ม à la carte ราคา 45 ผ่านเส้นทางจริงของ ticket 05</summary>
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
            name = UniqueName("โค้ก"), categoryId, kind = "AlaCarte", price = 45m,
        });
        Assert.Equal(HttpStatusCode.Created, drink.StatusCode);

        return new Menu((await JsonAsync(meat)).GetProperty("id").GetInt32(),
            (await JsonAsync(drink)).GetProperty("id").GetInt32());
    }

    private sealed record OpenedSession(int Id, string Token);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner)
    {
        var table = await owner.PostAsJsonAsync("/api/tables", new { name = UniqueName("W"), seats = 4, zone = "A" });
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

    /// <summary>ลูกค้าส่งออเดอร์ด้วย token (ไม่มี auth) แล้วคืน id ของรอบที่เพิ่งบันทึก</summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSessionId == session.Id)
            .OrderByDescending(o => o.Number).Select(o => o.Id).FirstAsync());
    }

    private static object Line(int menuItemId, int quantity) => new { menuItemId, quantity };

    private static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, int orderId, string? status) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status });

    private static Task<HttpResponseMessage> CancelAsync(HttpClient client, int orderId, string? reason) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/cancel", new { reason });

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    /// <summary>ปฏิเสธด้วยสถานะ HTTP และ <c>code</c> ตามสัญญา พร้อมข้อความไทยที่แสดงต่อผู้ใช้ได้ตรง ๆ</summary>
    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
    }

    private static Task<Order> StoredAsync(TestApp app, int orderId) =>
        app.WithDbAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));

    private static Task MarkPaidAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(async db =>
        {
            // การชำระเงินเป็นของ ticket 18 — ที่นี่ตั้งสถานะด้วยข้อมูลจึงไม่รอใคร
            await db.TableSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Paid)
                .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow));
            return 0;
        });

    private static List<OrderDto> StatusChanges(RecordingNotifier notifier, int orderId) =>
        notifier.Events.Where(e => e.Event == "orderStatusChanged")
            .Select(e => (OrderDto)e.Payload).Where(o => o.Id == orderId).ToList();

    private static List<OrderEditedCall> Edits(RecordingNotifier notifier, int orderId) =>
        notifier.Events.Where(e => e.Event == "orderEdited")
            .Select(e => (OrderEditedCall)e.Payload).Where(c => ((OrderDto)c.Order).Id == orderId).ToList();

    // ------------------------------------------------------------------ เลื่อนสถานะ

    [Fact]
    public async Task Status_moves_one_step_at_a_time_and_Ready_is_the_last_step()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session, Line(menu.MeatId, 2));
        var submitted = await StoredAsync(app, orderId);

        // ข้ามขั้น Pending → Ready ไม่ได้
        await AssertRefusedAsync(await AdvanceAsync(kitchen, orderId, "Ready"),
            HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);
        Assert.Equal(OrderStatus.Pending, (await StoredAsync(app, orderId)).Status);

        var preparing = await OkAsync(await AdvanceAsync(kitchen, orderId, "Preparing"));
        Assert.Equal(orderId, IdOf(preparing));
        Assert.Equal("Preparing", preparing.GetProperty("status").GetString());
        Assert.Equal(session.Id, preparing.GetProperty("sessionId").GetInt32());
        Assert.Single(preparing.GetProperty("items").EnumerateArray());
        var stored = await StoredAsync(app, orderId);
        Assert.Equal(OrderStatus.Preparing, stored.Status);
        Assert.True(stored.UpdatedAt > submitted.UpdatedAt);

        // กดซ้ำ (ข้อมูลเก่าบนจอ) = ไม่มีแถวที่ยังเป็น Pending ให้เลื่อน
        await AssertRefusedAsync(await AdvanceAsync(kitchen, orderId, "Preparing"),
            HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);

        var ready = await OkAsync(await AdvanceAsync(kitchen, orderId, "Ready"));
        Assert.Equal("Ready", ready.GetProperty("status").GetString());

        // Ready คือขั้นสุดท้าย: ไม่มีทางไปต่อ และไม่มีการย้อนกลับ ส่วน Cancelled มีเส้นทางของตัวเอง
        foreach (var target in new[] { "Ready", "Served", "Preparing", "Pending", "Cancelled" })
        {
            await AssertRefusedAsync(await AdvanceAsync(kitchen, orderId, target),
                HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);
        }

        Assert.Equal(OrderStatus.Ready, (await StoredAsync(app, orderId)).Status);

        // ประกาศเฉพาะการเลื่อนที่สำเร็จ หลังบันทึกแล้ว ด้วยสถานะใหม่ — การปฏิเสธไม่ประกาศอะไร
        Assert.Equal(["Preparing", "Ready"], StatusChanges(notifier, orderId).Select(o => o.Status));
        Assert.Empty(Edits(notifier, orderId));
    }

    [Fact]
    public async Task Status_change_needs_a_target_an_existing_order_and_a_staff_login()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session, Line(menu.MeatId, 1));

        await AssertRefusedAsync(await AdvanceAsync(kitchen, orderId, null),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await kitchen.PostAsync($"/api/orders/{orderId}/status", null),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Equal(HttpStatusCode.NotFound, (await AdvanceAsync(kitchen, int.MaxValue, "Preparing")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CancelAsync(kitchen, int.MaxValue, "วัตถุดิบหมด")).StatusCode);

        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await AdvanceAsync(anonymous, orderId, "Preparing")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CancelAsync(anonymous, orderId, "วัตถุดิบหมด")).StatusCode);
        Assert.Equal(OrderStatus.Pending, (await StoredAsync(app, orderId)).Status);

        // เจ้าของร้านทำงานบนจอครัวด้วยบัญชีของตัวเองได้ (KitchenAccess)
        Assert.Equal("Preparing", (await OkAsync(await AdvanceAsync(owner, orderId, "preparing")))
            .GetProperty("status").GetString());
        Assert.Single(StatusChanges(notifier, orderId));
    }

    /// <summary>
    /// สองแท็บเล็ตกด "เริ่มทำ" ออเดอร์เดียวกันพร้อมกัน ต้องสำเร็จครั้งเดียว
    ///
    /// เพื่อให้สองคำขอชนกันจริงที่ฐานข้อมูล (ไม่ใช่แค่ส่งพร้อมกันแล้วหวังว่าจะชน) เทสต์ล็อกแถวของออเดอร์ไว้ก่อน
    /// แล้วปล่อยเมื่อทั้งสองคำขอไปรอที่คำสั่ง UPDATE ของแถวนั้นแล้ว: การเลื่อนแบบ "อ่านสถานะแล้วค่อยเขียน" จะอ่าน
    /// Pending ได้ทั้งคู่และสำเร็จทั้งคู่ ส่วน conditional update จากสถานะที่คาดไว้ต้องได้ 0 แถวในคำขอที่สอง
    /// </summary>
    [Fact]
    public async Task Two_tablets_advancing_the_same_order_at_once_get_exactly_one_success()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var tabletA = await KitchenClientAsync(app);
        var tabletB = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session, Line(menu.MeatId, 1));

        await using var holder = new NpgsqlConnection(postgres.ConnectionString);
        await holder.OpenAsync();
        // แอปในเทสต์ใช้ connection string เดียวกับ fixture จึงเป็นฐานข้อมูลเดียวกัน
        var database = holder.Database;
        await using var lockTx = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("""SELECT 1 FROM "Orders" WHERE "Id" = @id FOR UPDATE""", holder, lockTx))
        {
            hold.Parameters.AddWithValue("id", orderId);
            await hold.ExecuteScalarAsync();
        }

        var first = AdvanceAsync(tabletA, orderId, "Preparing");
        var second = AdvanceAsync(tabletB, orderId, "Preparing");
        await WaitForBlockedOrderUpdatesAsync(database, 2);
        await lockTx.CommitAsync();
        var responses = await Task.WhenAll(first, second);

        var succeeded = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        var refused = responses.Where(r => r.StatusCode != HttpStatusCode.OK).ToList();
        Assert.Single(succeeded);
        Assert.Single(refused);
        Assert.Equal("Preparing", (await JsonAsync(succeeded[0])).GetProperty("status").GetString());
        await AssertRefusedAsync(refused[0], HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);

        Assert.Equal(OrderStatus.Preparing, (await StoredAsync(app, orderId)).Status);
        Assert.Single(StatusChanges(notifier, orderId));

        // คู่เดียวกันสำหรับขั้นถัดไป ส่งพร้อมกันโดยไม่ล็อก: ผลยังเป็นสำเร็จหนึ่ง ปฏิเสธหนึ่ง
        var again = await Task.WhenAll(AdvanceAsync(tabletA, orderId, "Ready"), AdvanceAsync(tabletB, orderId, "Ready"));
        Assert.Single(again, r => r.StatusCode == HttpStatusCode.OK);
        await AssertRefusedAsync(again.Single(r => r.StatusCode != HttpStatusCode.OK),
            HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);
        Assert.Equal(["Preparing", "Ready"], StatusChanges(notifier, orderId).Select(o => o.Status));
    }

    /// <summary>รอจน Postgres รายงานว่ามี UPDATE ของตาราง Orders รอล็อกอยู่ครบจำนวน (ไม่มีการ sleep)</summary>
    private async Task WaitForBlockedOrderUpdatesAsync(string database, int expected)
    {
        await using var probe = new NpgsqlConnection(postgres.ConnectionString);
        await probe.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = @db AND wait_event_type = 'Lock' AND query ILIKE 'UPDATE "Orders"%'
            """, probe);
        count.Parameters.AddWithValue("db", database);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while ((long)(await count.ExecuteScalarAsync())! < expected)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"คำขอเลื่อนสถานะไม่ได้ไปรอที่แถวของออเดอร์ครบ {expected} คำขอ");
        }
    }

    // ------------------------------------------------------------------ ยกเลิก

    [Fact]
    public async Task Cancel_needs_a_reason_of_one_to_two_hundred_characters()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session, Line(menu.DrinkId, 1));

        foreach (var reason in new[] { null, "", "   ", new string('ก', 201) })
        {
            await AssertRefusedAsync(await CancelAsync(kitchen, orderId, reason),
                HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        await AssertRefusedAsync(await kitchen.PostAsync($"/api/orders/{orderId}/cancel", null),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var untouched = await StoredAsync(app, orderId);
        Assert.Equal(OrderStatus.Pending, untouched.Status);
        Assert.Null(untouched.CancelReason);
        Assert.Empty(Edits(notifier, orderId));

        // ขอบบนพอดี 200 ตัวอักษรผ่าน (ช่องว่างหัวท้ายไม่นับ)
        var longest = new string('ข', 200);
        var cancelled = await OkAsync(await CancelAsync(kitchen, orderId, $"  {longest} "));
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(longest, cancelled.GetProperty("cancelReason").GetString());
        Assert.Equal(longest, (await StoredAsync(app, orderId)).CancelReason);
    }

    [Fact]
    public async Task Cancel_works_from_Pending_Preparing_and_Ready_but_never_twice()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);

        var pending = await SubmitAsync(app, session, Line(menu.MeatId, 1));
        var preparing = await SubmitAsync(app, session, Line(menu.MeatId, 2));
        var ready = await SubmitAsync(app, session, Line(menu.DrinkId, 1));
        await OkAsync(await AdvanceAsync(kitchen, preparing, "Preparing"));
        await OkAsync(await AdvanceAsync(kitchen, ready, "Preparing"));
        await OkAsync(await AdvanceAsync(kitchen, ready, "Ready"));

        foreach (var (orderId, reason, client) in new[]
                 {
                     (pending, "วัตถุดิบหมด: หมูสามชั้น", kitchen),
                     (preparing, "ลูกค้าแจ้งเปลี่ยน", owner),
                     (ready, "สั่งผิด/ซ้ำ", kitchen),
                 })
        {
            var body = await OkAsync(await CancelAsync(client, orderId, reason));
            Assert.Equal(orderId, IdOf(body));
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.Equal(reason, body.GetProperty("cancelReason").GetString());

            var stored = await StoredAsync(app, orderId);
            Assert.Equal(OrderStatus.Cancelled, stored.Status);
            Assert.Equal(reason, stored.CancelReason);

            // ยกเลิกซ้ำไม่ได้ เหตุผลเดิมไม่ถูกทับ และเลื่อนสถานะต่อจาก Cancelled ไม่ได้
            await AssertRefusedAsync(await CancelAsync(kitchen, orderId, "อีกเหตุผล"),
                HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);
            await AssertRefusedAsync(await AdvanceAsync(kitchen, orderId, "Preparing"),
                HttpStatusCode.Conflict, ErrorCodes.InvalidStatusTransition);
            Assert.Equal(reason, (await StoredAsync(app, orderId)).CancelReason);

            // การยกเลิกประกาศเป็น orderEdited ครั้งเดียว ไปที่ครัว/เจ้าของร้านและ group ของรอบนี้
            var edit = Assert.Single(Edits(notifier, orderId));
            Assert.Equal(session.Id, edit.SessionId);
            Assert.Null(edit.PrimarySessionId);
            Assert.Equal("Cancelled", ((OrderDto)edit.Order).Status);
            Assert.Equal(reason, ((OrderDto)edit.Order).CancelReason);
        }

        // การยกเลิกไม่ใช่การเลื่อนสถานะ: orderStatusChanged มีเฉพาะสามครั้งที่เลื่อนจริง
        Assert.Equal(3, notifier.Events.Count(e => e.Event == "orderStatusChanged"));
    }

    /// <summary>ออเดอร์ที่ยกเลิกไม่ถูกคิดเงิน — ดูผ่านบิลของหน้ารายละเอียดรอบ (ตัวคำนวณบิลจุดเดียวที่ทุกที่ใช้)</summary>
    [Fact]
    public async Task A_cancelled_order_is_not_billed()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);

        var cancelled = await SubmitAsync(app, session, Line(menu.DrinkId, 2), Line(menu.MeatId, 1));
        var kept = await SubmitAsync(app, session, Line(menu.DrinkId, 1));

        var before = (await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"))).GetProperty("bill");
        Assert.Equal(135m, before.GetProperty("alaCarteTotal").GetDecimal());

        await OkAsync(await AdvanceAsync(kitchen, cancelled, "Preparing"));
        await OkAsync(await CancelAsync(kitchen, cancelled, "วัตถุดิบหมด"));

        var detail = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));
        var bill = detail.GetProperty("bill");
        Assert.Equal(45m, bill.GetProperty("alaCarteTotal").GetDecimal());
        var line = Assert.Single(bill.GetProperty("alaCarteLines").EnumerateArray());
        Assert.Equal(1, line.GetProperty("quantity").GetInt32());
        Assert.Equal(before.GetProperty("subtotal").GetDecimal() - 90m, bill.GetProperty("subtotal").GetDecimal());

        // รอบที่ยกเลิกยังอยู่ในรายการของรอบนั้นพร้อมเหตุผล (ให้เจ้าของร้านเห็นว่าทำไมไม่ถูกคิดเงิน)
        var orders = detail.GetProperty("orders").EnumerateArray().ToList();
        var shown = orders.Single(o => IdOf(o) == cancelled);
        Assert.Equal("Cancelled", shown.GetProperty("status").GetString());
        Assert.Equal("วัตถุดิบหมด", shown.GetProperty("cancelReason").GetString());
        Assert.Equal("Pending", orders.Single(o => IdOf(o) == kept).GetProperty("status").GetString());
    }

    /// <summary>
    /// รอบที่ชำระแล้วมีได้แค่ Ready/Cancelled (การจ่ายถูกขวางโดย Pending/Preparing) — ยกเลิก Ready ของบิลที่ปิดแล้ว
    /// จะทำให้ยอดบนหน้ารายละเอียดไม่ตรงกับใบเสร็จ จึงปฏิเสธด้วย <c>SESSION_CLOSED</c>
    /// </summary>
    [Fact]
    public async Task Orders_of_a_paid_table_cannot_be_cancelled()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var session = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, session, Line(menu.DrinkId, 1));
        await OkAsync(await AdvanceAsync(kitchen, orderId, "Preparing"));
        await OkAsync(await AdvanceAsync(kitchen, orderId, "Ready"));
        await MarkPaidAsync(app, session.Id);

        await AssertRefusedAsync(await CancelAsync(kitchen, orderId, "วัตถุดิบหมด"),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Equal(OrderStatus.Ready, (await StoredAsync(app, orderId)).Status);
        Assert.Empty(Edits(notifier, orderId));
    }

    // ------------------------------------------------------------------ ใครได้รับอะไร (WebSocket จริง)

    /// <summary>
    /// ผ่าน endpoint จริงและ <see cref="LiveNotifier"/> ตัวจริง: การเลื่อนสถานะถึงครัวและเจ้าของร้าน (คนละครั้งเดียว)
    /// และไม่ถึงลูกค้าแม้แต่โต๊ะเจ้าของออเดอร์; การยกเลิกถึงครัว เจ้าของร้าน และโต๊ะนั้น แต่ไม่ถึงโต๊ะอื่น
    /// </summary>
    [Fact]
    public async Task Over_the_hub_status_changes_reach_staff_only_and_a_cancel_reaches_the_table_too()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchenHttp = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var table = await OpenSessionAsync(app, owner);
        var otherTable = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, table, Line(menu.MeatId, 1));

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var customer = await ConnectAsync(app, token: table.Token);
        await using var otherCustomer = await ConnectAsync(app, token: otherTable.Token);

        await OkAsync(await AdvanceAsync(kitchenHttp, orderId, "Preparing"));

        var seen = await kitchen.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == orderId, Within);
        Assert.Equal("Preparing", seen.GetProperty("status").GetString());
        await ownerLive.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == orderId, Within);
        await BarrierAsync(app, kitchen, ownerLive, customer, otherCustomer);

        Assert.Single(kitchen.Inbox.Of("orderStatusChanged"));
        Assert.Single(ownerLive.Inbox.Of("orderStatusChanged"));
        Assert.Empty(customer.Inbox.Of("orderStatusChanged"));
        Assert.Empty(otherCustomer.Inbox.Of("orderStatusChanged"));

        await OkAsync(await CancelAsync(kitchenHttp, orderId, "วัตถุดิบหมด"));

        foreach (var live in new[] { kitchen, ownerLive, customer })
        {
            var edited = await live.Inbox.WaitForAsync("orderEdited", p => IdOf(p) == orderId, Within);
            Assert.Equal("Cancelled", edited.GetProperty("status").GetString());
            Assert.Equal("วัตถุดิบหมด", edited.GetProperty("cancelReason").GetString());
        }

        await BarrierAsync(app, kitchen, ownerLive, customer, otherCustomer);
        Assert.Single(kitchen.Inbox.Of("orderEdited"));
        Assert.Single(ownerLive.Inbox.Of("orderEdited"));
        Assert.Single(customer.Inbox.Of("orderEdited"));
        Assert.Empty(otherCustomer.Inbox.Of("orderEdited"));
        Assert.All(otherCustomer.Inbox.Names(), name => Assert.Equal("menuItemChanged", name));
        Assert.Empty(customer.Inbox.Of("orderStatusChanged"));
    }
}
