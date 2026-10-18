using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Orders;
using FoodQueue.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Demo;

/// <summary>
/// Seed ของ Demo Mode (spec "Demo Mode", user stories 105, 106) — งานตอนสตาร์ทที่รันเมื่อ
/// <c>Demo:Enabled=true</c> <b>และ</b>ฐานข้อมูลยังไม่มีโต๊ะเลยเท่านั้น (สวิตช์เดียวกับ endpoint)
///
/// กติกาที่ spec กำหนดและถูกบังคับที่นี่:
/// <list type="bullet">
/// <item><b>กำหนดซ้ำได้</b>: สุ่มจาก <see cref="RandomSeed"/> คงที่ ลำดับการสุ่มจึงไม่เปลี่ยนระหว่างครั้งที่รัน</item>
/// <item><b>idempotent</b>: มีโต๊ะอยู่แล้ว = ข้อมูล demo มีอยู่แล้ว จึงไม่แตะทั้งแถวข้อมูลและบัญชี</item>
/// <item>ร้านตัวอย่าง (SC 10%, VAT 7%, PromptPay) · โต๊ะ 12 (A1–A6, B1–B6; B3 6 ที่นั่ง รวม 50) ·
/// แพ็กเกจ 3 ระดับ · เมนู 44 รายการใน 8 หมวด · ประวัติ <see cref="HistoryDays"/> วัน (ไม่รวมวันนี้)
/// วันละ <see cref="MinBillsPerDay"/>–<see cref="MaxBillsPerDay"/> บิล เวลาไทย 11:00–21:30
/// สัดส่วน Cash 40 / PromptPay 45 / Card 15 และรายการปรับยอดราว 10% ของบิล</item>
/// <item><b>ผ่านบริการชุดเดียวกับ API</b>: รอบการนั่ง ออเดอร์ การเลื่อนสถานะ/ยกเลิก การปรับยอด และการชำระเงิน
/// ล้วนเรียก <see cref="ISessionService"/>, <see cref="IOrderService"/>, <see cref="OrderWorkflowService"/>,
/// <see cref="BillAdjustmentService"/>, <see cref="PaymentService"/> — ไม่มีการประกอบแถวบิลหรือใบเสร็จเอง
/// สิ่งที่เขียนฐานข้อมูลตรงมีเพียงแค็ตตาล็อก (ตั้งค่าร้าน/โต๊ะ/แพ็กเกจ/หมวด/เมนู) ซึ่งไม่มี endpoint หรือ
/// บริการกลางรองรับในโค้ดชุดนี้ (ดูรายงานของ ticket 24) และ "การย้อนเวลา" ของประวัติที่ต้องเป็นอดีต</item>
/// </list>
/// </summary>
public sealed class DemoSeeder : IStartupTask
{
    /// <summary>seed คงที่ — ผลลัพธ์จึงเหมือนกันทุกเครื่องทุกครั้งที่ seed (spec "Seed แบบกำหนดซ้ำได้")</summary>
    public const int RandomSeed = 42;

    /// <summary>จำนวนวันของประวัติ (ไม่รวมวันนี้)</summary>
    public const int HistoryDays = 30;

    public const int MinBillsPerDay = 20;
    public const int MaxBillsPerDay = 45;

    /// <summary>ช่วงเวลาเปิดโต๊ะของประวัติ: 11:00 ถึง 21:30 ตามเวลาไทย</summary>
    public const int FirstOpenedMinute = 11 * 60;
    public const int LastOpenedMinute = 21 * 60 + 30;

    /// <summary>สัดส่วนวิธีจ่าย (%) ของประวัติ — Cash 40 / PromptPay 45 / Card 15</summary>
    private const int CashShare = 40;
    private const int PromptPayShare = 45;

    /// <summary>ราว 10% ของบิลมีรายการปรับยอด</summary>
    private const int AdjustmentShare = 10;

