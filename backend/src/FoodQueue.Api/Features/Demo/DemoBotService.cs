using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Orders;
using FoodQueue.Api.Features.Sessions;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Demo;

/// <summary>
/// Bot จำลองลูกค้า (spec "Demo Mode", user stories 107, 110) — งานเบื้องหลังที่ทำให้ demo มีชีวิต:
/// วนทุก 20–40 วินาที และในแต่ละรอบทำสี่อย่างกับ <b>รอบของบอทเองเท่านั้น</b> (<c>TableSession.IsDemoBot</c>)
/// <list type="number">
/// <item>เปิดรอบเพิ่มให้มีรอบของบอทที่ยัง Open อย่างน้อย <see cref="TargetOpenRounds"/> รอบ — ไม่แตะโต๊ะที่คนจริงถืออยู่
/// และไม่เปิดเกินเป้า</item>
/// <item>สั่งอาหาร <see cref="MinOrderItems"/>–<see cref="MaxOrderItems"/> รายการบนรอบที่ยังไม่หมดเวลาและมีออเดอร์น้อยที่สุด</item>
/// <item>เลื่อนออเดอร์ที่ค้างสถานะเดิมตั้งแต่ <see cref="StaleAfterSeconds"/> วินาทีขึ้นไปไปหนึ่งขั้น (ถึง Ready)</item>
/// <item>รับชำระเงินสดรอบที่เลย <c>EndsAt</c> แล้วและไม่มีออเดอร์ Pending/Preparing</item>
/// </list>
///
/// <b>ผ่านบริการชุดเดียวกับ endpoint เสมอ</b>: <see cref="ISessionService"/> (เปิดรอบ), <see cref="IOrderService"/>
/// (สร้างออเดอร์ ลำดับการตรวจและ event <c>orderCreated</c>), <see cref="OrderWorkflowService"/> (เลื่อนสถานะ/ยกเลิก
/// => <c>orderStatusChanged</c>) และ <see cref="PaymentService"/> (ปิดบิล => <c>sessionChanged</c>/<c>sessionClosed</c>)
/// — บอทไม่เขียนแถวของธุรกิจเองเลย กฎและ event จึงเป็นของผลิตภัณฑ์ ไม่ใช่ของบอท
///
/// <b>สวิตช์เดียว</b>: <see cref="ShouldRun"/> เป็นจริงเมื่อ <c>Demo:Enabled=true</c> เท่านั้น ปิด Demo = ไม่มีบอท
/// (และ <see cref="RunCycleAsync"/> ก็ไม่ทำอะไรด้วย) — <see cref="DemoSeeder"/> ใช้สวิตช์เดียวกัน
///
/// <b>รอบที่ทดสอบได้</b>: <see cref="RunCycleAsync"/> เป็นเมธอดสาธารณะ หนึ่งครั้ง = หนึ่งรอบเต็ม เทสต์จึงเรียกได้เลย
/// ไม่ต้องรอ 20–40 วินาที; "เวลาปัจจุบัน" มาจากนาฬิกาเดียวที่อ่านครั้งเดียวต่อรอบ และการตัดสินว่าออเดอร์ค้าง/รอบหมดเวลา
/// อ่านจากข้อมูล (<c>Order.UpdatedAt</c>, <c>TableSession.EndsAt</c>) ซึ่งเทสต์ตั้งได้ตรง ๆ; ส่วนการสุ่มมาจาก
/// <see cref="Random"/> ที่ seed ด้วย <see cref="DefaultRandomSeed"/> (ตั้งได้ผ่าน <see cref="From"/>) ลำดับการสุ่ม
/// ของรอบจึงเหมือนเดิมทุกครั้งที่สถานะร้านเหมือนเดิม
/// </summary>
public sealed class DemoBotService(
    IServiceScopeFactory scopes,
    IConfiguration config,
    ILogger<DemoBotService> logger,
    int randomSeed = DemoBotService.DefaultRandomSeed) : BackgroundService
{
    /// <summary>seed ของ bot — ลำดับการสุ่มคงที่เหมือนกันทุกเครื่อง (เทสต์เลือก seed เองได้)</summary>
    public const int DefaultRandomSeed = 2025;

    /// <summary>จำนวนรอบของบอทที่ต้องเปิดค้างไว้ ("มี session ของ bot เปิดอย่างน้อย 3 โต๊ะ")</summary>
    public const int TargetOpenRounds = 3;

    /// <summary>จำนวนรายการต่อรอบที่บอทส่ง ("สั่งอาหาร 2–6 รายการ")</summary>
    public const int MinOrderItems = 2;
    public const int MaxOrderItems = 6;

    /// <summary>ออเดอร์ที่ค้างสถานะเดิมตั้งแต่เวลานี้ขึ้นไปถือว่า "ค้าง" แล้วให้เลื่อนขั้น ("เกิน 120 วินาที")</summary>
    public const int StaleAfterSeconds = 120;

    /// <summary>ช่วงพักระหว่างรอบ ("วนทุก 20–40 วินาที")</summary>
    public const int MinCycleSeconds = 20;
    public const int MaxCycleSeconds = 40;

    /// <summary>สัดส่วนของบรรทัดที่เป็นเครื่องดื่มคิดเงินแยก (เหมือน seed ของ demo: 2 ใน 5)</summary>
    private const int AlaCarteShare = 40;

    /// <summary>เพดานการสุ่มต่อรอบ — กันลูปไม่จบเมนูที่มีไม่พอสำหรับจำนวนบรรทัดที่ต้องการ</summary>
    private const int PickingAttempts = 24;

    private readonly Random _random = new(randomSeed);

    public static DemoBotService From(IServiceProvider services, int randomSeed = DefaultRandomSeed) => new(
        services.GetRequiredService<IServiceScopeFactory>(),
        services.GetRequiredService<IConfiguration>(),
        services.GetRequiredService<ILogger<DemoBotService>>(),
        randomSeed);

    /// <summary>
    /// arm loop เมื่อ Demo เปิด (สวิตช์เดียวกับ endpoint และ seed) และไม่ใช่ environment <c>Testing</c>:
    /// เทสต์ seam 1 เรียก <see cref="RunCycleAsync"/> เป็นรอบเอง timer ที่วิ่งเองจะไปแก้ข้อมูลของ fixture
    /// ระหว่างเทสต์ — เจ้าของงานนี้เลือกจุดต่อนี้เพราะ <c>Program.cs</c> ถูก freeze และไม่มีที่ลงทะเบียน
    /// <c>IHostedService</c> จากไฟล์ของฟีเจอร์ (ดูรายงาน)
    /// </summary>
    public static bool ShouldRun(IConfiguration config, IHostEnvironment environment) =>
        DemoMode.IsEnabled(config) && !environment.IsEnvironment("Testing");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!DemoMode.IsEnabled(config))
        {
            return;
        }

        logger.LogInformation("Demo bot: เริ่มทำงาน · วนทุก {Min}–{Max} วินาที", MinCycleSeconds, MaxCycleSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // พักก่อนรอบแรกเสมอ: ตอนสตาร์ท host ยัง seed ข้อมูลตัวอย่างไม่เสร็จ
                await Task.Delay(TimeSpan.FromSeconds(
                    MinCycleSeconds + _random.Next(MaxCycleSeconds - MinCycleSeconds + 1)), stoppingToken);
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // รอบที่พังต้องไม่ฆ่าบอท: log แล้ววนรอบถัดไปต่อ
                logger.LogError(ex, "Demo bot: cycle failed · รอบถัดไปทำงานต่อ");
            }
        }
    }

    /// <summary>
    /// หนึ่งรอบเต็มของบอท เรียงตามลำดับที่แผนกำหนด: เปิดรอบให้ครบ → สั่งอาหาร → เลื่อนออเดอร์ที่ค้าง → รับชำระรอบที่หมดเวลา
    ///
    /// แต่ละขั้นจับข้อผิดพลาดของตัวเองแล้ว log: ขั้นหนึ่งพังไม่ทำให้ขั้นอื่นหรือรอบถัดไปหยุด (spec: "error ในรอบหนึ่ง
    /// ต้อง log แล้วทำรอบถัดไปต่อ") นาฬิกาของรอบอ่านครั้งเดียวเพื่อให้ทั้งสี่ขั้นเห็นเวลาเดียวกัน
    /// </summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        if (!DemoMode.IsEnabled(config))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await StepAsync("top up open rounds", () => TopUpAsync(ct));
        await StepAsync("place an order", () => PlaceOrderAsync(now, ct));
        await StepAsync("advance stale orders", () => AdvanceStaleAsync(now, ct));
        await StepAsync("pay out expired rounds", () => PayOutAsync(now, ct));
    }

    private async Task StepAsync(string step, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Demo bot: step \"{Step}\" failed · รอบถัดไปทำงานต่อ", step);
        }
    }

    // ------------------------------------------------------------------ 1. ให้มีรอบของบอทเปิดอยู่อย่างน้อย TargetOpenRounds รอบ

    /// <summary>
    /// เติมรอบของบอทให้ครบเป้า: เลือกเฉพาะโต๊ะที่<b>ว่าง</b> (ไม่มี session Open ไม่ว่าเจ้าของจะเป็นใคร) จึงเปิดทับโต๊ะ
    /// ที่คนจริงนั่งอยู่ไม่ได้ตั้งแต่การเลือก และ partial unique index ในฐานข้อมูลเป็นตัวตัดสินสุดท้ายถ้าเกิดการแข่งกัน
    /// ตัวเลขของรอบใหม่เป็นกลาง ๆ ของแพ็กเกจที่ใช้งานอยู่กับผู้ใหญ่ 2 ท่าน เหมือน <c>POST /api/demo/customer-session</c>
    /// </summary>
    private async Task TopUpAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var open = await OpenBotRoundIdsAsync(db, ct);
        var missing = TargetOpenRounds - open.Count;
        if (missing <= 0)
        {
            return;
        }

        var freeTables = await db.Tables.AsNoTracking()
            .Where(t => t.IsActive
                        && !db.TableSessions.Any(s => s.TableId == t.Id && s.Status == SessionStatus.Open))
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(ct);
        var packages = await db.Packages.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Rank)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (freeTables.Count == 0 || packages.Count == 0)
        {
            return;
        }

        var packageId = packages[packages.Count / 2];
        var sessions = SessionService.From(sp);
        var opened = 0;

        foreach (var tableId in freeTables)
        {
            if (opened >= missing)
            {
                break;
            }

            // 2–4 ผู้ใหญ่ และครึ่งหนึ่งมีเด็กหนึ่งคน — ร้านตัวอย่างจึงมีทั้งรอบคู่และรอบครอบครัว
            var adults = 2 + _random.Next(0, 3);
            var children = _random.Next(0, 3);
            if (_random.Next(100) < 50)
            {
                children = 0;
            }

            try
            {
                var round = await sessions.OpenAsync(tableId, packageId, adults, children, ct, isDemoBot: true);
                opened++;
                db.ChangeTracker.Clear();
                logger.LogInformation("Demo bot: เปิดโต๊ะ {Table} · {Package} · {Adults} ผู้ใหญ่ {Children} เด็ก",
                    round.TableName, round.PackageName, adults, children);
            }
            catch (AppException ex)
            {
                // โต๊ะที่เพิ่งถูกคนจริงเปิดไปก่อนระหว่างรอบนี้: ข้ามไปโต๊ะถัดไป (ไม่แตะรอบของคนคนนั้น)
                db.ChangeTracker.Clear();
                logger.LogWarning("Demo bot: เปิดโต๊ะไม่ได้ ({Code}) · ข้ามโต๊ะนี้", ex.Code);
            }
        }
    }

    // ------------------------------------------------------------------ 2. สั่งอาหาร 2–6 รายการบนรอบของบอท

    /// <summary>
    /// สั่งบนรอบของบอทที่ยัง<b>ไม่หมดเวลา</b>และมีออเดอร์ค้างอยู่น้อยที่สุด (เสมอกันเลือก id น้อยก่อน) — การหมุนแบบนี้
    /// ทำให้ทุกรอบได้ออเดอร์จริงและเป็นลำดับเดิมทุกครั้งที่สถานะร้านเหมือนเดิม โดยไม่ต้องใช้การสุ่มเลือกโต๊ะ
    /// ออเดอร์ถูกสร้างผ่าน <see cref="IOrderService"/> ตัวเดียวกับหน้าลูกค้า ลำดับการตรวจและ <c>orderCreated</c>
    /// จึงเหมือนกันเป๊ะ (เมนูที่หมด/ไม่อยู่ในแพ็กเกจจะถูกปฏิเสธที่นั่น ไม่ใช่ที่นี่)
    /// </summary>
    private async Task PlaceOrderAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var target = await db.TableSessions.AsNoTracking()
            .Where(s => s.IsDemoBot && s.Status == SessionStatus.Open
                        && s.MergedIntoSessionId == null && s.EndsAt > now)
            .OrderBy(s => s.Orders.Count(o => o.Status != OrderStatus.Cancelled))
            .ThenBy(s => s.Id)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(ct);
        if (target == 0)
        {
            return;
        }

        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new RestaurantSettings();
        var menu = await db.MenuItems.AsNoTracking()
            .Where(m => m.IsActive && m.IsAvailable)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var sessions = SessionService.From(sp);
        var round = await sessions.RequireAsync(target, ct);
        var lines = PickLines(round.PackageRankSnapshot, settings, menu);
        if (lines.Count == 0)
        {
            logger.LogWarning("Demo bot: ไม่มีเมนูที่สั่งได้สำหรับรอบนี้ · ข้ามการสั่ง");
            return;
        }

        var order = await OrderService.From(sp).CreateAsync(round, lines, ct);
        logger.LogInformation("Demo bot: โต๊ะ {Table} สั่งรอบที่ {Number} · {Items} รายการ",
            order.TableName, order.Number, order.Items.Sum(i => i.Quantity));
    }

    /// <summary>
    /// เลือก 2–6 รายการแบบสุ่มจากเมนูที่<b>ร้านและแพ็กเกจของรอบนี้ยอมให้สั่ง</b> (active + มีของ + rank ถึง)
    /// เพดานของร้านยังเป็นเพดานจริง: จำนวนต่อเมนู ≤ <c>MaxQtyPerItemPerOrder</c> และจำนวนรวมต่อรอบ ≤
    /// <c>MaxItemsPerOrder</c> — บอทจึงไม่เคยทำให้บริการปฏิเสธ (ไม่ต้องมีกฎของตัวเองซ้อนกับ OrderService)
    /// แต่ละบรรทัดกันที่ไว้หนึ่งช่องจากเพดานรวมสำหรับบรรทัดที่ยังต้องมี จำนวนบรรทัดที่ได้จึงเท่าที่สุ่มไว้เสมอ
    /// </summary>
    private List<OrderLineRequest> PickLines(int rank, RestaurantSettings settings, IReadOnlyList<MenuItem> menu)
    {
        var buffet = menu
            .Where(m => m.Kind == MenuItemKind.Buffet && (m.MinPackageRank ?? 0) <= rank)
            .ToList();
        var alaCarte = menu.Where(m => m.Kind == MenuItemKind.AlaCarte).ToList();
        if (buffet.Count == 0 && alaCarte.Count == 0)
        {
            return [];
        }

        var perItem = Math.Max(1, settings.MaxQtyPerItemPerOrder);
        var perRound = Math.Max(1, Math.Min(MaxOrderItems, settings.MaxItemsPerOrder));
        var wanted = Math.Min(MinOrderItems + _random.Next(0, MaxOrderItems - MinOrderItems + 1), perRound);

        var quantities = new Dictionary<int, int>();
        var total = 0;
        for (var attempt = 0; attempt < PickingAttempts && quantities.Count < wanted && total < perRound; attempt++)
        {
            var candidates = alaCarte.Count > 0 && (buffet.Count == 0 || _random.Next(100) < AlaCarteShare)
                ? alaCarte
                : buffet;
            var item = candidates[_random.Next(candidates.Count)];

            var already = quantities.GetValueOrDefault(item.Id);
            // กันที่ให้บรรทัดที่ยังต้องมี: บรรทัดสุดท้ายจึงไม่กินเพดานของรอบจนหมด
            var reserved = wanted - quantities.Count - 1;
            var room = Math.Min(perItem - already, perRound - total - reserved);
            if (room <= 0)
            {
                continue;
            }

            var quantity = 1 + _random.Next(room);
            quantities[item.Id] = already + quantity;
            total += quantity;
        }

        return quantities
            .OrderBy(pair => pair.Key)
            .Select(pair => new OrderLineRequest(pair.Key, pair.Value, null))
            .ToList();
    }

    // ------------------------------------------------------------------ 3. เลื่อนออเดอร์ที่ค้างสถานะเดิมเกิน 120 วินาที

    /// <summary>
    /// ออเดอร์ของรอบบอทที่ยัง Pending/Preparing และ <c>UpdatedAt</c> (เวลาที่สถานะเปลี่ยนครั้งล่าสุด) เก่ากว่าหรือเท่ากับ
    /// <c>now - 120 วินาที</c> ถูกเลื่อนไปหนึ่งขั้นผ่าน <see cref="OrderWorkflowService"/> ตัวเดียวกับจอครัว:
    /// Pending → Preparing → Ready และหยุดที่ Ready (ไม่มี Served) การแข่งกับการกดของจอครัวจบที่
    /// <c>INVALID_STATUS_TRANSITION</c> ซึ่งนับเป็นเรื่องปกติของรอบนี้ (log ระดับ warning แล้วทำรายการถัดไปต่อ)
    /// </summary>
    private async Task AdvanceStaleAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var cutoff = now.AddSeconds(-StaleAfterSeconds);
        var stale = await db.Orders.AsNoTracking()
            .Where(o => o.TableSession!.IsDemoBot
                        && o.TableSession!.MergedIntoSessionId == null
                        && o.TableSession!.Status == SessionStatus.Open
                        && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing)
                        && o.UpdatedAt <= cutoff)
            .OrderBy(o => o.Id)
            .Select(o => new { o.Id, o.Status })
            .ToListAsync(ct);

        var workflow = OrderWorkflowService.From(sp);
        foreach (var order in stale)
        {
            var target = order.Status == OrderStatus.Pending ? OrderStatus.Preparing : OrderStatus.Ready;
            try
            {
                var changed = await workflow.ChangeStatusAsync(order.Id, target, ct);
                db.ChangeTracker.Clear();
                if (changed is not null)
                {
                    logger.LogInformation("Demo bot: โต๊ะ {Table} รอบที่ {Number} → {Status}",
                        changed.TableName, changed.Number, changed.Status);
                }
            }
            catch (AppException ex)
            {
                db.ChangeTracker.Clear();
                logger.LogWarning("Demo bot: เลื่อนออเดอร์ {OrderId} ไม่ได้ ({Code}) · ทำรายการถัดไปต่อ",
                    order.Id, ex.Code);
            }
        }
    }

    // ------------------------------------------------------------------ 4. รับชำระเงินสดรอบของบอทที่หมดเวลาแล้ว

    /// <summary>
    /// รอบบอทที่เลย <c>EndsAt</c> แล้วและ<b>ไม่มี</b>ออเดอร์ Pending/Preparing ปิดบิลด้วยเงินสดผ่าน
    /// <see cref="PaymentService"/> ตัวเดียวกับหน้าจอเจ้าของร้าน (กฎ "ออเดอร์ค้างขวางการปิดบิล" เลขใบเสร็จ และ
    /// <c>sessionClosed</c> มาจากที่นั่น) รับเงินพอดีบิลจึงทอน 0 — บอทไม่แตะรอบที่ถูกรวมเข้าบิลของคนอื่นเพราะบิลนั้น
    /// เป็นของคนที่กดรวม (การชำระของรอบหลักปิดรอบนั้นให้อยู่แล้ว)
    /// </summary>
    private async Task PayOutAsync(DateTimeOffset now, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        var due = await db.TableSessions.AsNoTracking()
            .Where(s => s.IsDemoBot && s.Status == SessionStatus.Open
                        && s.MergedIntoSessionId == null
                        && s.EndsAt < now
                        && !s.Orders.Any(o => o.Status == OrderStatus.Pending || o.Status == OrderStatus.Preparing))
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var sessions = SessionService.From(sp);
        var payments = PaymentService.From(sp);
        foreach (var sessionId in due)
        {
            var round = await sessions.RequireAsync(sessionId, ct);
            var bill = await sessions.ComputeBillAsync(round, ct);
            var paid = await payments.PayAsync(sessionId, PaymentMethod.Cash, bill.Total, ct);
            db.ChangeTracker.Clear();
            logger.LogInformation("Demo bot: โต๊ะ {Table} จ่ายเงินสด · ฿{Total} · ใบเสร็จ {ReceiptNo}",
                paid.TableName, paid.Bill.Total, paid.Payment?.ReceiptNo ?? "-");
        }
    }

    /// <summary>
    /// รอบของบอทที่ "มีชีวิต" สำหรับเป้า 3 รอบ: ยัง Open และไม่ได้ถูกรวมเข้าบิลของรอบอื่น — รอบที่ถูกรวมแล้ว
    /// เป็นของบิลที่คนกดรวม บอทจึงไม่นับและไม่แตะ (รอบถัดไปจะเปิดรอบใหม่ชดเชยเอง)
    /// </summary>
    private static Task<List<int>> OpenBotRoundIdsAsync(AppDbContext db, CancellationToken ct) =>
        db.TableSessions.AsNoTracking()
            .Where(s => s.IsDemoBot && s.Status == SessionStatus.Open && s.MergedIntoSessionId == null)
            .OrderBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(ct);
}

