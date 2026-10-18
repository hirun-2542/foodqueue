using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Demo;
using FoodQueue.Api.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodQueue.Api.Tests;

/// <summary>
/// ตัวเก็บ log ของ host จริง — ให้เทสต์อ่าน "สิ่งที่บอทบันทึกไว้" ได้โดยไม่ต้องมีที่เขียน log ของตัวเอง
/// (ลงทะเบียนเป็น <see cref="ILoggerProvider"/> เพิ่มจากของ host)
/// </summary>
public sealed class BotLog : ILoggerProvider
{
    /// <summary>หมวดของ log ที่บอทเขียน — ใช้แยก "log ของบอท" จาก log ของ ASP.NET/EF ที่ host เขียนเอง</summary>
    public const string CategoryFragment = "Features.Demo.DemoBotService";

    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines.ToArray();

    /// <summary>บรรทัดที่บอทเขียนเท่านั้น</summary>
    public IReadOnlyList<string> BotLines =>
        _lines.Where(line => line.Contains(CategoryFragment, StringComparison.Ordinal)).ToArray();

    /// <summary>บอทเขียนบรรทัดที่มีข้อความนี้หรือไม่ (ใช้ยืนยันว่าขั้นที่พังถูก log จริง)</summary>
    public bool BotLogged(string fragment) =>
        BotLines.Any(line => line.Contains(fragment, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Sink(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Sink(BotLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner._lines.Enqueue($"{logLevel}|{category}|{formatter(state, exception)}|{exception?.GetType().Name}");
    }
}

/// <summary>
/// ตัวประกาศ event ที่ล้มเฉพาะการประกาศ <c>orderCreated</c> ครั้งแรก แล้วทำงานปกติหลังจากนั้น —
/// <see cref="ILiveNotifier"/> คือ seam ตัวจริงที่ <c>Program.cs</c> แทนด้วย <c>LiveNotifier</c> ที่ส่งขึ้น hub
/// การให้มันโยนหนึ่งครั้งจึงจำลอง "รอบที่พังกลางทาง" ได้โดยไม่ต้องวางกับดักเทสต์ไว้ในโค้ดโปรดักชัน
/// </summary>
public sealed class FlakyNotifier : ILiveNotifier
{
    private readonly RecordingNotifier _inner = new();
    private int _calls;

    public IReadOnlyList<(string Event, object Payload)> Events => _inner.Events.ToArray();

    public int Failures { get; private set; }

    public Task OrderCreated(object order, int sessionId, int? primarySessionId = null)
    {
        if (Interlocked.Increment(ref _calls) == 1)
        {
            Failures++;
            return Task.FromException(new InvalidOperationException("จำลอง: ประกาศ orderCreated ไม่สำเร็จ"));
        }

        return _inner.OrderCreated(order, sessionId, primarySessionId);
    }

    public Task OrderStatusChanged(object order) => _inner.OrderStatusChanged(order);

    public Task OrderEdited(object order, int sessionId, int? primarySessionId = null) =>
        _inner.OrderEdited(order, sessionId, primarySessionId);

    public Task SessionChanged(object summary, int sessionId) => _inner.SessionChanged(summary, sessionId);

    public Task MenuItemChanged(int menuItemId, bool isAvailable, bool isActive) =>
        _inner.MenuItemChanged(menuItemId, isAvailable, isActive);

    public Task SessionClosed(int sessionId) => _inner.SessionClosed(sessionId);

    public Task SessionClosedMany(IEnumerable<int> sessionIds) => _inner.SessionClosedMany(sessionIds);
}

/// <summary>
/// Ticket 25 — Bot จำลองลูกค้า ผ่าน seam 1: แอปจริง + Postgres จริง (Demo เปิด)
///
/// ยืนยันพฤติกรรมภายนอกเท่านั้น: แถวในฐานข้อมูลที่ผู้ใช้เห็น สถานะของรอบ/ออเดอร์ และ event ที่ระบบประกาศ
/// (ผ่าน <see cref="RecordingNotifier"/>) — ไม่ยืนยันโครงสร้างภายในของบอท
///
/// <b>หนึ่งรอบ = หนึ่งการเรียก <see cref="DemoBotService.RunCycleAsync"/></b> เทสต์จึงไม่ต้องรอ 20–40 วินาที
/// และอ่านผลได้ทันที "เวลาปัจจุบัน" ของบอทมาจาก <c>DateTimeOffset.UtcNow</c> ครั้งเดียวต่อรอบ และการตัดสินว่า
/// รอบหมดเวลาหรือออเดอร์ค้างอ่านจากข้อมูล (<c>TableSession.EndsAt</c>, <c>Order.UpdatedAt</c>) ซึ่งเทสต์ตั้งได้ตรง ๆ
/// การสุ่มมาจาก seed คงที่ที่ <see cref="DemoBotService.From"/> ใช้เป็นค่าเริ่มต้น จึงไม่มี sleep และไม่มีผลข้างเคียง
/// จากเวลาจริง — host ที่ environment <c>Testing</c> ไม่ปล่อย loop ของบอทเอง (ดู <c>DemoBotService.ShouldRun</c>)
///
/// ทุกเทสต์ได้ host และฐานข้อมูลใหม่ของตัวเอง (โคลนจาก template เหมือนเทสต์อื่นของชุดนี้) จึงไม่มีสถานะข้ามกัน
/// </summary>
[Collection(ApiCollection.Name)]
public class DemoBotTests(PostgresFixture postgres)
{
    private static readonly Regex ReceiptPattern = new(@"^R(\d{8})-(\d{6})$", RegexOptions.Compiled);

    private readonly PostgresFixture _postgres = postgres;

    /// <summary>
    /// host ของเทสต์นี้: Demo เปิด, ฐานข้อมูลใหม่, และเลือกได้ว่าจะแทน notifier/log ด้วยของเทสต์หรือไม่
    /// ก่อนสตาร์ท host มีการใส่โต๊ะปิดใช้งานไว้หนึ่งแถว — <c>DemoSeeder</c> ถือว่า "มีโต๊ะ = seed ไปแล้ว"
    /// จึงข้ามการสร้างประวัติ 30 วัน (~1,000 บิล) ซึ่งไม่เกี่ยวกับสิ่งที่ ticket นี้ทดสอบ และทำให้แต่ละเทสต์ช้าโดยเปล่าประโยชน์
    /// (โต๊ะนั้นปิดใช้งาน จึงไม่ถูกนับเป็นโต๊ะว่างให้บอทเปิดและไม่กระทบจำนวนรอบที่บอทเปิด)
    /// </summary>
    private TestApp NewApp(ILiveNotifier? notifier = null, BotLog? log = null)
    {
        var connectionString = _postgres.NewDatabase();
        GuardAgainstTheDemoSeed(connectionString);

        Action<IServiceCollection>? testServices = null;
        if (notifier is not null || log is not null)
        {
            testServices = services =>
            {
                if (notifier is not null)
                {
                    services.AddSingleton(notifier);
                }

                if (log is not null)
                {
                    services.AddSingleton<ILoggerProvider>(log);
                }
            };
        }

        var app = new TestApp(connectionString, testServices, demo: true);
        _ = app.CreateClient();
        return app;
    }

    /// <summary>โต๊ะปิดใช้งานหนึ่งแถว = เงื่อนไข idempotent ของ <see cref="DemoSeeder"/> (<c>db.Tables.AnyAsync()</c>)</summary>
    private static void GuardAgainstTheDemoSeed(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        using var db = new AppDbContext(options);
        db.Tables.Add(new Table { Name = "seed-guard", Seats = 1, Zone = null, SortOrder = 0, IsActive = false });
        db.SaveChanges();
    }

    // ------------------------------------------------------------------ เปิดรอบให้ครบเป้า

    [Fact]
    public async Task A_cycle_opens_the_rounds_it_is_short_of_and_never_exceeds_the_target()
    {
        using var app = NewApp();
        await SeedTablesAsync(app, 6, "U1");
        await SeedMenuAndPackageAsync(app, "U1");

        await RunCyclesAsync(app, DemoBotService.TargetOpenRounds);

        var opened = await OpenBotRoundsAsync(app);
        Assert.Equal(DemoBotService.TargetOpenRounds, opened.Count);
        Assert.All(opened, round => Assert.Equal(SessionStatus.Open, round.Status));
        Assert.All(opened, round => Assert.True(round.EndsAt > DateTimeOffset.UtcNow));

        // รอบถัดไปอีกหลายรอบต้องไม่เปิดเพิ่ม และไม่ปิดของเดิม
        await RunCyclesAsync(app, 4);

        var after = await OpenBotRoundsAsync(app);
        Assert.Equal(DemoBotService.TargetOpenRounds, after.Count);
        Assert.Equal(opened.Select(r => r.Id), after.Select(r => r.Id));
    }

    [Fact]
    public async Task A_cycle_opens_nothing_when_every_table_is_taken_by_a_human()
    {
        using var app = NewApp();
        var tables = await SeedTablesAsync(app, 2, "U9");
        await SeedMenuAndPackageAsync(app, "U9");

        // สองโต๊ะเท่านั้น และทั้งคู่ถูกคนจริงถืออยู่ → บอทไม่มีที่เปิดเพิ่ม และต้องไม่แตะของคน
        var human = new List<int>();
        for (var i = 0; i < 2; i++)
        {
            human.Add((await OpenHumanRoundAsync(app, tables[i])).SessionId);
        }

        await RunCyclesAsync(app, 3);

        Assert.Empty(await OpenBotRoundsAsync(app));
        foreach (var sessionId in human)
        {
            var row = await RoundAsync(app, sessionId);
            Assert.False(row.IsDemoBot);
            Assert.Equal(SessionStatus.Open, row.Status);
        }
    }

    // ------------------------------------------------------------------ ไม่แตะรอบของคนจริง (user story 110)

    [Fact]
    public async Task A_cycle_never_touches_a_round_a_human_opened()
    {
        using var app = NewApp();
        var tables = await SeedTablesAsync(app, 6, "U2");
        await SeedMenuAndPackageAsync(app, "U2");

        // รอบของคนจริง: หมดเวลาไปแล้วหนึ่งชั่วโมง และมีออเดอร์ค้างในครัวเกิน 120 วินาที
        // ขั้นจ่ายเงินและขั้นเลื่อนสถานะจะ "อยาก" แตะรอบนี้ทั้งคู่ — บอทต้องไม่แตะเลย
        var human = await OpenHumanRoundAsync(app, tables[0]);
        var expiredAt = DateTimeOffset.UtcNow.AddHours(-1);
        await app.WithDbAsync(async db =>
        {
            await db.TableSessions.Where(s => s.Id == human.SessionId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.OpenedAt, expiredAt.AddHours(-2))
                .SetProperty(x => x.EndsAt, expiredAt));
            return 0;
        });
        await AddOrderDirectlyAsync(app, human.SessionId, OrderStatus.Pending, ageSeconds: 600);

        await RunCyclesAsync(app, 5);

        var row = await RoundAsync(app, human.SessionId);
        Assert.False(row.IsDemoBot);
        Assert.Equal(SessionStatus.Open, row.Status);
        Assert.Null(row.PaymentId);
        Assert.Equal(expiredAt.ToUnixTimeSeconds(), row.EndsAt.ToUnixTimeSeconds());
        Assert.Equal("Pending", row.OrderStatus);

        // โต๊ะของคนคนนี้ยังเป็นรอบเดิม (ไม่ถูกเปิดทับ ไม่ถูกยกเลิก) และบอทเปิดรอบของตัวเองบนโต๊ะอื่น
        var sessionsOnTable = await app.WithDbAsync(db => db.TableSessions.AsNoTracking()
            .Where(s => s.TableId == human.TableId).Select(s => s.Id).ToListAsync());
        Assert.Equal([human.SessionId], sessionsOnTable);
        Assert.Equal(DemoBotService.TargetOpenRounds, (await OpenBotRoundsAsync(app)).Count);
    }

    [Fact]
    public async Task A_cycle_does_not_open_a_table_a_human_holds()
    {
        using var app = NewApp();
        var tables = await SeedTablesAsync(app, 6, "U3");
        await SeedMenuAndPackageAsync(app, "U3");

        // สี่โต๊ะแรกถูกคนจริงจับไว้ (หนึ่งในนั้นหมดเวลาแล้ว) — บอทเหลือที่ว่างแค่สองโต๊ะ
        var held = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var opened = await OpenHumanRoundAsync(app, tables[i]);
            held.Add(opened.SessionId);
            if (i == 0)
            {
                await app.WithDbAsync(async db =>
                {
                    await db.TableSessions.Where(s => s.Id == opened.SessionId).ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.EndsAt, DateTimeOffset.UtcNow.AddMinutes(-5)));
                    return 0;
                });
            }
        }