    /// <summary>เมนูทั้งแปดหมวดตามลำดับของ plan.md Step 10 (Buffet ราคา 0 ตาม rank, เครื่องดื่มเป็น AlaCarte)</summary>
    private static readonly (string Category, string Name, MenuItemKind Kind, int? MinRank, decimal Price,
        string? Description)[] Menu =
    [
        ("น้ำซุป", "น้ำซุปใส", MenuItemKind.Buffet, 1, 0m, "ซุปกระดูกหมูใส"),
        ("น้ำซุป", "น้ำซุปต้มยำน้ำข้น", MenuItemKind.Buffet, 1, 0m, "เปรี้ยวเผ็ดแบบน้ำข้น"),
        ("น้ำซุป", "น้ำซุปหมาล่า", MenuItemKind.Buffet, 1, 0m, "เผ็ดหมาล่า"),
        ("น้ำซุป", "น้ำซุปทงคัตสึ", MenuItemKind.Buffet, 1, 0m, "ซุปกระดูกหมูญี่ปุ่น"),

        ("หมู", "สันคอหมูสไลซ์", MenuItemKind.Buffet, 1, 0m, "รวมในบุฟเฟ่ต์"),
        ("หมู", "หมูสามชั้นสไลซ์", MenuItemKind.Buffet, 1, 0m, null),
        ("หมู", "หมูนุ่มสไลซ์", MenuItemKind.Buffet, 1, 0m, "ไม่ติดมัน"),
        ("หมู", "หมูสันในสไลซ์", MenuItemKind.Buffet, 1, 0m, null),
        ("หมู", "เบคอนหมูสไลด์", MenuItemKind.Buffet, 1, 0m, null),

        ("เนื้อวัว", "เนื้อวัวสไลซ์", MenuItemKind.Buffet, 2, 0m, null),
        ("เนื้อวัว", "ริบอายสไลซ์", MenuItemKind.Buffet, 2, 0m, null),
        ("เนื้อวัว", "เนื้อโคขุนสไลซ์", MenuItemKind.Buffet, 2, 0m, null),
        ("เนื้อวัว", "เนื้อวากิวสไลซ์", MenuItemKind.Buffet, 3, 0m, "เกรดพรีเมียม"),
        ("เนื้อวัว", "ลิ้นวัวสไลซ์", MenuItemKind.Buffet, 3, 0m, null),

        ("ทะเล", "กุ้งขาว", MenuItemKind.Buffet, 2, 0m, null),
        ("ทะเล", "หมึกกล้วย", MenuItemKind.Buffet, 2, 0m, null),
        ("ทะเล", "ปลาช่อนสไลซ์", MenuItemKind.Buffet, 2, 0m, null),
        ("ทะเล", "แซลมอนสไลซ์", MenuItemKind.Buffet, 3, 0m, null),
        ("ทะเล", "หอยเชลล์", MenuItemKind.Buffet, 3, 0m, null),
        ("ทะเล", "ปูอัด", MenuItemKind.Buffet, 2, 0m, null),

        ("ลูกชิ้น/ของทอด", "ลูกชิ้นกุ้ง", MenuItemKind.Buffet, 1, 0m, null),
        ("ลูกชิ้น/ของทอด", "ลูกชิ้นปลา", MenuItemKind.Buffet, 1, 0m, null),
        ("ลูกชิ้น/ของทอด", "ลูกชิ้นหมู", MenuItemKind.Buffet, 1, 0m, null),
        ("ลูกชิ้น/ของทอด", "เกี๊ยวซ่าทอด", MenuItemKind.Buffet, 1, 0m, null),
        ("ลูกชิ้น/ของทอด", "เต้าหู้ทอด", MenuItemKind.Buffet, 1, 0m, null),
        ("ลูกชิ้น/ของทอด", "เทมปุระผัก", MenuItemKind.Buffet, 2, 0m, null),

        ("ผัก", "ผักกาดขาว", MenuItemKind.Buffet, 1, 0m, null),
        ("ผัก", "เห็ดรวม", MenuItemKind.Buffet, 1, 0m, null),
        ("ผัก", "ผักบุ้งจีน", MenuItemKind.Buffet, 1, 0m, null),
        ("ผัก", "ข้าวโพดอ่อน", MenuItemKind.Buffet, 1, 0m, null),
        ("ผัก", "วุ้นเส้น", MenuItemKind.Buffet, 1, 0m, null),
        ("ผัก", "ฟักทอง", MenuItemKind.Buffet, 1, 0m, null),

        ("ของหวาน", "ไอศกรีม", MenuItemKind.Buffet, 1, 0m, null),
        ("ของหวาน", "ขนมปังสอดไส้", MenuItemKind.Buffet, 1, 0m, null),
        ("ของหวาน", "บัวลอย", MenuItemKind.Buffet, 1, 0m, null),
        ("ของหวาน", "เยลลี่ผลไม้", MenuItemKind.Buffet, 1, 0m, null),

        ("เครื่องดื่ม", "น้ำเปล่า", MenuItemKind.AlaCarte, null, 15m, null),
        ("เครื่องดื่ม", "โค้ก", MenuItemKind.AlaCarte, null, 25m, null),
        ("เครื่องดื่ม", "น้ำอัดลม", MenuItemKind.AlaCarte, null, 25m, null),
        ("เครื่องดื่ม", "น้ำส้ม", MenuItemKind.AlaCarte, null, 35m, null),
        ("เครื่องดื่ม", "ชามะนาว", MenuItemKind.AlaCarte, null, 35m, null),
        ("เครื่องดื่ม", "ชาไทยเย็น", MenuItemKind.AlaCarte, null, 45m, "คิดเงินแยก"),
        ("เครื่องดื่ม", "กาแฟเย็น", MenuItemKind.AlaCarte, null, 45m, null),
        ("เครื่องดื่ม", "เบียร์สิงห์", MenuItemKind.AlaCarte, null, 89m, null),
    ];

