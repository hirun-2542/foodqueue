using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// ออเดอร์ใหม่หนึ่งครั้งที่ระบบส่งออก: <see cref="SessionId"/> คือรอบที่ออเดอร์เป็นของ และ
/// <see cref="PrimarySessionId"/> คือรอบหลักเมื่อรอบนั้นถูกรวมเข้าบิลอื่น (null = ไม่ได้ถูกรวม)
/// การกระจายไป group ครัว/เจ้าของร้าน/รอบนั้นอยู่ใน <see cref="LiveNotifier"/> จุดเดียว
/// </summary>
public sealed record OrderCreatedCall(object Order, int SessionId, int? PrimarySessionId);

/// <summary>
/// ตัวบันทึก event realtime แทน <see cref="LiveNotifier"/> เพื่อให้เห็น "สิ่งที่ถูกส่งออกจากระบบ"
/// หลังบันทึกฐานข้อมูลสำเร็จ โดยไม่ต้องมีไคลเอนต์ SignalR จริงในเทสต์
/// </summary>
public sealed class RecordingNotifier : ILiveNotifier
{
    public ConcurrentQueue<(string Event, object Payload)> Events { get; } = new();

    public Task OrderCreated(object order, int sessionId, int? primarySessionId = null)
    {
        Events.Enqueue(("orderCreated", new OrderCreatedCall(order, sessionId, primarySessionId)));
        return Task.CompletedTask;
    }

    public Task OrderStatusChanged(object order) => Task.CompletedTask;

    public Task OrderEdited(object order, int sessionId, int? primarySessionId = null) => Task.CompletedTask;

    public Task SessionChanged(object summary, int sessionId)
    {
        Events.Enqueue(("sessionChanged", summary));
        return Task.CompletedTask;
    }

    public Task MenuItemChanged(int menuItemId, bool isAvailable, bool isActive) => Task.CompletedTask;

    public Task SessionClosed(int sessionId)
    {
        Events.Enqueue(("sessionClosed", sessionId));
        return Task.CompletedTask;
    }