/// <summary>
/// งานตอนสตาร์ทของบอท — Program.cs ถูก freeze จึงไม่มีที่ลงทะเบียน <c>IHostedService</c> จากไฟล์ของฟีเจอร์
/// ไฟล์นี้จึงใช้จุดต่อที่ Program.cs เปิดไว้แล้ว (<see cref="IStartupTask"/> ถูกค้นหาด้วย reflection เหมือน
/// <c>DemoSeeder</c>/<c>AuthStartupTask</c>)
///
/// <b>เริ่มหลัง host สตาร์ทเสร็จเท่านั้น</b> (<c>ApplicationStarted</c>): <c>StartupTasks.RunAsync</c> ถูก await
/// ก่อน <c>app.Run()</c> จึงเสร็จก่อน event นี้เสมอ — บอทจึงไม่แย่งโต๊ะกับ seed ประวัติของ demo (การเริ่มตอน
/// <see cref="RunAsync"/> เองเคยทำให้ seeder ล้มด้วย <c>TABLE_OCCUPIED</c> เพราะทั้งคู่เปิดโต๊ะพร้อมกัน)
/// ตอน host ปิดก็สั่งหยุดผ่าน <c>ApplicationStopping</c>
/// </summary>
public sealed class DemoBotStartupTask : IStartupTask
{
    public Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        if (!DemoBotService.ShouldRun(config, services.GetRequiredService<IHostEnvironment>()))
        {
            return Task.CompletedTask;
        }

        var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStarted.Register(() =>
        {
            var bot = DemoBotService.From(services);
            lifetime.ApplicationStopping.Register(() => _ = bot.StopAsync(CancellationToken.None));
            _ = bot.StartAsync(CancellationToken.None);
        });

        return Task.CompletedTask;
    }
}
