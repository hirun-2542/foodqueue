using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 10 — ส่งออเดอร์ผ่าน seam 1: แอปจริง + Postgres จริง
/// ยืนยันพฤติกรรมภายนอกเท่านั้น (สถานะ HTTP, `code`, ข้อมูลที่คืน, แถวในฐานข้อมูล, event ที่ถูกส่ง)
/// โดยเฉพาะ "ลำดับการตรวจ" ของ spec ซึ่งเป็นสัญญาหลักของ ticket นี้: ความผิดที่ตรวจถึงก่อนต้องเป็น
/// ความผิดที่ถูกรายงาน แม้คำขอเดียวจะผิดหลายข้อ
/// </summary>
[Collection(ApiCollection.Name)]
public class OrderTests(PostgresFixture postgres)
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString()!;

    private static void AssertThai(string message) =>
        Assert.True(message.Any(c => c >= '\u0e00' && c <= '\u0e7f'), $"ข้อความต้องเป็นภาษาไทย: {message}");

    /// <summary>รหัส + สถานะ HTTP + ข้อความไทยของความผิดหนึ่งข้อ — เทสต์จึงอ่านเป็น "กฎ" ได้ตรง ๆ</summary>
    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code)
    {
        Assert.Equal(ErrorCodes.StatusFor(code), (int)response.StatusCode);
        Assert.Equal(code, await ProblemCodeAsync(response));
        AssertThai((await JsonAsync(response)).GetProperty("message").GetString()!);
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static async Task<int> CreateTableAsync(HttpClient owner, string prefix)
    {
        var response = await owner.PostAsJsonAsync("/api/tables",
            new { name = UniqueName(prefix), seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// แพ็กเกจสร้างผ่านฐานข้อมูลตรง ๆ (เหมือน ticket 06/07) เพราะ Rank มี unique index ร่วมกับเทสต์อื่น
    /// ในคอลเลกชันเดียวกัน จึงต้องเลือก rank ที่ยังว่างจริง ๆ
    /// </summary>
    private static async Task<int> SeedPackageAsync(TestApp app, int rank) =>
        await app.WithDbAsync(async db =>
        {
            var package = new Package
            {
                Name = $"P{Guid.NewGuid():N}"[..12],
                Rank = rank,
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    /// <summary>rank ว่างตัวแรก — จำกัดไม่ให้ถึง 99 เพราะเทสต์หนึ่งต้องใช้ rank+1</summary>
    private static async Task<int> FreeRankAsync(TestApp app) =>
        await app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            return Enumerable.Range(1, 98).First(r => !taken.Contains(r));
        });

    private static async Task<int> CreateCategoryAsync(HttpClient owner, string prefix)
    {
        var response = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName(prefix) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>เมนูสร้างผ่านเส้นทางจริงของ ticket 05 เพื่อไม่ให้เทสต์นี้ลอกกฎของเมนูมาใช้ซ้ำ</summary>
    private static async Task<int> CreateBuffetItemAsync(HttpClient owner, int categoryId, string prefix,
        int minPackageRank, bool isAvailable = true)
    {
        var response = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = UniqueName(prefix),
            categoryId,
            kind = "Buffet",
            minPackageRank,
            price = 0m,
            isAvailable,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    private static async Task<int> CreateAlaCarteItemAsync(HttpClient owner, int categoryId, string prefix,
        decimal price = 45m)
    {
        var response = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = UniqueName(prefix),
            categoryId,
            kind = "AlaCarte",
            price,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>รอบที่เปิดจากเส้นทางจริงของ ticket 07 พร้อม token ที่ลูกค้าใช้ส่งออเดอร์</summary>
    private sealed record OpenedSession(int Id, string Token, int TableId);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int rank)
    {
        var tableId = await CreateTableAsync(owner, "T");
        var packageId = await SeedPackageAsync(app, rank);
        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = 2, childCount = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, tableId);
    }

    /// <summary>ลิมิตของร้านตั้งผ่าน endpoint จริง — เทสต์เรื่องลิมิตต้องไม่พึ่งค่าเริ่มต้นของแถว settings</summary>
    private static async Task SetLimitsAsync(HttpClient owner, int maxQtyPerItem, int maxItems,
        int lastOrderWarningMinutes = 15)
    {
        var response = await owner.PutAsJsonAsync("/api/settings", new
        {
            name = "ชาบูเดโม่",
            address = "99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110",
            phone = "02-000-0000",
            serviceChargePercent = 10m,
            vatPercent = 7m,
            maxQtyPerItemPerOrder = maxQtyPerItem,
            maxItemsPerOrder = maxItems,
            lastOrderWarningMinutes,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>ส่งออเดอร์จากหน้าลูกค้า: ไม่มี auth ใช้ token เป็นตัวระบุเท่านั้น</summary>
    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string token,
        params object[] items) =>
        client.PostAsJsonAsync($"/api/s/{token}/orders", new { items });

    private static object Line(int menuItemId, int quantity, string? note = null) =>
        note is null ? new { menuItemId, quantity } : new { menuItemId, quantity, note };

    private static Task<List<Order>> OrdersOfAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(db => db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.TableSessionId == sessionId).OrderBy(o => o.Number).ToListAsync());

    private static List<OrderCreatedCall> Announced(RecordingNotifier notifier) =>
        notifier.Events.Where(e => e.Event == "orderCreated")
            .Select(e => (OrderCreatedCall)e.Payload).ToList();

    /// <summary>แก้เวลาของรอบเป็น "เลย EndsAt แล้ว" ด้วยข้อมูล ไม่มีการ sleep ในเทสต์ชุดนี้</summary>
    private static Task ExpireAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OpenedAt, DateTimeOffset.UtcNow.AddMinutes(-120))
                .SetProperty(x => x.EndsAt, DateTimeOffset.UtcNow.AddMinutes(-30)));
            return 0;
        });

    /// <summary>ลิมิตของเทสต์ชุดนี้: 3 ต่อเมนู, 5 ต่อรอบ — ทำให้ลิมิตต่อเมนูแพ้ลิมิตรวมได้จริง</summary>
    private const int MaxQty = 3;
    private const int MaxItems = 5;

    // ---------------------------------------------------------------- ลำดับการตรวจ (1)

    [Fact]
    public async Task An_unknown_token_is_session_not_found_and_a_paid_session_is_closed()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "tok", 1);

        var unknown = await SubmitAsync(owner, new string('z', 43), Line(itemId, 1));
        await AssertRefusedAsync(unknown, ErrorCodes.SessionNotFound);

        // การชำระเงินเป็นของ ticket 18 — เทสต์นี้ตั้งสถานะด้วยข้อมูลจึงไม่รอใคร
        await app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == session.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SessionStatus.Paid));
            return 0;
        });

        var paid = await SubmitAsync(owner, session.Token, Line(itemId, 1));
        await AssertRefusedAsync(paid, ErrorCodes.SessionClosed);

        // ไม่มีออเดอร์ถูกสร้าง และไม่มีการประกาศออเดอร์ใหม่ให้ครัวเลย
        Assert.Empty(await OrdersOfAsync(app, session.Id));
        Assert.Empty(Announced(notifier));
    }

    // ---------------------------------------------------------------- ลำดับการตรวจ (2)

    [Fact]
    public async Task Expiry_is_checked_before_emptiness_and_an_expired_session_stays_open_for_the_bill()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "exp", 1);
        await ExpireAsync(app, session.Id);

        // รายการว่างและเลยเวลาพร้อมกัน → ต้องได้ SESSION_EXPIRED (ข้อ 2 มาก่อน EMPTY_ORDER ของข้อ 3)
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token), ErrorCodes.SessionExpired);
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(itemId, 1)),
            ErrorCodes.SessionExpired);

        // การสั่งปิด แต่รอบยัง Open ให้ปรับยอดและชำระได้ (ไม่มีเส้นทางต่อเวลา)
        Assert.Equal(SessionStatus.Open, await app.WithDbAsync(db => db.TableSessions.AsNoTracking()
            .Where(s => s.Id == session.Id).Select(s => s.Status).SingleAsync()));
        Assert.Empty(await OrdersOfAsync(app, session.Id));
        Assert.Empty(Announced(notifier));
    }

    // ---------------------------------------------------------------- ลำดับการตรวจ (3)

    [Fact]
    public async Task Emptiness_is_checked_before_the_limits_and_before_item_lookup()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));

        // ไม่ส่ง items เลย / ส่งลิสต์ว่าง / ส่งบรรทัดที่จำนวนต่ำกว่า 1 — ทั้งสามแบบเป็นออเดอร์ว่าง
        await AssertRefusedAsync(
            await owner.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { }),
            ErrorCodes.EmptyOrder);
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token), ErrorCodes.EmptyOrder);
        // บรรทัดที่จำนวน 0 ชี้เมนูที่ไม่มีอยู่จริง: ข้อ 3 (รายการไม่ว่าง) ต้องมาก่อนข้อ 6 (เมนูมีอยู่)
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(999_999, 0)),
            ErrorCodes.EmptyOrder);

        Assert.Empty(await OrdersOfAsync(app, session.Id));
        Assert.Empty(Announced(notifier));
    }

    // ---------------------------------------------------------------- ลำดับการตรวจ (4) และ (5)

    [Fact]
    public async Task The_limits_are_checked_before_the_item_lookup()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));

        // เมนูที่ไม่มีอยู่จริง แต่จำนวนเกินลิมิตต่อเมนู → ต้องได้ QTY_LIMIT_EXCEEDED (ข้อ 4 มาก่อนข้อ 6)
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(999_998, MaxQty + 1)),
            ErrorCodes.QtyLimitExceeded);

        // เมนูที่ไม่มีอยู่จริงหกรายการ จำนวนละ 1 → ต้องได้ ORDER_ITEMS_LIMIT_EXCEEDED (ข้อ 5 มาก่อนข้อ 6)
        var unknown = Enumerable.Range(0, MaxItems + 1).Select(i => Line(900_000 + i, 1)).ToArray();
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, unknown),
            ErrorCodes.OrderItemsLimitExceeded);

        Assert.Empty(await OrdersOfAsync(app, session.Id));
    }

    [Fact]
    public async Task Duplicate_lines_of_one_item_sum_against_the_per_item_limit()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "dup", 1);

        // 3 + 3 ของเมนูเดียวกัน = 6 > ลิมิต 3 แม้เป็นคนละบรรทัด (คนละหมายเหตุ)
        var over = await SubmitAsync(owner, session.Token, Line(itemId, MaxQty, "ไม่เผ็ด"),
            Line(itemId, MaxQty));
        await AssertRefusedAsync(over, ErrorCodes.QtyLimitExceeded);
        Assert.Contains($"{MaxQty}", (await JsonAsync(over)).GetProperty("message").GetString()!);

        // เท่าลิมิตพอดีผ่านได้ และยังเป็นสองบรรทัดในออเดอร์เดียว (หมายเหตุต่างกัน)
        var atLimit = await SubmitAsync(owner, session.Token, Line(itemId, 2, "ไม่เผ็ด"), Line(itemId, 1));
        Assert.Equal(HttpStatusCode.Created, atLimit.StatusCode);

        var order = Assert.Single(await OrdersOfAsync(app, session.Id));
        Assert.Equal(2, order.Items.Count);
        Assert.Equal(MaxQty, order.Items.Sum(i => i.Quantity));
    }

    [Fact]
    public async Task The_per_item_limit_is_checked_before_the_per_round_limit()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var categoryId = await CreateCategoryAsync(owner, "C");
        var first = await CreateBuffetItemAsync(owner, categoryId, "lim1", 1);
        var second = await CreateBuffetItemAsync(owner, categoryId, "lim2", 1);

        // 4+2 = 6 รายการ: ผิดทั้งลิมิตต่อเมนู (4 > 3) และลิมิตรวม (6 > 5) → ต้องได้รหัสของข้อ 4
        await AssertRefusedAsync(
            await SubmitAsync(owner, session.Token, Line(first, MaxQty + 1), Line(second, 2)),
            ErrorCodes.QtyLimitExceeded);

        // 3+3 = 6: ต่อเมนูเท่าลิมิตพอดี แต่รวมเกิน → ต้องได้รหัสของข้อ 5
        await AssertRefusedAsync(
            await SubmitAsync(owner, session.Token, Line(first, MaxQty), Line(second, MaxQty)),
            ErrorCodes.OrderItemsLimitExceeded);
        Assert.Empty(await OrdersOfAsync(app, session.Id));
    }

    [Fact]
    public async Task Over_the_per_round_limit_is_order_items_limit_exceeded()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var categoryId = await CreateCategoryAsync(owner, "C");
        var lines = new List<object>();
        for (var i = 0; i < 3; i++)
        {
            lines.Add(Line(await CreateBuffetItemAsync(owner, categoryId, $"tot{i}", 1), 2));
        }

        // 2+2+2 = 6 > 5 โดยไม่มีเมนูไหนเกินลิมิตต่อเมนู
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, [.. lines]),
            ErrorCodes.OrderItemsLimitExceeded);
        Assert.Empty(await OrdersOfAsync(app, session.Id));
    }

    // ---------------------------------------------------------------- ลำดับการตรวจ (6), (7), (8)

    [Fact]
    public async Task A_missing_or_inactive_item_is_item_not_found()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var inactive = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "off", 1);
        await app.WithDbAsync(async db =>
        {
            await db.MenuItems.Where(m => m.Id == inactive)
                .ExecuteUpdateAsync(m => m.SetProperty(x => x.IsActive, false));
            return 0;
        });

        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(999_999, 1)),
            ErrorCodes.ItemNotFound);
        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(inactive, 1)),
            ErrorCodes.ItemNotFound);
        Assert.Empty(await OrdersOfAsync(app, session.Id));
    }

    [Fact]
    public async Task A_sold_out_item_is_item_unavailable_not_item_not_found()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "sold", 1,
            isAvailable: false);

        var refused = await SubmitAsync(owner, session.Token, Line(itemId, 1));
        await AssertRefusedAsync(refused, ErrorCodes.ItemUnavailable);
        // ข้อความบอกชื่อเมนูที่หมด เพื่อให้ลูกค้ารู้ว่าต้องเอาอะไรออกจากตะกร้า
        Assert.Contains("หมด", (await JsonAsync(refused)).GetProperty("message").GetString()!);

        // หมดชั่วคราวแต่ยัง active: รหัสจึงเป็นข้อ 7 ไม่ใช่ข้อ 6
        Assert.True(await app.WithDbAsync(db => db.MenuItems.AsNoTracking()
            .Where(m => m.Id == itemId).Select(m => m.IsActive).SingleAsync()));
        Assert.Empty(await OrdersOfAsync(app, session.Id));
    }

    [Fact]
    public async Task A_higher_rank_buffet_item_is_item_not_in_package_for_a_lower_package()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var rank = await FreeRankAsync(app);
        var session = await OpenSessionAsync(app, owner, rank);
        var categoryId = await CreateCategoryAsync(owner, "C");
        var premium = await CreateBuffetItemAsync(owner, categoryId, "rk", rank + 1);
        var included = await CreateBuffetItemAsync(owner, categoryId, "in", rank);
        var alaCarte = await CreateAlaCarteItemAsync(owner, categoryId, "dr");

        await AssertRefusedAsync(await SubmitAsync(owner, session.Token, Line(premium, 1)),
            ErrorCodes.ItemNotInPackage);

        // À la carte ไม่มีแพ็กเกจขั้นต่ำ จึงสั่งได้ทุกระดับ และเมนู rank เท่ากันก็สั่งได้
        var accepted = await SubmitAsync(owner, session.Token, Line(included, 1), Line(alaCarte, 1));
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Single(await OrdersOfAsync(app, session.Id));
    }

    // ---------------------------------------------------------------- เลขรอบ, snapshot และคำตอบถึงลูกค้า

    [Fact]
    public async Task Two_rounds_are_numbered_one_then_two_and_the_customer_answer_carries_no_round_number()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var categoryId = await CreateCategoryAsync(owner, "C");
        var buffet = await CreateBuffetItemAsync(owner, categoryId, "num", 1);
        var drink = await CreateAlaCarteItemAsync(owner, categoryId, "drink", 45m);

        var first = await SubmitAsync(owner, session.Token, Line(buffet, 2, " ไม่เผ็ด "), Line(drink, 1));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // สัญญาถึงหน้าลูกค้า = คำยืนยันเท่านั้น: ไม่มีเลขรอบ ไม่มีสถานะ ไม่มีรายการ
        var confirmation = await JsonAsync(first);
        Assert.Equal(3, confirmation.GetProperty("itemCount").GetInt32());
        Assert.True(confirmation.TryGetProperty("submittedAt", out _));
        Assert.False(confirmation.TryGetProperty("number", out _));
        Assert.False(confirmation.TryGetProperty("status", out _));
        Assert.False(confirmation.TryGetProperty("items", out _));

        var second = await SubmitAsync(owner, session.Token, Line(drink, 1));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var orders = await OrdersOfAsync(app, session.Id);
        Assert.Equal(new[] { 1, 2 }, orders.Select(o => o.Number).ToList());
        Assert.All(orders, o => Assert.Equal(OrderStatus.Pending, o.Status));
        Assert.All(orders, o => Assert.Null(o.CancelReason));

        // snapshot ต่อบรรทัด: บุฟเฟ่ต์ราคา 0, à la carte ราคาที่ snapshot, หมายเหตุตัดช่องว่างหัวท้าย
        var buffetLine = orders[0].Items.Single(i => i.MenuItemId == buffet);
        Assert.Equal(0m, buffetLine.UnitPrice);
        Assert.Equal(MenuItemKind.Buffet, buffetLine.KindSnapshot);
        Assert.Equal(2, buffetLine.Quantity);
        Assert.Equal("ไม่เผ็ด", buffetLine.Note);
        var drinkLine = orders[0].Items.Single(i => i.MenuItemId == drink);
        Assert.Equal(45m, drinkLine.UnitPrice);
        Assert.Equal(MenuItemKind.AlaCarte, drinkLine.KindSnapshot);
        Assert.Null(drinkLine.Note);
    }

    [Fact]
    public async Task A_later_price_change_does_not_touch_an_order_that_was_already_submitted()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var categoryId = await CreateCategoryAsync(owner, "C");
        var drink = await CreateAlaCarteItemAsync(owner, categoryId, "snap", 45m);

        Assert.Equal(HttpStatusCode.Created,
            (await SubmitAsync(owner, session.Token, Line(drink, 1))).StatusCode);

        var updated = await owner.PutAsJsonAsync($"/api/menu-items/{drink}", new
        {
            name = UniqueName("drink"),
            categoryId,
            kind = "AlaCarte",
            price = 60m,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var order = Assert.Single(await OrdersOfAsync(app, session.Id));
        Assert.Equal(45m, Assert.Single(order.Items).UnitPrice);
    }

    [Fact]
    public async Task A_note_over_the_column_length_is_refused_with_a_thai_message_and_creates_no_order()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "note", 1);

        await AssertRefusedAsync(
            await SubmitAsync(owner, session.Token, Line(itemId, 1, new string('ก', 101))),
            ErrorCodes.ValidationFailed);

        // ยาว 100 ตัวอักษรพอดีผ่าน (ขอบเขตเดียวกับคอลัมน์ OrderItem.Note)
        Assert.Equal(HttpStatusCode.Created,
            (await SubmitAsync(owner, session.Token, Line(itemId, 1, new string('ก', 100)))).StatusCode);
        Assert.Single(await OrdersOfAsync(app, session.Id));
    }

    [Fact]
    public async Task Two_simultaneous_submissions_get_numbers_one_and_two()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "race", 1);

        // สองเครื่องกดส่งพร้อมกัน: max+1 ใน transaction เดียว + retry หนึ่งครั้งต้องไม่ทำให้ใครพลาด
        var responses = await Task.WhenAll(
            SubmitAsync(owner, session.Token, Line(itemId, 1)),
            SubmitAsync(owner, session.Token, Line(itemId, 1)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(new[] { 1, 2 }, (await OrdersOfAsync(app, session.Id)).Select(o => o.Number).ToList());
    }

    [Fact]
    public async Task The_next_round_number_is_max_plus_one_not_a_count()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "maxn", 1);

        // รอบที่ 1-4 ถูกยกเลิกไปแล้วและรอบที่ 5 ยังอยู่: เลขรอบถัดไปคือ 6 ไม่ใช่ 2 (นับจากจำนวนที่ยังไม่ถูกยกเลิก)
        var created = DateTimeOffset.UtcNow.AddMinutes(-10);
        await app.WithDbAsync(async db =>
        {
            for (var number = 1; number <= 5; number++)
            {
                db.Orders.Add(new Order
                {
                    TableSessionId = session.Id,
                    Number = number,
                    Status = number < 5 ? OrderStatus.Cancelled : OrderStatus.Pending,
                    CancelReason = number < 5 ? "ลูกค้าแจ้งเปลี่ยน" : null,
                    CreatedAt = created,
                    UpdatedAt = created,
                });
            }

            await db.SaveChangesAsync();
            return 0;
        });

        Assert.Equal(HttpStatusCode.Created,
            (await SubmitAsync(owner, session.Token, Line(itemId, 1))).StatusCode);
        Assert.Equal(6, (await OrdersOfAsync(app, session.Id)).Max(o => o.Number));

        // ออเดอร์ที่ถูกยกเลิกยังกินเลขรอบอยู่ (unique index ร่วมกับออเดอร์ที่ยังไม่ถูกยกเลิก)
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, (await OrdersOfAsync(app, session.Id)).Select(o => o.Number));
    }

    // ---------------------------------------------------------------- realtime

    [Fact]
    public async Task Order_created_is_announced_after_the_write_for_this_session_only()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        await SetLimitsAsync(owner, MaxQty, MaxItems);
        var session = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var other = await OpenSessionAsync(app, owner, await FreeRankAsync(app));
        var itemId = await CreateBuffetItemAsync(owner, await CreateCategoryAsync(owner, "C"), "evt", 1);

        Assert.Equal(HttpStatusCode.Created,
            (await SubmitAsync(owner, session.Token, Line(itemId, 2))).StatusCode);

        // ตัวบันทึกเห็น "ครั้งเดียว" และครั้งนั้นชี้ group ของรอบที่สั่ง (LiveNotifier กระจายต่อให้ครัว/เจ้าของร้าน
        // และ group ของรอบนั้นเท่านั้น — ไม่มีรอบอื่นได้รับ) และรอบนี้ไม่ได้ถูกรวมเข้าบิลอื่น
        var call = Assert.Single(Announced(notifier));
        Assert.Equal(session.Id, call.SessionId);
        Assert.Null(call.PrimarySessionId);

        var dto = Assert.IsType<OrderDto>(call.Order);
        Assert.Equal(1, dto.Number);
        Assert.Equal(session.Id, dto.SessionId);
        Assert.Equal("Pending", dto.Status);
        Assert.Equal(2, dto.Items.Single().Quantity);

        // ออเดอร์ที่ถูกปฏิเสธต้องไม่ประกาศเลย (event หลังบันทึกสำเร็จเท่านั้น)
        var before = Announced(notifier).Count;
        await SubmitAsync(owner, session.Token, Line(999_999, 1));
        Assert.Equal(before, Announced(notifier).Count);
        Assert.DoesNotContain(Announced(notifier), c => c.SessionId == other.Id);
    }
}
