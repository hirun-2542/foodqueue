using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 18 — รับชำระเงินสด/บัตร ผ่าน seam 1 (แอปจริง + Postgres จริง + hub จริง)
/// <c>POST /api/sessions/{id}/payment {method, amountReceived}</c> คืนรายละเอียด session ที่ปิดแล้วพร้อม <c>payment</c>
///
/// กฎตามลำดับของ spec: session ต้อง Open (<c>ALREADY_PAID</c>) → ไม่มีออเดอร์ Pending/Preparing
/// (<c>ORDERS_IN_PROGRESS</c>, Ready ไม่ขวาง) → เงินสดต้อง ≥ total (<c>INSUFFICIENT_AMOUNT</c>)
/// ทุกข้อพิสูจน์จากสิ่งที่ผู้ใช้เห็น: สถานะ HTTP + code + ข้อมูลที่ตอบ + event ที่ไคลเอนต์ได้รับ + แถวในฐานข้อมูล
/// </summary>
[Collection(ApiCollection.Name)]
public partial class PaymentTests(PostgresFixture postgres)
{
    [GeneratedRegex(@"^R(\d{8})-(\d{6})$")]
    private static partial Regex ReceiptFormat();

    // ------------------------------------------------------------------ helpers

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

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"คาดว่า 200 แต่ได้ {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await JsonAsync(response);
    }

    /// <summary>ปฏิเสธด้วยสถานะ HTTP และ <c>code</c> ตามสัญญา พร้อมข้อความไทยที่แสดงต่อผู้ใช้ได้ตรง ๆ</summary>
    private static async Task<JsonElement> AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status,
        string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
        return body;
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    /// <summary>SC 10% VAT 7% ตาม DATA และลิมิตต่อรอบตามค่าเริ่มต้น</summary>
    private static Task SetRatesAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            await db.Settings.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, "ชาบูเดโม่")
                .SetProperty(x => x.ServiceChargePercent, 10m)
                .SetProperty(x => x.VatPercent, 7m)
                .SetProperty(x => x.MaxQtyPerItemPerOrder, 5)
                .SetProperty(x => x.MaxItemsPerOrder, 20));
            return 0;
        });

    private static Task<Package> SeedPackageAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var package = new Package
            {
                Name = "Gold",
                Rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r)),
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package;
        });

    private sealed record OpenedSession(int Id, string Token, string TableName);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int packageId,
        int adults = 2, int children = 1)
    {
        var name = UniqueName("B");
        var table = await owner.PostAsJsonAsync("/api/tables", new { name, seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = adults, childCount = children });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, name);
    }

    private sealed record Menu(int MeatId, int BeerId);

    /// <summary>เมนูผ่านเส้นทางจริง: หมูสามชั้น (บุฟเฟ่ต์) และเบียร์สิงห์ ฿89 (คิดเงินแยก)</summary>
    private static async Task<Menu> CreateMenuAsync(HttpClient owner)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        async Task<int> ItemAsync(object body)
        {
            var response = await owner.PostAsJsonAsync("/api/menu-items", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("id").GetInt32();
        }

        return new Menu(
            await ItemAsync(new { name = UniqueName("หมู"), categoryId, kind = "Buffet", minPackageRank = (int?)1, price = 0m, isAvailable = true }),
            await ItemAsync(new { name = UniqueName("เบียร์"), categoryId, kind = "AlaCarte", minPackageRank = (int?)null, price = 89m, isAvailable = true }));
    }

    private static Task<HttpResponseMessage> SubmitRawAsync(HttpClient customer, OpenedSession session,
        params object[] items) =>
        customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });

    /// <summary>ลูกค้าส่งหนึ่งรอบด้วย token แล้วคืน id ของรอบนั้น (จาก <c>Location</c> ของคำตอบ 201)</summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await SubmitRawAsync(customer, session, items);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
    }

    private static object Line(int menuItemId, int quantity) => new { menuItemId, quantity, note = (string?)null };

    private static async Task AdvanceAsync(HttpClient staff, int orderId, string status) =>
        await OkAsync(await staff.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status }));

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, int sessionId, string method,
        decimal? amountReceived = null) =>
        client.PostAsJsonAsync($"/api/sessions/{sessionId}/payment", new { method, amountReceived });

    private static async Task<JsonElement> DetailAsync(HttpClient owner, int sessionId) =>
        await OkAsync(await owner.GetAsync($"/api/sessions/{sessionId}"));

    private static Task<List<Payment>> StoredPaymentsAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(db => db.Payments.AsNoTracking().Where(p => p.TableSessionId == sessionId).ToListAsync());

    private static Task<TableSession> StoredSessionAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(db => db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

    /// <summary>
    /// โต๊ะตาม o06: Gold 2 ผู้ใหญ่ 1 เด็ก + เบียร์สิงห์ 2 × ฿89 + ปรับยอด +฿50 →
    /// subtotal ฿1,225.00 · SC ฿122.50 · VAT ฿94.33 · รวม ฿1,441.83 (ออเดอร์ถูกเลื่อนจน Ready)
    /// </summary>
    private static async Task<OpenedSession> FrameB2Async(TestApp app, HttpClient owner, Menu menu, int packageId)
    {
        var session = await OpenSessionAsync(app, owner, packageId);
        var orderId = await SubmitAsync(app, session, Line(menu.MeatId, 2), Line(menu.BeerId, 2));
        await AdvanceAsync(owner, orderId, "Preparing");
        await AdvanceAsync(owner, orderId, "Ready");
        await OkAsync(await owner.PostAsJsonAsync($"/api/sessions/{session.Id}/adjustments",
            new { description = "ค่าปรับอาหารเหลือ", amount = 50m }));
        return session;
    }

    private sealed record World(TestApp App, HttpClient Owner, Menu Menu, int PackageId);

    private async Task<World> WorldAsync(TestApp app)
    {
        await SetRatesAsync(app);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app);
        return new World(app, owner, await CreateMenuAsync(owner), package.Id);
    }

    /// <summary>รอจน Postgres รายงานว่ามีคำสั่งที่ขึ้นต้นด้วย <paramref name="queryPrefix"/> รอล็อกอยู่ครบจำนวน (ไม่มีการ sleep)</summary>
    private async Task WaitForBlockedAsync(string database, string queryPrefix, int expected)
    {
        await using var probe = new NpgsqlConnection(postgres.ConnectionString);
        await probe.OpenAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = @db AND wait_event_type = 'Lock' AND query ILIKE @prefix
            """, probe);
        count.Parameters.AddWithValue("db", database);
        count.Parameters.AddWithValue("prefix", queryPrefix + "%");

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        while ((long)(await count.ExecuteScalarAsync())! < expected)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"ไม่มีคำขอไปรอล็อก ({queryPrefix}) ครบ {expected} คำขอ");
        }
    }

    // ------------------------------------------------------------------ กฎการปิดบิล

    [Fact]
    public async Task Cash_short_of_the_total_is_refused_and_the_bill_stays_open()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        Assert.Equal(1441.83m, (await DetailAsync(world.Owner, session.Id)).GetProperty("bill").GetProperty("total").GetDecimal());

        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash", 1441.82m),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.InsufficientAmount);
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "cash", 0m),
            HttpStatusCode.UnprocessableEntity, ErrorCodes.InsufficientAmount);

        var detail = await DetailAsync(world.Owner, session.Id);
        Assert.Equal("Open", detail.GetProperty("status").GetString());
        Assert.False(detail.TryGetProperty("payment", out var payment) && payment.ValueKind != JsonValueKind.Null);
        Assert.Empty(await StoredPaymentsAsync(app, session.Id));

        // ครบพอดีจ่ายได้ เงินทอน 0
        var paid = await OkAsync(await PayAsync(world.Owner, session.Id, "Cash", 1441.83m));
        Assert.Equal(0m, paid.GetProperty("payment").GetProperty("change").GetDecimal());
    }

    [Fact]
    public async Task Cash_records_the_amount_received_and_the_change_while_card_takes_the_total()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        var cash = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        var cashPaid = await OkAsync(await PayAsync(world.Owner, cash.Id, "Cash", 1500m));
        Assert.Equal("Paid", cashPaid.GetProperty("status").GetString());
        var cashPayment = cashPaid.GetProperty("payment");
        Assert.Equal("Cash", cashPayment.GetProperty("method").GetString());
        Assert.Equal(1500m, cashPayment.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(58.17m, cashPayment.GetProperty("change").GetDecimal());
        Assert.Equal(1441.83m, cashPayment.GetProperty("total").GetDecimal());

        // บัตร: amountReceived ที่ส่งมาไม่มีผล รับเท่ากับยอดรวมพอดี
        var card = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        var cardPayment = (await OkAsync(await PayAsync(world.Owner, card.Id, "Card", 5000m))).GetProperty("payment");
        Assert.Equal("Card", cardPayment.GetProperty("method").GetString());
        Assert.Equal(1441.83m, cardPayment.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(0m, cardPayment.GetProperty("change").GetDecimal());

        // ยอดแยกส่วน ณ ตอนจ่ายถูกเก็บไว้กับ Payment (ใบเสร็จ/รายงานไม่ต้องคำนวณใหม่)
        var stored = Assert.Single(await StoredPaymentsAsync(app, cash.Id));
        Assert.Equal(
            (997m, 178m, 50m, 1225m, 122.50m, 94.33m, 1441.83m, 1500m, 58.17m),
            (stored.BuffetTotal, stored.AlaCarteTotal, stored.AdjustmentTotal, stored.Subtotal, stored.ServiceCharge,
                stored.Vat, stored.Total, stored.AmountReceived, stored.Change));
    }

    [Fact]
    public async Task Paying_again_is_refused_with_already_paid_and_keeps_one_payment()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);

        var first = await OkAsync(await PayAsync(world.Owner, session.Id, "Card"));
        var receipt = first.GetProperty("payment").GetProperty("receiptNo").GetString()!;

        var again = await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash", 5000m),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);
        Assert.Contains(receipt, again.GetProperty("message").GetString());
        Assert.Single(await StoredPaymentsAsync(app, session.Id));

        // ALREADY_PAID มาก่อนกฎอื่น: เงินไม่พอกับบิลที่ปิดแล้วก็ยังได้ ALREADY_PAID
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash", 1m),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);

        // รอบที่ยกเลิกไปแล้วไม่มีบิลให้รับเงิน — session ไม่ Open จึงเป็นกฎข้อแรกเดียวกัน
        var voided = await OpenSessionAsync(app, world.Owner, world.PackageId);
        await OkAsync(await world.Owner.PostAsync($"/api/sessions/{voided.Id}/void", null));
        await AssertRefusedAsync(await PayAsync(world.Owner, voided.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);
        Assert.Empty(await StoredPaymentsAsync(app, voided.Id));
    }

    [Fact]
    public async Task Pending_or_preparing_orders_block_closing_and_are_named()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await OpenSessionAsync(app, world.Owner, world.PackageId);

        var ready = await SubmitAsync(app, session, Line(world.Menu.MeatId, 1));
        await AdvanceAsync(world.Owner, ready, "Preparing");
        await AdvanceAsync(world.Owner, ready, "Ready");
        var preparing = await SubmitAsync(app, session, Line(world.Menu.MeatId, 1));
        await AdvanceAsync(world.Owner, preparing, "Preparing");
        var pending = await SubmitAsync(app, session, Line(world.Menu.BeerId, 1));
        var cancelled = await SubmitAsync(app, session, Line(world.Menu.BeerId, 1));
        await OkAsync(await world.Owner.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "ลูกค้าเปลี่ยนใจ" }));

        // ORDERS_IN_PROGRESS มาก่อนเงินไม่พอ (ลำดับของ spec)
        var refused = await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash", 1m),
            HttpStatusCode.Conflict, ErrorCodes.OrdersInProgress);

        var message = refused.GetProperty("message").GetString()!;
        Assert.Contains("รอบที่ 2 (กำลังทำ)", message);
        Assert.Contains("รอบที่ 3 (รอรับ)", message);
        Assert.DoesNotContain("รอบที่ 1", message);
        Assert.DoesNotContain("รอบที่ 4", message);

        var named = refused.GetProperty("orders").EnumerateArray()
            .Select(o => (o.GetProperty("id").GetInt32(), o.GetProperty("number").GetInt32(), o.GetProperty("status").GetString()))
            .ToList();
        Assert.Equal([(preparing, 2, "Preparing"), (pending, 3, "Pending")], named);

        Assert.Equal(SessionStatus.Open, (await StoredSessionAsync(app, session.Id)).Status);
        Assert.Empty(await StoredPaymentsAsync(app, session.Id));

        // ครัวทำจนเหลือแต่ Ready → ปิดบิลได้
        await AdvanceAsync(world.Owner, preparing, "Ready");
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.OrdersInProgress);
        await AdvanceAsync(world.Owner, pending, "Preparing");
        await AdvanceAsync(world.Owner, pending, "Ready");
        Assert.Equal("Paid", (await OkAsync(await PayAsync(world.Owner, session.Id, "Card"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_ready_orders_left_does_not_block_payment()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await OpenSessionAsync(app, world.Owner, world.PackageId);
        var order = await SubmitAsync(app, session, Line(world.Menu.BeerId, 1));
        await AdvanceAsync(world.Owner, order, "Preparing");
        await AdvanceAsync(world.Owner, order, "Ready");

        var paid = await OkAsync(await PayAsync(world.Owner, session.Id, "Cash", 2000m));

        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        Assert.Equal("Ready", Assert.Single(paid.GetProperty("orders").EnumerateArray()).GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------ ใบเสร็จ และ token

    [Fact]
    public async Task A_payment_issues_a_receipt_number_and_retires_the_token_at_once()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);

        var paid = await OkAsync(await PayAsync(world.Owner, session.Id, "Cash", 2000m));
        var payment = paid.GetProperty("payment");
        var receipt = payment.GetProperty("receiptNo").GetString()!;
        var paidAt = payment.GetProperty("paidAt").GetDateTimeOffset();

        // R + วันที่ตามเวลาไทยของเวลาที่จ่าย + - + 6 หลัก
        var match = ReceiptFormat().Match(receipt);
        Assert.True(match.Success, receipt);
        Assert.Equal(Bkk.DateOf(paidAt).ToString("yyyyMMdd", CultureInfo.InvariantCulture), match.Groups[1].Value);

        // session ปิด โต๊ะว่างทันที ใบเสร็จอ่านกลับได้จากรายละเอียด
        var stored = await StoredSessionAsync(app, session.Id);
        Assert.Equal(SessionStatus.Paid, stored.Status);
        Assert.NotNull(stored.ClosedAt);
        Assert.Equal(receipt, Assert.Single(await StoredPaymentsAsync(app, session.Id)).ReceiptNo);
        Assert.Equal(receipt, (await DetailAsync(world.Owner, session.Id)).GetProperty("payment").GetProperty("receiptNo").GetString());
        var overview = await OkAsync(await world.Owner.GetAsync("/api/tables/overview"));
        var table = overview.EnumerateArray().Single(t => t.GetProperty("name").GetString() == session.TableName);
        Assert.True(!table.TryGetProperty("sessionId", out var sid) || sid.ValueKind == JsonValueKind.Null);

        // token เดิม: สั่งไม่ได้ อ่านไม่ได้ (SESSION_CLOSED) และต่อ hub ไม่ได้
        using var customer = app.CreateClient();
        await AssertRefusedAsync(await SubmitRawAsync(customer, session, Line(world.Menu.MeatId, 1)),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        var closed = await AssertRefusedAsync(await customer.GetAsync($"/api/s/{session.Token}"),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Equal("Paid", closed.GetProperty("closedAs").GetString());
        Assert.Equal("ชาบูเดโม่", closed.GetProperty("shopName").GetString());
        // หน้า "ขอบคุณที่ใช้บริการ" ไม่แสดงยอดเงิน: คำตอบของลูกค้าไม่มีตัวเลขเงินใด ๆ
        var customerText = closed.GetRawText();
        Assert.DoesNotContain("1441", customerText);
        Assert.DoesNotContain("receipt", customerText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("total", customerText, StringComparison.OrdinalIgnoreCase);
        await LiveClient.AssertRejectedAsync(app, token: session.Token);

        // รอบที่ยกเลิกต่างจากรอบที่ชำระแล้ว — จอลูกค้าจึงไม่ขึ้น "ชำระเงินเรียบร้อยแล้ว" ให้รอบที่ไม่ได้จ่าย
        var voided = await OpenSessionAsync(app, world.Owner, world.PackageId);
        await OkAsync(await world.Owner.PostAsync($"/api/sessions/{voided.Id}/void", null));
        var voidedBody = await AssertRefusedAsync(await customer.GetAsync($"/api/s/{voided.Token}"),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Equal("Voided", voidedBody.GetProperty("closedAs").GetString());
    }

    [Fact]
    public async Task Receipt_numbers_come_from_the_sequence_and_never_repeat_across_tables_paid_at_once()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var sessions = new List<OpenedSession>();
        for (var i = 0; i < 6; i++)
        {
            sessions.Add(await OpenSessionAsync(app, world.Owner, world.PackageId));
        }

        var responses = await Task.WhenAll(sessions.Select(s => PayAsync(world.Owner, s.Id, "Card")));
        var receipts = new List<string>();
        foreach (var response in responses)
        {
            receipts.Add((await OkAsync(response)).GetProperty("payment").GetProperty("receiptNo").GetString()!);
        }

        Assert.All(receipts, r => Assert.Matches(ReceiptFormat(), r));
        Assert.Equal(receipts.Count, receipts.Distinct().Count());
        var sequence = receipts.Select(r => long.Parse(ReceiptFormat().Match(r).Groups[2].Value, CultureInfo.InvariantCulture))
            .Order().ToList();
        Assert.Equal(sequence.Count, sequence.Distinct().Count());

        // คำขอที่ถูกปฏิเสธไม่กินเลข: ใบถัดไปต่อจากใบล่าสุดพอดี
        var blocked = await OpenSessionAsync(app, world.Owner, world.PackageId);
        await SubmitAsync(app, blocked, Line(world.Menu.MeatId, 1));
        await AssertRefusedAsync(await PayAsync(world.Owner, blocked.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.OrdersInProgress);
        var next = await OpenSessionAsync(app, world.Owner, world.PackageId);
        var nextReceipt = (await OkAsync(await PayAsync(world.Owner, next.Id, "Card")))
            .GetProperty("payment").GetProperty("receiptNo").GetString()!;
        Assert.Equal(sequence.Max() + 1,
            long.Parse(ReceiptFormat().Match(nextReceipt).Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// "จ่ายพร้อมกันสองคำขอได้ใบเสร็จใบเดียว": ล็อกแถว session ไว้ก่อน แล้วยิงสองคำขอ (เงินสดกับบัตร) จนทั้งคู่ไปรอที่ล็อก
    /// เดียวกันจริง (ยืนยันจาก pg_stat_activity ไม่ใช่การรอเวลา) แล้วปล่อยพร้อมกัน — สำเร็จหนึ่ง ALREADY_PAID หนึ่ง
    /// Payment หนึ่งแถว และ event ปิดรอบหนึ่งครั้ง
    /// </summary>
    [Fact]
    public async Task Two_simultaneous_payments_yield_exactly_one_receipt()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        var ownerCookie = await LiveClient.AuthCookieAsync(app, "DemoSeed:OwnerEmail");
        await using var ownerLive = await LiveClient.ConnectAsync(app, cookie: ownerCookie);
        var secondTablet = await OwnerClientAsync(app);

        await using var holder = new NpgsqlConnection(app.ConnectionString);
        await holder.OpenAsync();
        await using var lockTx = await holder.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("""SELECT 1 FROM "TableSessions" WHERE "Id" = @id FOR UPDATE""", holder, lockTx))
        {
            hold.Parameters.AddWithValue("id", session.Id);
            await hold.ExecuteScalarAsync();
        }

        var cash = PayAsync(world.Owner, session.Id, "Cash", 1500m);
        var card = PayAsync(secondTablet, session.Id, "Card");
        await WaitForBlockedAsync(holder.Database, "SELECT 1 FROM \"TableSessions\"", 2);
        await lockTx.CommitAsync();
        var responses = await Task.WhenAll(cash, card);

        var succeeded = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        await AssertRefusedAsync(Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);

        var receipt = (await JsonAsync(succeeded)).GetProperty("payment").GetProperty("receiptNo").GetString();
        var stored = Assert.Single(await StoredPaymentsAsync(app, session.Id));
        Assert.Equal(receipt, stored.ReceiptNo);
        Assert.Single(await app.WithDbAsync(db => db.Payments.AsNoTracking().Where(p => p.ReceiptNo == receipt).ToListAsync()));

        await LiveClient.BarrierAsync(app, ownerLive);
        Assert.Single(ownerLive.Inbox.Of("sessionClosed"), e => e.GetProperty("sessionId").GetInt32() == session.Id);
    }

    /// <summary>
    /// ออเดอร์ที่ลูกค้าส่งเข้ามาขณะกำลังปิดบิล: การชำระถือล็อกแถว session อยู่ ออเดอร์จึงต้องรอแล้วได้ SESSION_CLOSED
    /// ไม่ใช่เล็ดลอดเข้าไปค้างเป็น Pending ในบิลที่ออกใบเสร็จแล้ว
    /// </summary>
    [Fact]
    public async Task An_order_sent_while_the_bill_is_being_closed_is_refused_once_the_payment_commits()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await OpenSessionAsync(app, world.Owner, world.PackageId);

        // จำลองการชำระที่ยังไม่ commit: ล็อกแถวแล้วตั้ง Paid ใน transaction เดียวกัน
        await using var holder = new NpgsqlConnection(app.ConnectionString);
        await holder.OpenAsync();
        await using var payTx = await holder.BeginTransactionAsync();
        await using (var pay = new NpgsqlCommand("""
            SELECT 1 FROM "TableSessions" WHERE "Id" = @id FOR UPDATE;
            UPDATE "TableSessions" SET "Status" = 'Paid', "ClosedAt" = now() WHERE "Id" = @id;
            """, holder, payTx))
        {
            pay.Parameters.AddWithValue("id", session.Id);
            await pay.ExecuteNonQueryAsync();
        }

        using var customer = app.CreateClient();
        var submit = SubmitRawAsync(customer, session, Line(world.Menu.MeatId, 1));
        await WaitForBlockedAsync(holder.Database, "", 1);
        await payTx.CommitAsync();

        await AssertRefusedAsync(await submit, HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Empty(await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSessionId == session.Id).ToListAsync()));
    }

    // ------------------------------------------------------------------ realtime

    [Fact]
    public async Task Paying_tells_the_table_and_the_owner_then_disconnects_the_customer()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        var other = await OpenSessionAsync(app, world.Owner, world.PackageId);

        await using var customer = await LiveClient.ConnectAsync(app, token: session.Token);
        await using var neighbour = await LiveClient.ConnectAsync(app, token: other.Token);
        await using var ownerLive = await LiveClient.ConnectAsync(app,
            cookie: await LiveClient.AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        customer.Hub.Closed += _ =>
        {
            disconnected.TrySetResult();
            return Task.CompletedTask;
        };

        await OkAsync(await PayAsync(world.Owner, session.Id, "Cash", 1500m));

        // ลูกค้าโต๊ะนี้ได้ sessionClosed ก่อนถูกตัด แล้วการเชื่อมต่อถูกปิดจากฝั่งเซิร์ฟเวอร์
        await customer.Inbox.WaitForAsync("sessionClosed", e => e.GetProperty("sessionId").GetInt32() == session.Id,
            LiveClient.Within);
        await disconnected.Task.WaitAsync(LiveClient.Within);
        Assert.Equal(HubConnectionState.Disconnected, customer.Hub.State);

        // เจ้าของร้านได้ทั้ง sessionChanged (Paid) และ sessionClosed
        await ownerLive.Inbox.WaitForAsync("sessionClosed", e => e.GetProperty("sessionId").GetInt32() == session.Id,
            LiveClient.Within);
        var changed = await ownerLive.Inbox.WaitForAsync("sessionChanged",
            e => e.GetProperty("id").GetInt32() == session.Id, LiveClient.Within);
        Assert.Equal("Paid", changed.GetProperty("status").GetString());

        // โต๊ะอื่นยังต่ออยู่ และไม่ได้ event ของโต๊ะนี้
        await LiveClient.BarrierAsync(app, neighbour);
        Assert.Equal(HubConnectionState.Connected, neighbour.Hub.State);
        Assert.Empty(neighbour.Inbox.Of("sessionClosed"));
        Assert.Empty(neighbour.Inbox.Of("sessionChanged"));
    }

    // ------------------------------------------------------------------ เวลา และสิทธิ์

    [Fact]
    public async Task A_round_past_its_end_time_can_no_longer_order_but_can_still_be_paid()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);
        await app.WithDbAsync(async db =>
        {
            var past = DateTimeOffset.UtcNow.AddMinutes(-17);
            await db.TableSessions.Where(s => s.Id == session.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OpenedAt, past.AddMinutes(-90))
                .SetProperty(x => x.EndsAt, past));
            return 0;
        });

        using var customer = app.CreateClient();
        await AssertRefusedAsync(await SubmitRawAsync(customer, session, Line(world.Menu.MeatId, 1)),
            HttpStatusCode.Conflict, ErrorCodes.SessionExpired);

        var paid = await OkAsync(await PayAsync(world.Owner, session.Id, "Cash", 1441.83m));
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Only_the_owner_may_take_payment_and_the_request_is_checked()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var session = await FrameB2Async(app, world.Owner, world.Menu, world.PackageId);

        var kitchen = await KitchenClientAsync(app);
        Assert.Equal(HttpStatusCode.Forbidden, (await PayAsync(kitchen, session.Id, "Card")).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PayAsync(anonymous, session.Id, "Card")).StatusCode);

        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Bitcoin"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "1"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash"),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await PayAsync(world.Owner, session.Id, "Cash", 1500.555m),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await PayAsync(world.Owner, 987654, "Card"),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        Assert.Equal(SessionStatus.Open, (await StoredSessionAsync(app, session.Id)).Status);
        Assert.Empty(await StoredPaymentsAsync(app, session.Id));
    }
}