    public async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        if (!DemoMode.IsEnabled(config))
        {
            return;
        }

        // idempotent: มีโต๊ะ = seed ของ demo เคยรันไปแล้ว (สวิตช์เดียวกันนี้คุมทั้ง endpoint และ seed)
        using (var check = services.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await db.Tables.AnyAsync())
            {
                return;
            }
        }

        await DemoAccounts.EnsureAsync(services, config);
        var catalog = await CatalogAsync(services);
        await HistoryAsync(services, catalog);
    }

    /// <summary>แค็ตตาล็อกที่ seed สร้าง พร้อมลิมิตการสั่งของร้าน (ใช้สุ่มจำนวนบรรทัดของประวัติให้ถูกกฎ)</summary>
    private sealed record Catalog(IReadOnlyList<MenuItem> Menu, int MaxQtyPerItem, int MaxItemsPerOrder);

    /// <summary>
    /// แค็ตตาล็อกของร้านตัวอย่าง — เขียนฐานข้อมูลตรงเพราะโค้ดชุดนี้ไม่มี endpoint/บริการกลางของโต๊ะ แพ็กเกจ
    /// หมวด เมนู และตั้งค่า (โมดูลเหล่านั้นถือ DbContext เองในไฟล์ endpoint) การเรียก HTTP ไปหาตัวเอง
    /// จากงานตอนสตาร์ทจะผูก seed กับลำดับการสตาร์ทของ host โดยไม่จำเป็น
    /// </summary>
    private async Task<Catalog> CatalogAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SettingsAsync(db);

        var tables = new List<Table>();
        for (var i = 1; i <= 6; i++)
        {
            tables.Add(new Table { Name = $"A{i}", Seats = 4, Zone = "A", SortOrder = i });
            tables.Add(new Table
            {
                Name = $"B{i}",
                // B3 เป็นโต๊ะใหญ่ 6 ที่นั่ง (spec) — ที่นั่งรวมทั้งร้าน = 11×4 + 6 = 50
                Seats = i == 3 ? 6 : 4,
                Zone = "B",
                SortOrder = i + 6,
            });
        }

        db.Tables.AddRange(tables);

        db.Packages.AddRange(
            new Package { Name = "Silver", Rank = 1, AdultPrice = 299m, ChildPrice = 149m, DurationMinutes = 90 },
            new Package { Name = "Gold", Rank = 2, AdultPrice = 399m, ChildPrice = 199m, DurationMinutes = 90 },
            new Package { Name = "Platinum", Rank = 3, AdultPrice = 549m, ChildPrice = 279m, DurationMinutes = 120 });

        var categories = new Dictionary<string, Category>(StringComparer.Ordinal);
        var items = new List<MenuItem>();
        var sortOrder = 0;
        foreach (var line in Menu)
        {
            if (!categories.TryGetValue(line.Category, out var category))
            {
                category = new Category { Name = line.Category, SortOrder = categories.Count + 1 };
                categories[line.Category] = category;
                db.Categories.Add(category);
            }

            items.Add(new MenuItem
            {
                Category = category,
                Name = line.Name,
                Description = line.Description,
                Kind = line.Kind,
                MinPackageRank = line.MinRank,
                Price = line.Price,
                SortOrder = ++sortOrder,
            });
        }

        db.MenuItems.AddRange(items);
        await db.SaveChangesAsync(CancellationToken.None);

        // ตั้งค่าเป็นแถวเดียวของร้าน (Program.cs สร้างให้ตอน migrate) — ค่าที่นี่คือ "ร้านตัวอย่าง" ของ demo
        var settings = await db.Settings.AsNoTracking().SingleAsync(CancellationToken.None);
        return new Catalog(items, settings.MaxQtyPerItemPerOrder, settings.MaxItemsPerOrder);
    }

    /// <summary>ตั้งค่าของร้านตัวอย่าง: ชื่อ/ที่อยู่/โทร, SC 10%, VAT 7%, PromptPay ตัวอย่าง</summary>
    private static async Task SettingsAsync(AppDbContext db)
    {
        var settings = await db.Settings.FirstOrDefaultAsync(CancellationToken.None);
        if (settings is null)
        {
            settings = new RestaurantSettings { Id = 1 };
            db.Settings.Add(settings);
        }

        settings.Name = "ชาบูเดโม่";
        settings.Address = "99/9 ถ.สุขุมวิท เขตวัฒนา กรุงเทพฯ 10110";
        settings.Phone = "02-000-0000";
        settings.PromptPayId = "0812345678";
        settings.ServiceChargePercent = 10m;
        settings.VatPercent = 7m;
        settings.MaxQtyPerItemPerOrder = 5;
        settings.MaxItemsPerOrder = 20;
        settings.LastOrderWarningMinutes = 15;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// ประวัติ 30 วัน: แต่ละวันมี 20–45 รอบที่ปิดแล้ว ทุกอย่างผ่านบริการชุดเดียวกับ API
    /// (เปิดรอบ → สั่งอาหาร → ปิดงานครัว/ยกเลิก → ปรับยอด → รับชำระ) แล้วจึงย้อนเวลาเปิด/ปิดของรอบ
    /// ให้ตกในอดีต เพราะเวลาปัจจุบันเป็นตัวตัดสินว่าเปิดรอบและสั่งอาหารได้หรือไม่ (ห้ามสั่งหลัง EndsAt)
    /// ซึ่งประวัติของ "เมื่อวานและก่อนหน้า" ต้องไม่ถูกกฎนั้นปฏิเสธ
    /// </summary>
    private async Task HistoryAsync(IServiceProvider services, Catalog catalog)
    {
        var random = new Random(RandomSeed);
        var today = Bkk.DateOf(DateTimeOffset.UtcNow);
        var buffet = catalog.Menu.Where(m => m.Kind == MenuItemKind.Buffet).ToList();
        var alaCarte = catalog.Menu.Where(m => m.Kind == MenuItemKind.AlaCarte).ToList();
        var tableIds = await TableIdsAsync(services);
        var packages = await PackagesAsync(services);
        if (tableIds.Count == 0 || packages.Count == 0)
        {
            return;
        }

        for (var dayOffset = HistoryDays; dayOffset >= 1; dayOffset--)
        {
            var day = today.AddDays(-dayOffset);
            var bills = random.Next(MinBillsPerDay, MaxBillsPerDay + 1);
            for (var i = 0; i < bills; i++)
            {
                await OneBillAsync(services, random, day, tableIds, packages, buffet, alaCarte, catalog);
            }
        }
    }

    /// <summary>หนึ่งบิลของประวัติ — ลำดับการเรียกเหมือนหน้าจอจริง ไม่มีการประกอบแถวบิลเอง</summary>
    private async Task OneBillAsync(IServiceProvider services, Random random, DateOnly day, IReadOnlyList<int> tableIds,
        IReadOnlyList<Package> packages, IReadOnlyList<MenuItem> buffet, IReadOnlyList<MenuItem> alaCarte,
        Catalog catalog)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var sessions = SessionService.From(sp);
        var orders = OrderService.From(sp);
        var workflow = OrderWorkflowService.From(sp);
        var adjustments = BillAdjustmentService.From(sp);
        var payments = PaymentService.From(sp);

        var ct = CancellationToken.None;
        var tableId = tableIds[random.Next(tableIds.Count)];
        var package = packages[random.Next(packages.Count)];
        var adults = 2 + random.Next(0, 3);
        var children = random.Next(0, 3);
        if (random.Next(100) < 50)
        {
            children = 0;
        }

        var detail = await sessions.OpenAsync(tableId, package.Id, adults, children, ct, isDemoBot: true);

        // จำนวนรอบ 1–5 โดยแต่ละรอบเป็นออเดอร์ที่ครัวทำจบแล้ว (Ready) หรือถูกยกเลิก
        var rounds = 1 + random.Next(0, 5);
        for (var round = 0; round < rounds; round++)
        {
            var session = (await sessions.RequireAsync(detail.Id, ct));
            var lines = PickLines(random, session.PackageRankSnapshot, catalog.MaxQtyPerItem,
                catalog.MaxItemsPerOrder, buffet, alaCarte);
            var order = await orders.CreateAsync(session, lines, ct);

            if (random.Next(100) < 15)
            {
                await workflow.CancelAsync(order.Id, CancelReasons[random.Next(CancelReasons.Length)], ct);
            }
            else
            {
                await workflow.ChangeStatusAsync(order.Id, OrderStatus.Preparing, ct);
                await workflow.ChangeStatusAsync(order.Id, OrderStatus.Ready, ct);
            }

            db.ChangeTracker.Clear();
        }

        if (random.Next(100) < AdjustmentShare)
        {
            var positive = random.Next(2) == 0;
            await adjustments.AddAsync(detail.Id,
                positive ? "ค่าอาหารเหลือ" : "ส่วนลดลูกค้าประจำ",
                positive ? 50m : -20m, ct);
            db.ChangeTracker.Clear();
        }

        // วิธีจ่ายและเวลาของบิลถูกสุ่มไว้ก่อนปิดบิล เพื่อให้เลขใบเสร็จกับเวลาที่บันทึกตรงกัน
        var method = PaymentMethodFor(random);
        var openedAt = Bkk.StartOfDay(day)
            .AddMinutes(random.Next(FirstOpenedMinute, LastOpenedMinute + 1))
            .ToUniversalTime();
        var paidAt = openedAt.AddMinutes(random.Next(45, 115));

        // ปิดบิลผ่านบริการเดียวกับหน้าจอเจ้าของร้าน: กฎ "ไม่มีออเดอร์ค้าง" การปัด SC/VAT และเลขใบเสร็จ
        // มาจาก PaymentService ทั้งหมด (ทุกออเดอร์เป็น Ready หรือ Cancelled แล้ว)
        var closed = await sessions.RequireAsync(detail.Id, ct);
        var bill = await sessions.ComputeBillAsync(closed, ct);
        await payments.PayAsync(detail.Id, method, method == PaymentMethod.Cash ? bill.Total : null, ct);

        await BackdateAsync(db, detail.Id, openedAt, paidAt, package.DurationMinutes, ct);
    }

    /// <summary>
    /// ย้อนเวลาของบิลให้ตกในอดีต — ขั้นเดียวของการ seed ที่เขียนฐานข้อมูลตรง เพราะเวลาปัจจุบันคือสิ่งที่
    /// บริการทุกตัวใช้ตัดสิน (เปิดรอบได้ สั่งได้เมื่อยังไม่เลย <c>EndsAt</c>) จึงไม่มีทางเปิดรอบของ
    /// "30 วันก่อน" ผ่านบริการได้ และ <c>Program.cs</c> ถูก freeze จึงเพิ่มนาฬิกาที่ฉีดได้ไม่ได้
    /// ยอดเงิน สถานะ และลำดับใบเสร็จยังมาจากบริการทั้งหมด: ที่นี่แก้แค่ instants กับวันที่ในเลขใบเสร็จ
    /// </summary>
    private static async Task BackdateAsync(AppDbContext db, int sessionId, DateTimeOffset openedAt,
        DateTimeOffset paidAt, int durationMinutes, CancellationToken ct)
    {
        await db.TableSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OpenedAt, openedAt)
                .SetProperty(x => x.EndsAt, openedAt.AddMinutes(durationMinutes))
                .SetProperty(x => x.ClosedAt, paidAt), ct);

        // เลขใบเสร็จ = "R" + วันที่ไทยของเวลาที่ชำระ + ลำดับเดิม (ลำดับมาจาก sequence ของฐานข้อมูล ไม่ซ้ำอยู่แล้ว)
        var payment = await db.Payments.AsNoTracking().FirstAsync(p => p.TableSessionId == sessionId, ct);
        var sequence = long.Parse(payment.ReceiptNo[(payment.ReceiptNo.IndexOf('-') + 1)..]);
        var receiptNo = PaymentService.ReceiptNo(paidAt, sequence);

        await db.Payments.Where(p => p.TableSessionId == sessionId)
            .ExecuteUpdateAsync(p => p
                .SetProperty(x => x.PaidAt, paidAt)
                .SetProperty(x => x.ReceiptNo, receiptNo), ct);

        // เวลาของออเดอร์: ครัวทำจบก่อนปิดบิลเสมอ
        await db.Orders.Where(o => o.TableSessionId == sessionId)
            .ExecuteUpdateAsync(o => o
                .SetProperty(x => x.CreatedAt, openedAt)
                .SetProperty(x => x.UpdatedAt, paidAt), ct);
    }

    /// <summary>
    /// 2–6 รายการแบบสุ่ม: บุฟเฟ่ต์เฉพาะที่ rank ถึง + เครื่องดื่มคิดเงินแยก
    /// จำนวนต่อเมนูและจำนวนรวมต่อรอบถูกจำกัดตามลิมิตของร้าน (ค่าที่ seed เขียนไว้) เสมอ
    /// เพราะออเดอร์ของประวัติเดินผ่านการตรวจชุดเดียวกับที่ลูกค้าจริงเจอ
    /// </summary>
    private static List<OrderLineRequest> PickLines(Random random, int rank, int maxQtyPerItem, int maxQtyPerOrder,
        IReadOnlyList<MenuItem> buffet, IReadOnlyList<MenuItem> alaCarte)
    {
        var eligible = buffet.Where(m => (m.MinPackageRank ?? 0) <= rank).ToList();
        var quantities = new Dictionary<int, int>();
        var picks = Math.Min(6, maxQtyPerOrder);
        var total = 0;

        for (var i = 0; i < picks && total < maxQtyPerOrder; i++)
        {
            // สองในห้าของบรรทัดเป็นเครื่องดื่ม เพื่อให้มียอด AlaCarte ในบิลและ top items มีความหมาย
            var item = random.Next(100) < 40
                ? alaCarte[random.Next(alaCarte.Count)]
                : eligible[random.Next(eligible.Count)];

            var already = quantities.GetValueOrDefault(item.Id);
            var room = Math.Min(maxQtyPerItem - already, maxQtyPerOrder - total);
            if (room <= 0)
            {
                continue;
            }

            var quantity = 1 + random.Next(room);
            quantities[item.Id] = already + quantity;
            total += quantity;
        }

        return quantities
            .OrderBy(pair => pair.Key)
            .Select(pair => new OrderLineRequest(pair.Key, pair.Value, null))
            .ToList();
    }

    /// <summary>สัดส่วนวิธีจ่ายของประวัติ: Cash 40% / PromptPay 45% / Card 15% (แผนก้อนเดียวบน 0–99)</summary>
    private static PaymentMethod PaymentMethodFor(Random random)
    {
        var pick = random.Next(100);
        if (pick < CashShare)
        {
            return PaymentMethod.Cash;
        }

        return pick < CashShare + PromptPayShare ? PaymentMethod.PromptPay : PaymentMethod.Card;
    }

    private static async Task<IReadOnlyList<int>> TableIdsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Tables.AsNoTracking().OrderBy(t => t.SortOrder).Select(t => t.Id).ToListAsync();
    }

    private static async Task<IReadOnlyList<Package>> PackagesAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Packages.AsNoTracking().OrderBy(p => p.Rank).ToListAsync();
    }

    /// <summary>เหตุผลยกเลิกที่ครัวใช้จริง (หน้าจอครัว k2) — ข้อความเต็มเป็นของหน้านั้น ที่นี่เอาแบบสั้น</summary>
    private static readonly string[] CancelReasons =
    [
        "วัตถุดิบหมด",
        "ลูกค้าแจ้งเปลี่ยน",
        "สั่งผิด/ซ้ำ",
    ];
}
