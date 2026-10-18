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
/// Ticket 08 — ภาพรวมผังโต๊ะผ่าน seam 1: แอปจริง + Postgres จริง
/// ยืนยันเฉพาะพฤติกรรมภายนอก (สถานะ HTTP, ข้อมูลที่คืน, สถานะแถวในฐานข้อมูล) และไม่ sleep เลย:
/// เวลาที่ขึ้นกับเวลาเซิร์ฟเวอร์ถูกพิสูจน์ด้วยการตั้ง <c>OpenedAt</c>/<c>EndsAt</c> ในข้อมูล
/// </summary>
[Collection(ApiCollection.Name)]
public class OverviewTests(PostgresFixture postgres)
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

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static async Task<int> CreateTableAsync(HttpClient client, string prefix, int seats = 4)
    {
        var response = await client.PostAsJsonAsync("/api/tables",
            new { name = UniqueName(prefix), seats, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>แพ็กเกจสร้างผ่านฐานข้อมูลตรง ๆ เหมือน ticket 06/07 เพราะ Rank ต้องไม่ซ้ำกับเทสต์อื่น</summary>
    private static async Task<int> SeedPackageAsync(TestApp app, decimal adult = 399m, decimal child = 199m,
        int minutes = 90) =>
        await app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r));
            var package = new Package
            {
                Name = $"P{Guid.NewGuid():N}"[..12],
                Rank = rank,
                AdultPrice = adult,
                ChildPrice = child,
                DurationMinutes = minutes,
                IsActive = true,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private static async Task<int> OpenSessionAsync(HttpClient client, int tableId, int packageId,
        int adults = 2, int children = 0)
    {
        var response = await client.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = adults, childCount = children });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>
    /// ยังไม่มีเส้นทางสั่งอาหารจนกว่า ticket 10 — เทสต์นี้จึง seed ออเดอร์ลงฐานข้อมูลตรง ๆ
    /// (เหมือน SessionTests) โดยให้เลขรอบไม่ซ้ำกันภายในรอบเดียวกัน
    /// </summary>
    private static async Task SeedOrdersAsync(TestApp app, int sessionId, params OrderStatus[] statuses) =>
        await app.WithDbAsync(async db =>
        {
            var created = DateTimeOffset.UtcNow;
            // Unique index คือ (SessionId, Number): เลขรอบต้องต่อจากของเดิมในรอบนั้น
            var number = await db.Orders.Where(o => o.TableSessionId == sessionId)
                .Select(o => (int?)o.Number).MaxAsync() ?? 0;
            foreach (var status in statuses)
            {
                db.Orders.Add(new Order
                {
                    TableSessionId = sessionId,
                    Number = ++number,
                    Status = status,
                    CancelReason = status == OrderStatus.Cancelled ? "ลูกค้าแจ้งเปลี่ยน" : null,
                    CreatedAt = created,
                    UpdatedAt = created,
                });
            }

            await db.SaveChangesAsync();
            return 0;
        });

    /// <summary>เลื่อนเวลาของรอบไปในอดีต/อนาคตด้วยข้อมูล ไม่ใช่ด้วยการรอ</summary>
    private static Task<DateTimeOffset> MoveSessionClockAsync(TestApp app, int sessionId,
        TimeSpan openedAgo, TimeSpan duration) =>
        app.WithDbAsync(async db =>
        {
            var session = await db.TableSessions.SingleAsync(s => s.Id == sessionId);
            session.OpenedAt = DateTimeOffset.UtcNow - openedAgo;
            session.EndsAt = session.OpenedAt + duration;
            await db.SaveChangesAsync();
            return session.OpenedAt;
        });

    private static async Task<JsonElement> OverviewAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/tables/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await JsonAsync(response);
    }

    private static JsonElement RowFor(JsonElement overview, int tableId) =>
        overview.EnumerateArray().Single(row => row.GetProperty("id").GetInt32() == tableId);

    [Fact]
    public async Task A_free_table_reports_no_session_fields_at_all()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var free = await CreateTableAsync(client, "F");
        var disabled = await CreateTableAsync(client, "G");
        var disable = await client.PutAsJsonAsync($"/api/tables/{disabled}",
            new { name = UniqueName("G"), seats = 4, isActive = false });
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        var overview = await OverviewAsync(client);
        var row = RowFor(overview, free);

        Assert.Equal("A", row.GetProperty("zone").GetString());
        Assert.Equal(4, row.GetProperty("seats").GetInt32());
        Assert.True(row.GetProperty("isActive").GetBoolean());
        // โต๊ะว่าง: ไม่มีฟิลด์ของ session เลย (null ถูกตัดออกจาก JSON ของ Program.cs ทั้งหมด)
        foreach (var field in new[]
                 {
                     "sessionId", "packageName", "adults", "children", "openedAt", "endsAt", "minutesUsed",
                     "ordersWaiting", "runningTotal",
                 })
        {
            Assert.False(row.TryGetProperty(field, out _), $"โต๊ะว่างไม่ควรมีฟิลด์ {field}");
        }

        // โต๊ะที่ปิดใช้งานไม่อยู่ในผัง (เปิดรอบไม่ได้อยู่แล้ว)
        Assert.DoesNotContain(overview.EnumerateArray(), r => r.GetProperty("id").GetInt32() == disabled);
    }

    [Fact]
    public async Task An_occupied_table_reports_its_package_guests_times_waiting_orders_and_running_total()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "H");
        var packageId = await SeedPackageAsync(app, adult: 399m, child: 199m, minutes: 90);
        var sessionId = await OpenSessionAsync(client, tableId, packageId, adults: 2, children: 1);
        await SeedOrdersAsync(app, sessionId, OrderStatus.Pending, OrderStatus.Preparing, OrderStatus.Ready,
            OrderStatus.Cancelled);
        var row = await app.WithDbAsync(async db =>
            await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

        var card = RowFor(await OverviewAsync(client), tableId);

        Assert.Equal(sessionId, card.GetProperty("sessionId").GetInt32());
        Assert.Equal(row.PackageNameSnapshot, card.GetProperty("packageName").GetString());
        Assert.Equal(2, card.GetProperty("adults").GetInt32());
        Assert.Equal(1, card.GetProperty("children").GetInt32());
        Assert.Equal(row.OpenedAt, card.GetProperty("openedAt").GetDateTimeOffset());
        Assert.Equal(row.EndsAt, card.GetProperty("endsAt").GetDateTimeOffset());
        // Pending + Preparing = 2 รอบที่ครัวยังทำไม่จบ; Ready จบแล้ว และ Cancelled ไม่ถูกคิดเงิน
        Assert.Equal(2, card.GetProperty("ordersWaiting").GetInt32());
        // ยอดสะสมต้องเป็นยอดเดียวกับบิลของรอบนั้น (seam เดียวกับหน้า session/การชำระเงิน)
        var detail = await JsonAsync(await client.GetAsync($"/api/sessions/{sessionId}"));
        Assert.Equal(997m, detail.GetProperty("bill").GetProperty("buffetTotal").GetDecimal());
        Assert.Equal(detail.GetProperty("bill").GetProperty("total").GetDecimal(),
            card.GetProperty("runningTotal").GetDecimal());
    }

    [Fact]
    public async Task Minutes_used_comes_from_the_server_clock_and_the_stored_open_time()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "I");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);
        await MoveSessionClockAsync(app, sessionId, openedAgo: TimeSpan.FromMinutes(47),
            duration: TimeSpan.FromMinutes(90));

        var card = RowFor(await OverviewAsync(client), tableId);

        // 47 นาที ± 1 (คำขอและเวลาที่ตั้งในข้อมูลเกิดห่างกันไม่ถึงวินาที) — ไม่มีการ sleep
        Assert.InRange(card.GetProperty("minutesUsed").GetInt32(), 46, 48);
        // ไคลเอนต์ไม่ต้องคำนวณเอง: เซิร์ฟเวอร์ส่งทั้งช่วงเวลาและนาทีที่ใช้ไปมาให้ครบ
        Assert.Equal(47d,
            (DateTimeOffset.UtcNow - card.GetProperty("openedAt").GetDateTimeOffset()).TotalMinutes, 1);
    }

    [Fact]
    public async Task Orders_waiting_counts_only_orders_the_kitchen_has_not_finished()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "J");
        var packageId = await SeedPackageAsync(app);
        var sessionId = await OpenSessionAsync(client, tableId, packageId);

        var beforeOrders = RowFor(await OverviewAsync(client), tableId);
        Assert.Equal(0, beforeOrders.GetProperty("ordersWaiting").GetInt32());

        await SeedOrdersAsync(app, sessionId, OrderStatus.Pending, OrderStatus.Cancelled);
        var afterCancel = RowFor(await OverviewAsync(client), tableId);
        // ยกเลิกแล้วไม่ใช่ "รอครัว" และไม่ถูกคิดเงิน
        Assert.Equal(1, afterCancel.GetProperty("ordersWaiting").GetInt32());

        await SeedOrdersAsync(app, sessionId, OrderStatus.Ready);
        var afterReady = RowFor(await OverviewAsync(client), tableId);
        // Ready เป็นปลายทางของครัวแล้ว จึงไม่นับเป็นออเดอร์ค้าง (ยังอยู่บนการ์ดครัวจนพนักงานนำไปเสิร์ฟ)
        Assert.Equal(1, afterReady.GetProperty("ordersWaiting").GetInt32());
    }

    [Fact]
    public async Task A_session_past_its_end_time_is_still_on_the_floor_with_its_full_card()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(client, "K");
        var packageId = await SeedPackageAsync(app, minutes: 90);
        var sessionId = await OpenSessionAsync(client, tableId, packageId, adults: 2, children: 1);
        // ผ่านไปแล้ว 107 นาทีของรอบ 90 นาที: การสั่งใหม่ปิดแล้ว แต่บิลยังต้องอยู่ให้ปรับยอดและชำระได้
        await MoveSessionClockAsync(app, sessionId, openedAgo: TimeSpan.FromMinutes(107),
            duration: TimeSpan.FromMinutes(90));
        await SeedOrdersAsync(app, sessionId, OrderStatus.Pending);

        var card = RowFor(await OverviewAsync(client), tableId);

        Assert.Equal(sessionId, card.GetProperty("sessionId").GetInt32());
        Assert.InRange(card.GetProperty("minutesUsed").GetInt32(), 106, 108);
        Assert.Equal(1, card.GetProperty("ordersWaiting").GetInt32());
        var detail = await JsonAsync(await client.GetAsync($"/api/sessions/{sessionId}"));
        Assert.Equal(detail.GetProperty("bill").GetProperty("total").GetDecimal(),
            card.GetProperty("runningTotal").GetDecimal());
        Assert.True(card.GetProperty("endsAt").GetDateTimeOffset() < DateTimeOffset.UtcNow);
        // ไม่มีเส้นทางต่อเวลา — ครัว/เจ้าของเห็นเวลาสิ้นสุดเดิมเสมอ
        Assert.Equal(90d,
            (card.GetProperty("endsAt").GetDateTimeOffset() - card.GetProperty("openedAt").GetDateTimeOffset())
            .TotalMinutes, 1);
    }

    [Fact]
    public async Task The_running_total_follows_the_same_seam_so_a_merged_bill_shows_on_the_host_table()
    {
        using var app = new TestApp(postgres);
        var client = await OwnerClientAsync(app);
        var hostTable = await CreateTableAsync(client, "L");
        var mergedTable = await CreateTableAsync(client, "M");
        var packageId = await SeedPackageAsync(app, adult: 399m, child: 199m);
        var hostSession = await OpenSessionAsync(client, hostTable, packageId, adults: 2, children: 0);
        var mergedSession = await OpenSessionAsync(client, mergedTable, packageId, adults: 1, children: 1);

        await app.WithDbAsync(async db =>
        {
            var merged = await db.TableSessions.SingleAsync(s => s.Id == mergedSession);
            merged.MergedIntoSessionId = hostSession;
            await db.SaveChangesAsync();
            return 0;
        });

        var overview = await OverviewAsync(client);
        // โต๊ะหลักแสดงยอดรวมของทั้งสองรอบผ่าน Billing/BillCalculator — เท่ากับยอดที่หน้า session ของ
        // โต๊ะหลักคำนวณ (2 × 399 + 1 × 399 + 1 × 199 = 1,396 ก่อน SC/VAT ของร้าน)
        var hostDetail = await JsonAsync(await client.GetAsync($"/api/sessions/{hostSession}"));
        Assert.Equal(1396m, hostDetail.GetProperty("bill").GetProperty("buffetTotal").GetDecimal());
        Assert.Equal(hostDetail.GetProperty("bill").GetProperty("total").GetDecimal(),
            RowFor(overview, hostTable).GetProperty("runningTotal").GetDecimal());
        // โต๊ะที่ถูกรวมยังมีรอบของตัวเองอยู่ (ยังสั่งผ่าน QR เดิมได้จนกว่าจะชำระ) แต่ยอดสะสมของมันคือยอดของตัวเอง
        var mergedDetail = await JsonAsync(await client.GetAsync($"/api/sessions/{mergedSession}"));
        Assert.Equal(mergedDetail.GetProperty("bill").GetProperty("total").GetDecimal(),
            RowFor(overview, mergedTable).GetProperty("runningTotal").GetDecimal());
        Assert.Equal(mergedSession, RowFor(overview, mergedTable).GetProperty("sessionId").GetInt32());
    }

    [Fact]
    public async Task The_kitchen_may_read_the_overview_and_anonymous_callers_get_401()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var tableId = await CreateTableAsync(owner, "N");
        var packageId = await SeedPackageAsync(app);
        await OpenSessionAsync(owner, tableId, packageId);

        var kitchen = await KitchenClientAsync(app);
        var kitchenResponse = await kitchen.GetAsync("/api/tables/overview");

        Assert.Equal(HttpStatusCode.OK, kitchenResponse.StatusCode);
        var card = RowFor(await JsonAsync(kitchenResponse), tableId);
        Assert.Equal(2, card.GetProperty("adults").GetInt32());

        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/tables/overview")).StatusCode);
    }
}