        await RunCyclesAsync(app, 4);

        var botRounds = await OpenBotRoundsAsync(app);
        Assert.Equal(2, botRounds.Count);
        Assert.DoesNotContain(botRounds, round => tables.Take(4).Contains(round.TableId));

        // รอบของคนทั้งสี่อยู่ครบ สถานะเดิม ไม่มีบิล และเป็นรอบเดียวบนโต๊ะของตัวเอง
        foreach (var sessionId in held)
        {
            var row = await RoundAsync(app, sessionId);
            Assert.False(row.IsDemoBot);
            Assert.Equal(SessionStatus.Open, row.Status);
            Assert.Null(row.PaymentId);
            Assert.Equal(sessionId, await app.WithDbAsync(db => db.TableSessions.AsNoTracking()
                .Where(s => s.TableId == row.TableId).Select(s => s.Id).SingleAsync()));
        }
    }

    // ------------------------------------------------------------------ ผ่านบริการชุดเดียวกับ endpoint

    [Fact]
    public async Task A_bot_order_goes_through_the_same_service_and_is_announced()
    {
        var notifier = new RecordingNotifier();
        using var app = NewApp(notifier);
        await SeedTablesAsync(app, 6, "U4");
        await SeedMenuAndPackageAsync(app, "U4");

        await RunCyclesAsync(app, 3);

        var orders = await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Include(o => o.Items).Include(o => o.TableSession)
            .Where(o => o.TableSession!.IsDemoBot)
            .OrderBy(o => o.Id).ToListAsync());

        Assert.NotEmpty(orders);

        // 2–6 รายการต่อรอบ อยู่ที่สถานะตั้งต้น และอยู่ในเพดานต่อเมนูของร้าน (ลิมิตที่ OrderService บังคับ)
        Assert.All(orders, order => Assert.InRange(order.Items.Sum(i => i.Quantity),
            DemoBotService.MinOrderItems, DemoBotService.MaxOrderItems));
        Assert.All(orders, order => Assert.Equal(OrderStatus.Pending, order.Status));
        Assert.All(orders, order => Assert.All(order.Items, item => Assert.InRange(item.Quantity, 1, 5)));
        Assert.All(orders, order => Assert.True(order.TableSession!.IsDemoBot));

        // event ออกมาจากบริการเดียวกัน: orderCreated ของรอบบอทเท่านั้น พร้อมข้อมูลร้าน/เลขรอบ
        var announced = notifier.Events.Where(e => e.Event == "orderCreated")
            .Select(e => (OrderCreatedCall)e.Payload).ToList();
        Assert.Equal(orders.Count, announced.Count);
        var botRoundIds = (await OpenBotRoundsAsync(app)).Select(r => r.Id).ToList();
        Assert.All(announced, call => Assert.Contains(call.SessionId, botRoundIds));
        Assert.All(announced, call => Assert.Null(call.PrimarySessionId));

        // บอทหมุนไปทุกรอบ (รอบละออเดอร์ในรอบนี้) เลขรอบของแต่ละรอบจึงเริ่มที่ 1
        foreach (var group in orders.GroupBy(o => o.TableSessionId))
        {
            var numbers = group.Select(o => o.Number).Order().ToList();
            Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
        }

        // คิวของเจ้าของร้าน (ที่ป๊อปอัปของ ticket 15 อ่าน) มองเห็นออเดอร์ของบอท
        var owner = await OwnerClientAsync(app);
        var queue = await JsonAsync(await owner.GetAsync("/api/orders"));
        var queued = queue.EnumerateArray().Select(o => o.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(orders[0].Id, queued);
    }

    // ------------------------------------------------------------------ เลื่อนสถานะเฉพาะออเดอร์ที่ค้าง

    [Fact]
    public async Task The_advance_step_moves_only_orders_that_are_actually_stale()
    {
        using var app = NewApp();
        var tables = await SeedTablesAsync(app, 2, "U5");
        var packageId = await SeedMenuAndPackageAsync(app, "U5");

        var staleRound = await SeedRoundDirectlyAsync(app, tables[0], packageId, isDemoBot: true,
            endsAt: DateTimeOffset.UtcNow.AddMinutes(40), orders: [(OrderStatus.Pending, 300)]);
        var freshRound = await SeedRoundDirectlyAsync(app, tables[1], packageId,
            isDemoBot: true, endsAt: DateTimeOffset.UtcNow.AddMinutes(40), orders: [(OrderStatus.Pending, 10)]);

        await RunCyclesAsync(app, 1);

        // ค้าง 300 วินาที → เดินหนึ่งขั้น; เพิ่งส่ง 10 วินาทีก่อน → ไม่ถูกแตะ
        Assert.Equal("Preparing", (await RoundAsync(app, staleRound)).OrderStatus);
        Assert.Equal("Pending", (await RoundAsync(app, freshRound)).OrderStatus);

        // Ready คือขั้นสุดท้าย: ออเดอร์ที่ Ready แล้วและค้างนานต้องไม่ถูกเลื่อนอีก
        await SetOrderStatusDirectlyAsync(app, staleRound, OrderStatus.Ready, ageSeconds: 900);
        await RunCyclesAsync(app, 1);
        Assert.Equal("Ready", (await RoundAsync(app, staleRound)).OrderStatus);
    }

    // ------------------------------------------------------------------ จ่ายรอบที่หมดเวลาและไม่มีออเดอร์ค้าง

    [Fact]
    public async Task The_payout_step_skips_a_round_with_work_in_progress_and_pays_one_without()
    {
        var notifier = new RecordingNotifier();
        using var app = NewApp(notifier);
        var tables = await SeedTablesAsync(app, 3, "U6");
        var packageId = await SeedMenuAndPackageAsync(app, "U6");
        var now = DateTimeOffset.UtcNow;

        var blocked = await SeedRoundDirectlyAsync(app, tables[0], packageId, isDemoBot: true,
            endsAt: now.AddMinutes(-10), orders: [(OrderStatus.Pending, 10)]);
        var payable = await SeedRoundDirectlyAsync(app, tables[1], packageId, isDemoBot: true,
            endsAt: now.AddMinutes(-10), orders: [(OrderStatus.Ready, 10)]);
        var notExpired = await SeedRoundDirectlyAsync(app, tables[2], packageId, isDemoBot: true,
            endsAt: now.AddMinutes(30), orders: [(OrderStatus.Ready, 10)]);

        await RunCyclesAsync(app, 1);

        var paid = await RoundAsync(app, payable);
        Assert.Equal(SessionStatus.Paid, paid.Status);
        Assert.Equal("Cash", paid.PaymentMethod);
        Assert.NotNull(paid.PaymentId);
        Assert.Matches(ReceiptPattern, paid.ReceiptNo!);
        Assert.Equal(paid.PaymentTotal, paid.PaymentReceived);
        Assert.Equal(0m, paid.PaymentChange);

        // ออเดอร์ค้างขวางการปิดบิล และรอบที่ยังไม่หมดเวลาก็ไม่ถูกปิด
        var stillBlocked = await RoundAsync(app, blocked);
        Assert.Equal(SessionStatus.Open, stillBlocked.Status);
        Assert.Null(stillBlocked.PaymentId);
        Assert.Equal(SessionStatus.Open, (await RoundAsync(app, notExpired)).Status);

        // ปิดบิลผ่านบริการเดียวกับหน้าจอเจ้าของร้าน: sessionClosed ถูกประกาศออกไป (ของรอบที่ปิดเท่านั้น)
        var closed = notifier.Events.Where(e => e.Event == "sessionClosed")
            .Select(e => (int)e.Payload).ToList();
        Assert.Contains(payable, closed);
        Assert.DoesNotContain(blocked, closed);
        Assert.DoesNotContain(notExpired, closed);
    }

    [Fact]
    public async Task The_advance_step_turns_a_stale_blocking_order_ready_so_a_later_cycle_can_pay_the_round()
    {
        using var app = NewApp();
        var tables = await SeedTablesAsync(app, 1, "U7");
        var packageId = await SeedMenuAndPackageAsync(app, "U7");

        // รอบบอทที่หมดเวลาแล้วและมีออเดอร์ค้างเกิน 120 วินาที: รอบแรกเลื่อน Pending → Preparing (Preparing ยังขวาง
        // การปิดบิล จึงยังไม่จ่าย), รอบที่สอง Preparing → Ready แล้วขั้นจ่ายเงินของรอบเดียวกันจึงปิดบิลได้ทันที
        var round = await SeedRoundDirectlyAsync(app, tables[0], packageId, isDemoBot: true,
            endsAt: DateTimeOffset.UtcNow.AddMinutes(-10), orders: [(OrderStatus.Pending, 300)]);

        await RunCyclesAsync(app, 1);
        Assert.Equal("Preparing", (await RoundAsync(app, round)).OrderStatus);
        Assert.Equal(SessionStatus.Open, (await RoundAsync(app, round)).Status);

        await SetOrderStatusDirectlyAsync(app, round, OrderStatus.Preparing, ageSeconds: 300);
        await RunCyclesAsync(app, 1);

        // "Ready" คือขั้นสุดท้าย: ออเดอร์ไม่ขวางการปิดบิลอีกต่อไป รอบเดียวกันจึงรับเงินได้ในรอบนั้นเลย
        var paid = await RoundAsync(app, round);
        Assert.Equal("Ready", paid.OrderStatus);
        Assert.Equal(SessionStatus.Paid, paid.Status);
        Assert.Equal("Cash", paid.PaymentMethod);
        Assert.Matches(ReceiptPattern, paid.ReceiptNo!);
    }

    // ------------------------------------------------------------------ รอบที่พังต้องไม่ฆ่าบอท

    [Fact]
    public async Task A_cycle_that_throws_is_logged_and_the_following_cycle_still_works()
    {
        var notifier = new FlakyNotifier();
        var log = new BotLog();
        using var app = NewApp(notifier, log);
        var tables = await SeedTablesAsync(app, 1, "U8");
        var packageId = await SeedMenuAndPackageAsync(app, "U8");
        var round = await SeedRoundDirectlyAsync(app, tables[0], packageId, isDemoBot: true,
            endsAt: DateTimeOffset.UtcNow.AddMinutes(40), orders: [(OrderStatus.Pending, 300)]);

        // รอบแรก: ประกาศ orderCreated ล้ม → ขั้นสั่งอาหารพังทั้งขั้น แต่ขั้นเลื่อนสถานะในรอบเดียวกันยังทำต่อ
        await RunCyclesAsync(app, 1);
        Assert.Equal(1, notifier.Failures);
        Assert.True(log.BotLogged("place an order"), "ต้อง log ว่าขั้นไหนพัง");
        Assert.True(log.BotLogged("InvalidOperationException"), "ต้อง log ตัวข้อผิดพลาดด้วย");
        Assert.Equal("Preparing", (await RoundAsync(app, round)).OrderStatus);
        var afterFailure = await app.WithDbAsync(db => db.Orders.AsNoTracking().CountAsync());

        // รอบถัดไปทำงานปกติ: ออเดอร์ใหม่ถูกสร้างและประกาศจริง และไม่มีอะไรพังเพิ่ม
        await RunCyclesAsync(app, 1);
        Assert.Equal(1, notifier.Failures);
        Assert.Single(notifier.Events, e => e.Event == "orderCreated");
        Assert.Equal(afterFailure + 1, await app.WithDbAsync(db => db.Orders.AsNoTracking().CountAsync()));
    }

    // ------------------------------------------------------------------ ปิด Demo = ไม่มีบอท

    [Fact]
    public async Task With_the_demo_off_the_bot_does_not_exist()
    {
        var log = new BotLog();
        using var app = new TestApp(_postgres.NewDatabase(),
            services => services.AddSingleton<ILoggerProvider>(log));
        _ = app.CreateClient();

        // สวิตช์เดียว: Demo ปิด ⇒ ไม่มีบอท (ด่านแรกก่อนทุกอย่าง)
        Assert.False(DemoBotService.ShouldRun(app.Services.GetRequiredService<IConfiguration>(),
            app.Services.GetRequiredService<IHostEnvironment>()));

        // loop ของ host ที่ปิด Demo: จบเองทันที ไม่แตะฐานข้อมูล และไม่เขียน log ของบอทแม้แต่บรรทัดเดียว
        var bot = DemoBotService.From(app.Services);
        await bot.StartAsync(CancellationToken.None);
        await (bot.ExecuteTask ?? Task.CompletedTask);
        await bot.StopAsync(CancellationToken.None);

        Assert.Empty(log.BotLines);
        Assert.Equal(0, await app.WithDbAsync(db => db.TableSessions.CountAsync()));

        // และรอบที่เรียกเองก็ไม่ทำอะไรเลย
        await bot.RunCycleAsync();
        Assert.Equal(0, await app.WithDbAsync(db => db.TableSessions.CountAsync()));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>เรียกหนึ่งรอบของบอทต่อครั้ง — หนึ่งรอบ = หนึ่ง <see cref="DemoBotService.RunCycleAsync"/></summary>
    private static async Task RunCyclesAsync(TestApp app, int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            using var scope = app.NewScope();
            await DemoBotService.From(scope.ServiceProvider).RunCycleAsync();
        }
    }

    /// <summary>โต๊ะของเทสต์นี้ — ชื่อมี prefix เฉพาะเทสต์เพื่อให้อ่านฐานข้อมูลตามรอยได้</summary>
    private static async Task<int[]> SeedTablesAsync(TestApp app, int count, string prefix) =>
        await app.WithDbAsync(async db =>
        {
            var tables = Enumerable.Range(1, count).Select(i => new Table
            {
                Name = $"{prefix}-{i}",
                Seats = 4,
                Zone = "T",
                SortOrder = i,
            }).ToList();
            db.Tables.AddRange(tables);
            await db.SaveChangesAsync();
            return tables.Select(t => t.Id).ToArray();
        });

    /// <summary>
    /// แพ็กเกจหนึ่งตัว (rank ว่างตัวแรก) + เมนูบุฟเฟ่ต์ rank 1 สองอย่างและเครื่องดื่ม AlaCarte สองอย่าง
    /// ที่มีของและ active ทั้งหมด — บอทจึงมีของให้สั่งจริงในทุกเทสต์
    /// </summary>
    private static async Task<int> SeedMenuAndPackageAsync(TestApp app, string prefix) =>
        await app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var rank = Enumerable.Range(1, 98).First(r => !taken.Contains(r));
            var category = new Category { Name = $"{prefix}-menu", SortOrder = 1 };
            db.Categories.Add(category);
            db.MenuItems.AddRange(
                new MenuItem
                {
                    Category = category, Name = $"{prefix}-หมูสไลซ์", Kind = MenuItemKind.Buffet,
                    MinPackageRank = 1, Price = 0m, SortOrder = 1,
                },
                new MenuItem
                {
                    Category = category, Name = $"{prefix}-ผักรวม", Kind = MenuItemKind.Buffet,
                    MinPackageRank = 1, Price = 0m, SortOrder = 2,
                },
                new MenuItem
                {
                    Category = category, Name = $"{prefix}-โค้ก", Kind = MenuItemKind.AlaCarte,
                    Price = 25m, SortOrder = 3,
                },
                new MenuItem
                {
                    Category = category, Name = $"{prefix}-ชาไทย", Kind = MenuItemKind.AlaCarte,
                    Price = 45m, SortOrder = 4,
                });
            var package = new Package
            {
                Name = $"{prefix}-Gold", Rank = rank, AdultPrice = 399m, ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private sealed record HumanRound(int SessionId, int TableId);

    /// <summary>รอบที่คนจริงเปิด — ผ่านเส้นทางจริงของเจ้าของร้าน (POST /api/sessions)</summary>
    private static async Task<HumanRound> OpenHumanRoundAsync(TestApp app, int tableId)
    {
        var packageId = await app.WithDbAsync(db => db.Packages.AsNoTracking()
            .OrderBy(p => p.Id).Select(p => p.Id).FirstAsync());
        var owner = await OwnerClientAsync(app);
        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = 2, childCount = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        return new HumanRound(body.GetProperty("id").GetInt32(), body.GetProperty("tableId").GetInt32());
    }

    /// <summary>รอบของบอทที่ยังมีชีวิต (Open และไม่ได้ถูกรวมเข้าบิลของรอบอื่น) — เป้าคือสามรอบนี้</summary>
    private static Task<List<(int Id, int TableId, SessionStatus Status, DateTimeOffset EndsAt)>> OpenBotRoundsAsync(
        TestApp app) =>
        app.WithDbAsync(async db => (await db.TableSessions.AsNoTracking()
                .Where(s => s.IsDemoBot && s.Status == SessionStatus.Open && s.MergedIntoSessionId == null)
                .OrderBy(s => s.Id)
                .Select(s => new { s.Id, s.TableId, s.Status, s.EndsAt })
                .ToListAsync())
            .Select(s => (s.Id, s.TableId, s.Status, s.EndsAt)).ToList());

    private sealed record RoundRow(int Id, int TableId, bool IsDemoBot, SessionStatus Status,
        DateTimeOffset EndsAt, int? PaymentId, string? PaymentMethod, decimal? PaymentTotal,
        decimal? PaymentReceived, decimal? PaymentChange, string? ReceiptNo, string? OrderStatus);

    /// <summary>สถานะที่ผู้ใช้เห็นของรอบหนึ่งรอบ พร้อมออเดอร์แรก (เรียงตามเลขรอบ)</summary>
    private static Task<RoundRow> RoundAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(async db =>
        {
            var row = await db.TableSessions.AsNoTracking()
                .Include(s => s.Orders.OrderBy(o => o.Number))
                .SingleAsync(s => s.Id == sessionId);
            var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.TableSessionId == sessionId);
            return new RoundRow(row.Id, row.TableId, row.IsDemoBot, row.Status, row.EndsAt,
                payment?.Id, payment?.Method.ToString(), payment?.Total, payment?.AmountReceived,
                payment?.Change, payment?.ReceiptNo, row.Orders.FirstOrDefault()?.Status.ToString());
        });

    /// <summary>
    /// รอบหนึ่งรอบที่เขียนลงฐานข้อมูลตรง ๆ พร้อมออเดอร์ตามสถานะ/อายุที่กำหนด — ใช้จัดฉาก "ออเดอร์ค้าง" และ
    /// "รอบหมดเวลา" โดยไม่ต้องรอเวลาจริง (วิธีตั้ง instants แบบเดียวกับเทสต์อื่นของชุดนี้) การแทรกเองนี้ไม่มีผลต่อ
    /// สิ่งที่ทดสอบ: บอทอ่านฉาก แล้วต้องเดินผ่านบริการจริงทุกขั้น
    /// </summary>
    private static async Task<int> SeedRoundDirectlyAsync(TestApp app, int tableId, int packageId,
        bool isDemoBot, DateTimeOffset endsAt, params (OrderStatus Status, int AgeSeconds)[] orders)
    {
        return await app.WithDbAsync(async db =>
        {
            var package = await db.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId);
            var menuItemId = await db.MenuItems.AsNoTracking().Select(m => m.Id).FirstAsync();
            var now = DateTimeOffset.UtcNow;

            var round = new TableSession
            {
                TableId = tableId,
                PackageId = packageId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                AdultCount = 2,
                ChildCount = 0,
                PackageNameSnapshot = package.Name,
                AdultPriceSnapshot = package.AdultPrice,
                ChildPriceSnapshot = package.ChildPrice,
                PackageRankSnapshot = package.Rank,
                OpenedAt = now.AddMinutes(-package.DurationMinutes),
                EndsAt = endsAt,
                Status = SessionStatus.Open,
                IsDemoBot = isDemoBot,
            };

            var number = 0;
            foreach (var (status, ageSeconds) in orders)
            {
                var at = now.AddSeconds(-ageSeconds);
                round.Orders.Add(new Order
                {
                    Number = ++number,
                    Status = status,
                    CreatedAt = at,
                    UpdatedAt = at,
                    Items =
                    [
                        new OrderItem
                        {
                            MenuItemId = menuItemId,
                            NameSnapshot = "ของบอท",
                            KindSnapshot = MenuItemKind.Buffet,
                            UnitPrice = 0m,
                            Quantity = 1,
                        },
                    ],
                });
            }

            db.TableSessions.Add(round);
            await db.SaveChangesAsync();
            return round.Id;
        });
    }

    private static Task AddOrderDirectlyAsync(TestApp app, int sessionId, OrderStatus status, int ageSeconds) =>
        app.WithDbAsync(async db =>
        {
            var menuItemId = await db.MenuItems.AsNoTracking().Select(m => m.Id).FirstAsync();
            var next = (await db.Orders.Where(o => o.TableSessionId == sessionId)
                .MaxAsync(o => (int?)o.Number) ?? 0) + 1;
            var at = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds);
            db.Orders.Add(new Order
            {
                TableSessionId = sessionId,
                Number = next,
                Status = status,
                CreatedAt = at,
                UpdatedAt = at,
                Items =
                [
                    new OrderItem
                    {
                        MenuItemId = menuItemId, NameSnapshot = "ของคนจริง",
                        KindSnapshot = MenuItemKind.Buffet, UnitPrice = 0m, Quantity = 1,
                    },
                ],
            });
            await db.SaveChangesAsync();
            return 0;
        });

    private static Task SetOrderStatusDirectlyAsync(TestApp app, int sessionId, OrderStatus status,
        int ageSeconds) =>
        app.WithDbAsync(async db =>
        {
            var at = DateTimeOffset.UtcNow.AddSeconds(-ageSeconds);
            await db.Orders.Where(o => o.TableSessionId == sessionId).ExecuteUpdateAsync(o => o
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.UpdatedAt, at));
            return 0;
        });

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

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key}");

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