    public Task SessionClosedMany(IEnumerable<int> sessionIds)
    {
        foreach (var id in sessionIds)
        {
            Events.Enqueue(("sessionClosed", id));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Ticket 07 — เปิดโต๊ะ/QR เฉพาะ session/ยกเลิกโต๊ะ ผ่าน seam 1: แอปจริง + Postgres จริง
/// ยืนยันเฉพาะพฤติกรรมภายนอก (สถานะ HTTP, `code`, ข้อมูลที่คืน, สถานะแถวในฐานข้อมูลที่ผู้ใช้เห็น
/// และ event ที่ถูกส่งหลังบันทึกสำเร็จ)
/// </summary>
[Collection(ApiCollection.Name)]
public class SessionTests(PostgresFixture postgres)
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString()!;

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("message").GetString()!;

    private static void AssertThai(string message) =>
        Assert.True(message.Any(c => c >= '\u0e00' && c <= '\u0e7f'), $"ข้อความต้องเป็นภาษาไทย: {message}");

    /// <summary>
    /// เหตุการณ์เดียวกันต้องเท่ากัน "ถึงระดับที่ฐานข้อมูลเก็บได้" (timestamptz = ไมโครวินาที)
    /// ส่วนค่าที่เพิ่งสร้างในหน่วยความจำยังมี ticks 100 ns ครบ จึงเทียบแบบนี้แทนการเทียบตรง ๆ
    /// </summary>
    private static void AssertSameInstant(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(Math.Abs((expected - actual).TotalMilliseconds) < 1,
            $"คาดว่า {expected:O} แต่ได้ {actual:O}");

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    /// <summary>โต๊ะ/แพ็กเกจถูกสร้างผ่านเส้นทางจริงของ ticket 04/06 เพื่อไม่ให้เซสชันเทสต์ลอกกฎของโมดูลอื่นมาใช้ซ้ำ</summary>
    private static async Task<int> CreateTableAsync(HttpClient client, string prefix, int seats = 4)
    {
        var response = await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName(prefix), seats, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// แพ็กเกจของเทสต์สร้างผ่านฐานข้อมูลตรง ๆ (เหมือน ticket 06) เพราะ Rank ต้องไม่ซ้ำกับเทสต์อื่น
    /// ในคอลเลกชันเดียวกัน และต้องอยู่ในช่วง 1–99 ตามกฎของ ticket 04
    /// </summary>
    private static async Task<int> SeedPackageAsync(TestApp app, string? name = null, decimal adult = 399m,
        decimal child = 199m, int minutes = 90, bool active = true) =>
        await app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r));
            var package = new Package
            {
                Name = name ?? $"P{Guid.NewGuid():N}"[..12],
                Rank = rank,
                AdultPrice = adult,
                ChildPrice = child,
                DurationMinutes = minutes,
                IsActive = active,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private static Task<Package> PackageRowAsync(TestApp app, int packageId) =>
        app.WithDbAsync(async db => await db.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId));

    private static Task<HttpResponseMessage> OpenAsync(HttpClient client, int tableId, int packageId,
        int adults = 2, int children = 0) =>
        client.PostAsJsonAsync("/api/sessions", new
        {
            tableId,
            packageId,
            adultCount = adults,
            childCount = children,
        });

    private static async Task<int> OpenSessionAsync(HttpClient client, int tableId, int packageId,
        int adults = 2, int children = 0)
    {
        var response = await OpenAsync(client, tableId, packageId, adults, children);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    private static async Task<TableSession> SessionRowAsync(TestApp app, int sessionId) =>
        await app.WithDbAsync(async db => await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

    /// <summary>ไม่มีเส้นทางสั่งอาหารจริงจนกว่า ticket 10 — เทสต์ void จึง seed ออเดอร์ลงฐานข้อมูลตรง ๆ</summary>
    private static async Task SeedOrderAsync(TestApp app, int sessionId, OrderStatus status)
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        await app.WithDbAsync(async db =>
        {
            db.Orders.Add(new Order
            {
                TableSessionId = sessionId,
                Number = 1,
                Status = status,
                CancelReason = status == OrderStatus.Cancelled ? "ลูกค้าแจ้งเปลี่ยน" : null,
                CreatedAt = created,
                UpdatedAt = created,
            });
            await db.SaveChangesAsync();
            return 0;
        });
    }

    private static string TodayInBangkok() =>
        Bkk.DateOf(DateTimeOffset.UtcNow).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Opening_a_table_creates_the_session_with_its_token_snapshots_and_end_time()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "X");
        var packageName = $"Gold{Guid.NewGuid():N}"[..12];
        var packageId = await SeedPackageAsync(app, packageName, adult: 399m, child: 199m, minutes: 90);

        var response = await OpenAsync(client, tableId, packageId, adults: 2, children: 1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        var sessionId = body.GetProperty("id").GetInt32();
        Assert.Equal($"/api/sessions/{sessionId}", response.Headers.Location?.ToString());

        Assert.Equal(tableId, body.GetProperty("tableId").GetInt32());
        Assert.Equal(packageName, body.GetProperty("packageName").GetString());
        Assert.Equal(2, body.GetProperty("adults").GetInt32());
        Assert.Equal(1, body.GetProperty("children").GetInt32());
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isDemoBot").GetBoolean());

        var row = await SessionRowAsync(app, sessionId);
        Assert.Equal(SessionStatus.Open, row.Status);
        Assert.Equal(packageName, row.PackageNameSnapshot);
        Assert.Equal(399m, row.AdultPriceSnapshot);
        Assert.Equal(199m, row.ChildPriceSnapshot);
        // EndsAt = OpenedAt + DurationMinutes ของแพ็กเกจ และไม่มีการต่อเวลา
        Assert.Equal(90d, (row.EndsAt - row.OpenedAt).TotalMinutes, 3);
        AssertSameInstant(row.OpenedAt, body.GetProperty("openedAt").GetDateTimeOffset());
        Assert.Equal(row.Token, body.GetProperty("qrUrl").GetString()!.Split('/').Last());
        Assert.Equal($"{ConfigValue(app, "App:PublicBaseUrl")}/s/{row.Token}", body.GetProperty("qrUrl").GetString());

        // snapshot ชุดเดียวกันคือสิ่งที่บิลใช้: 2 × ฿399 + 1 × ฿199 = ฿997.00 ตามกรอบ o02
        var bill = body.GetProperty("bill");
        Assert.Equal(399m, bill.GetProperty("adultPrice").GetDecimal());
        Assert.Equal(199m, bill.GetProperty("childPrice").GetDecimal());
        Assert.Equal(997m, bill.GetProperty("buffetTotal").GetDecimal());
        Assert.Empty(bill.GetProperty("alaCarteLines").EnumerateArray());
        Assert.Empty(bill.GetProperty("adjustments").EnumerateArray());
        Assert.Equal(0, body.GetProperty("orders").GetArrayLength());
        // ยังไม่มีผู้จ่าย: payment เป็น null และ JSON ตัด property ที่เป็น null ทิ้งตาม Program.cs
        Assert.False(body.TryGetProperty("payment", out var payment) && payment.ValueKind != JsonValueKind.Null,
            "รอบที่ยังไม่ชำระต้องไม่มี payment");
    }

    [Fact]
    public async Task A_second_session_on_the_same_table_is_refused_with_table_occupied()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "Y");
        var packageId = await SeedPackageAsync(app);
        await OpenSessionAsync(client, tableId, packageId);

        var second = await OpenAsync(client, tableId, packageId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("TABLE_OCCUPIED", await ProblemCodeAsync(second));
        AssertThai(await ProblemMessageAsync(second));
        Assert.Equal(1, await app.WithDbAsync(db => db.TableSessions
            .CountAsync(s => s.TableId == tableId && s.Status == SessionStatus.Open)));
    }

    [Fact]
    public async Task Two_owners_opening_the_same_table_at_once_leave_exactly_one_session()
    {
        using var app = new TestApp(postgres);
        var first = await OwnerClientAsync(app);
        var second = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(first, "Z");
        var packageId = await SeedPackageAsync(app);

        // ทังสองคำขอเข้าไปถึง INSERT พร้อมกัน — partial unique index ในฐานข้อมูลคือตัวตัดสิน
        var responses = await Task.WhenAll(OpenAsync(first, tableId, packageId), OpenAsync(second, tableId, packageId));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("TABLE_OCCUPIED", await ProblemCodeAsync(loser));
        AssertThai(await ProblemMessageAsync(loser));
        Assert.Equal(1, await app.WithDbAsync(db => db.TableSessions
            .CountAsync(s => s.TableId == tableId && s.Status == SessionStatus.Open)));
    }

    [Fact]
    public async Task Tokens_are_43_base64url_characters_and_never_repeat()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);
        var firstTable = await CreateTableAsync(client, "A");
        var secondTable = await CreateTableAsync(client, "B");

