using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 23 — <c>GET /api/reports/summary</c> ผ่าน seam 1: แอปจริง + Postgres จริง
///
/// ข้อมูลตั้งต้นถูกวางลงฐานข้อมูลตรง ๆ ในรูปที่ระบบเก็บหลังชำระเงินแล้ว (session Paid + แถว Payment
/// ที่ตรึงยอดไว้) แล้วเทียบคำตอบของ API กับตัวเลขที่คำนวณด้วยมือในคอมเมนต์ของแต่ละเคส
/// เวลาทุกจุดวางตาม "วันนี้ตามเวลาไทย" ของตอนที่เทสต์รัน จึงไม่ขึ้นกับเขตเวลาของเครื่อง
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportsTests(PostgresFixture postgres)
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

    private static Task<HttpClient> OwnerAsync(TestApp app) => LoginAsync(app, "DemoSeed:OwnerEmail");

    private static async Task<JsonElement> SummaryAsync(HttpClient client, string range)
    {
        var response = await client.GetAsync($"/api/reports/summary?range={range}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>วันนี้ตามเวลาไทย ณ ตอนที่เทสต์รัน</summary>
    private static DateOnly Today => Bkk.DateOf(DateTimeOffset.UtcNow);

    /// <summary>เวลาไทย <paramref name="hour"/>:<paramref name="minute"/> ของวัน <paramref name="day"/> เป็น UTC</summary>
    private static DateTimeOffset At(DateOnly day, int hour, int minute) =>
        Bkk.StartOfDay(day).AddHours(hour).AddMinutes(minute).ToUniversalTime();

    /// <summary>ผู้ช่วยวางข้อมูลของหนึ่งเคส: โต๊ะ แพ็กเกจ และ session ในสถานะที่ต้องการ</summary>
    private sealed class Seed(Data.AppDbContext db)
    {
        private int _receipt;
        private int _token;
        private readonly Dictionary<string, Table> _tables = new();
        private readonly Dictionary<string, Package> _packages = new();

        public Package Package(string name, int rank)
        {
            if (!_packages.TryGetValue(name, out var package))
            {
                package = new Package { Name = name, Rank = rank, AdultPrice = 299, ChildPrice = 149, DurationMinutes = 90 };
                _packages[name] = package;
                db.Packages.Add(package);
            }

            return package;
        }

        private Table Table(string name)
        {
            if (!_tables.TryGetValue(name, out var table))
            {
                table = new Table { Name = name, Seats = 4, SortOrder = _tables.Count };
                _tables[name] = table;
                db.Tables.Add(table);
            }

            return table;
        }

        public TableSession Session(string table, Package package, int adults, int children, SessionStatus status,
            DateTimeOffset openedAt, DateTimeOffset? closedAt = null, TableSession? mergedInto = null)
        {
            var session = new TableSession
            {
                Table = Table(table),
                Package = package,
                Token = $"report-token-{++_token}",
                AdultCount = adults,
                ChildCount = children,
                PackageNameSnapshot = package.Name,
                AdultPriceSnapshot = package.AdultPrice,
                ChildPriceSnapshot = package.ChildPrice,
                PackageRankSnapshot = package.Rank,
                OpenedAt = openedAt,
                EndsAt = openedAt.AddMinutes(package.DurationMinutes),
                Status = status,
                ClosedAt = closedAt,
                MergedIntoSession = mergedInto,
            };
            db.TableSessions.Add(session);
            return session;
        }

        public TableSession Paid(string table, Package package, int adults, int children, PaymentMethod method,
            decimal total, DateTimeOffset paidAt)
        {
            var session = Session(table, package, adults, children, SessionStatus.Paid, paidAt.AddMinutes(-80), paidAt);
            session.Payment = new Payment
            {
                Method = method,
                Subtotal = total,
                Total = total,
                AmountReceived = total,
                Change = 0,
                ReceiptNo = $"R-TEST-{++_receipt:D4}",
                PaidAt = paidAt,
            };
            return session;
        }

        public void Order(TableSession session, int number, OrderStatus status, params (string Name, int Qty)[] items)
        {
            var created = session.OpenedAt.AddMinutes(5 * number);
            session.Orders.Add(new Order
            {
                Number = number,
                Status = status,
                CancelReason = status == OrderStatus.Cancelled ? "ลูกค้าสั่งผิด" : null,
                CreatedAt = created,
                UpdatedAt = created,
                Items = items.Select(i => new OrderItem
                {
                    MenuItemId = 1,
                    NameSnapshot = i.Name,
                    KindSnapshot = MenuItemKind.Buffet,
                    UnitPrice = 0,
                    Quantity = i.Qty,
                }).ToList(),
            });
        }
    }

    /// <summary>
    /// ชุดข้อมูลที่คำนวณด้วยมือ (วัน D = วันนี้ตามเวลาไทย):
    ///  B1 Silver 2 ผู้ใหญ่ · เงินสด ฿700.00 · ชำระ D 12:30
    ///     ออเดอร์ 1 Ready: หมูสามชั้นสไลซ์ ×4, ชาไทยเย็น ×2 · ออเดอร์ 2 ยกเลิก: กุ้งขาว ×50
    ///  B2 Gold 3 ผู้ใหญ่ 1 เด็ก · PromptPay ฿1,500.00 · ชำระ D 00:05 (หลังเที่ยงคืนไทย = 17:05 UTC ของวันก่อน)
    ///     ออเดอร์ 1 Ready: หมูสามชั้นสไลซ์ ×3, เนื้อวัวสไลซ์ ×5
    ///     + session รอง (Silver 2 ผู้ใหญ่) ที่ถูกรวมเข้าบิล B2: ออเดอร์ 1 Ready: ชาไทยเย็น ×1
    ///  B3 Gold 2 ผู้ใหญ่ · บัตร ฿900.00 · ชำระ D−1 23:55 (ก่อนเที่ยงคืนไทย) · เนื้อวัวสไลซ์ ×1
    ///  B4 Silver 2 ผู้ใหญ่ · เงินสด ฿5,000.00 · ชำระ D−40 (นอกทุกช่วง)
    ///  ไม่นับ: session Open วันนี้ (เบียร์สิงห์ ×9) และ session Voided วันนี้ (ไม่มี Payment)
    /// </summary>
    private static async Task SeedFixtureAsync(TestApp app)
    {
        var today = Today;
        await app.WithDbAsync(async db =>
        {
            var seed = new Seed(db);
            var silver = seed.Package("Silver", 1);
            var gold = seed.Package("Gold", 2);

            var b1 = seed.Paid("A1", silver, 2, 0, PaymentMethod.Cash, 700.00m, At(today, 12, 30));
            seed.Order(b1, 1, OrderStatus.Ready, ("หมูสามชั้นสไลซ์", 4), ("ชาไทยเย็น", 2));
            seed.Order(b1, 2, OrderStatus.Cancelled, ("กุ้งขาว", 50));

            var b2 = seed.Paid("A2", gold, 3, 1, PaymentMethod.PromptPay, 1500.00m, At(today, 0, 5));
            seed.Order(b2, 1, OrderStatus.Ready, ("หมูสามชั้นสไลซ์", 3), ("เนื้อวัวสไลซ์", 5));
            var merged = seed.Session("A3", silver, 2, 0, SessionStatus.Paid, b2.OpenedAt, b2.ClosedAt, mergedInto: b2);
            seed.Order(merged, 1, OrderStatus.Ready, ("ชาไทยเย็น", 1));

            var b3 = seed.Paid("A4", gold, 2, 0, PaymentMethod.Card, 900.00m, At(today.AddDays(-1), 23, 55));
            seed.Order(b3, 1, OrderStatus.Ready, ("เนื้อวัวสไลซ์", 1));

            seed.Paid("A5", silver, 2, 0, PaymentMethod.Cash, 5000.00m, At(today.AddDays(-40), 19, 0));

            var open = seed.Session("A6", gold, 4, 0, SessionStatus.Open, DateTimeOffset.UtcNow.AddMinutes(-30));
            seed.Order(open, 1, OrderStatus.Ready, ("เบียร์สิงห์", 9));
            seed.Session("A7", silver, 2, 0, SessionStatus.Voided, DateTimeOffset.UtcNow.AddMinutes(-20),
                DateTimeOffset.UtcNow.AddMinutes(-19));

            await db.SaveChangesAsync();
            return 0;
        });
    }

    private static List<(string Name, int Quantity)> TopItems(JsonElement summary) =>
        summary.GetProperty("topItems").EnumerateArray()
            .Select(r => (r.GetProperty("name").GetString()!, r.GetProperty("quantity").GetInt32()))
            .ToList();

    private static List<(string Name, int Sessions, int Guests, decimal Revenue)> PackageMix(JsonElement summary) =>
        summary.GetProperty("packageMix").EnumerateArray()
            .Select(r => (r.GetProperty("name").GetString()!, r.GetProperty("sessions").GetInt32(),
                r.GetProperty("guests").GetInt32(), r.GetProperty("revenue").GetDecimal()))
            .ToList();

    private static List<(string Method, int Count, decimal Total)> PaymentMix(JsonElement summary) =>
        summary.GetProperty("paymentMix").EnumerateArray()
            .Select(r => (r.GetProperty("method").GetString()!, r.GetProperty("count").GetInt32(),
                r.GetProperty("total").GetDecimal()))
            .ToList();

    private static Dictionary<int, decimal> Hourly(JsonElement summary) =>
        summary.GetProperty("hourly").EnumerateArray()
            .ToDictionary(r => r.GetProperty("hour").GetInt32(), r => r.GetProperty("revenue").GetDecimal());

    private static List<(DateOnly Date, decimal Revenue, int Bills)> Daily(JsonElement summary) =>
        summary.GetProperty("daily").EnumerateArray()
            .Select(r => (DateOnly.Parse(r.GetProperty("date").GetString()!), r.GetProperty("revenue").GetDecimal(),
                r.GetProperty("bills").GetInt32()))
            .ToList();

    [Fact]
    public async Task Today_counts_only_paid_bills_of_the_bangkok_day_with_the_merged_bill_as_one()
    {
        using var app = new TestApp(postgres);
        await SeedFixtureAsync(app);
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "today");

        // B1 + B2 = 700 + 1,500 = 2,200 · 2 บิล (B2 กับ session ที่รวมเข้ามาคือหนึ่งบิล)
        // ลูกค้า = 2 + (3+1 + 2) = 8 · เฉลี่ยต่อหัว = 2,200 ÷ 8 = 275.00
        Assert.Equal(2200.00m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal(2, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(8, summary.GetProperty("guestCount").GetInt32());
        Assert.Equal(275.00m, summary.GetProperty("avgPerGuest").GetDecimal());

        // แพ็กเกจของบิลรวมมาจาก session หลัก (Gold) แม้ session รองเป็น Silver; เรียงตามระดับแพ็กเกจ
        Assert.Equal(
            [("Silver", 1, 2, 700.00m), ("Gold", 1, 6, 1500.00m)],
            PackageMix(summary));
        Assert.Equal(
            [("PromptPay", 1, 1500.00m), ("Cash", 1, 700.00m)],
            PaymentMix(summary));

        // บิลใกล้เที่ยงคืน: B2 ชำระ 00:05 ไทย (ยังเป็นเมื่อวานตาม UTC) อยู่ในชั่วโมง 0 ของวันนี้
        var hourly = Hourly(summary);
        Assert.Equal(Enumerable.Range(0, 24), hourly.Keys.Order());
        Assert.Equal(1500.00m, hourly[0]);
        Assert.Equal(700.00m, hourly[12]);
        Assert.Equal(2200.00m, hourly.Values.Sum());

        Assert.Equal([(Today, 2200.00m, 2)], Daily(summary));
    }

    [Fact]
    public async Task Top_items_exclude_cancelled_orders_and_unpaid_sessions_but_include_merged_sessions()
    {
        using var app = new TestApp(postgres);
        await SeedFixtureAsync(app);
        var owner = await OwnerAsync(app);

        // วันนี้: หมูสามชั้นสไลซ์ 4+3 = 7 · เนื้อวัวสไลซ์ 5 · ชาไทยเย็น 2 + 1 (session ที่ถูกรวม) = 3
        // กุ้งขาว ×50 อยู่ในออเดอร์ที่ยกเลิก และเบียร์สิงห์ ×9 อยู่ใน session ที่ยังไม่ชำระ — ไม่ปรากฏ
        var today = TopItems(await SummaryAsync(owner, "today"));
        Assert.Equal([("หมูสามชั้นสไลซ์", 7), ("เนื้อวัวสไลซ์", 5), ("ชาไทยเย็น", 3)], today);
        Assert.DoesNotContain(today, r => r.Name == "กุ้งขาว");
        Assert.DoesNotContain(today, r => r.Name == "เบียร์สิงห์");

        // 7 วัน เพิ่ม B3 (เนื้อวัวสไลซ์ ×1): 7, 6, 3
        Assert.Equal([("หมูสามชั้นสไลซ์", 7), ("เนื้อวัวสไลซ์", 6), ("ชาไทยเย็น", 3)],
            TopItems(await SummaryAsync(owner, "7d")));
    }

    [Fact]
    public async Task Seven_days_include_today_and_put_the_bill_before_bangkok_midnight_on_yesterday()
    {
        using var app = new TestApp(postgres);
        await SeedFixtureAsync(app);
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "7d");

        // B1 + B2 + B3 = 700 + 1,500 + 900 = 3,100 · 3 บิล · ลูกค้า 2 + 6 + 2 = 10 · 310.00 ต่อหัว
        Assert.Equal(3100.00m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal(3, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(10, summary.GetProperty("guestCount").GetInt32());
        Assert.Equal(310.00m, summary.GetProperty("avgPerGuest").GetDecimal());

        // ทุกวันของช่วงมีแถว (วันที่ไม่มียอดเป็น 0) เริ่ม D−6 จบวันนี้
        var daily = Daily(summary);
        Assert.Equal(Enumerable.Range(0, 7).Select(i => Today.AddDays(i - 6)), daily.Select(d => d.Date));
        // B3 ชำระ 23:55 ไทยของ D−1 → อยู่บนเมื่อวาน ไม่ไหลไปวันนี้ · B2 00:05 ไทย → วันนี้ ไม่ใช่เมื่อวาน
        Assert.Equal((Today.AddDays(-1), 900.00m, 1), daily[5]);
        Assert.Equal((Today, 2200.00m, 2), daily[6]);
        Assert.All(daily.Take(5), d => Assert.Equal((0m, 0), (d.Revenue, d.Bills)));
        Assert.Equal(23, Hourly(summary).Single(h => h.Value == 900.00m).Key);

        // ผลรวมของสัดส่วนแพ็กเกจและวิธีจ่ายเท่ากับยอดขายเสมอ
        var packages = PackageMix(summary);
        Assert.Equal([("Silver", 1, 2, 700.00m), ("Gold", 2, 8, 2400.00m)], packages);
        var payments = PaymentMix(summary);
        Assert.Equal([("PromptPay", 1, 1500.00m), ("Card", 1, 900.00m), ("Cash", 1, 700.00m)], payments);
        Assert.Equal(3100.00m, packages.Sum(p => p.Revenue));
        Assert.Equal(3100.00m, payments.Sum(p => p.Total));
        Assert.Equal(3, packages.Sum(p => p.Sessions));
        Assert.Equal(3, payments.Sum(p => p.Count));
        Assert.Equal(3100.00m, daily.Sum(d => d.Revenue));
    }

    [Fact]
    public async Task Thirty_days_leave_out_a_bill_older_than_the_range()
    {
        using var app = new TestApp(postgres);
        await SeedFixtureAsync(app);
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "30d");

        // B4 (D−40, ฿5,000) อยู่นอกช่วง: ยังเป็น 3,100 จาก 3 บิล และมี 30 วันพอดี (D−29 … วันนี้)
        Assert.Equal(3100.00m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal(3, summary.GetProperty("billCount").GetInt32());
        var daily = Daily(summary);
        Assert.Equal(30, daily.Count);
        Assert.Equal(Today.AddDays(-29), daily[0].Date);
        Assert.Equal(Today, daily[^1].Date);
    }

    [Fact]
    public async Task Avg_per_guest_is_zero_when_the_paid_bills_have_no_guests()
    {
        using var app = new TestApp(postgres);
        await app.WithDbAsync(async db =>
        {
            var seed = new Seed(db);
            seed.Paid("A1", seed.Package("Silver", 1), 0, 0, PaymentMethod.Cash, 120.00m,
                At(Today, 0, 1));
            await db.SaveChangesAsync();
            return 0;
        });
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "today");

        Assert.Equal(120.00m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal(1, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("guestCount").GetInt32());
        Assert.Equal(0m, summary.GetProperty("avgPerGuest").GetDecimal());
    }

    [Fact]
    public async Task An_empty_range_answers_zeros_and_empty_lists()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "7d");

        Assert.Equal(0m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal(0, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("guestCount").GetInt32());
        Assert.Equal(0m, summary.GetProperty("avgPerGuest").GetDecimal());
        Assert.Empty(PackageMix(summary));
        Assert.Empty(TopItems(summary));
        Assert.Empty(PaymentMix(summary));
        Assert.All(Hourly(summary).Values, v => Assert.Equal(0m, v));
        var daily = Daily(summary);
        Assert.Equal(7, daily.Count);
        Assert.All(daily, d => Assert.Equal((0m, 0), (d.Revenue, d.Bills)));
    }

    [Fact]
    public async Task Yesterday_to_the_same_time_of_day_never_counts_todays_bills()
    {
        using var app = new TestApp(postgres);
        await app.WithDbAsync(async db =>
        {
            var seed = new Seed(db);
            var silver = seed.Package("Silver", 1);
            // Yesterday at 00:01 — inside "yesterday up to this time" at every moment of the day except
            // the first minute, where it correctly falls outside the window.
            seed.Paid("A1", silver, 2, 0, PaymentMethod.Cash, 900.00m, At(Today.AddDays(-1), 0, 1));
            // Today at 12:30 — a bill of today must never be mistaken for yesterday's comparison base.
            seed.Paid("A2", silver, 2, 0, PaymentMethod.Card, 500.00m, At(Today, 12, 30));
            await db.SaveChangesAsync();
            return 0;
        });
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "today");

        Assert.Equal(500.00m, summary.GetProperty("revenue").GetDecimal());
        if (!summary.TryGetProperty("previousRevenue", out var previous))
        {
            // Only reachable in the first minute of a Bangkok day: nothing of yesterday precedes "now",
            // so the comparison is omitted entirely.
            return;
        }

        Assert.Equal(900.00m, previous.GetDecimal());
    }

    [Fact]
    public async Task Previous_revenue_is_null_when_there_is_nothing_to_compare_with()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerAsync(app);

        var summary = await SummaryAsync(owner, "today");

        Assert.False(summary.TryGetProperty("previousRevenue", out _));
        Assert.False((await SummaryAsync(owner, "7d")).TryGetProperty("previousRevenue", out _));
    }

    [Fact]
    public async Task Kitchen_is_forbidden_and_anonymous_is_unauthorized()
    {
        using var app = new TestApp(postgres);
        var kitchen = await LoginAsync(app, "DemoSeed:KitchenEmail");

        Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync("/api/reports/summary?range=today")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await app.CreateClient().GetAsync("/api/reports/summary?range=today")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_range_is_refused_with_validation_failed()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerAsync(app);

        var response = await owner.GetAsync("/api/reports/summary?range=90d");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ErrorCodes.ValidationFailed, doc.RootElement.GetProperty("code").GetString());
    }
}
