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

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 16 — บิลและการปรับยอด ผ่าน seam 1 (แอปจริง + Postgres จริง) ไม่มี unit test ของตัวคำนวณแยก:
/// ทุกตัวเลขอ่านจาก <c>GET /api/sessions/{id}</c> (บิลที่เจ้าของร้านเห็น) หลังสร้างข้อมูลผ่านเส้นทางจริง
/// <c>POST /api/sessions/{id}/adjustments {description, amount}</c> และ
/// <c>DELETE /api/sessions/{id}/adjustments/{adjId}</c> คืนรายละเอียด session พร้อมบิลใหม่
///
/// แถวตั้งค่าร้านมีแถวเดียวที่ใช้ร่วมกันทั้งคอลเลกชัน (เทสต์ในคอลเลกชันรันทีละตัว) — ทุกเทสต์ที่ดูยอด
/// SC/VAT จึงตั้งอัตราของตัวเองก่อนเสมอ
/// </summary>
[Collection(ApiCollection.Name)]
public class BillingTests(PostgresFixture postgres)
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

    /// <summary>ปฏิเสธด้วยสถานะ HTTP และ <c>code</c> ตามสัญญา พร้อมข้อความไทยที่แสดงต่อผู้ใช้ได้ตรง ๆ</summary>
    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        Assert.Contains(body.GetProperty("message").GetString()!, c => c >= '\u0e00' && c <= '\u0e7f');
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    /// <summary>อัตราของร้านสำหรับเทสต์นี้ และลิมิตต่อรอบตามค่าเริ่มต้นของ DATA (5 ต่อเมนู 20 ต่อรอบ)</summary>
    private static Task SetRatesAsync(TestApp app, decimal serviceChargePercent, decimal vatPercent) =>
        app.WithDbAsync(async db =>
        {
            await db.Settings.ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ServiceChargePercent, serviceChargePercent)
                .SetProperty(x => x.VatPercent, vatPercent)
                .SetProperty(x => x.MaxQtyPerItemPerOrder, 5)
                .SetProperty(x => x.MaxItemsPerOrder, 20));
            return 0;
        });

    /// <summary>Rank มี unique index ร่วมกับเทสต์อื่นในคอลเลกชัน จึงสร้างผ่านฐานข้อมูลด้วย rank ที่ยังว่าง</summary>
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

    private sealed record OpenedSession(int Id, string Token);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int packageId,
        int adults = 2, int children = 0)
    {
        var table = await owner.PostAsJsonAsync("/api/tables", new { name = UniqueName("B"), seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = adults, childCount = children });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await JsonAsync(response)).GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token);
    }

    private static async Task<int> CreateCategoryAsync(HttpClient owner)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        return (await JsonAsync(category)).GetProperty("id").GetInt32();
    }

    /// <summary>เมนูผ่านเส้นทางจริงของ ticket 05: บุฟเฟ่ต์ (rank 1 ขึ้นไป ราคา 0) หรือ à la carte ตามราคา</summary>
    private static async Task<int> CreateItemAsync(HttpClient owner, int categoryId, string name, decimal? price = null)
    {
        var response = await owner.PostAsJsonAsync("/api/menu-items", price is { } p
            ? new { name, categoryId, kind = "AlaCarte", minPackageRank = (int?)null, price = p, isAvailable = true }
            : new { name, categoryId, kind = "Buffet", minPackageRank = (int?)1, price = 0m, isAvailable = true });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>ลูกค้าส่งหนึ่งรอบด้วย token (ไม่มี auth) แล้วคืน id ของรอบที่เพิ่งบันทึก</summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Where(o => o.TableSessionId == session.Id)
            .OrderByDescending(o => o.Number).Select(o => o.Id).FirstAsync());
    }

    private static object Line(int menuItemId, int quantity, string? note = null) =>
        new { menuItemId, quantity, note };

    private static async Task<JsonElement> BillAsync(HttpClient owner, int sessionId) =>
        (await OkAsync(await owner.GetAsync($"/api/sessions/{sessionId}"))).GetProperty("bill");

    private static Task<HttpResponseMessage> AdjustAsync(HttpClient client, int sessionId, object body) =>
        client.PostAsJsonAsync($"/api/sessions/{sessionId}/adjustments", body);

    private static Task<HttpResponseMessage> RemoveAsync(HttpClient client, int sessionId, int adjustmentId) =>
        client.DeleteAsync($"/api/sessions/{sessionId}/adjustments/{adjustmentId}");

    private static decimal Money(JsonElement bill, string field) => bill.GetProperty(field).GetDecimal();

    private static int LastAdjustmentId(JsonElement detail) =>
        detail.GetProperty("adjustments").EnumerateArray().Last().GetProperty("id").GetInt32();

    private static Task<List<BillAdjustment>> StoredAdjustmentsAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(db => db.BillAdjustments.AsNoTracking()
            .Where(a => a.TableSessionId == sessionId).OrderBy(a => a.Id).ToListAsync());

    private static Task SetStatusAsync(TestApp app, int sessionId, SessionStatus status) =>
        app.WithDbAsync(async db =>
        {
            // การชำระเงิน (ticket 18) ยังไม่มี — ตั้งสถานะด้วยข้อมูลเพื่อดูว่าบิลที่ปิดแล้วถูกล็อก
            await db.TableSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow));
            return 0;
        });

    // ------------------------------------------------------------------ ตัวคำนวณบิล

    /// <summary>
    /// design/DATA.md "Session A1": Gold 2 ผู้ใหญ่ ฿399, รอบที่ 1–3 ตามข้อมูล (รอบที่ 3 ยังรอรับ แต่ชาไทยของรอบนั้นนับแล้ว)
    /// บิล: ผู้ใหญ่ 2×฿399 = ฿798.00 · ชาไทยเย็น 2×฿45 = ฿90.00 · โค้ก 1×฿25 = ฿25.00 · subtotal ฿913.00
    /// · ค่าบริการ 10% ฿91.30 · VAT 7% ฿70.30 · รวม ฿1,074.60
    /// </summary>
    [Fact]
    public async Task Session_A1_from_DATA_reconciles_line_by_line()
    {
        using var app = new TestApp(postgres);
        await SetRatesAsync(app, 10m, 7m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, "Gold", adult: 399m, child: 199m);
        var categoryId = await CreateCategoryAsync(owner);

        var shoulder = await CreateItemAsync(owner, categoryId, "สันคอหมูสไลซ์");
        var beef = await CreateItemAsync(owner, categoryId, "เนื้อวัวสไลซ์");
        var soup = await CreateItemAsync(owner, categoryId, "น้ำซุปต้มยำน้ำข้น");
        var cabbage = await CreateItemAsync(owner, categoryId, "ผักกาดขาว");
        var tender = await CreateItemAsync(owner, categoryId, "หมูนุ่มสไลซ์");
        var shrimpBall = await CreateItemAsync(owner, categoryId, "ลูกชิ้นกุ้ง");
        var mushroom = await CreateItemAsync(owner, categoryId, "เห็ดรวม");
        var belly = await CreateItemAsync(owner, categoryId, "หมูสามชั้นสไลซ์");
        var thaiTea = await CreateItemAsync(owner, categoryId, "ชาไทยเย็น", 45m);
        var coke = await CreateItemAsync(owner, categoryId, "โค้ก", 25m);

        var session = await OpenSessionAsync(app, owner, package.Id, adults: 2);
        await SubmitAsync(app, session, Line(shoulder, 3), Line(beef, 2), Line(soup, 1), Line(cabbage, 2));
        await SubmitAsync(app, session, Line(tender, 3), Line(shrimpBall, 2), Line(mushroom, 1), Line(thaiTea, 1),
            Line(coke, 1));
        await SubmitAsync(app, session, Line(belly, 2, "ไม่ติดมัน"), Line(mushroom, 1), Line(thaiTea, 1));

        var detail = await OkAsync(await owner.GetAsync($"/api/sessions/{session.Id}"));
        var bill = detail.GetProperty("bill");

        // ค่าบุฟเฟ่ต์: ผู้ใหญ่ 2 × ฿399 = ฿798.00 (ไม่มีเด็ก)
        Assert.Equal(2, bill.GetProperty("adults").GetInt32());
        Assert.Equal(0, bill.GetProperty("children").GetInt32());
        Assert.Equal(399m, Money(bill, "adultPrice"));
        Assert.Equal(798.00m, Money(bill, "buffetTotal"));

        // เมนูคิดเงินแยก รวมตามชื่อ+ราคา ข้ามรอบ — บุฟเฟ่ต์ไม่โผล่ในบรรทัดเหล่านี้
        var lines = bill.GetProperty("alaCarteLines").EnumerateArray()
            .Select(l => (l.GetProperty("name").GetString(), l.GetProperty("quantity").GetInt32(),
                l.GetProperty("unitPrice").GetDecimal(), l.GetProperty("amount").GetDecimal()))
            .ToList();
        Assert.Equal([("ชาไทยเย็น", 2, 45m, 90.00m), ("โค้ก", 1, 25m, 25.00m)], lines);
        Assert.Equal(115.00m, Money(bill, "alaCarteTotal"));

        Assert.Empty(bill.GetProperty("adjustments").EnumerateArray());
        Assert.Equal(0m, Money(bill, "adjustmentTotal"));
        Assert.Equal(913.00m, Money(bill, "subtotal"));
        Assert.Equal(10m, Money(bill, "serviceChargePercent"));
        Assert.Equal(91.30m, Money(bill, "serviceCharge"));
        Assert.Equal(7m, Money(bill, "vatPercent"));
        Assert.Equal(70.30m, Money(bill, "vat"));
        Assert.Equal(1074.60m, Money(bill, "total"));

        // รายการของหน้ารายละเอียดยังแยกเป็นรอบ (รอบที่ 3 ยังรอรับ) และหมายเหตุอยู่กับบรรทัดของมัน
        var orders = detail.GetProperty("orders").EnumerateArray().ToList();
        Assert.Equal([1, 2, 3], orders.Select(o => o.GetProperty("number").GetInt32()));
        Assert.All(orders, o => Assert.Equal("Pending", o.GetProperty("status").GetString()));
        Assert.Contains(orders[2].GetProperty("items").EnumerateArray(),
            i => i.GetProperty("note").GetString() == "ไม่ติดมัน");
    }

    /// <summary>spec 64: ราคาบุฟเฟ่ต์ของโต๊ะล็อกตาม snapshot ตอนเปิด — แก้ราคาแพ็กเกจทีหลังกระทบเฉพาะรอบที่เปิดใหม่</summary>
    [Fact]
    public async Task Buffet_is_billed_from_the_prices_snapshotted_when_the_table_opened()
    {
        using var app = new TestApp(postgres);
        await SetRatesAsync(app, 0m, 0m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("Gold"), adult: 399m, child: 199m);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 2, children: 1);

        var update = await owner.PutAsJsonAsync($"/api/packages/{package.Id}", new
        {
            name = package.Name,
            rank = package.Rank,
            adultPrice = 459m,
            childPrice = 229m,
            durationMinutes = 90,
            isActive = true,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var bill = await BillAsync(owner, session.Id);
        Assert.Equal(399m, Money(bill, "adultPrice"));
        Assert.Equal(199m, Money(bill, "childPrice"));
        Assert.Equal(997.00m, Money(bill, "buffetTotal"));
        Assert.Equal(997.00m, Money(bill, "total"));

        // ราคาใหม่มีผลจริง — แต่กับรอบที่เปิดหลังแก้เท่านั้น
        var later = await OpenSessionAsync(app, owner, package.Id, adults: 2, children: 1);
        var laterBill = await BillAsync(owner, later.Id);
        Assert.Equal(459m, Money(laterBill, "adultPrice"));
        Assert.Equal(1147.00m, Money(laterBill, "buffetTotal"));
    }

    /// <summary>spec 63: ออเดอร์ที่ยกเลิกไม่ถูกคิดเงิน ทั้งบรรทัดเมนูคิดเงินแยกและยอดรวม</summary>
    [Fact]
    public async Task Cancelled_orders_are_not_billed()
    {
        using var app = new TestApp(postgres);
        await SetRatesAsync(app, 10m, 7m);
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("Gold"));
        var categoryId = await CreateCategoryAsync(owner);
        var beer = await CreateItemAsync(owner, categoryId, "เบียร์สิงห์", 89m);
        var coke = await CreateItemAsync(owner, categoryId, "โค้ก", 25m);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 2);

        await SubmitAsync(app, session, Line(beer, 2));
        var cancelled = await SubmitAsync(app, session, Line(beer, 1), Line(coke, 2));
        var cancel = await kitchen.PostAsJsonAsync($"/api/orders/{cancelled}/cancel", new { reason = "สั่งผิด/ซ้ำ" });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var bill = await BillAsync(owner, session.Id);
        var line = Assert.Single(bill.GetProperty("alaCarteLines").EnumerateArray());
        Assert.Equal("เบียร์สิงห์", line.GetProperty("name").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(178.00m, line.GetProperty("amount").GetDecimal());
        // 798 + 178 = 976.00 · SC 97.60 · VAT (1073.60 × 7%) = 75.152 → 75.15 · รวม 1148.75
        Assert.Equal(976.00m, Money(bill, "subtotal"));
        Assert.Equal(97.60m, Money(bill, "serviceCharge"));
        Assert.Equal(75.15m, Money(bill, "vat"));
        Assert.Equal(1148.75m, Money(bill, "total"));
    }

    /// <summary>spec: subtotal 1000.05 กับ SC 10% ได้ service 100.01 (100.005 ปัดแบบ AwayFromZero ไม่ใช่ banker's)</summary>
    [Fact]
    public async Task Service_charge_rounds_half_away_from_zero()
    {
        using var app = new TestApp(postgres);
        await SetRatesAsync(app, 10m, 0m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("P"), adult: 1000m, child: 0m);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 1);

        var detail = await OkAsync(await AdjustAsync(owner, session.Id,
            new { description = "ค่าน้ำแข็ง", amount = 0.05m }));

        var bill = detail.GetProperty("bill");
        Assert.Equal(1000.05m, Money(bill, "subtotal"));
        Assert.Equal(100.01m, Money(bill, "serviceCharge"));
        Assert.Equal(0m, Money(bill, "vat"));
        Assert.Equal(1100.06m, Money(bill, "total"));

        // VAT ปัดแบบเดียวกันบน (subtotal + service): (1000.05 + 100.01) × 7% = 77.0042 → 77.00
        await SetRatesAsync(app, 10m, 7m);
        var withVat = await BillAsync(owner, session.Id);
        Assert.Equal(100.01m, Money(withVat, "serviceCharge"));
        Assert.Equal(77.00m, Money(withVat, "vat"));
        Assert.Equal(1177.06m, Money(withVat, "total"));
    }

    // ------------------------------------------------------------------ ปรับยอด

    /// <summary>
    /// spec 60–61: ปรับยอดทั้งบวกและลบพร้อมเหตุผล ลบรายการที่ใส่ผิดได้ บิลในคำตอบคำนวณใหม่ทันที
    /// และเจ้าของร้าน/โต๊ะได้ <c>sessionChanged</c> หลังบันทึกสำเร็จ
    /// </summary>
    [Fact]
    public async Task Positive_and_negative_adjustments_change_the_bill_and_can_be_removed()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        await SetRatesAsync(app, 10m, 7m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("Gold"), adult: 399m, child: 199m);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 2, children: 1);

        // frame o04: ค่าปรับอาหารเหลือ +฿50.00 บนบุฟเฟ่ต์ ฿997
        var fine = await OkAsync(await AdjustAsync(owner, session.Id,
            new { description = "  ค่าปรับอาหารเหลือ  ", amount = 50m }));
        var fineBill = fine.GetProperty("bill");
        var fineRow = Assert.Single(fineBill.GetProperty("adjustments").EnumerateArray());
        Assert.Equal("ค่าปรับอาหารเหลือ", fineRow.GetProperty("description").GetString());
        Assert.Equal(50m, fineRow.GetProperty("amount").GetDecimal());
        Assert.Equal(50m, Money(fineBill, "adjustmentTotal"));
        Assert.Equal(1047.00m, Money(fineBill, "subtotal"));
        Assert.Single(fine.GetProperty("adjustments").EnumerateArray());

        // ส่วนลดเป็นยอดลบ: 1047 − 120 = 927.00 · SC 92.70 · VAT (1019.70 × 7%) = 71.379 → 71.38 · รวม 1091.08
        var discounted = await OkAsync(await AdjustAsync(owner, session.Id,
            new { description = "ส่วนลดสมาชิก", amount = -120m }));
        var bill = discounted.GetProperty("bill");
        Assert.Equal(["ค่าปรับอาหารเหลือ", "ส่วนลดสมาชิก"], bill.GetProperty("adjustments").EnumerateArray()
            .Select(a => a.GetProperty("description").GetString()));
        Assert.Equal(-70m, Money(bill, "adjustmentTotal"));
        Assert.Equal(927.00m, Money(bill, "subtotal"));
        Assert.Equal(92.70m, Money(bill, "serviceCharge"));
        Assert.Equal(71.38m, Money(bill, "vat"));
        Assert.Equal(1091.08m, Money(bill, "total"));

        // GET เห็นบิลเดียวกับคำตอบ และฐานข้อมูลเก็บทั้งสองรายการ
        Assert.Equal(1091.08m, Money(await BillAsync(owner, session.Id), "total"));
        var stored = await StoredAdjustmentsAsync(app, session.Id);
        Assert.Equal([50m, -120m], stored.Select(a => a.Amount));

        // ลบรายการที่ใส่ผิด → บิลกลับไปเหลือแค่ค่าปรับ
        var fineId = fine.GetProperty("adjustments")[0].GetProperty("id").GetInt32();
        var discountId = LastAdjustmentId(discounted);
        var removed = await OkAsync(await RemoveAsync(owner, session.Id, discountId));
        Assert.Equal(1047.00m, Money(removed.GetProperty("bill"), "subtotal"));
        Assert.Equal([fineId], (await StoredAdjustmentsAsync(app, session.Id)).Select(a => a.Id));

        // ลบซ้ำ = ไม่พบแล้ว (อีกเครื่องลบไปก่อน)
        await AssertRefusedAsync(await RemoveAsync(owner, session.Id, discountId),
            HttpStatusCode.NotFound, ErrorCodes.ValidationFailed);

        // ทุกการเปลี่ยนที่สำเร็จประกาศ sessionChanged ของรอบนี้ — การปฏิเสธไม่ประกาศ
        var changes = notifier.Events.Where(e => e.Event == "sessionChanged")
            .Select(e => (SessionSummaryDto)e.Payload).Where(s => s.Id == session.Id).ToList();
        Assert.Equal(1 + 3, changes.Count); // เปิดโต๊ะ + เพิ่ม 2 + ลบ 1
        Assert.All(changes, s => Assert.Equal("Open", s.Status));
    }

    /// <summary>spec 62: ห้ามทำให้ subtotal ติดลบ ทั้งตอนเพิ่มส่วนลดและตอนลบรายการบวกที่ส่วนลดพึ่งอยู่</summary>
    [Fact]
    public async Task An_adjustment_that_would_make_the_subtotal_negative_is_refused()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        await SetRatesAsync(app, 10m, 7m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("S"), adult: 299m, child: 149m);
        var session = await OpenSessionAsync(app, owner, package.Id, adults: 1);

        await AssertRefusedAsync(await AdjustAsync(owner, session.Id, new { description = "ส่วนลด", amount = -299.01m }),
            (HttpStatusCode)422, ErrorCodes.NegativeSubtotal);
        Assert.Empty(await StoredAdjustmentsAsync(app, session.Id));

        // ลดได้พอดีศูนย์
        var zero = await OkAsync(await AdjustAsync(owner, session.Id, new { description = "ฟรีทั้งโต๊ะ", amount = -299m }));
        Assert.Equal(0m, Money(zero.GetProperty("bill"), "subtotal"));
        Assert.Equal(0m, Money(zero.GetProperty("bill"), "total"));

        // +100 แล้ว −100: subtotal ยังเป็น 0 — การลบ +100 ทิ้งจะทำให้ติดลบ จึงถูกปฏิเสธ
        var plus = await OkAsync(await AdjustAsync(owner, session.Id, new { description = "ค่าน้ำ", amount = 100m }));
        var plusId = LastAdjustmentId(plus);
        await OkAsync(await AdjustAsync(owner, session.Id, new { description = "ส่วนลดค่าน้ำ", amount = -100m }));
        await AssertRefusedAsync(await RemoveAsync(owner, session.Id, plusId),
            (HttpStatusCode)422, ErrorCodes.NegativeSubtotal);
        Assert.Equal(3, (await StoredAdjustmentsAsync(app, session.Id)).Count);
        Assert.Equal(0m, Money(await BillAsync(owner, session.Id), "subtotal"));

        Assert.Equal(1 + 3, notifier.Events.Count(e =>
            e.Event == "sessionChanged" && ((SessionSummaryDto)e.Payload).Id == session.Id));
    }

    /// <summary>
    /// ปรับยอดได้เฉพาะ session Open: บิลที่ชำระแล้วหรือโต๊ะที่ยกเลิกถูกปฏิเสธด้วย <c>SESSION_CLOSED</c>
    /// ส่วนรอบที่เลย EndsAt แล้ว (ปิดการสั่ง) ยังปรับยอดได้ตาม spec
    /// </summary>
    [Fact]
    public async Task Adjustments_need_an_open_bill_but_not_open_ordering()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("G"));

        var expired = await OpenSessionAsync(app, owner, package.Id);
        await app.WithDbAsync(async db =>
        {
            var past = DateTimeOffset.UtcNow.AddMinutes(-17);
            await db.TableSessions.Where(s => s.Id == expired.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OpenedAt, past.AddMinutes(-90))
                .SetProperty(x => x.EndsAt, past));
            return 0;
        });
        var late = await OkAsync(await AdjustAsync(owner, expired.Id, new { description = "ค่าปรับอาหารเหลือ", amount = 50m }));
        Assert.Equal(848m, Money(late.GetProperty("bill"), "subtotal"));

        var paid = await OpenSessionAsync(app, owner, package.Id);
        var paidAdjustment = LastAdjustmentId(await OkAsync(await AdjustAsync(owner, paid.Id,
            new { description = "ค่าปรับอาหารเหลือ", amount = 50m })));
        await SetStatusAsync(app, paid.Id, SessionStatus.Paid);
        await AssertRefusedAsync(await AdjustAsync(owner, paid.Id, new { description = "ส่วนลด", amount = -20m }),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        await AssertRefusedAsync(await RemoveAsync(owner, paid.Id, paidAdjustment),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Single(await StoredAdjustmentsAsync(app, paid.Id));

        var voided = await OpenSessionAsync(app, owner, package.Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/sessions/{voided.Id}/void", null)).StatusCode);
        await AssertRefusedAsync(await AdjustAsync(owner, voided.Id, new { description = "ส่วนลด", amount = -20m }),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        Assert.Empty(await StoredAdjustmentsAsync(app, voided.Id));
    }

    [Fact]
    public async Task Adjustment_input_is_checked_and_only_the_owner_may_adjust()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("G"));
        var session = await OpenSessionAsync(app, owner, package.Id);

        foreach (var body in new object[]
                 {
                     new { description = "", amount = 10m },
                     new { description = "   ", amount = 10m },
                     new { description = new string('ก', 101), amount = 10m },
                     new { description = "ส่วนลด", amount = 0m },
                     new { description = "ส่วนลด", amount = (decimal?)null },
                     new { description = "ส่วนลด", amount = -1.005m },
                     new { description = "ค่าปรับ", amount = 100_000.01m },
                 })
        {
            await AssertRefusedAsync(await AdjustAsync(owner, session.Id, body),
                HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        }

        Assert.Empty(await StoredAdjustmentsAsync(app, session.Id));
        // 100 ตัวอักษรพอดียังรับ
        await OkAsync(await AdjustAsync(owner, session.Id, new { description = new string('ก', 100), amount = 1m }));

        await AssertRefusedAsync(await AdjustAsync(owner, int.MaxValue, new { description = "ส่วนลด", amount = -1m }),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        // รายการของอีกโต๊ะลบผ่านโต๊ะนี้ไม่ได้ และยังอยู่ครบ
        var other = await OpenSessionAsync(app, owner, package.Id);
        var otherAdjustment = LastAdjustmentId(await OkAsync(await AdjustAsync(owner, other.Id,
            new { description = "ค่าปรับอาหารเหลือ", amount = 50m })));
        await AssertRefusedAsync(await RemoveAsync(owner, session.Id, otherAdjustment),
            HttpStatusCode.NotFound, ErrorCodes.ValidationFailed);
        Assert.Single(await StoredAdjustmentsAsync(app, other.Id));

        // ครัวไม่เห็นข้อมูลการเงิน และไม่ล็อกอิน = 401
        var kitchen = await KitchenClientAsync(app);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await AdjustAsync(kitchen, session.Id, new { description = "ส่วนลด", amount = -1m })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RemoveAsync(kitchen, other.Id, otherAdjustment)).StatusCode);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await AdjustAsync(anonymous, session.Id, new { description = "ส่วนลด", amount = -1m })).StatusCode);
        Assert.Single(await StoredAdjustmentsAsync(app, other.Id));
    }

    /// <summary>ไม่มีบิลฝั่งลูกค้า: ข้อมูล session ที่ token เห็นไม่มียอด บรรทัดบิล หรือการปรับยอดเลย</summary>
    [Fact]
    public async Task The_customer_view_of_a_table_carries_no_bill()
    {
        using var app = new TestApp(postgres);
        await SetRatesAsync(app, 10m, 7m);
        var owner = await OwnerClientAsync(app);
        var package = await SeedPackageAsync(app, UniqueName("G"));
        var session = await OpenSessionAsync(app, owner, package.Id);
        await OkAsync(await AdjustAsync(owner, session.Id, new { description = "ค่าปรับอาหารเหลือ", amount = 50m }));

        using var customer = app.CreateClient();
        var raw = await (await customer.GetAsync($"/api/s/{session.Token}")).Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
        foreach (var money in new[] { "bill", "total", "subtotal", "adjustments", "servicecharge", "vat", "payment" })
        {
            Assert.DoesNotContain(names, n => n.Contains(money));
        }

        Assert.DoesNotContain("ค่าปรับอาหารเหลือ", raw);
    }
}