        var first = await OpenSessionAsync(client, firstTable, packageId);
        var second = await OpenSessionAsync(client, secondTable, packageId);

        var tokens = new[] { (await SessionRowAsync(app, first)).Token, (await SessionRowAsync(app, second)).Token };
        foreach (var token in tokens)
        {
            Assert.Equal(43, token.Length);
            Assert.Matches("^[A-Za-z0-9_-]{43}$", token);
        }

        Assert.NotEqual(tokens[0], tokens[1]);
    }

    [Fact]
    public async Task Changing_a_package_price_later_does_not_change_an_open_bill()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "C");
        var packageName = $"Gold{Guid.NewGuid():N}"[..12];
        var packageId = await SeedPackageAsync(app, packageName, adult: 399m, child: 199m, minutes: 90);
        var sessionId = await OpenSessionAsync(client, tableId, packageId, adults: 2, children: 1);

        var updated = await client.PutAsJsonAsync($"/api/packages/{packageId}", new
        {
            name = packageName,
            rank = (await app.WithDbAsync(async db => (await db.Packages.SingleAsync(p => p.Id == packageId)).Rank)),
            adultPrice = 999m,
            childPrice = 499m,
            durationMinutes = 60,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var detail = await JsonAsync(await client.GetAsync($"/api/sessions/{sessionId}"));

        Assert.Equal(399m, detail.GetProperty("bill").GetProperty("adultPrice").GetDecimal());
        Assert.Equal(199m, detail.GetProperty("bill").GetProperty("childPrice").GetDecimal());
        Assert.Equal(997m, detail.GetProperty("bill").GetProperty("buffetTotal").GetDecimal());
    }

    [Fact]
    public async Task A_disabled_table_cannot_be_opened()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "D");
        var packageId = await SeedPackageAsync(app);
        var table = await JsonAsync(await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("D2"), seats = 4 }));
        var disabledId = table.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/tables/{disabledId}",
            new { name = table.GetProperty("name").GetString(), seats = 4, isActive = false })).StatusCode);

        var response = await OpenAsync(client, disabledId, packageId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
        Assert.Equal(0, await app.WithDbAsync(db => db.TableSessions.CountAsync(s => s.TableId == disabledId)));
        // โต๊ะที่ยังใช้งานอยู่เปิดได้ตามปกติ ไม่ใช่การปฏิเสธทั้งระบบ
        Assert.Equal(HttpStatusCode.Created, (await OpenAsync(client, tableId, packageId)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_package_cannot_be_used_to_open_a_table()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "E");
        var packageId = await SeedPackageAsync(app, active: false);

        var response = await OpenAsync(client, tableId, packageId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
        Assert.Equal(0, await app.WithDbAsync(db => db.TableSessions.CountAsync(s => s.TableId == tableId)));
    }

    [Fact]
    public async Task Unknown_table_or_package_is_refused_with_a_thai_message()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);

        var missingTable = await OpenAsync(client, 999_999_981, packageId);
        var missingPackage = await OpenAsync(client, await CreateTableAsync(client, "F"), 999_999_982);

        Assert.Equal(HttpStatusCode.BadRequest, missingTable.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(missingTable));
        AssertThai(await ProblemMessageAsync(missingTable));
        Assert.Equal(HttpStatusCode.BadRequest, missingPackage.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(missingPackage));
        AssertThai(await ProblemMessageAsync(missingPackage));
    }

    [Theory]
    [InlineData(0, 0, "1 ท่าน")]
    [InlineData(-1, 0, "1 ท่าน")]
    [InlineData(2, -1, "เด็ก")]
    public async Task Adult_or_child_counts_outside_the_rule_are_refused(int adults, int children, string hint)
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "G");
        var packageId = await SeedPackageAsync(app);

        var response = await OpenAsync(client, tableId, packageId, adults, children);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        AssertThai(message);
        Assert.Contains(hint, message);
        Assert.Equal(0, await app.WithDbAsync(db => db.TableSessions.CountAsync(s => s.TableId == tableId)));
    }

    [Fact]
    public async Task Voiding_a_session_without_orders_frees_the_table_for_the_next_guest()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "H");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);

        var response = await client.PostAsync($"/api/sessions/{sessionId}/void", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(sessionId, body.GetProperty("id").GetInt32());
        Assert.Equal("Voided", body.GetProperty("status").GetString());

        var row = await SessionRowAsync(app, sessionId);
        Assert.Equal(SessionStatus.Voided, row.Status);
        Assert.NotNull(row.ClosedAt);

        // โต๊ะว่างจริง: เปิดรอบใหม่บนโต๊ะเดิมได้ทันที
        Assert.Equal(HttpStatusCode.Created, (await OpenAsync(client, tableId, packageId)).StatusCode);
    }

    [Fact]
    public async Task Voiding_a_session_that_still_has_an_order_is_refused()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "J");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);
        await SeedOrderAsync(app, sessionId, OrderStatus.Pending);

        var response = await client.PostAsync($"/api/sessions/{sessionId}/void", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("SESSION_HAS_ORDERS", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
        Assert.Equal(SessionStatus.Open, (await SessionRowAsync(app, sessionId)).Status);
    }

    [Fact]
    public async Task Voiding_is_allowed_when_every_order_was_cancelled()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "K");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);
        await SeedOrderAsync(app, sessionId, OrderStatus.Cancelled);

        var response = await client.PostAsync($"/api/sessions/{sessionId}/void", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Voided", (await JsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Voiding_a_session_that_is_already_closed_is_refused()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "L");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/sessions/{sessionId}/void", null)).StatusCode);

        var again = await client.PostAsync($"/api/sessions/{sessionId}/void", null);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("SESSION_CLOSED", await ProblemCodeAsync(again));
        AssertThai(await ProblemMessageAsync(again));
    }

    [Fact]
    public async Task A_table_with_an_open_session_cannot_be_disabled()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "M");
        var packageId = await SeedPackageAsync(app);
        await OpenSessionAsync(client, tableId, packageId);

        var response = await client.PutAsJsonAsync($"/api/tables/{tableId}",
            new { name = UniqueName("M"), seats = 4, isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("TABLE_OCCUPIED", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
        Assert.True(await app.WithDbAsync(db => db.Tables.Where(t => t.Id == tableId).Select(t => t.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Session_detail_returns_the_qr_url_an_empty_bill_and_the_session_state()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "N");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId, adults: 2, children: 1);

        var response = await client.GetAsync($"/api/sessions/{sessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        var row = await SessionRowAsync(app, sessionId);
        Assert.Equal($"{ConfigValue(app, "App:PublicBaseUrl")}/s/{row.Token}", body.GetProperty("qrUrl").GetString());
        Assert.Equal("Open", body.GetProperty("status").GetString());
        Assert.Equal("A", body.GetProperty("zone").GetString());
        Assert.Equal(0, body.GetProperty("orders").GetArrayLength());

        var bill = body.GetProperty("bill");
        Assert.Equal(997m, bill.GetProperty("buffetTotal").GetDecimal());
        Assert.Equal(0m, bill.GetProperty("alaCarteTotal").GetDecimal());
        Assert.Equal(0m, bill.GetProperty("adjustmentTotal").GetDecimal());
        Assert.Equal(bill.GetProperty("buffetTotal").GetDecimal(), bill.GetProperty("subtotal").GetDecimal());
        Assert.Empty(bill.GetProperty("alaCarteLines").EnumerateArray());
        Assert.Empty(bill.GetProperty("adjustments").EnumerateArray());
    }

    [Fact]
    public async Task An_unknown_session_is_a_session_not_found_problem()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var response = await client.GetAsync("/api/sessions/999999971");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("SESSION_NOT_FOUND", await ProblemCodeAsync(response));
        AssertThai(await ProblemMessageAsync(response));
    }

    [Fact]
    public async Task Sessions_are_listed_by_status_and_thai_calendar_day()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "P");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);

        var today = await JsonAsync(await client.GetAsync($"/api/sessions?status=Open&date={TodayInBangkok()}"));
        var ids = today.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(sessionId, ids);
        var listed = Assert.Single(today.EnumerateArray(), e => e.GetProperty("id").GetInt32() == sessionId);
        Assert.Equal("Open", listed.GetProperty("status").GetString());
        Assert.Equal(2, listed.GetProperty("adults").GetInt32());
        AssertSameInstant((await SessionRowAsync(app, sessionId)).OpenedAt,
            listed.GetProperty("openedAt").GetDateTimeOffset());

        var paidToday = await JsonAsync(await client.GetAsync($"/api/sessions?status=Paid&date={TodayInBangkok()}"));
        Assert.DoesNotContain(sessionId, paidToday.EnumerateArray().Select(e => e.GetProperty("id").GetInt32()));
    }

    [Fact]
    public async Task A_status_or_date_the_api_cannot_read_is_a_validation_failed()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);

        var badStatus = await client.GetAsync("/api/sessions?status=Served");
        var badDate = await client.GetAsync("/api/sessions?date=16/10/2569");

        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(badStatus));
        AssertThai(await ProblemMessageAsync(badStatus));
        Assert.Equal(HttpStatusCode.BadRequest, badDate.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(badDate));
        AssertThai(await ProblemMessageAsync(badDate));
    }

    [Fact]
    public async Task There_is_no_route_that_extends_a_session()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "Q");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);

        var response = await client.PostAsJsonAsync($"/api/sessions/{sessionId}/extend", new { minutes = 15 });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"คาดว่า 404/405 แต่ได้ {(int)response.StatusCode}");
        // เวลาหมดของรอบเดิมไม่ถูกแตะ
        var row = await SessionRowAsync(app, sessionId);
        Assert.Equal(90d, (row.EndsAt - row.OpenedAt).TotalMinutes, 3);
    }

    [Fact]
    public async Task Session_routes_answer_401_without_login_and_403_for_kitchen()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(owner, "R");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(owner, tableId, packageId);

        using var anonymous = new TestApp(postgres);
        var guest = anonymous.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(guest, tableId, packageId)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync($"/api/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await guest.PostAsync($"/api/sessions/{sessionId}/void", null)).StatusCode);

        var kitchen = await KitchenClientAsync(app);
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(kitchen, tableId, packageId)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync($"/api/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync("/api/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await kitchen.PostAsync($"/api/sessions/{sessionId}/void", null)).StatusCode);

        // คำขอที่ถูกปฏิเสธต้องไม่ทิ้งผลข้างเคียงในฐานข้อมูล
        Assert.Equal(1, await app.WithDbAsync(db => db.TableSessions.CountAsync(s => s.TableId == tableId)));
    }

    [Fact]
    public async Task Opening_and_voiding_announce_themselves_to_the_owner_and_the_session_group()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "S");
        var packageId = await SeedPackageAsync(app);

        var sessionId = await OpenSessionAsync(client, tableId, packageId);
        Assert.Contains(notifier.Events, e =>
            e.Event == "sessionChanged" && ((FoodQueue.Api.Contracts.SessionSummaryDto)e.Payload).Id == sessionId &&
            ((FoodQueue.Api.Contracts.SessionSummaryDto)e.Payload).Status == "Open");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/sessions/{sessionId}/void", null)).StatusCode);
        Assert.Contains(notifier.Events, e => e.Event == "sessionClosed" && (int)e.Payload == sessionId);
    }

    /// <summary>
    /// เส้นทางที่หน้าลูกค้าใช้: token → session (ticket 09) — seam ไม่ตัดสินรหัสข้อผิดพลาดเอง
    /// จึงคืนแถวของ token ที่ปิดแล้วด้วย และคืน null เฉพาะ token ที่ไม่มีอยู่จริง
    /// </summary>
    [Fact]
    public async Task Find_by_token_returns_the_row_for_open_and_closed_tokens_but_null_for_garbage()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var packageId = await SeedPackageAsync(app);

        var openTable = await CreateTableAsync(client, "X");
        var openSessionId = await OpenSessionAsync(client, openTable, packageId, adults: 3, children: 2);
        var openToken = (await SessionRowAsync(app, openSessionId)).Token;

        var closedTable = await CreateTableAsync(client, "Y");
        var closedSessionId = await OpenSessionAsync(client, closedTable, packageId);
        var closedToken = (await SessionRowAsync(app, closedSessionId)).Token;
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"/api/sessions/{closedSessionId}/void", null)).StatusCode);

        using var scope = app.NewScope();
        var sessions = SessionService.From(scope.ServiceProvider);

        var open = await sessions.FindByTokenAsync(openToken);
        Assert.NotNull(open);
        Assert.Equal(openSessionId, open!.Id);
        Assert.Equal(SessionStatus.Open, open.Status);
        Assert.Equal(3, open.AdultCount);
        Assert.Equal(2, open.ChildCount);
        // navigation ที่ ticket 09 ใช้แมป CustomerSessionDto ติดมาด้วย ไม่ต้องยิงคำขอเพิ่ม
        Assert.Equal(openTable, open.TableId);
        Assert.NotNull(open.Table);
        Assert.NotNull(open.Package);
        Assert.Equal(packageId, open.PackageId);
        Assert.Equal((await SessionRowAsync(app, openSessionId)).Token, open.Token);

        // token ที่ถูกยกเลิกแล้วยังเป็นแถวที่มีอยู่ — ผู้เรียกจึงแยก SESSION_CLOSED จาก SESSION_NOT_FOUND ได้
        var closed = await sessions.FindByTokenAsync(closedToken);
        Assert.NotNull(closed);
        Assert.Equal(closedSessionId, closed!.Id);
        Assert.Equal(SessionStatus.Voided, closed.Status);

        Assert.Null(await sessions.FindByTokenAsync("ไม่มีtokenนี้อยู่จริง"));
        Assert.Null(await sessions.FindByTokenAsync(""));
    }
}
