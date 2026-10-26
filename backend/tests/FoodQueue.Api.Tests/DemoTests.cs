using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Demo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// host เดียวที่เปิด Demo และ seed แล้วสำหรับทั้งคลาส <see cref="DemoTests"/>
/// seed ใช้เวลาราว 30 วินาที จึงทำครั้งเดียวต่อคลาส ไม่ใช่ต่อเทสต์ — fixture ที่เป็น IDisposable
/// ถูก dispose ตอนจบคลาส จึงไม่ทิ้ง host หรือ connection pool ค้างแบบ static
/// </summary>
public sealed class DemoAppFixture : IDisposable
{
    public TestApp App { get; }

    public DemoAppFixture(PostgresFixture postgres)
    {
        App = new TestApp(postgres, demo: true);
        // seed รันตอนสตาร์ทของ host: เริ่มทำงานเมื่อมีคำขอแรก
        _ = App.CreateClient();
    }

    public void Dispose() => App.Dispose();
}

/// <summary>
/// Ticket 24 — Demo Mode ผ่าน seam 1: แอปจริง + Postgres จริง
///
/// ยืนยันพฤติกรรมภายนอกเท่านั้น (สถานะ HTTP, <c>code</c>, ข้อมูลที่คืน, แถวในฐานข้อมูลที่ผู้ใช้เห็น)
/// เทสต์อื่นของชุดนี้ปิด Demo ไว้ (<c>TestApp</c>) ส่วนไฟล์นี้เปิด demo เฉพาะ host ที่ต้องมีข้อมูลตัวอย่าง
/// และ seed ครั้งเดียวต่อคลาสเพื่อไม่ให้เวลารันทั้งชุดบวม (seed ~1,000 บิลผ่านบริการจริงชุดเดียวกับ API)
/// </summary>
[Collection(ApiCollection.Name)]
public class DemoTests : IClassFixture<DemoAppFixture>
{
    private readonly DemoAppFixture _demo;
    private readonly PostgresFixture _postgres;

    public DemoTests(PostgresFixture postgres, DemoAppFixture demo)
    {
        _postgres = postgres;
        _demo = demo;
    }

    private static readonly Regex ReceiptPattern = new(@"^R(\d{8})-(\d{6})$", RegexOptions.Compiled);

    /// <summary>
    /// host ที่เปิด Demo และ seed แล้ว — ใช้ร่วมกันทั้งคลาส (เทสต์ในคลาสเดียวรันทีละตัว)
    /// การ seed คือพฤติกรรมตอนสตาร์ท: host เริ่มทำงานเมื่อมีคำขอแรก
    /// </summary>
    private TestApp Seeded() => _demo.App;

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString()!;

    /// <summary>เข้าใช้เป็นพนักงานผ่านปุ่ม demo — ไม่มีรหัสผ่านในคำขอ</summary>
    private static async Task<HttpClient> DemoLoginAsync(TestApp app, string role)
    {
        var client = app.CreateClient();
        var login = await client.PostAsJsonAsync("/api/demo/login", new { role });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return client;
    }

    private static async Task<string> RoleAsync(HttpClient client)
    {
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return (await JsonAsync(me)).GetProperty("role").GetString()!;
    }

    // ------------------------------------------------------------------ สวิตช์เดียว (spec 109)

