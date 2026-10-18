using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 20 — ข้อมูลพิมพ์ 80mm และประวัติบิลที่ชำระแล้ว ผ่าน seam 1 (แอปจริง + Postgres จริง)
/// <c>GET /api/sessions/{id}/print-data?doc=qr|prebill|receipt</c> คืนข้อมูลชุดเดียวกับหน้าจอเจ้าของร้าน
/// (บิลจากตัวคำนวณจุดเดียว) และ <c>GET /api/sessions/history?date=yyyy-MM-dd</c> คืนบิลที่ชำระในวันนั้นตามเวลาไทย
///
/// การชำระเงิน (ticket 18) ยังไม่อยู่ในสาขานี้ บิลที่ "ชำระแล้ว" จึงสร้างด้วยข้อมูลตรง ๆ ในรูปเดียวกับที่
/// การชำระเงินบันทึก: session เป็น Paid, ClosedAt = เวลาชำระ และแถว Payment หนึ่งแถว
/// </summary>
[Collection(ApiCollection.Name)]
public class PrintTests(PostgresFixture postgres)
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

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"คาดว่า 200 แต่ได้ {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await JsonAsync(response);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..10];

    /// <summary>หัวเอกสารและอัตราตาม frame p1: ชาบูเดโม่ · SC 10% · VAT 7% · ลิมิต 5 ต่อเมนู 20 ต่อรอบ</summary>
    private static Task SetShopAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            await db.Settings.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Name, "ชาบูเดโม่")
                .SetProperty(x => x.Address, "99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110")
                .SetProperty(x => x.Phone, "02-000-0000")
                .SetProperty(x => x.TaxId, "0-1055-67000-00-0")
                .SetProperty(x => x.PromptPayId, "0812345678")
                .SetProperty(x => x.ServiceChargePercent, 10m)
                .SetProperty(x => x.VatPercent, 7m)
                .SetProperty(x => x.MaxQtyPerItemPerOrder, 5)
                .SetProperty(x => x.MaxItemsPerOrder, 20));
            return 0;
        });

    private static Task<Package> SeedPackageAsync(TestApp app, string name = "Gold", decimal adult = 399m,
        decimal child = 199m) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var package = new Package
            {
                Name = name,
                Rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r)),
                AdultPrice = adult,
                ChildPrice = child,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package;
        });

    private sealed record OpenedSession(int Id, string Token, string TableName);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int packageId,
        int adults = 2, int children = 0)
    {
        var tableName = UniqueName("B");
        var table = await owner.PostAsJsonAsync("/api/tables", new { name = tableName, seats = 4, zone = "B" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = adults, childCount = children });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, tableName);
    }

    /// <summary>เบียร์สิงห์ ฿89 (à la carte) ผ่านเส้นทางเมนูจริง แล้วลูกค้าสั่ง 2 ขวดด้วย token</summary>
    private static async Task OrderBeerAsync(TestApp app, HttpClient owner, OpenedSession session)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        var item = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = "เบียร์สิงห์", categoryId, kind = "AlaCarte", minPackageRank = (int?)null, price = 89m,
            isAvailable = true,
        });
        Assert.Equal(HttpStatusCode.Created, item.StatusCode);
        var menuItemId = (await JsonAsync(item)).GetProperty("id").GetInt32();

        using var customer = app.CreateClient();
        var order = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders",
            new { items = new[] { new { menuItemId, quantity = 2, note = (string?)null } } });
        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
    }

    /// <summary>
    /// โต๊ะ B2 ของ frame p1: Gold 2 ผู้ใหญ่ ฿399 + 1 เด็ก ฿199, เบียร์สิงห์ 2 × ฿89, ค่าปรับอาหารเหลือ +฿50
    /// → ยอดก่อนค่าบริการ ฿1,225.00 · ค่าบริการ 10% ฿122.50 · VAT 7% ฿94.33 · รวม ฿1,441.83
    /// </summary>
    private static async Task<OpenedSession> SeedFrameBillAsync(TestApp app, HttpClient owner)
    {
        await SetShopAsync(app);
        var package = await SeedPackageAsync(app);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 2, children: 1);
        await OrderBeerAsync(app, owner, session);
        var adjust = await owner.PostAsJsonAsync($"/api/sessions/{session.Id}/adjustments",
            new { description = "ค่าปรับอาหารเหลือ", amount = 50m });
        Assert.Equal(HttpStatusCode.OK, adjust.StatusCode);
        return session;
    }

    /// <summary>บันทึกการชำระในรูปเดียวกับที่ ticket 18 บันทึก (Paid + ClosedAt = PaidAt + แถว Payment)</summary>
    private static Task MarkPaidAsync(TestApp app, int sessionId, DateTimeOffset paidAt, string receiptNo,
        PaymentMethod method, decimal total, decimal received) =>
        app.WithDbAsync(async db =>
        {
            var session = await db.TableSessions.SingleAsync(s => s.Id == sessionId);
            session.Status = SessionStatus.Paid;
            session.ClosedAt = paidAt;
            db.Payments.Add(new Payment
            {
                TableSessionId = sessionId,
                Method = method,
                Subtotal = total,
                Total = total,
                AmountReceived = received,
                Change = received - total,
                ReceiptNo = receiptNo,
                PaidAt = paidAt,
            });
            await db.SaveChangesAsync();
            return 0;
        });

    private static Task<HttpResponseMessage> PrintDataAsync(HttpClient client, int sessionId, string? doc) =>
        client.GetAsync(doc is null
            ? $"/api/sessions/{sessionId}/print-data"
            : $"/api/sessions/{sessionId}/print-data?doc={doc}");

    private static decimal Money(JsonElement bill, string field) => bill.GetProperty(field).GetDecimal();

    // ------------------------------------------------------------------ ข้อมูลพิมพ์

    [Fact]
    public async Task Pre_bill_of_an_open_session_carries_the_calculators_bill_and_no_payment()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var session = await SeedFrameBillAsync(app, owner);

        var data = await OkAsync(await PrintDataAsync(owner, session.Id, "prebill"));
        var detail = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));

        // ยอดเดียวกับบิลที่ API ให้สำหรับ session นี้ ทุกบรรทัด (ตัวคำนวณตัวเดียวกัน)
        Assert.Equal(detail.GetProperty("bill").GetRawText(), data.GetProperty("bill").GetRawText());

        // และตรงกับตัวเลขของ frame p1 ที่คำนวณมือไว้
        var bill = data.GetProperty("bill");
        Assert.Equal(1225.00m, Money(bill, "subtotal"));
        Assert.Equal(122.50m, Money(bill, "serviceCharge"));
        Assert.Equal(94.33m, Money(bill, "vat"));
        Assert.Equal(1441.83m, Money(bill, "total"));

        // null ไม่ถูกเขียนลง JSON (Program.cs: WhenWritingNull) — ยังไม่ชำระ = ไม่มี payment
        Assert.False(data.TryGetProperty("payment", out _));
        Assert.Equal(session.Id, data.GetProperty("sessionId").GetInt32());
        Assert.Equal(session.TableName, data.GetProperty("tableName").GetString());
        Assert.Equal("Gold", data.GetProperty("packageName").GetString());
        Assert.Equal(2, data.GetProperty("adults").GetInt32());
        Assert.Equal(1, data.GetProperty("children").GetInt32());
        Assert.Equal(detail.GetProperty("openedAt").GetDateTimeOffset(), data.GetProperty("openedAt").GetDateTimeOffset());
        Assert.Equal(detail.GetProperty("endsAt").GetDateTimeOffset(), data.GetProperty("endsAt").GetDateTimeOffset());
        Assert.EndsWith($"/s/{session.Token}", data.GetProperty("qrUrl").GetString());

        var settings = data.GetProperty("settings");
        Assert.Equal("ชาบูเดโม่", settings.GetProperty("name").GetString());
        Assert.Equal("99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110", settings.GetProperty("address").GetString());
        Assert.Equal("02-000-0000", settings.GetProperty("phone").GetString());
        Assert.Equal("0-1055-67000-00-0", settings.GetProperty("taxId").GetString());
        Assert.Equal("0812345678", settings.GetProperty("promptPayId").GetString());

        // QR slip ของรอบที่เปิดอยู่ได้ข้อมูลชุดเดียวกัน พร้อมลิมิตต่อรอบสำหรับบรรทัดท้าย slip
        var slip = await OkAsync(await PrintDataAsync(owner, session.Id, "qr"));
        Assert.Equal(data.GetProperty("qrUrl").GetString(), slip.GetProperty("qrUrl").GetString());
        Assert.Equal(5, slip.GetProperty("settings").GetProperty("maxQtyPerItemPerOrder").GetInt32());
        Assert.Equal(20, slip.GetProperty("settings").GetProperty("maxItemsPerOrder").GetInt32());
    }

    [Fact]
    public async Task Receipt_of_a_paid_session_carries_the_payment_and_its_receipt_number()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var session = await SeedFrameBillAsync(app, owner);
        var paidAt = new DateTimeOffset(2026, 10, 16, 12, 46, 0, TimeSpan.Zero);
        await MarkPaidAsync(app, session.Id, paidAt, "R20261016-000047", PaymentMethod.Cash, 1441.83m, 1500m);

        var data = await OkAsync(await PrintDataAsync(owner, session.Id, "receipt"));

        var payment = data.GetProperty("payment");
        Assert.Equal("R20261016-000047", payment.GetProperty("receiptNo").GetString());
        Assert.Equal("Cash", payment.GetProperty("method").GetString());
        Assert.Equal(1500m, payment.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(58.17m, payment.GetProperty("change").GetDecimal());
        Assert.Equal(paidAt, payment.GetProperty("paidAt").GetDateTimeOffset());

        // ใบเสร็จพิมพ์ซ้ำได้ด้วยยอดเดียวกับที่รับเงินไป
        var bill = data.GetProperty("bill");
        Assert.Equal(1441.83m, Money(bill, "total"));
        Assert.Equal(payment.GetProperty("total").GetDecimal(), Money(bill, "total"));
        Assert.Equal("0-1055-67000-00-0", data.GetProperty("settings").GetProperty("taxId").GetString());

        // ใบแจ้งยอดของบิลที่ชำระแล้วยังเปิดดูได้ (ยอดเดียวกัน) แต่ QR slip ของรอบที่ปิดแล้วไม่พิมพ์
        var prebill = await OkAsync(await PrintDataAsync(owner, session.Id, "prebill"));
        Assert.Equal(bill.GetRawText(), prebill.GetProperty("bill").GetRawText());
        await AssertRefusedAsync(await PrintDataAsync(owner, session.Id, "qr"), HttpStatusCode.Conflict,
            "SESSION_CLOSED");
    }

    [Fact]
    public async Task Receipt_of_a_session_that_is_not_paid_is_refused()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app);
        var open = await OpenSessionAsync(app, owner, package.Id);
        var voided = await OpenSessionAsync(app, owner, package.Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/sessions/{voided.Id}/void", null)).StatusCode);

        await AssertRefusedAsync(await PrintDataAsync(owner, open.Id, "receipt"), HttpStatusCode.Conflict,
            "SESSION_CLOSED");
        await AssertRefusedAsync(await PrintDataAsync(owner, voided.Id, "receipt"), HttpStatusCode.Conflict,
            "SESSION_CLOSED");

        // เอกสารอื่นของรอบที่เปิดอยู่ยังพิมพ์ได้ และไม่มีการชำระเงินเกิดขึ้นจากการขอใบเสร็จ
        Assert.Equal(HttpStatusCode.OK, (await PrintDataAsync(owner, open.Id, "prebill")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PrintDataAsync(owner, open.Id, null)).StatusCode);
        Assert.False(await app.WithDbAsync(db => db.Payments.AnyAsync(p => p.TableSessionId == open.Id)));

        await AssertRefusedAsync(await PrintDataAsync(owner, open.Id, "kitchen"), HttpStatusCode.BadRequest,
            "VALIDATION_FAILED");
        await AssertRefusedAsync(await PrintDataAsync(owner, 987654, "prebill"), HttpStatusCode.NotFound,
            "SESSION_NOT_FOUND");
    }

    [Fact]
    public async Task Print_data_and_history_are_owner_only()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        using var anonymous = app.CreateClient();
        var package = await SeedPackageAsync(app);
        var session = await OpenSessionAsync(app, owner, package.Id);

        foreach (var url in new[]
                 {
                     $"/api/sessions/{session.Id}/print-data?doc=prebill",
                     $"/api/sessions/{session.Id}/print-data?doc=qr",
                     "/api/sessions/history?date=2026-10-16",
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await kitchen.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(url)).StatusCode);
        }
    }

    /// <summary>
    /// บิลรวม (ticket 22): รอบที่ถูกรวม (ยัง Open) อ่านใบแจ้งยอด/ใบเสร็จของรอบหลัก แต่ QR slip ยังเป็น
    /// ของรอบตัวเอง เพราะ QR นั้นคือทางสั่งอาหารของโต๊ะนั้น (ยังใช้ได้จนกว่าจะปิดบิล)
    /// </summary>
    [Fact]
    public async Task Receipt_of_a_merged_session_is_the_primary_bills_receipt()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var session = await SeedFrameBillAsync(app, owner);
        var mergedIn = await OpenSessionAsync(app, owner, (await SeedPackageAsync(app)).Id, adults: 3);
        await app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == mergedIn.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.MergedIntoSessionId, session.Id));
            return 0;
        });

        var prebill = await OkAsync(await PrintDataAsync(owner, mergedIn.Id, "prebill"));
        Assert.Equal(session.Id, prebill.GetProperty("sessionId").GetInt32());
        Assert.Equal(session.TableName, prebill.GetProperty("tableName").GetString());
        // ยอดของบิลรวม: บุฟเฟ่ต์ 2×฿399 + 1×฿199 (หลัก) + 3×฿399 (รอบที่รวม) = ฿1,994 + เบียร์ ฿178
        // + ค่าปรับ ฿50 → subtotal ฿2,422.00 — ยอดเดียวกับที่ API ให้กับรอบหลัก
        Assert.Equal(5, prebill.GetProperty("bill").GetProperty("adults").GetInt32());
        Assert.Equal(1, prebill.GetProperty("bill").GetProperty("children").GetInt32());
        Assert.Equal(2422.00m, Money(prebill.GetProperty("bill"), "subtotal"));
        var primary = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));
        Assert.Equal(primary.GetProperty("bill").GetRawText(), prebill.GetProperty("bill").GetRawText());

        // QR slip ยังเป็นของรอบตัวเอง (โต๊ะของรอบนั้น และ URL ของ token ตัวเอง)
        var slip = await OkAsync(await PrintDataAsync(owner, mergedIn.Id, "qr"));
        Assert.Equal(mergedIn.Id, slip.GetProperty("sessionId").GetInt32());
        Assert.Equal(mergedIn.TableName, slip.GetProperty("tableName").GetString());
        Assert.Equal(3, slip.GetProperty("adults").GetInt32());
        Assert.EndsWith($"/s/{mergedIn.Token}", slip.GetProperty("qrUrl").GetString());

        // เมื่อปิดบิล: รอบที่ถูกรวมเป็น Paid พร้อมรอบหลัก → ใบเสร็จคือใบเดียวของบิลรวม และ QR ตายทั้งคู่
        var paidAt = new DateTimeOffset(2026, 10, 16, 12, 46, 0, TimeSpan.Zero);
        await MarkPaidAsync(app, session.Id, paidAt, "R20261016-000048", PaymentMethod.PromptPay, 1441.83m,
            1441.83m);
        await app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == mergedIn.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Paid)
                .SetProperty(x => x.ClosedAt, paidAt));
            return 0;
        });

        var receipt = await OkAsync(await PrintDataAsync(owner, mergedIn.Id, "receipt"));
        Assert.Equal(session.Id, receipt.GetProperty("sessionId").GetInt32());
        Assert.Equal("R20261016-000048", receipt.GetProperty("payment").GetProperty("receiptNo").GetString());
        Assert.Equal(2422.00m, Money(receipt.GetProperty("bill"), "subtotal"));
        await AssertRefusedAsync(await PrintDataAsync(owner, mergedIn.Id, "qr"), HttpStatusCode.Conflict,
            "SESSION_CLOSED");
    }

    // ------------------------------------------------------------------ ประวัติการชำระ

    [Fact]
    public async Task History_lists_only_the_paid_bills_of_that_bangkok_day_latest_first()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var gold = await SeedPackageAsync(app, "Gold");
        var silver = await SeedPackageAsync(app, "Silver", adult: 299m, child: 149m);

        var evening = await OpenSessionAsync(app, owner, gold.Id, adults: 2, children: 1);
        var lunch = await OpenSessionAsync(app, owner, silver.Id, adults: 3);
        var afterMidnight = await OpenSessionAsync(app, owner, gold.Id, adults: 4);
        var stillOpen = await OpenSessionAsync(app, owner, gold.Id);
        var voided = await OpenSessionAsync(app, owner, gold.Id);
        var mergedIn = await OpenSessionAsync(app, owner, gold.Id, adults: 2);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/sessions/{voided.Id}/void", null)).StatusCode);

        // 16:30Z = 23:30 ของวันที่ 16 ที่กรุงเทพฯ · 17:10Z = 00:10 ของวันที่ 17 · 05:15Z = 12:15 ของวันที่ 16
        await MarkPaidAsync(app, evening.Id, new DateTimeOffset(2026, 10, 16, 16, 30, 0, TimeSpan.Zero),
            "R20261016-000002", PaymentMethod.PromptPay, 1949.11m, 1949.11m);
        await MarkPaidAsync(app, lunch.Id, new DateTimeOffset(2026, 10, 16, 5, 15, 0, TimeSpan.Zero),
            "R20261016-000001", PaymentMethod.Cash, 1043.90m, 1100m);
        await MarkPaidAsync(app, afterMidnight.Id, new DateTimeOffset(2026, 10, 16, 17, 10, 0, TimeSpan.Zero),
            "R20261017-000003", PaymentMethod.Card, 2401.20m, 2401.20m);

        // บิลรวม (ticket 22): รอบที่ถูกรวมเป็น Paid พร้อมรอบหลักแต่ไม่มีใบเสร็จของตัวเอง → หนึ่งแถว คนรวมกัน
        await app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == mergedIn.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MergedIntoSessionId, evening.Id)
                .SetProperty(x => x.Status, SessionStatus.Paid)
                .SetProperty(x => x.ClosedAt, new DateTimeOffset(2026, 10, 16, 16, 30, 0, TimeSpan.Zero)));
            return 0;
        });

        var day16 = (await OkAsync(await owner.GetAsync("/api/sessions/history?date=2026-10-16")))
            .EnumerateArray().ToList();
        Assert.Equal([evening.Id, lunch.Id], day16.Select(r => r.GetProperty("sessionId").GetInt32()));

        var row = day16[0];
        Assert.Equal("R20261016-000002", row.GetProperty("receiptNo").GetString());
        Assert.Equal(evening.TableName, row.GetProperty("tableName").GetString());
        Assert.Equal("Gold", row.GetProperty("packageName").GetString());
        Assert.Equal(5, row.GetProperty("guests").GetInt32()); // 2 ผู้ใหญ่ 1 เด็ก + 2 ผู้ใหญ่ของรอบที่รวมเข้ามา
        Assert.Equal("PromptPay", row.GetProperty("method").GetString());
        Assert.Equal(1949.11m, row.GetProperty("total").GetDecimal());
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 16, 30, 0, TimeSpan.Zero),
            row.GetProperty("paidAt").GetDateTimeOffset());
        Assert.Equal(3, day16[1].GetProperty("guests").GetInt32());
        Assert.Equal("Cash", day16[1].GetProperty("method").GetString());

        var day17 = (await OkAsync(await owner.GetAsync("/api/sessions/history?date=2026-10-17")))
            .EnumerateArray().ToList();
        Assert.Equal([afterMidnight.Id], day17.Select(r => r.GetProperty("sessionId").GetInt32()));

        // ตัวกรองเดิมของรายการ session ก็ตัดวันตามเวลาไทยแบบเดียวกัน
        var listed = (await OkAsync(await owner.GetAsync("/api/sessions?status=Paid&date=2026-10-16")))
            .EnumerateArray().Select(s => s.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(evening.Id, listed);
        Assert.Contains(lunch.Id, listed);
        Assert.DoesNotContain(afterMidnight.Id, listed);
        Assert.DoesNotContain(stillOpen.Id, listed);
        Assert.DoesNotContain(voided.Id, listed);

        Assert.Empty((await OkAsync(await owner.GetAsync("/api/sessions/history?date=2026-10-15"))).EnumerateArray());
        await AssertRefusedAsync(await owner.GetAsync("/api/sessions/history?date=16/10/2569"),
            HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }
}
