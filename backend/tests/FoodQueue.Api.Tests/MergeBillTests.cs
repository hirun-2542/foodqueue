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

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 22 — รวมบิลหลายโต๊ะ ชำระครั้งเดียว ผ่าน seam 1 (แอปจริง + Postgres จริง + hub จริง)
/// <c>POST /api/sessions/{id}/merge {"sessionIds":[...]}</c> (OwnerOnly) ผูก session ที่ยัง Open อื่นเข้าบิลหลัก
///
/// ตัวเลขตั้งต้นคือตัวอย่างใน mockup o19 (และ DATA.md):
/// A1 Gold 2 ผู้ใหญ่ = บุฟเฟ่ต์ ฿798.00 + ชาไทยเย็น 2 × ฿45 + โค้ก 1 × ฿25 → subtotal ฿913.00
/// A2 Silver 3 ผู้ใหญ่ 1 เด็ก = ฿897.00 + ฿149.00 + โค้ก 2 × ฿25 → subtotal ฿1,096.00
/// รวม ฿2,009.00 · ค่าบริการ 10% ฿200.90 · VAT 7% ฿154.69 · รวมทั้งสิ้น ฿2,364.59
///
/// ทุกข้อพิสูจน์จากสิ่งที่ผู้ใช้เห็น: HTTP + code + ข้อมูลที่ตอบ + event ที่ไคลเอนต์ได้รับ + แถวในฐานข้อมูล
/// </summary>
[Collection(ApiCollection.Name)]
public partial class MergeBillTests(PostgresFixture postgres)
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
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"คาดว่าสำเร็จ แต่ได้ {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
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

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..14];

    /// <summary>SC 10% / VAT 7% ตาม DATA และลิมิตต่อรอบตามค่าเริ่มต้น</summary>
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

    /// <summary>แพ็กเกจตาม DATA: Silver rank 1 (฿299/฿149) และ Gold rank 2 (฿399/฿199)</summary>
    private sealed record Packages(int SilverId, int GoldId);

    private static Task<Packages> SeedPackagesAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var ranks = Enumerable.Range(1, 99).Where(r => !taken.Contains(r)).Take(2).ToList();
            var silver = new Package
            {
                Name = "Silver", Rank = ranks[0], AdultPrice = 299m, ChildPrice = 149m, DurationMinutes = 90,
            };
            var gold = new Package
            {
                Name = "Gold", Rank = ranks[1], AdultPrice = 399m, ChildPrice = 199m, DurationMinutes = 90,
            };
            db.Packages.AddRange(silver, gold);
            await db.SaveChangesAsync();
            return new Packages(silver.Id, gold.Id);
        });

    private sealed record OpenedSession(int Id, string Token, string TableName);

    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner, int packageId,
        int adults, int children)
    {
        var name = UniqueName("T");
        var table = await owner.PostAsJsonAsync("/api/tables", new { name, seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, table.StatusCode);
        var tableId = (await JsonAsync(table)).GetProperty("id").GetInt32();

        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = adults, childCount = children });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        using var doc = JsonDocument.Parse(text);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, name);
    }

    /// <summary>เมนูผ่านเส้นทางจริง: หมูสามชั้น (บุฟเฟ่ต์), ชาไทยเย็น ฿45 และโค้ก ฿25 (คิดเงินแยก)</summary>
    private sealed record Menu(int MeatId, int TeaId, int CokeId);

    private static async Task<Menu> CreateMenuAsync(HttpClient owner)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        async Task<int> ItemAsync(string name, string kind, int? minRank, decimal price)
        {
            var response = await owner.PostAsJsonAsync("/api/menu-items",
                new { name, categoryId, kind, minPackageRank = minRank, price, isAvailable = true });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await JsonAsync(response)).GetProperty("id").GetInt32();
        }

        return new Menu(
            await ItemAsync(UniqueName("หมูสามชั้น"), "Buffet", 1, 0m),
            await ItemAsync(UniqueName("ชาไทยเย็น"), "AlaCarte", null, 45m),
            await ItemAsync(UniqueName("โค้ก"), "AlaCarte", null, 25m));
    }

    private static object Line(int menuItemId, int quantity) => new { menuItemId, quantity, note = (string?)null };

    /// <summary>ลูกค้าส่งหนึ่งรอบด้วย token แล้วคืน id ของรอบนั้น (จาก <c>Location</c> ของคำตอบ 201)</summary>
    private static async Task<int> SubmitAsync(TestApp app, OpenedSession session, params object[] items)
    {
        using var customer = app.CreateClient();
        var response = await customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        return int.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
    }

    private static Task<HttpResponseMessage> SubmitRawAsync(HttpClient customer, OpenedSession session,
        params object[] items) =>
        customer.PostAsJsonAsync($"/api/s/{session.Token}/orders", new { items });

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, int sessionId, string method,
        decimal? amountReceived = null) =>
        client.PostAsJsonAsync($"/api/sessions/{sessionId}/payment", new { method, amountReceived });

    private static Task<HttpResponseMessage> MergeAsync(HttpClient client, int primaryId, params int[] others) =>
        client.PostAsJsonAsync($"/api/sessions/{primaryId}/merge", new { sessionIds = others });

    private static async Task<JsonElement> DetailAsync(HttpClient owner, int sessionId) =>
        await OkAsync(await owner.GetAsync($"/api/sessions/{sessionId}"));

    private static Task<TableSession> StoredAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(db => db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

    private static Task<List<Payment>> StoredPaymentsAsync(TestApp app) =>
        app.WithDbAsync(db => db.Payments.AsNoTracking().ToListAsync());

    // ------------------------------------------------------------------ โลกของ o19

    /// <summary>
    /// สองโต๊ะของตัวอย่าง mockup o19: A1 = Gold 2 ผู้ใหญ่ (บุฟเฟ่ต์ ฿798 + ชาไทย 2 + โค้ก 1 = ฿913.00),
    /// A2 = Silver 3 ผู้ใหญ่ 1 เด็ก (฿897 + ฿149 + โค้ก 2 = ฿1,096.00)
    /// </summary>
    private sealed record World(TestApp App, HttpClient Owner, Menu Menu, OpenedSession A1, OpenedSession A2);

    private async Task<World> WorldAsync(TestApp app)
    {
        await SetRatesAsync(app);
        var owner = await OwnerClientAsync(app);
        var packages = await SeedPackagesAsync(app);
        var menu = await CreateMenuAsync(owner);
        var a1 = await OpenSessionAsync(app, owner, packages.GoldId, adults: 2, children: 0);
        var a2 = await OpenSessionAsync(app, owner, packages.SilverId, adults: 3, children: 1);

        // A1: บุฟเฟ่ต์ 2 × ฿399 = ฿798.00 + ชาไทยเย็น 2 × ฿45 = ฿90.00 + โค้ก ฿25.00 → ฿913.00
        var a1Round = await SubmitAsync(app, a1, Line(menu.MeatId, 2), Line(menu.TeaId, 2), Line(menu.CokeId, 1));
        await AdvanceAsync(owner, a1Round, "Preparing");
        await AdvanceAsync(owner, a1Round, "Ready");

        // A2: บุฟเฟ่ต์ 3 × ฿299 + 1 × ฿149 = ฿1,046.00 + โค้ก 2 × ฿25 = ฿50.00 → ฿1,096.00
        var a2Round = await SubmitAsync(app, a2, Line(menu.MeatId, 3), Line(menu.CokeId, 2));
        await AdvanceAsync(owner, a2Round, "Preparing");
        await AdvanceAsync(owner, a2Round, "Ready");

        return new World(app, owner, menu, a1, a2);
    }

    private static async Task AdvanceAsync(HttpClient staff, int orderId, string status) =>
        await OkAsync(await staff.PostAsJsonAsync($"/api/orders/{orderId}/status", new { status }));

    /// <summary>ยอดของรอบเดียวก่อนรวม — ใช้ยืนยันว่าตัวเลขตั้งต้นตรงกับ mockup ก่อนคิดบิลรวม</summary>
    private static (decimal Subtotal, decimal Service, decimal Vat, decimal Total) Bill(JsonElement session)
    {
        var bill = session.GetProperty("bill");
        return (bill.GetProperty("subtotal").GetDecimal(), bill.GetProperty("serviceCharge").GetDecimal(),
            bill.GetProperty("vat").GetDecimal(), bill.GetProperty("total").GetDecimal());
    }

    // ------------------------------------------------------------------ ตัวเลขของ mockup o19

    [Fact]
    public async Task The_merged_bill_matches_the_o19_frame_number_for_number()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        // ก่อนรวม: สองบิลแยกกัน ฿913.00 และ ฿1,096.00 พร้อม SC/VAT ของตัวเอง
        Assert.Equal((913.00m, 91.30m, 70.30m, 1074.60m), Bill(await DetailAsync(world.Owner, world.A1.Id)));
        Assert.Equal((1096.00m, 109.60m, 84.39m, 1289.99m), Bill(await DetailAsync(world.Owner, world.A2.Id)));

        var merged = await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        // บิลรวมของ A1: subtotal ฿2,009.00 · ค่าบริการ ฿200.90 · VAT ฿154.69 · รวมทั้งสิ้น ฿2,364.59
        Assert.Equal((2009.00m, 200.90m, 154.69m, 2364.59m), Bill(merged));
        // ลูกค้ารวมทุก session (5 ผู้ใหญ่ 1 เด็ก) และค่าบุฟเฟ่ต์คือผลรวมของทุกรอบ ฿1,844.00
        // (A1 2 × ฿399 + A2 3 × ฿299 + ฿149) — ราคาต่อหัวที่แสดงคือของบิลหลัก (Gold)
        var bill = merged.GetProperty("bill");
        Assert.Equal(5, bill.GetProperty("adults").GetInt32());
        Assert.Equal(1, bill.GetProperty("children").GetInt32());
        Assert.Equal(399m, bill.GetProperty("adultPrice").GetDecimal());
        Assert.Equal(1844.00m, bill.GetProperty("buffetTotal").GetDecimal());

        // รายการของทั้งสองรอบอยู่ในบิลเดียว และบรรทัดที่ซ้ำกันถูกยุบรวม
        var tea = bill.GetProperty("alaCarteLines").EnumerateArray()
            .Single(l => l.GetProperty("unitPrice").GetDecimal() == 45m);
        Assert.Equal(2, tea.GetProperty("quantity").GetInt32());
        Assert.Equal(90m, tea.GetProperty("amount").GetDecimal());
        var coke = bill.GetProperty("alaCarteLines").EnumerateArray()
            .Single(l => l.GetProperty("unitPrice").GetDecimal() == 25m);
        Assert.Equal(3, coke.GetProperty("quantity").GetInt32());
        Assert.Equal(75m, coke.GetProperty("amount").GetDecimal());

        // รอบที่ถูกรวมไม่มีความเปลี่ยนแปลงในฐานข้อมูลนอกจากตัวชี้ไปบิลหลัก — ยัง Open และยังถือออเดอร์ของตัวเอง
        var child = await StoredAsync(app, world.A2.Id);
        Assert.Equal(SessionStatus.Open, child.Status);
        Assert.Equal(world.A1.Id, child.MergedIntoSessionId);

        // หน้าจอของรอบที่ถูกรวมบอกได้ว่าบิลของมันไปรวมที่ไหน แต่ยังแสดงยอดของตัวเอง
        var childDetail = await DetailAsync(world.Owner, world.A2.Id);
        Assert.Equal(world.A1.Id, childDetail.GetProperty("mergedIntoSessionId").GetInt32());
        Assert.Equal(1096.00m, childDetail.GetProperty("bill").GetProperty("subtotal").GetDecimal());

        // โต๊ะหลักแสดงยอดรวมบนผังโต๊ะผ่าน seam เดียวกัน
        var overview = await OkAsync(await world.Owner.GetAsync("/api/tables/overview"));
        var hostCard = overview.EnumerateArray().Single(t => t.GetProperty("name").GetString() == world.A1.TableName);
        Assert.Equal(2364.59m, hostCard.GetProperty("runningTotal").GetDecimal());
    }

    /// <summary>
    /// หน้าต่าง "รวมบิล" (frame o19) ต้องเห็นตัวเลขเดียวกับบิลจริงก่อนกดยืนยัน: ตัวอย่างบิลรวมของ A1 + A2
    /// มาจาก BillCalculator ตัวเดียวกับที่ใช้ตอนอ่านบิล จึงได้ ฿2,009.00 / ฿200.90 / ฿154.69 / ฿2,364.59 เท่ากัน
    /// และไม่มีอะไรถูกเขียนโดยการขอดูตัวอย่าง
    /// </summary>
    [Fact]
    public async Task The_dialog_preview_shows_the_merged_bill_before_confirming_and_writes_nothing()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        var preview = await OkAsync(
            await world.Owner.PostAsJsonAsync($"/api/sessions/{world.A1.Id}/merge/preview",
                new { sessionIds = new[] { world.A2.Id } }));

        Assert.Equal(world.A1.Id, preview.GetProperty("primarySessionId").GetInt32());
        Assert.Equal(world.A1.TableName, preview.GetProperty("primaryTableName").GetString());
        Assert.Equal(913.00m, preview.GetProperty("primarySubtotal").GetDecimal());
        Assert.Equal(1074.60m, preview.GetProperty("primaryTotal").GetDecimal());

        // โต๊ะที่เลือกอยู่กับยอดของแต่ละโต๊ะ และโต๊ะที่เลือกได้ที่เหลือ (ยังไม่ติ๊กอะไร = ว่าง)
        var chosen = Assert.Single(preview.GetProperty("mergeable").EnumerateArray());
        Assert.Equal(world.A2.TableName, chosen.GetProperty("tableName").GetString());
        Assert.Equal(1096.00m, chosen.GetProperty("subtotal").GetDecimal());
        Assert.Empty(preview.GetProperty("candidates").EnumerateArray());

        Assert.Equal((2009.00m, 200.90m, 154.69m, 2364.59m), Bill(preview));

        // ขอดูตัวอย่างไม่เขียนอะไร: ยังไม่มีรอบใดถูกรวมและไม่มี Payment
        Assert.Null((await StoredAsync(app, world.A1.Id)).MergedIntoSessionId);
        Assert.Null((await StoredAsync(app, world.A2.Id)).MergedIntoSessionId);
        Assert.Empty(await StoredPaymentsAsync(app));

        // ยังไม่ติ๊กอะไร = บิลตัวอย่างคือบิลของรอบนี้ตามลำพัง และ A2 รออยู่ในรายการให้เลือก
        var none = await OkAsync(
            await world.Owner.PostAsJsonAsync($"/api/sessions/{world.A1.Id}/merge/preview",
                new { sessionIds = Array.Empty<int>() }));
        Assert.Equal(913.00m, none.GetProperty("bill").GetProperty("subtotal").GetDecimal());
        Assert.Empty(none.GetProperty("mergeable").EnumerateArray());
        Assert.Equal(world.A2.TableName,
            Assert.Single(none.GetProperty("candidates").EnumerateArray()).GetProperty("tableName").GetString());
    }

    [Fact]
    public async Task Merging_emits_sessionChanged_for_every_round_involved()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        await using var ownerLive = await LiveClient.ConnectAsync(app,
            cookie: await LiveClient.AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var childLive = await LiveClient.ConnectAsync(app, token: world.A2.Token);

        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        await LiveClient.BarrierAsync(app, ownerLive, childLive);
        var host = Assert.Single(ownerLive.Inbox.Of("sessionChanged"),
            e => e.GetProperty("id").GetInt32() == world.A1.Id);
        Assert.Equal("Open", host.GetProperty("status").GetString());
        // รอบที่ถูกรวมได้ event ด้วย เพื่อให้จอของลูกค้ารอบนั้นรู้ว่าบิลไปรวมที่ไหน
        Assert.Single(childLive.Inbox.Of("sessionChanged"), e => e.GetProperty("id").GetInt32() == world.A2.Id);
    }

    // ------------------------------------------------------------------ กฎการรวม

    [Fact]
    public async Task Merging_is_not_reversible_and_a_child_cannot_be_merged_again()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var third = await OpenSessionAsync(app, world.Owner,
            (await StoredAsync(app, world.A1.Id)).PackageId, adults: 2, children: 0);

        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        // รอบที่เป็นลูกของบิลหนึ่งแล้ว: รวมเข้าบิลอื่นอีกไม่ได้
        await AssertRefusedAsync(await MergeAsync(world.Owner, third.Id, world.A2.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        // …และมันเป็นเจ้าของบิลไม่ได้ขณะที่ตัวเองเป็นลูกอยู่
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A2.Id, third.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        // รอบที่เป็นเจ้าของบิลรวมอยู่: ยกไปรวมกับบิลอื่นไม่ได้
        await AssertRefusedAsync(await MergeAsync(world.Owner, third.Id, world.A1.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);

        // ไม่มีอะไรเปลี่ยน: A1 ← A2 ยังคงเดิม และ third ยังเป็นบิลของตัวเองที่ไม่ถูกรวม
        Assert.Equal(world.A1.Id, (await StoredAsync(app, world.A2.Id)).MergedIntoSessionId);
        Assert.Null((await StoredAsync(app, third.Id)).MergedIntoSessionId);
        Assert.Equal(2364.59m, Bill(await DetailAsync(world.Owner, world.A1.Id)).Total);
    }

    [Fact]
    public async Task A_paid_voided_or_unknown_round_cannot_be_merged()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        // รอบที่ชำระแล้ว → SESSION_CLOSED (ทั้งในฐานะบิลหลักและในฐานะรอบที่ถูกรวม)
        await OkAsync(await PayAsync(world.Owner, world.A2.Id, "Card"));
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A2.Id, world.A1.Id),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);

        // รอบที่ถูกยกเลิก → SESSION_CLOSED เช่นกัน
        var voided = await OpenSessionAsync(app, world.Owner, (await StoredAsync(app, world.A1.Id)).PackageId, 2, 0);
        await OkAsync(await world.Owner.PostAsync($"/api/sessions/{voided.Id}/void", null));
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, voided.Id),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);

        // รอบที่ไม่มีอยู่ → SESSION_NOT_FOUND
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, 987654),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        // ทุกคำขอที่ถูกปฏิเสธไม่ทิ้งร่องรอย: ไม่มีรอบใดถูกรวมและบิลของ A1 ยังเป็นของตัวเอง
        Assert.Null((await StoredAsync(app, world.A1.Id)).MergedIntoSessionId);
        Assert.Equal(1074.60m, Bill(await DetailAsync(world.Owner, world.A1.Id)).Total);
    }

    [Fact]
    public async Task A_round_cannot_be_merged_into_itself_and_the_request_is_checked()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, world.A1.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id, world.A2.Id),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await MergeAsync(world.Owner, world.A1.Id, 0),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        await AssertRefusedAsync(await MergeAsync(world.Owner, 987654, world.A2.Id),
            HttpStatusCode.NotFound, ErrorCodes.SessionNotFound);

        Assert.Null((await StoredAsync(app, world.A1.Id)).MergedIntoSessionId);
        Assert.Null((await StoredAsync(app, world.A2.Id)).MergedIntoSessionId);
    }

    [Fact]
    public async Task Only_the_owner_may_merge()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);

        var kitchen = await KitchenClientAsync(app);
        Assert.Equal(HttpStatusCode.Forbidden, (await MergeAsync(kitchen, world.A1.Id, world.A2.Id)).StatusCode);

        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await MergeAsync(anonymous, world.A1.Id, world.A2.Id)).StatusCode);

        Assert.Null((await StoredAsync(app, world.A2.Id)).MergedIntoSessionId);
    }

    // ------------------------------------------------------------------ ออเดอร์หลังรวม และการชำระครั้งเดียว

    [Fact]
    public async Task A_merged_round_keeps_ordering_through_its_own_qr_and_the_round_lands_in_the_merged_bill()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        // A2 ยังสั่งผ่าน QR เดิมได้ตาม EndsAt ของตัวเอง (รอบของมันยัง Open)
        using var customer = app.CreateClient();
        var late = await SubmitRawAsync(customer, world.A2, Line(world.Menu.TeaId, 1));
        var text = await late.Content.ReadAsStringAsync();
        Assert.True(late.StatusCode == HttpStatusCode.Created, text);
        var orderId = int.Parse(late.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);

        // ออเดอร์ใหม่เข้าบิลรวมทันที (ชาไทยเย็นอีก ฿45 → subtotal ฿2,054.00 · ค่าบริการ ฿205.40 · VAT ฿158.16 · รวม ฿2,417.56)
        var mergedBill = Bill(await DetailAsync(world.Owner, world.A1.Id));
        Assert.Equal(2054.00m, mergedBill.Subtotal);
        Assert.Equal(205.40m, mergedBill.Service);
        Assert.Equal(158.16m, mergedBill.Vat);
        Assert.Equal(2417.56m, mergedBill.Total);

        // รอบที่สั่งจาก A2 ปรากฏในรายการของบิลหลัก และยังเป็นออเดอร์ของ session A2
        var orders = (await DetailAsync(world.Owner, world.A1.Id)).GetProperty("orders").EnumerateArray()
            .ToList();
        Assert.Contains(orders, o => o.GetProperty("id").GetInt32() == orderId);
        Assert.Equal(world.A2.Id, orders.Single(o => o.GetProperty("id").GetInt32() == orderId)
            .GetProperty("sessionId").GetInt32());

        // ครัวยังทำงานค้างอยู่ → ยังปิดบิลไม่ได้ และข้อความบอกโต๊ะของรอบที่ค้าง (A2 ส่งรอบใหม่เป็นรอบที่ 2
        // ขณะที่เลขรอบของ A1 ก็มีรอบที่ 1 ของตัวเอง — การระบุโต๊ะจึงจำเป็น)
        var refused = await AssertRefusedAsync(await PayAsync(world.Owner, world.A1.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.OrdersInProgress);
        Assert.Contains($"โต๊ะ {world.A2.TableName} รอบที่ 2 (รอรับ)", refused.GetProperty("message").GetString());
        var named = Assert.Single(refused.GetProperty("orders").EnumerateArray());
        Assert.Equal(orderId, named.GetProperty("id").GetInt32());
        Assert.Equal(world.A2.TableName, named.GetProperty("tableName").GetString());

        // ครัวทำจนเสร็จแล้วบิลรวมปิดได้
        await AdvanceAsync(world.Owner, orderId, "Preparing");
        await AdvanceAsync(world.Owner, orderId, "Ready");
        Assert.Equal("Paid", (await OkAsync(await PayAsync(world.Owner, world.A1.Id, "Card")))
            .GetProperty("status").GetString());
    }
    /// <summary>
    /// ticket 19: บิลที่ถูกรวมชำระด้วย PromptPay ได้ผ่านเส้นทางเดียวกัน — ไม่มีการรับเงินที่หน้าเคาน์เตอร์
    /// (รับเท่ากับยอดรวมพอดี เงินทอน 0) และออกใบเสร็จใบเดียวให้ยอดรวมของทุกรอบ
    /// </summary>
    [Fact]
    public async Task A_merged_bill_can_be_paid_by_promptpay_with_one_receipt_and_nothing_tendered()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        // PromptPay: ค่าที่ส่งมากับ amountReceived ไม่มีความหมาย รับเท่ากับยอดรวมของบิลที่รวมแล้วเสมอ
        var paid = await OkAsync(await PayAsync(world.Owner, world.A1.Id, "PromptPay", 9999m));

        var payment = paid.GetProperty("payment");
        Assert.Equal("PromptPay", payment.GetProperty("method").GetString());
        Assert.Matches(ReceiptFormat(), payment.GetProperty("receiptNo").GetString());
        Assert.Equal(2364.59m, payment.GetProperty("total").GetDecimal());
        Assert.Equal(2364.59m, payment.GetProperty("amountReceived").GetDecimal());
        Assert.Equal(0m, payment.GetProperty("change").GetDecimal());

        // ใบเสร็จใบเดียวที่รอบหลัก และทุกรอบที่รวมเป็น Paid พร้อมกัน
        var stored = Assert.Single(await StoredPaymentsAsync(app));
        Assert.Equal(world.A1.Id, stored.TableSessionId);
        Assert.Equal(PaymentMethod.PromptPay, stored.Method);
        Assert.Equal(SessionStatus.Paid, (await StoredAsync(app, world.A1.Id)).Status);
        Assert.Equal(SessionStatus.Paid, (await StoredAsync(app, world.A2.Id)).Status);

        // รอบลูกใช้ token เดิมสั่งเพิ่มไม่ได้
        using var childCustomer = app.CreateClient();
        await AssertRefusedAsync(await SubmitRawAsync(childCustomer, world.A2, Line(world.Menu.MeatId, 1)),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
    }

    [Fact]
    public async Task Paying_once_makes_every_merged_round_paid_with_one_receipt_and_frees_every_table()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        await using var ownerLive = await LiveClient.ConnectAsync(app,
            cookie: await LiveClient.AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var childLive = await LiveClient.ConnectAsync(app, token: world.A2.Token);

        var paid = await OkAsync(await PayAsync(world.Owner, world.A1.Id, "Cash", 2400m));

        // ใบเสร็จใบเดียวที่ session หลัก ออกให้ยอดรวมทั้งบิล
        var payment = paid.GetProperty("payment");
        var receipt = payment.GetProperty("receiptNo").GetString()!;
        Assert.Matches(ReceiptFormat(), receipt);
        Assert.Equal(2364.59m, payment.GetProperty("total").GetDecimal());
        Assert.Equal(35.41m, payment.GetProperty("change").GetDecimal());
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
        var storedPayments = await StoredPaymentsAsync(app);
        Assert.Single(storedPayments);
        Assert.Equal(world.A1.Id, storedPayments[0].TableSessionId);

        // ทุกรอบที่รวมเป็น Paid พร้อมกัน ตอนปิดเท่ากัน และมีใบเสร็จเพียงใบเดียวในระบบ
        var host = await StoredAsync(app, world.A1.Id);
        var child = await StoredAsync(app, world.A2.Id);
        Assert.Equal(SessionStatus.Paid, host.Status);
        Assert.Equal(SessionStatus.Paid, child.Status);
        Assert.NotNull(host.ClosedAt);
        Assert.NotNull(child.ClosedAt);
        Assert.Equal(host.ClosedAt, child.ClosedAt);

        // token ทุกอันสั่งไม่ได้ (ทั้งของบิลหลักและของรอบที่ถูกรวม)
        using var hostCustomer = app.CreateClient();
        using var childCustomer = app.CreateClient();
        await AssertRefusedAsync(await SubmitRawAsync(hostCustomer, world.A1, Line(world.Menu.MeatId, 1)),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        await AssertRefusedAsync(await SubmitRawAsync(childCustomer, world.A2, Line(world.Menu.MeatId, 1)),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        await AssertRefusedAsync(await childCustomer.GetAsync($"/api/s/{world.A2.Token}"),
            HttpStatusCode.Conflict, ErrorCodes.SessionClosed);
        await LiveClient.AssertRejectedAsync(app, token: world.A2.Token);

        // โต๊ะทั้งหมดว่างบนผัง (ทั้งโต๊ะหลักและโต๊ะที่ถูกรวม)
        var overview = await OkAsync(await world.Owner.GetAsync("/api/tables/overview"));
        foreach (var name in new[] { world.A1.TableName, world.A2.TableName })
        {
            Assert.True(!RowFor(overview, name).TryGetProperty("sessionId", out var sid)
                        || sid.ValueKind == JsonValueKind.Null);
        }

        // ทั้งสองรอบได้ sessionClosed และจอของรอบที่ถูกรวมถูกตัดการเชื่อมต่อ
        await childLive.Inbox.WaitForAsync("sessionClosed",
            e => e.GetProperty("sessionId").GetInt32() == world.A2.Id, LiveClient.Within);
        Assert.Equal(HubConnectionState.Disconnected, childLive.Hub.State);
        await LiveClient.BarrierAsync(app, ownerLive);
        Assert.Single(ownerLive.Inbox.Of("sessionClosed"), e => e.GetProperty("sessionId").GetInt32() == world.A1.Id);
        Assert.Single(ownerLive.Inbox.Of("sessionClosed"), e => e.GetProperty("sessionId").GetInt32() == world.A2.Id);

        // ชำระซ้ำไม่ได้
        await AssertRefusedAsync(await PayAsync(world.Owner, world.A1.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);
        await AssertRefusedAsync(await PayAsync(world.Owner, world.A2.Id, "Card"),
            HttpStatusCode.Conflict, ErrorCodes.AlreadyPaid);
        Assert.Single(await StoredPaymentsAsync(app));
    }

    [Fact]
    public async Task A_merged_bill_counts_as_one_bill_in_the_reports_with_every_guest()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));
        await OkAsync(await PayAsync(world.Owner, world.A1.Id, "PromptPay"));

        var summary = await OkAsync(await world.Owner.GetAsync("/api/reports/summary?range=today"));
        // บิลรวม = ใบเสร็จใบเดียว จึงนับเป็นหนึ่งบิล
        Assert.Equal(1, summary.GetProperty("billCount").GetInt32());
        Assert.Equal(2364.59m, summary.GetProperty("revenue").GetDecimal());
        // ลูกค้ารวมทุก session ที่ถูกรวม (A1 2 + A2 3 ผู้ใหญ่ 1 เด็ก = 6) เฉลี่ยต่อหัว ฿394.10
        Assert.Equal(6, summary.GetProperty("guestCount").GetInt32());
        Assert.Equal(394.10m, summary.GetProperty("avgPerGuest").GetDecimal());

        // แพ็กเกจนับตาม session หลัก (Gold) ส่วนจำนวนลูกค้านับรวมทุก session ของบิลนั้น
        var mix = summary.GetProperty("packageMix").EnumerateArray().Single();
        Assert.Equal("Gold", mix.GetProperty("name").GetString());
        Assert.Equal(1, mix.GetProperty("sessions").GetInt32());
        Assert.Equal(6, mix.GetProperty("guests").GetInt32());
        Assert.Equal(2364.59m, mix.GetProperty("revenue").GetDecimal());

        // ออเดอร์ของรอบที่ถูกรวมอยู่ใน Top items ด้วย (ชาไทยเย็น ฿45 ×2 จาก A1)
        Assert.Contains(summary.GetProperty("topItems").EnumerateArray(),
            r => r.GetProperty("quantity").GetInt32() == 2);
    }

    /// <summary>
    /// รอบที่ถูกรวมเข้าบิลอื่นยัง Open เพื่อให้ลูกค้าสั่งผ่าน QR เดิมได้ — จึงต้องไม่มีทางปิดมันเองได้
    /// ไม่งั้นบิลลูกจะออกใบเสร็จสำหรับยอดของตัวเอง ขณะที่บิลหลักยังคิดออเดอร์ชุดเดียวกันอยู่ = คิดเงินซ้ำ
    /// (พบจริงบน stack: ปิดรอบลูกด้วยเงินสดได้ใบเสร็จ R20261018-000940 ฿721.50 และยอดของรอบหลักยังเป็น ฿1,443.00)
    /// </summary>
    [Fact]
    public async Task A_merged_child_cannot_be_paid_or_voided_on_its_own()
    {
        using var app = new TestApp(postgres);
        var world = await WorldAsync(app);
        var merged = await OkAsync(await MergeAsync(world.Owner, world.A1.Id, world.A2.Id));

        // บิลหลักคิดออเดอร์ของรอบลูกแล้ว (ยอดรวมเท่ากับที่ mockup o19 ให้) และรอบลูกยัง Open แต่ชี้มาที่บิลหลัก
        Assert.Equal(2364.59m, Bill(merged).Total);
        Assert.Equal(world.A1.Id, (await DetailAsync(world.Owner, world.A2.Id))
            .GetProperty("mergedIntoSessionId").GetInt32());

        // ชำระที่รอบลูกไม่ได้ — ต้องบอกให้ไปจ่ายที่บิลหลัก และต้องไม่มีเงินหรือใบเสร็จเกิดขึ้นเลย
        var refused = await AssertRefusedAsync(await PayAsync(world.Owner, world.A2.Id, "Cash", 999_99m),
            HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        Assert.Contains(world.A1.TableName, refused.GetProperty("message").GetString());
        Assert.Empty(await StoredPaymentsAsync(app));
        Assert.Equal(SessionStatus.Open, (await StoredAsync(app, world.A2.Id)).Status);
        Assert.Equal(SessionStatus.Open, (await StoredAsync(app, world.A1.Id)).Status);

        // ยกเลิกรอบลูกก็ไม่ได้ด้วยเหตุผลเดียวกัน (ออเดอร์ของมันอยู่ในบิลหลัก)
        await AssertRefusedAsync(await world.Owner.PostAsync($"/api/sessions/{world.A2.Id}/void", null),
            HttpStatusCode.Conflict, ErrorCodes.SessionHasOrders);
        Assert.Equal(SessionStatus.Open, (await StoredAsync(app, world.A2.Id)).Status);

        // และตัวเลขยังคิดครั้งเดียว: ยอดรวมทั้งบิลไม่ขยับหลังความพยายามทั้งสองครั้ง
        Assert.Equal(2364.59m, Bill(await DetailAsync(world.Owner, world.A1.Id)).Total);

        // บิลหลักยังปิดได้ตามปกติ — ใบเสร็จใบเดียว ยอดรวม ยอดเดียว
        var paid = await OkAsync(await PayAsync(world.Owner, world.A1.Id, "Cash", 2400m));
        Assert.Equal(2364.59m, paid.GetProperty("payment").GetProperty("total").GetDecimal());
        Assert.Single(await StoredPaymentsAsync(app));
        Assert.Equal(SessionStatus.Paid, (await StoredAsync(app, world.A2.Id)).Status);
    }

    private static JsonElement RowFor(JsonElement overview, string tableName) =>
        overview.EnumerateArray().Single(t => t.GetProperty("name").GetString() == tableName);
}