    [Fact]
    public async Task Status_is_open_without_auth_and_says_the_demo_is_on()
    {
        var client = Seeded().CreateClient();

        var response = await client.GetAsync("/api/demo/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Status_says_the_demo_is_off_when_the_switch_is_off()
    {
        using var app = new TestApp(_postgres);

        var response = await app.CreateClient().GetAsync("/api/demo/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Every_demo_endpoint_answers_404_when_the_demo_is_off()
    {
        using var app = new TestApp(_postgres);
        var client = app.CreateClient();

        var status = await client.GetAsync("/api/demo/status");
        var login = await client.PostAsJsonAsync("/api/demo/login", new { role = "Owner" });
        var customer = await client.PostAsync("/api/demo/customer-session", null);

        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, customer.StatusCode);
    }

    [Fact]
    public async Task The_seeder_does_not_run_when_the_demo_is_off()
    {
        using var app = new TestApp(_postgres);
        _ = app.CreateClient();

        var counts = await app.WithDbAsync(async db => new
        {
            Tables = await db.Tables.CountAsync(),
            Sessions = await db.TableSessions.CountAsync(),
        });

        Assert.Equal(0, counts.Tables);
        Assert.Equal(0, counts.Sessions);
    }

    /// <summary>รูปเมนูของ demo ต้องเปิดได้จริงจาก URL ที่เก็บไว้ ไม่ใช่แค่มีพาธในฐานข้อมูล</summary>
    [Fact]
    public async Task Seeded_menu_photos_are_served_as_images()
    {
        var app = Seeded();
        var path = await app.WithDbAsync(db => db.MenuItems.Where(m => m.Name == "เนื้อวัวสไลซ์")
            .Select(m => m.ImagePath).SingleAsync());

        var response = await app.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
    }

    // ------------------------------------------------------------------ ปุ่มเข้าใช้ (spec 104)

    [Fact]
    public async Task Login_signs_the_owner_in_without_a_password()
    {
        var client = await DemoLoginAsync(Seeded(), "Owner");

        Assert.Equal("Owner", await RoleAsync(client));
    }

    [Fact]
    public async Task Login_signs_the_kitchen_in_without_a_password()
    {
        var client = await DemoLoginAsync(Seeded(), "Kitchen");

        Assert.Equal("Kitchen", await RoleAsync(client));
    }

    /// <summary>บัญชีที่ใช้คือบัญชีชุดเดียวกับตอนสตาร์ท (DemoSeed ใน appsettings) ไม่ใช่ชุดที่สอง</summary>
    [Fact]
    public async Task Demo_login_uses_the_same_accounts_as_the_startup_seed()
    {
        var app = Seeded();
        var client = await DemoLoginAsync(app, "Owner");

        var email = (await JsonAsync(await client.GetAsync("/api/auth/me"))).GetProperty("email").GetString();

        Assert.Equal(ConfigValue(app, "DemoSeed:OwnerEmail"), email);
    }

    [Fact]
    public async Task Login_refuses_a_role_that_is_not_staff()
    {
        var response = await Seeded().CreateClient()
            .PostAsJsonAsync("/api/demo/login", new { role = "Customer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationFailed, await ProblemCodeAsync(response));
    }

    // ------------------------------------------------------------------ มุมมองลูกค้า (spec 105)

    [Fact]
    public async Task Customer_session_opens_a_bot_round_that_the_customer_screen_can_use()
    {
        var app = Seeded();
        var owner = await DemoLoginAsync(app, "Owner"); // พนักงานล็อกอินอยู่ก็ขอ token ได้ (ไม่ต้อง auth)

        var opened = await owner.PostAsync("/api/demo/customer-session", null);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        var token = (await JsonAsync(opened)).GetProperty("token").GetString()!;

        try
        {
            // token ต้องใช้เปิดหน้าลูกค้าได้จริง (43 ตัวอักษรตามสัญญา) และเป็นรอบของ bot
            Assert.Equal(43, token.Length);
            var session = await app.CreateClient().GetAsync($"/api/s/{token}");
            Assert.Equal(HttpStatusCode.OK, session.StatusCode);
            var body = await JsonAsync(session);
            Assert.Equal("Gold", body.GetProperty("packageName").GetString());
            Assert.Equal(2, body.GetProperty("adults").GetInt32());
            Assert.Equal(0, body.GetProperty("children").GetInt32());

            var row = await app.WithDbAsync(db =>
                db.TableSessions.AsNoTracking().SingleAsync(s => s.Token == token));
            Assert.True(row.IsDemoBot);
            Assert.Equal(SessionStatus.Open, row.Status);
        }
        finally
        {
            // คืนสภาพ host กลาง: รอบที่เปิดไว้อ่านอย่างเดียว ยกเลิกได้เพราะยังไม่มีออเดอร์
            await VoidAsync(app, owner, token);
        }
    }

    [Fact]
    public async Task Customer_session_returns_the_round_that_is_already_open()
    {
        var app = Seeded();
        var owner = await DemoLoginAsync(app, "Owner");

        var first = await owner.PostAsync("/api/demo/customer-session", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var token = (await JsonAsync(first)).GetProperty("token").GetString()!;

        try
        {
            var second = await owner.PostAsync("/api/demo/customer-session", null);

            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            Assert.Equal(token, (await JsonAsync(second)).GetProperty("token").GetString());
        }
        finally
        {
            await VoidAsync(app, owner, token);
        }
    }

    [Fact]
    public async Task Customer_session_refuses_when_no_table_is_free()
    {
        var app = Seeded();
        var owner = await DemoLoginAsync(app, "Owner");

        // เติมทุกโต๊ะที่ยังว่างด้วยรอบของเจ้าของร้าน (ไม่ใช่ bot) แล้วค่อย ๆ คืนสภาพใน finally
        var free = await app.WithDbAsync(async db => await db.Tables.AsNoTracking()
            .Where(t => t.IsActive
                        && !db.TableSessions.Any(s => s.TableId == t.Id && s.Status == SessionStatus.Open))
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync());
        var packageId = await app.WithDbAsync(async db => await db.Packages.AsNoTracking()
            .OrderBy(p => p.Rank).Select(p => p.Id).FirstAsync());
        var opened = new List<int>();

        try
        {
            foreach (var tableId in free)
            {
                var response = await owner.PostAsJsonAsync("/api/sessions", new
                {
                    tableId,
                    packageId,
                    adultCount = 2,
                    childCount = 0,
                });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                opened.Add((await JsonAsync(response)).GetProperty("id").GetInt32());
            }

            var refused = await owner.PostAsync("/api/demo/customer-session", null);

            Assert.Equal(ErrorCodes.StatusFor(ErrorCodes.TableOccupied), (int)refused.StatusCode);
            Assert.Equal(ErrorCodes.TableOccupied, await ProblemCodeAsync(refused));
        }
        finally
        {
            foreach (var id in opened)
            {
                await owner.PostAsync($"/api/sessions/{id}/void", null);
            }
        }
    }

    /// <summary>ยกเลิกรอบที่เปิดไว้แล้วด้วยมือ (ทดสอบต้องคืน host กลางให้สะอาดเสมอ)</summary>
    private static async Task VoidAsync(TestApp app, HttpClient owner, string token)
    {
        var id = await app.WithDbAsync(db => db.TableSessions.AsNoTracking()
            .Where(s => s.Token == token).Select(s => s.Id).FirstAsync());
        var response = await owner.PostAsync($"/api/sessions/{id}/void", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------------ seed (spec 106, "Demo Mode")

    [Fact]
    public async Task Seeder_is_idempotent_and_matches_the_spec_shape()
    {
        var app = Seeded();

        var before = await app.WithDbAsync(CountsAsync);

        // รันงานตอนสตาร์ทซ้ำบนฐานข้อมูลเดิม — ต้องไม่สร้างอะไรเพิ่มเลย
        using (var scope = app.NewScope())
        {
            await new DemoSeeder().RunAsync(scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<IConfiguration>());
        }

        var after = await app.WithDbAsync(CountsAsync);
        Assert.Equal(before, after);

        // ---- แค็ตตาล็อกร้านตัวอย่าง
        Assert.Equal(12, after.Tables);
        Assert.Equal(50, after.Seats);
        Assert.Equal(6, after.BigTableSeats);
        Assert.Equal(3, after.Packages);
        Assert.Equal(8, after.Categories);
        Assert.Equal(44, after.MenuItems);
        Assert.Equal(8, after.AlaCarteItems);
        Assert.Equal(0, after.InactiveMenuItems);

        var shop = await app.WithDbAsync(async db => await db.Settings.AsNoTracking().SingleAsync());
        Assert.Equal("ชาบูเดโม่", shop.Name);
        Assert.Equal(10m, shop.ServiceChargePercent);
        Assert.Equal(7m, shop.VatPercent);
        Assert.False(string.IsNullOrWhiteSpace(shop.PromptPayId));

        // บัญชี demo สองบทบาทมีจริงและผูก role แล้ว (สร้างโดยงานตอนสตาร์ท ไม่ใช่ชุดที่สอง)
        Assert.Equal("Owner", await RoleAsync(await DemoLoginAsync(app, "Owner")));
        Assert.Equal("Kitchen", await RoleAsync(await DemoLoginAsync(app, "Kitchen")));

        // ---- ประวัติ 30 วัน (ไม่รวมวันนี้)
        var history = await app.WithDbAsync(HistoryAsync);
        var today = Bkk.DateOf(DateTimeOffset.UtcNow);

        Assert.Equal(0, history.OpenSessions);
        Assert.Equal(0, history.HumanSessions);
        Assert.Equal(0, history.NotBotBills);
        Assert.Equal(DemoSeeder.HistoryDays, history.ByDay.Count);
        Assert.DoesNotContain(today, history.ByDay.Keys);
        Assert.Equal(new[] { today.AddDays(-DemoSeeder.HistoryDays), today.AddDays(-1) },
            new[] { history.ByDay.Keys.Min(), history.ByDay.Keys.Max() });

        foreach (var (day, day1) in history.ByDay)
        {
            Assert.InRange(day1.Count, DemoSeeder.MinBillsPerDay, DemoSeeder.MaxBillsPerDay);
            foreach (var bill in day1)
            {
                var local = Bkk.ToLocal(bill.OpenedAt);
                Assert.Equal(day, DateOnly.FromDateTime(local.DateTime));
                var minute = local.Hour * 60 + local.Minute;
                Assert.InRange(minute, DemoSeeder.FirstOpenedMinute, DemoSeeder.LastOpenedMinute);
            }
        }

        Assert.Equal(history.Bills, history.ByDay.Values.Sum(day => day.Count));
        Assert.Equal(history.Bills, history.Receipts.Count);
        Assert.All(history.Receipts, receipt => Assert.Matches(ReceiptPattern, receipt));
        Assert.Equal(history.Receipts.Count, history.Receipts.Distinct().Count());

        // สัดส่วนวิธีจ่าย Cash 40 / PromptPay 45 / Card 15 (เผื่อคลาดเคลื่อน ±6 จุดจากจำนวนตัวอย่าง)
        var bills = (decimal)history.Bills;
        Assert.InRange(history.Cash / bills * 100m, 34m, 46m);
        Assert.InRange(history.PromptPay / bills * 100m, 39m, 51m);
        Assert.InRange(history.Card / bills * 100m, 9m, 21m);
        Assert.Equal(history.Bills, history.Cash + history.PromptPay + history.Card);

        // รายการปรับยอดราว 10% ของบิล
        Assert.InRange(history.BillsWithAdjustment / bills * 100m, 5m, 15m);
    }
    [Fact]
    public async Task Seeded_history_shows_up_in_the_30_day_report()
    {
        var app = Seeded();
        var history = await app.WithDbAsync(HistoryAsync);
        var today = Bkk.DateOf(DateTimeOffset.UtcNow);

        // เข้าผ่านปุ่ม demo แล้วอ่านรายงานเป็นเจ้าของร้าน
        var owner = await DemoLoginAsync(app, "Owner");
        var summary = await JsonAsync(await owner.GetAsync("/api/reports/summary?range=30d"));

        // หน้าต่าง 30 วัน = วันนี้ + 29 วันก่อน → ไม่รวมวันที่ 30 ย้อนหลังของ seed
        var inWindow = history.ByDay.Where(pair => pair.Key > today.AddDays(-30)).ToList();
        var expectedBills = inWindow.Sum(pair => pair.Value.Count);
        var expectedRevenue = inWindow.Sum(pair => pair.Value.Sum(b => b.Total));

        Assert.Equal(expectedBills, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(expectedRevenue, summary.GetProperty("revenue").GetDecimal());
        Assert.True(summary.GetProperty("revenue").GetDecimal() > 0m);
        Assert.True(summary.GetProperty("guestCount").GetInt32() > 0);

        var daily = summary.GetProperty("daily").EnumerateArray()
            .Select(d => (Date: d.GetProperty("date").GetString()!, Bills: d.GetProperty("bills").GetInt32()))
            .ToList();
        Assert.Equal(30, daily.Count);
        Assert.Equal(today.AddDays(-29).ToString("yyyy-MM-dd"), daily[0].Date);
        Assert.Equal(today.ToString("yyyy-MM-dd"), daily[^1].Date);
        Assert.Equal(0, daily[^1].Bills);
        Assert.Equal(expectedBills, daily.Sum(d => d.Bills));

        // แต่ละแถวของรายงานตรงกับวันที่ seed ไว้ (วันนี้ยังไม่มีบิล — บอทเป็นของ ticket 25)
        foreach (var row in daily)
        {
            var day = DateOnly.Parse(row.Date);
            Assert.Equal(history.ByDay.TryGetValue(day, out var seeded) ? seeded.Count : 0, row.Bills);
        }

        var mix = summary.GetProperty("paymentMix").EnumerateArray()
            .ToDictionary(m => m.GetProperty("method").GetString()!, m => m.GetProperty("count").GetInt32());
        Assert.Equal(history.CashInWindow, mix.GetValueOrDefault("Cash"));
        Assert.Equal(history.PromptPayInWindow, mix.GetValueOrDefault("PromptPay"));
        Assert.Equal(history.CardInWindow, mix.GetValueOrDefault("Card"));

        Assert.NotEmpty(summary.GetProperty("topItems").EnumerateArray());

        // วันนี้ยังไม่มีบิลของ seed — รายงานวันนี้จึงว่าง (ผลงานจากบอทเป็นของ ticket 25)
        var todayReport = await JsonAsync(await owner.GetAsync("/api/reports/summary?range=today"));
        Assert.Equal(0, todayReport.GetProperty("billCount").GetInt32());
        Assert.Equal(0m, todayReport.GetProperty("revenue").GetDecimal());
    }

    // ------------------------------------------------------------------ ตัวช่วยอ่านฐานข้อมูล

    private sealed record Counts(int Tables, int Seats, int BigTableSeats, int Packages, int Categories,
        int MenuItems, int AlaCarteItems, int InactiveMenuItems);

    private static async Task<Counts> CountsAsync(Data.AppDbContext db) => new(
        await db.Tables.CountAsync(),
        await db.Tables.SumAsync(t => t.Seats),
        await db.Tables.Where(t => t.Name == "B3").Select(t => t.Seats).SingleAsync(),
        await db.Packages.CountAsync(),
        await db.Categories.CountAsync(),
        await db.MenuItems.CountAsync(),
        await db.MenuItems.CountAsync(m => m.Kind == MenuItemKind.AlaCarte),
        await db.MenuItems.CountAsync(m => !m.IsActive));

    private sealed record Bill(DateOnly Day, DateTimeOffset OpenedAt, decimal Total, PaymentMethod Method);

    private sealed record History(
        Dictionary<DateOnly, List<Bill>> ByDay,
        int Bills,
        int Cash,
        int PromptPay,
        int Card,
        int CashInWindow,
        int PromptPayInWindow,
        int CardInWindow,
        decimal BillsWithAdjustment,
        List<string> Receipts,
        int OpenSessions,
        int HumanSessions,
        int NotBotBills);

    private static async Task<History> HistoryAsync(Data.AppDbContext db)
    {
        var rows = await db.Payments.AsNoTracking()
            .Where(p => p.TableSession!.IsDemoBot)
            .Select(p => new
            {
                p.Method,
                p.Total,
                p.PaidAt,
                p.ReceiptNo,
                p.TableSessionId,
                OpenedAt = p.TableSession!.OpenedAt,
            })
            .ToListAsync();

        var today = Bkk.DateOf(DateTimeOffset.UtcNow);
        var window = today.AddDays(-29);
        var bills = rows
            .Select(r => new Bill(Bkk.DateOf(r.PaidAt), r.OpenedAt, r.Total, r.Method))
            .ToList();

        var adjusted = await db.BillAdjustments.AsNoTracking()
            .Where(a => a.TableSession!.IsDemoBot)
            .Select(a => a.TableSessionId)
            .Distinct()
            .CountAsync();

        return new History(
            bills.GroupBy(b => b.Day).ToDictionary(g => g.Key, g => g.ToList()),
            bills.Count,
            bills.Count(b => b.Method == PaymentMethod.Cash),
            bills.Count(b => b.Method == PaymentMethod.PromptPay),
            bills.Count(b => b.Method == PaymentMethod.Card),
            bills.Count(b => b.Method == PaymentMethod.Cash && b.Day >= window),
            bills.Count(b => b.Method == PaymentMethod.PromptPay && b.Day >= window),
            bills.Count(b => b.Method == PaymentMethod.Card && b.Day >= window),
            adjusted,
            rows.Select(r => r.ReceiptNo).ToList(),
            await db.TableSessions.CountAsync(s => s.Status == SessionStatus.Open),
            await db.TableSessions.CountAsync(s => s.Payment != null && !s.IsDemoBot),
            await db.Payments.CountAsync(p => !p.TableSession!.IsDemoBot));
    }
}
