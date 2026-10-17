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
/// Ticket 09 — ลูกค้าสแกน QR เข้าเมนูตามแพ็กเกจ ผ่าน seam 1: แอปจริง + Postgres จริง
/// ยืนยันเฉพาะพฤติกรรมภายนอก (สถานะ HTTP, `code`, ข้อความไทย, ข้อมูลที่คืน) ตามกฎใน spec:
/// เมนูที่เห็น = Buffet ที่ MinPackageRank &lt;= rank ของรอบนั้น + AlaCarte ทุกอย่าง เฉพาะที่ active
/// ลูกค้าไม่มี auth — token คือสิ่งที่ระบุรอบการนั่ง และต้องเห็นเฉพาะข้อมูลของโต๊ะตัวเอง
/// </summary>
[Collection(ApiCollection.Name)]
public class CustomerMenuTests(PostgresFixture postgres)
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

    /// <summary>ชื่อหมวด/เมนู/โต๊ะไม่ซ้ำกับของเทสต์อื่น (คอลเลกชันเดียวกันใช้ฐานข้อมูลร่วมกัน)</summary>
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static async Task<int> CreateCategoryAsync(HttpClient owner, string name)
    {
        var response = await owner.PostAsJsonAsync("/api/categories", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>เมนูถูกสร้างผ่านเส้นทางจริงของ ticket 05 เพื่อไม่ให้เทสต์นี้ลอกกฎของเมนูมาใช้ซ้ำ</summary>
    private static async Task<int> CreateItemAsync(HttpClient owner, int categoryId, string name, string kind,
        int? minPackageRank, decimal price, bool? isAvailable = null, bool? isActive = null)
    {
        var response = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name,
            categoryId,
            kind,
            minPackageRank,
            price,
            isAvailable,
            isActive,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt32();
    }

    private static Task<int> CreateBuffetItemAsync(HttpClient owner, int categoryId, string name, int minPackageRank,
        bool? isAvailable = null, bool? isActive = null) =>
        CreateItemAsync(owner, categoryId, name, "Buffet", minPackageRank, 0m, isAvailable, isActive);

    private static Task<int> CreateAlaCarteItemAsync(HttpClient owner, int categoryId, string name,
        decimal price = 45m, bool? isAvailable = null, bool? isActive = null) =>
        CreateItemAsync(owner, categoryId, name, "AlaCarte", null, price, isAvailable, isActive);

    private static async Task<string> CreateTableAsync(HttpClient owner, string name)
    {
        var response = await owner.PostAsJsonAsync("/api/tables", new { name, seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return name;
    }

    /// <summary>
    /// แพ็กเกจถูก seed ลงฐานข้อมูลตรง ๆ (เหมือน ticket 07) เพราะ Rank ต้องไม่ซ้ำกับเทสต์อื่นใน
    /// คอลเลกชันเดียวกัน — เทสต์นี้ต้อง "รู้เลข rank" เพื่อสร้างเมนูที่ min package rank ตรงกัน
    /// </summary>
    private static async Task<int> SeedPackageAsync(TestApp app, int rank, string name, int minutes = 90) =>
        await app.WithDbAsync(async db =>
        {
            var package = new Package
            {
                Name = name,
                Rank = rank,
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = minutes,
                IsActive = true,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    /// <summary>สอง rank ที่ยังว่างอยู่และเรียงกัน (rank ต่ำ = แพ็กเกจล่าง, rank สูง = แพ็กเกจบน)</summary>
    private static async Task<(int Low, int High)> TwoFreeRanksAsync(TestApp app) =>
        await app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.AsNoTracking().Select(p => p.Rank).ToListAsync();
            for (var low = 1; low < 99; low++)
            {
                if (!taken.Contains(low) && !taken.Contains(low + 1))
                {
                    return (low, low + 1);
                }
            }

            throw new InvalidOperationException("ไม่มี rank ว่างสองตัวที่เรียงกันให้ใช้ในเทสต์นี้");
        });

    private static async Task<string> OpenSessionTokenAsync(HttpClient owner, string tableId, int packageId,
        int adults = 2, int children = 0)
    {
        var response = await owner.PostAsJsonAsync("/api/sessions", new
        {
            tableId = await TableIdAsync(owner, tableId),
            packageId,
            adultCount = adults,
            childCount = children,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("qrUrl").GetString()!.Split('/').Last();
    }

    private static async Task<int> TableIdAsync(HttpClient owner, string name)
    {
        var tables = await BodyAsync(await owner.GetAsync("/api/tables"));
        foreach (var table in tables.EnumerateArray())
        {
            if (table.GetProperty("name").GetString() == name)
            {
                return table.GetProperty("id").GetInt32();
            }
        }

        throw new InvalidOperationException($"ไม่พบโต๊ะ {name}");
    }

    private static async Task<int> OpenSessionIdAsync(HttpClient owner, string tableName, int packageId,
        int adults = 2, int children = 0)
    {
        var response = await owner.PostAsJsonAsync("/api/sessions", new
        {
            tableId = await TableIdAsync(owner, tableName),
            packageId,
            adultCount = adults,
            childCount = children,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt32();
    }

    private static Task<HttpResponseMessage> SessionAsync(HttpClient customer, string token) =>
        customer.GetAsync($"/api/s/{token}");

    private static Task<HttpResponseMessage> MenuAsync(HttpClient customer, string token) =>
        customer.GetAsync($"/api/s/{token}/menu");

    private static Task<string> SessionTokenAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).Token);

    private static async Task SetSessionAsync(TestApp app, int sessionId, Action<TableSession> change) =>
        await app.WithDbAsync(async db =>
        {
            var session = await db.TableSessions.SingleAsync(s => s.Id == sessionId);
            change(session);
            await db.SaveChangesAsync();
            return 0;
        });

    /// <summary>เมนูที่ลูกค้าเห็น ตัดสินจากชื่อเมนู (ฐานข้อมูลใช้ร่วมกันกับเทสต์อื่น)</summary>
    private static IEnumerable<string> MenuItemNames(JsonElement menu) =>
        menu.GetProperty("categories").EnumerateArray()
            .SelectMany(c => c.GetProperty("items").EnumerateArray())
            .Select(i => i.GetProperty("name").GetString()!);

    private static IEnumerable<string> MenuCategoryNames(JsonElement menu) =>
        menu.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()!);

    private static JsonElement MenuItem(JsonElement menu, string name) =>
        menu.GetProperty("categories").EnumerateArray()
            .SelectMany(c => c.GetProperty("items").EnumerateArray())
            .Single(i => i.GetProperty("name").GetString() == name);

    // ------------------------------------------------------------ เมนูตามแพ็กเกจ

    [Fact]
    public async Task A_low_rank_session_does_not_see_a_higher_rank_buffet_item_while_the_high_rank_one_does()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, high) = await TwoFreeRanksAsync(app);
        var lowPackage = await SeedPackageAsync(app, low, Unique("Silver"));
        var highPackage = await SeedPackageAsync(app, high, Unique("Platinum"));

        var category = await CreateCategoryAsync(owner, Unique("หมู"));
        var itemForLow = Unique("สันคอหมูสไลซ์");
        var itemForHigh = Unique("ริบอายสไลซ์");
        _ = await CreateBuffetItemAsync(owner, category, itemForLow, low);
        _ = await CreateBuffetItemAsync(owner, category, itemForHigh, high);

        var lowTable = await CreateTableAsync(owner, Unique("A"));
        var highTable = await CreateTableAsync(owner, Unique("B"));
        var lowToken = await OpenSessionTokenAsync(owner, lowTable, lowPackage);
        var highToken = await OpenSessionTokenAsync(owner, highTable, highPackage);
        var customer = app.CreateClient();

        var lowMenu = await BodyAsync(await MenuAsync(customer, lowToken));
        var highMenu = await BodyAsync(await MenuAsync(customer, highToken));

        Assert.Contains(itemForLow, MenuItemNames(lowMenu));
        Assert.DoesNotContain(itemForHigh, MenuItemNames(lowMenu));
        // แพ็กเกจสูงสั่งเมนูของแพ็กเกจต่ำกว่าได้ด้วย (spec 81)
        Assert.Contains(itemForLow, MenuItemNames(highMenu));
        Assert.Contains(itemForHigh, MenuItemNames(highMenu));
    }

    [Fact]
    public async Task An_ala_carte_item_is_visible_to_every_rank_with_its_price()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, high) = await TwoFreeRanksAsync(app);
        var lowPackage = await SeedPackageAsync(app, low, Unique("Silver"));
        var highPackage = await SeedPackageAsync(app, high, Unique("Gold"));

        var category = await CreateCategoryAsync(owner, Unique("เครื่องดื่ม"));
        var drink = Unique("ชาไทยเย็น");
        _ = await CreateAlaCarteItemAsync(owner, category, drink, price: 45m);

        var lowToken = await OpenSessionTokenAsync(owner, await CreateTableAsync(owner, Unique("C")), lowPackage);
        var highToken = await OpenSessionTokenAsync(owner, await CreateTableAsync(owner, Unique("D")), highPackage);
        var customer = app.CreateClient();

        var lowMenu = await BodyAsync(await MenuAsync(customer, lowToken));
        var highMenu = await BodyAsync(await MenuAsync(customer, highToken));

        Assert.Contains(drink, MenuItemNames(lowMenu));
        Assert.Contains(drink, MenuItemNames(highMenu));
        var line = MenuItem(lowMenu, drink);
        Assert.Equal("AlaCarte", line.GetProperty("kind").GetString());
        Assert.Equal(45m, line.GetProperty("price").GetDecimal());
        Assert.True(line.GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task Inactive_items_are_absent_from_the_customer_menu()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, _) = await TwoFreeRanksAsync(app);
        var package = await SeedPackageAsync(app, low, Unique("Silver"));
        var category = await CreateCategoryAsync(owner, Unique("ทะเล"));

        var hiddenBuffet = Unique("เมนูที่ปิด");
        var hiddenAlaCarte = Unique("น้ำที่ปิด");
        var shown = Unique("หมึกกล้วย");
        _ = await CreateBuffetItemAsync(owner, category, hiddenBuffet, low, isActive: false);
        _ = await CreateAlaCarteItemAsync(owner, category, hiddenAlaCarte, isActive: false);
        _ = await CreateBuffetItemAsync(owner, category, shown, low);

        var token = await OpenSessionTokenAsync(owner, await CreateTableAsync(owner, Unique("E")), package);
        var menu = await BodyAsync(await MenuAsync(app.CreateClient(), token));
        var names = MenuItemNames(menu).ToList();

        Assert.Contains(shown, names);
        Assert.DoesNotContain(hiddenBuffet, names);
        Assert.DoesNotContain(hiddenAlaCarte, names);
    }

    [Fact]
    public async Task A_sold_out_item_is_reported_as_unavailable_instead_of_being_hidden()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, _) = await TwoFreeRanksAsync(app);
        var package = await SeedPackageAsync(app, low, Unique("Silver"));
        var category = await CreateCategoryAsync(owner, Unique("ทะเล"));

        var soldOut = Unique("กุ้งขาว");
        _ = await CreateBuffetItemAsync(owner, category, soldOut, low, isAvailable: false);

        var token = await OpenSessionTokenAsync(owner, await CreateTableAsync(owner, Unique("F")), package);
        var menu = await BodyAsync(await MenuAsync(app.CreateClient(), token));

        // แถวต้องยังอยู่ (สีเทา + ป้าย "หมด" ฝั่งจอ) เพื่อให้ลูกค้าเห็นว่าเมนูนี้มีแต่ของหมด
        Assert.Contains(soldOut, MenuItemNames(menu));
        Assert.False(MenuItem(menu, soldOut).GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task A_category_with_nothing_this_package_may_order_is_not_a_tab()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, high) = await TwoFreeRanksAsync(app);
        var package = await SeedPackageAsync(app, low, Unique("Silver"));

        var onlyHighCategory = Unique("พรีเมียม");
        var category = await CreateCategoryAsync(owner, onlyHighCategory);
        _ = await CreateBuffetItemAsync(owner, category, Unique("วากิว"), high);

        var token = await OpenSessionTokenAsync(owner, await CreateTableAsync(owner, Unique("G")), package);
        var menu = await BodyAsync(await MenuAsync(app.CreateClient(), token));

        // แท็บหมวดที่ไม่มีอะไรสั่งได้จะทำให้ลูกค้าเสียเวลากดเข้าไปเปล่า ๆ
        Assert.DoesNotContain(onlyHighCategory, MenuCategoryNames(menu));
    }

    // ------------------------------------------------------------ token และสถานะรอบ

    [Fact]
    public async Task An_unknown_token_is_refused_with_session_not_found_on_both_reads()
    {
        using var app = new TestApp(postgres);
        var customer = app.CreateClient();
        var token = Unique("ไม่มีจริง");

        foreach (var response in new[] { await SessionAsync(customer, token), await MenuAsync(customer, token) })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("SESSION_NOT_FOUND", await ProblemCodeAsync(response));
            AssertThai(await ProblemMessageAsync(response));
        }
    }

    [Fact]
    public async Task A_session_that_is_no_longer_open_is_refused_with_session_closed()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, _) = await TwoFreeRanksAsync(app);
        var package = await SeedPackageAsync(app, low, Unique("Silver"));

        var paid = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, Unique("H")), package);
        await SetSessionAsync(app, paid, s =>
        {
            s.Status = SessionStatus.Paid;
            s.ClosedAt = DateTimeOffset.UtcNow;
        });

        var voided = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, Unique("I")), package);
        await SetSessionAsync(app, voided, s =>
        {
            s.Status = SessionStatus.Voided;
            s.ClosedAt = DateTimeOffset.UtcNow;
        });

        var customer = app.CreateClient();
        foreach (var sessionId in new[] { paid, voided })
        {
            var token = await SessionTokenAsync(app, sessionId);
            foreach (var response in new[] { await SessionAsync(customer, token), await MenuAsync(customer, token) })
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                Assert.Equal("SESSION_CLOSED", await ProblemCodeAsync(response));
                AssertThai(await ProblemMessageAsync(response));
            }
        }
    }

    /// <summary>
    /// เลย EndsAt แล้วการสั่งถูกปิด (ticket 10 ตอบ SESSION_EXPIRED) แต่ฝั่งอ่านต้องยังทำงาน
    /// ไม่งั้นจอลูกค้าจะไม่รู้เวลาและขึ้น "หมดเวลาสั่งอาหาร" ไม่ได้ — ตั้งเวลาที่หมดผ่านข้อมูล ไม่มีการ sleep
    /// </summary>
    [Fact]
    public async Task A_session_past_its_end_time_still_reads_its_info_and_menu()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, _) = await TwoFreeRanksAsync(app);
        var package = await SeedPackageAsync(app, low, Unique("Silver"));
        var sessionId = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, Unique("J")), package);

        var endsAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await SetSessionAsync(app, sessionId, s => s.EndsAt = endsAt);

        var customer = app.CreateClient();
        var token = await SessionTokenAsync(app, sessionId);

        var info = await SessionAsync(customer, token);
        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        var body = await BodyAsync(info);
        Assert.Equal(endsAt.ToUnixTimeMilliseconds() / 1000,
            body.GetProperty("endsAt").GetDateTimeOffset().ToUnixTimeSeconds());

        Assert.Equal(HttpStatusCode.OK, (await MenuAsync(customer, token)).StatusCode);
    }

    // ------------------------------------------------------------ ข้อมูลรอบและนาฬิกา

    [Fact]
    public async Task The_session_read_carries_the_table_the_package_the_limits_the_banner_and_the_server_clock()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, _) = await TwoFreeRanksAsync(app);
        var packageName = Unique("Gold");
        var package = await SeedPackageAsync(app, low, packageName, minutes: 120);
        var tableName = Unique("A");

        var sessionId = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, tableName), package,
            adults: 2, children: 1);
        var token = await SessionTokenAsync(app, sessionId);
        var row = await app.WithDbAsync(db => db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));
        var settings = await app.WithDbAsync(db => db.Settings.AsNoTracking().SingleAsync());

        var response = await SessionAsync(app.CreateClient(), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);

        Assert.Equal(tableName, body.GetProperty("tableName").GetString());
        Assert.Equal("A", body.GetProperty("zone").GetString());
        Assert.Equal(packageName, body.GetProperty("packageName").GetString());
        Assert.Equal(low, body.GetProperty("packageRank").GetInt32());
        Assert.Equal(2, body.GetProperty("adults").GetInt32());
        Assert.Equal(1, body.GetProperty("children").GetInt32());
        Assert.Equal(row.OpenedAt.ToUnixTimeMilliseconds() / 1000,
            body.GetProperty("openedAt").GetDateTimeOffset().ToUnixTimeSeconds());
        Assert.Equal(120d, (row.EndsAt - row.OpenedAt).TotalMinutes, 3);

        // ลิมิตและนาทีเตือน Last order มาจากค่าตั้งของร้าน ชุดเดียวกับที่ ticket 10 บังคับใช้
        Assert.Equal(settings.LastOrderWarningMinutes, body.GetProperty("lastOrderWarningMinutes").GetInt32());
        Assert.Equal(settings.MaxQtyPerItemPerOrder, body.GetProperty("maxQtyPerItemPerOrder").GetInt32());
        Assert.Equal(settings.MaxItemsPerOrder, body.GetProperty("maxItemsPerOrder").GetInt32());

        // แบนเนอร์และชื่อร้านสำหรับหัวจอ (ค่าที่หายไปจะไม่ถูก serialize ออกมา)
        Assert.Equal(settings.Name, body.GetProperty("shopName").GetString());
        if (settings.BannerImagePath is null)
        {
            Assert.False(body.TryGetProperty("bannerImagePath", out var banner) && banner.ValueKind != JsonValueKind.Null,
                "ร้านที่ยังไม่อัปโหลดแบนเนอร์ต้องไม่มี bannerImagePath");
        }
        else
        {
            Assert.Equal(settings.BannerImagePath, body.GetProperty("bannerImagePath").GetString());
        }

        // นาฬิกาเซิร์ฟเวอร์: จอลูกค้าห้ามเชื่อนาฬิกามือถือ จึงต้องได้เวลาอ้างอิงจากที่นี่เสมอ
        var serverNow = body.GetProperty("serverNow").GetDateTimeOffset();
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - serverNow).TotalSeconds) < 30,
            $"serverNow ต้องเป็นเวลาปัจจุบันของเซิร์ฟเวอร์ แต่ได้ {serverNow:O}");
    }

    [Fact]
    public async Task One_token_never_carries_the_other_table_s_data()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var (low, high) = await TwoFreeRanksAsync(app);
        var firstPackage = Unique("Silver");
        var secondPackage = Unique("Platinum");
        var firstPackageId = await SeedPackageAsync(app, low, firstPackage);
        var secondPackageId = await SeedPackageAsync(app, high, secondPackage);
        var firstTable = Unique("A");
        var secondTable = Unique("B");

        var first = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, firstTable), firstPackageId,
            adults: 2, children: 0);
        var second = await OpenSessionIdAsync(owner, await CreateTableAsync(owner, secondTable), secondPackageId,
            adults: 4, children: 3);

        var customer = app.CreateClient();
        var firstBody = await (await SessionAsync(customer, await SessionTokenAsync(app, first))).Content.ReadAsStringAsync();
        var secondBody = await (await SessionAsync(customer, await SessionTokenAsync(app, second))).Content.ReadAsStringAsync();

        Assert.DoesNotContain(secondTable, firstBody);
        Assert.DoesNotContain(secondPackage, firstBody);
        Assert.DoesNotContain(firstTable, secondBody);
        Assert.DoesNotContain(firstPackage, secondBody);

        var firstJson = await BodyAsync(await SessionAsync(customer, await SessionTokenAsync(app, first)));
        Assert.Equal(firstTable, firstJson.GetProperty("tableName").GetString());
        Assert.Equal(firstPackage, firstJson.GetProperty("packageName").GetString());
        Assert.Equal(2, firstJson.GetProperty("adults").GetInt32());
        Assert.Equal(0, firstJson.GetProperty("children").GetInt32());

        var secondJson = await BodyAsync(await SessionAsync(customer, await SessionTokenAsync(app, second)));
        Assert.Equal(secondTable, secondJson.GetProperty("tableName").GetString());
        Assert.Equal(secondPackage, secondJson.GetProperty("packageName").GetString());
        Assert.Equal(3, secondJson.GetProperty("children").GetInt32());
    }
}
