using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Common;
using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Ticket 11 — จอครัวผ่าน seam 1: แอปจริง + Postgres จริง + ไคลเอนต์ SignalR จริงที่ต่อ <c>/hubs/live</c>
/// ของ in-process server (long polling ผ่าน handler ของ TestServer)
///
/// กฎ group ถูกพิสูจน์ด้วย "สิ่งที่แต่ละการเชื่อมต่อได้รับจริง" ไม่ใช่ชื่อ group: <see cref="RecordingNotifier"/>
/// แทนที่ <see cref="LiveNotifier"/> ทั้งตัว จึงเห็นแค่ว่าระบบประกาศอะไร ไม่เห็นว่าใครได้รับ
/// การพิสูจน์ว่า "ไม่ได้รับ" ใช้ barrier: หลัง event ที่ทดสอบ ส่ง <c>menuItemChanged</c> (group menu ที่ทุกคนอยู่)
/// แล้วรอจนทุกการเชื่อมต่อได้ barrier นั้น — SignalR ส่งถึงการเชื่อมต่อหนึ่งตามลำดับ ถ้าการเชื่อมต่อนั้นอยู่ใน
/// group ของ event ที่ทดสอบ event นั้นต้องมาถึงก่อน barrier เสมอ จึงไม่ต้องรอเวลาเลย
/// </summary>
[Collection(ApiCollection.Name)]
public class KitchenBoardTests(PostgresFixture postgres)
{
    /// <summary>acceptance ของ ticket: การ์ดต้องโผล่ภายใน 5 วินาที</summary>
    private static readonly TimeSpan Within = TimeSpan.FromSeconds(5);

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

    /// <summary>cookie <c>fq_auth</c> ของบัญชีหนึ่ง สำหรับส่งไปกับทุกคำขอของไคลเอนต์ SignalR</summary>
    private static async Task<string> AuthCookieAsync(TestApp app, string emailKey)
    {
        using var raw = new HttpClient(app.Server.CreateHandler()) { BaseAddress = app.Server.BaseAddress };
        var login = await raw.PostAsJsonAsync("/api/auth/login", new
        {
            email = ConfigValue(app, emailKey),
            password = ConfigValue(app, "DemoSeed:Password"),
        });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return login.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';')[0])
            .Single(c => c.StartsWith("fq_auth=", StringComparison.Ordinal));
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueName(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..18];

    private static async Task<int> CreateTableAsync(HttpClient owner)
    {
        var response = await owner.PostAsJsonAsync("/api/tables",
            new { name = UniqueName("K"), seats = 4, zone = "A" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await JsonAsync(response)).GetProperty("id").GetInt32();
    }

    /// <summary>Rank มี unique index ร่วมกับเทสต์อื่นในคอลเลกชัน จึงสร้างผ่านฐานข้อมูลด้วย rank ที่ยังว่าง</summary>
    private static Task<int> SeedPackageAsync(TestApp app) =>
        app.WithDbAsync(async db =>
        {
            var taken = await db.Packages.Select(p => p.Rank).ToListAsync();
            var package = new Package
            {
                Name = $"P{Guid.NewGuid():N}"[..12],
                Rank = Enumerable.Range(1, 99).First(r => !taken.Contains(r)),
                AdultPrice = 399m,
                ChildPrice = 199m,
                DurationMinutes = 90,
            };
            db.Packages.Add(package);
            await db.SaveChangesAsync();
            return package.Id;
        });

    private sealed record Menu(int MeatId, string MeatName, int DrinkId, string DrinkName);

    /// <summary>เมนูหนึ่งชุดผ่านเส้นทางจริงของ ticket 05: เนื้อบุฟเฟ่ต์ rank 1 และเครื่องดื่ม à la carte</summary>
    private static async Task<Menu> CreateMenuAsync(HttpClient owner)
    {
        var category = await owner.PostAsJsonAsync("/api/categories", new { name = UniqueName("C") });
        Assert.Equal(HttpStatusCode.Created, category.StatusCode);
        var categoryId = (await JsonAsync(category)).GetProperty("id").GetInt32();

        var meatName = UniqueName("หมู");
        var meat = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = meatName, categoryId, kind = "Buffet", minPackageRank = 1, price = 0m, isAvailable = true,
        });
        Assert.Equal(HttpStatusCode.Created, meat.StatusCode);

        var drinkName = UniqueName("ชา");
        var drink = await owner.PostAsJsonAsync("/api/menu-items", new
        {
            name = drinkName, categoryId, kind = "AlaCarte", price = 45m,
        });
        Assert.Equal(HttpStatusCode.Created, drink.StatusCode);

        return new Menu((await JsonAsync(meat)).GetProperty("id").GetInt32(), meatName,
            (await JsonAsync(drink)).GetProperty("id").GetInt32(), drinkName);
    }

    private sealed record OpenedSession(int Id, string Token, string TableName);

    /// <summary>รอบที่เปิดจากเส้นทางจริงของ ticket 07 พร้อม token ของ QR</summary>
    private static async Task<OpenedSession> OpenSessionAsync(TestApp app, HttpClient owner)
    {
        var tableId = await CreateTableAsync(owner);
        var packageId = await SeedPackageAsync(app);
        var response = await owner.PostAsJsonAsync("/api/sessions",
            new { tableId, packageId, adultCount = 2, childCount = 0 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await JsonAsync(response);
        var id = body.GetProperty("id").GetInt32();
        var token = await app.WithDbAsync(async db =>
            (await db.TableSessions.AsNoTracking().SingleAsync(s => s.Id == id)).Token);
        return new OpenedSession(id, token, body.GetProperty("tableName").GetString()!);
    }

    /// <summary>ลูกค้าส่งออเดอร์ด้วย token (ไม่มี auth) แล้วคืน id ของรอบที่เพิ่งบันทึก</summary>
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
        note is null ? new { menuItemId, quantity } : new { menuItemId, quantity, note };

    private static Task SetOrderAsync(TestApp app, int orderId, OrderStatus status, DateTimeOffset? createdAt = null) =>
        app.WithDbAsync(async db =>
        {
            var order = await db.Orders.SingleAsync(o => o.Id == orderId);
            order.Status = status;
            order.CancelReason = status == OrderStatus.Cancelled ? "วัตถุดิบหมด" : null;
            if (createdAt is { } at)
            {
                order.CreatedAt = at;
            }

            await db.SaveChangesAsync();
            return 0;
        });

    private static Task MarkPaidAsync(TestApp app, int sessionId) =>
        app.WithDbAsync(async db =>
        {
            // การชำระเงินเป็นของ ticket 18 — ที่นี่ตั้งสถานะด้วยข้อมูลจึงไม่รอใคร
            await db.TableSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, SessionStatus.Paid)
                .SetProperty(x => x.ClosedAt, DateTimeOffset.UtcNow));
            return 0;
        });

    // ------------------------------------------------------------------ ไคลเอนต์ hub

    private static readonly string[] AllEvents =
        ["orderCreated", "orderStatusChanged", "orderEdited", "sessionChanged", "menuItemChanged", "sessionClosed"];

    /// <summary>
    /// ชื่อบนสายคือชื่อเมธอดของ <see cref="ILiveClient"/> (<c>OrderCreated</c>) — ไคลเอนต์ JavaScript จับคู่ชื่อแบบไม่สนตัวพิมพ์
    /// จึงฟัง <c>orderCreated</c> ได้ตรงสัญญา แต่ไคลเอนต์ .NET สนตัวพิมพ์ จึงต้องลงทะเบียนด้วยชื่อบนสาย
    /// </summary>
    private static string Wire(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>ทุก event ที่การเชื่อมต่อหนึ่งได้รับ ตามลำดับที่มาถึง</summary>
    private sealed class Inbox
    {
        private readonly Lock _gate = new();
        private readonly List<(string Event, JsonElement Payload)> _events = [];
        private readonly List<(string Event, Func<JsonElement, bool> Match, TaskCompletionSource<JsonElement> Done)>
            _waiters = [];

        public void Record(string name, JsonElement payload)
        {
            lock (_gate)
            {
                _events.Add((name, payload));
                foreach (var waiter in _waiters.Where(w => w.Event == name && w.Match(payload)).ToList())
                {
                    waiter.Done.TrySetResult(payload);
                    _waiters.Remove(waiter);
                }
            }
        }

        public List<JsonElement> Of(string name)
        {
            lock (_gate)
            {
                return _events.Where(e => e.Event == name).Select(e => e.Payload).ToList();
            }
        }

        public List<string> Names()
        {
            lock (_gate)
            {
                return _events.Select(e => e.Event).ToList();
            }
        }

        public Task<JsonElement> WaitForAsync(string name, Func<JsonElement, bool> match, TimeSpan timeout)
        {
            lock (_gate)
            {
                var seen = _events.FirstOrDefault(e => e.Event == name && match(e.Payload));
                if (seen.Event is not null)
                {
                    return Task.FromResult(seen.Payload);
                }

                var done = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((name, match, done));
                return done.Task.WaitAsync(timeout);
            }
        }
    }

    private sealed record Live(HubConnection Hub, Inbox Inbox) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    /// <summary>WebSocket ผ่าน TestServer ตรง ๆ (เหมือนเบราว์เซอร์หลัง Caddy) พร้อม cookie ของพนักงานถ้ามี</summary>
    private static HubConnection BuildHub(TestApp app, string? token = null, string? cookie = null)
    {
        var path = token is null ? "/hubs/live" : $"/hubs/live?token={Uri.EscapeDataString(token)}";
        return new HubConnectionBuilder()
            .WithUrl(new Uri(app.Server.BaseAddress, path), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.SkipNegotiation = true;
                o.WebSocketFactory = async (context, ct) =>
                {
                    var client = app.Server.CreateWebSocketClient();
                    if (cookie is not null)
                    {
                        client.ConfigureRequest = request => request.Headers.Cookie = cookie;
                    }

                    return await client.ConnectAsync(context.Uri, ct);
                };
            })
            .Build();
    }

    /// <summary>ต่อ hub แล้วบันทึกทุก event; คืนเมื่อการเชื่อมต่อผ่าน OnConnected ของ hub แล้วเท่านั้น</summary>
    private static async Task<Live> ConnectAsync(TestApp app, string? token = null, string? cookie = null)
    {
        var hub = BuildHub(app, token, cookie);
        var inbox = new Inbox();
        foreach (var name in AllEvents)
        {
            hub.On<JsonElement>(Wire(name), payload => inbox.Record(name, payload));
        }

        await hub.StartAsync();
        var live = new Live(hub, inbox);

        // StartAsync จบที่ handshake ซึ่งเกิดก่อน OnConnectedAsync (ที่เพิ่ม group) จึงยังไม่รู้ว่าเข้า group แล้วหรือยัง:
        // ส่ง barrier ซ้ำจนกว่าจะได้รับหนึ่งตัว (ส่งก่อนเข้า group = หายไปเฉย ๆ) แล้วจึงถือว่าพร้อม
        var deadline = DateTimeOffset.UtcNow + Within;
        while (true)
        {
            try
            {
                await BarrierAsync(app, TimeSpan.FromMilliseconds(250), live);
                return live;
            }
            catch (TimeoutException) when (DateTimeOffset.UtcNow < deadline)
            {
                // ยังไม่เข้า group menu — ลองใหม่
            }
        }
    }

    /// <summary>
    /// ส่ง <c>menuItemChanged</c> ผ่าน <see cref="LiveNotifier"/> ตัวจริงแล้วรอให้ทุกการเชื่อมต่อได้รับ — ทุกอย่างที่ส่ง
    /// ก่อนหน้านี้ถึงการเชื่อมต่อเหล่านั้นแล้ว (หรือจะไม่มีวันถึง)
    /// </summary>
    private static Task BarrierAsync(TestApp app, params Live[] connections) => BarrierAsync(app, Within, connections);

    private static async Task BarrierAsync(TestApp app, TimeSpan timeout, params Live[] connections)
    {
        var marker = -Random.Shared.Next(1, int.MaxValue);
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILiveNotifier>().MenuItemChanged(marker, true, true);
        }

        foreach (var live in connections)
        {
            await live.Inbox.WaitForAsync("menuItemChanged",
                p => p.GetProperty("menuItemId").GetInt32() == marker, timeout);
        }
    }

    /// <summary>
    /// hub ปฏิเสธด้วย <c>Context.Abort()</c> ใน OnConnectedAsync: handshake อาจผ่านไปแล้ว (StartAsync สำเร็จ)
    /// แต่การเชื่อมต่อต้องถูกปิดจากฝั่งเซิร์ฟเวอร์ทันที โดยไม่ได้รับ event ใดเลย
    /// </summary>
    private static async Task AssertRejectedAsync(TestApp app, string? token = null, string? cookie = null)
    {
        await using var hub = BuildHub(app, token, cookie);
        var received = new List<string>();
        foreach (var name in AllEvents)
        {
            hub.On<JsonElement>(Wire(name), _ => received.Add(name));
        }

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var refusedAtStart = false;
        try
        {
            await hub.StartAsync();
        }
        catch (Exception)
        {
            refusedAtStart = true;
        }

        if (!refusedAtStart)
        {
            await closed.Task.WaitAsync(Within);
        }

        Assert.Equal(HubConnectionState.Disconnected, hub.State);
        Assert.Empty(received);
    }

    private static int IdOf(JsonElement order) => order.GetProperty("id").GetInt32();

    // ------------------------------------------------------------------ orderCreated

    [Fact]
    public async Task A_submitted_order_reaches_the_kitchen_owner_and_its_own_table_only()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var tableA = await OpenSessionAsync(app, owner);
        var tableB = await OpenSessionAsync(app, owner);

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var customerA = await ConnectAsync(app, token: tableA.Token);
        await using var customerB = await ConnectAsync(app, token: tableB.Token);

        var orderId = await SubmitAsync(app, tableA,
            Line(menu.MeatId, 2, "ไม่ติดมัน"), Line(menu.DrinkId, 1));

        // ครัวเห็นการ์ดภายใน 5 วินาทีโดยไม่ต้อง refetch และการ์ดมีทุกอย่างที่ต้องแสดง
        var card = await kitchen.Inbox.WaitForAsync("orderCreated", p => IdOf(p) == orderId, Within);
        Assert.Equal(tableA.Id, card.GetProperty("sessionId").GetInt32());
        Assert.Equal(tableA.TableName, card.GetProperty("tableName").GetString());
        Assert.Equal(1, card.GetProperty("number").GetInt32());
        Assert.Equal("Pending", card.GetProperty("status").GetString());
        var lines = card.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(menu.MeatName, lines[0].GetProperty("name").GetString());
        Assert.Equal("Buffet", lines[0].GetProperty("kind").GetString());
        Assert.Equal(2, lines[0].GetProperty("quantity").GetInt32());
        Assert.Equal("ไม่ติดมัน", lines[0].GetProperty("note").GetString());
        Assert.Equal(menu.DrinkName, lines[1].GetProperty("name").GetString());

        await customerA.Inbox.WaitForAsync("orderCreated", p => IdOf(p) == orderId, Within);
        await ownerLive.Inbox.WaitForAsync("orderCreated", p => IdOf(p) == orderId, Within);

        await BarrierAsync(app, kitchen, ownerLive, customerA, customerB);

        // โต๊ะอื่นไม่ได้อะไรเลยนอกจาก barrier ของ group menu
        Assert.Empty(customerB.Inbox.Of("orderCreated"));
        Assert.All(customerB.Inbox.Names(), name => Assert.Equal("menuItemChanged", name));

        // แต่ละการเชื่อมต่อได้รับรอบนี้ครั้งเดียว — เจ้าของร้านอยู่ทั้ง group owner และ kitchen ก็ต้องไม่ได้ซ้ำ
        Assert.Single(kitchen.Inbox.Of("orderCreated"), p => IdOf(p) == orderId);
        Assert.Single(ownerLive.Inbox.Of("orderCreated"), p => IdOf(p) == orderId);
        Assert.Single(customerA.Inbox.Of("orderCreated"), p => IdOf(p) == orderId);

        // event กับรายการของจอครัวเป็นรูปเดียวกัน: การ์ดที่มาทาง hub แทนแถวที่ refetch มาได้ตรง ๆ
        var kitchenHttp = await KitchenClientAsync(app);
        var listed = (await JsonAsync(await kitchenHttp.GetAsync("/api/orders")))
            .EnumerateArray().Single(o => IdOf(o) == orderId);
        foreach (var field in new[] { "sessionId", "tableName", "number", "status" })
        {
            Assert.Equal(card.GetProperty(field).ToString(), listed.GetProperty(field).ToString());
        }

        Assert.True(Math.Abs((card.GetProperty("createdAt").GetDateTimeOffset()
            - listed.GetProperty("createdAt").GetDateTimeOffset()).TotalMilliseconds) < 1);
        Assert.Equal(
            lines.Select(l => $"{l.GetProperty("name")}|{l.GetProperty("kind")}|{l.GetProperty("quantity")}"),
            listed.GetProperty("items").EnumerateArray()
                .Select(l => $"{l.GetProperty("name")}|{l.GetProperty("kind")}|{l.GetProperty("quantity")}"));
    }

    // ------------------------------------------------------------------ orderStatusChanged / role groups

    /// <summary>
    /// การเปลี่ยนสถานะเป็นของ ticket 12 (<c>OrderWorkflowService</c> จะเรียก <see cref="ILiveNotifier.OrderStatusChanged"/>)
    /// เทสต์นี้จึงเรียก <see cref="LiveNotifier"/> ตัวจริงตรง ๆ แล้วดูว่าใครได้รับจริง: ครัวและเจ้าของร้าน (ครั้งเดียว)
    /// ส่วนลูกค้า — แม้แต่โต๊ะเจ้าของออเดอร์ — ไม่ได้รับ
    /// </summary>
    [Fact]
    public async Task Status_changes_reach_kitchen_and_owner_but_never_a_customer()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var table = await OpenSessionAsync(app, owner);
        var orderId = await SubmitAsync(app, table, Line(menu.MeatId, 1));

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));
        await using var customer = await ConnectAsync(app, token: table.Token);

        var order = await app.WithDbAsync(db => db.Orders.AsNoTracking()
            .Include(o => o.TableSession!).ThenInclude(s => s.Table)
            .Include(o => o.Items)
            .SingleAsync(o => o.Id == orderId));
        order.Status = OrderStatus.Preparing;
        using (var scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ILiveNotifier>().OrderStatusChanged(order.ToDto());
        }

        var seen = await kitchen.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == orderId, Within);
        Assert.Equal("Preparing", seen.GetProperty("status").GetString());
        await ownerLive.Inbox.WaitForAsync("orderStatusChanged", p => IdOf(p) == orderId, Within);

        await BarrierAsync(app, kitchen, ownerLive, customer);

        Assert.Empty(customer.Inbox.Of("orderStatusChanged"));
        Assert.Single(kitchen.Inbox.Of("orderStatusChanged"));
        Assert.Single(ownerLive.Inbox.Of("orderStatusChanged"));
    }

    /// <summary>Kitchen: kitchen+menu เท่านั้น — ไม่เห็น event เฉพาะเจ้าของร้าน; Owner: owner+kitchen+menu</summary>
    [Fact]
    public async Task Staff_join_groups_by_role()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);
        var table = await OpenSessionAsync(app, owner);

        await using var kitchen = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:KitchenEmail"));
        await using var ownerLive = await ConnectAsync(app, cookie: await AuthCookieAsync(app, "DemoSeed:OwnerEmail"));

        using (var scope = app.Services.CreateScope())
        {
            var notifier = scope.ServiceProvider.GetRequiredService<ILiveNotifier>();
            await notifier.SessionChanged(new { id = table.Id }, table.Id);
            await notifier.SessionClosed(table.Id);
        }

        await ownerLive.Inbox.WaitForAsync("sessionChanged", p => p.GetProperty("id").GetInt32() == table.Id, Within);
        await ownerLive.Inbox.WaitForAsync("sessionClosed",
            p => p.GetProperty("sessionId").GetInt32() == table.Id, Within);
        await BarrierAsync(app, kitchen, ownerLive);

        Assert.Empty(kitchen.Inbox.Of("sessionChanged"));
        Assert.Empty(kitchen.Inbox.Of("sessionClosed"));
    }

    // ------------------------------------------------------------------ การปฏิเสธการเชื่อมต่อ

    [Fact]
    public async Task The_hub_rejects_a_garbage_token_a_closed_session_and_an_anonymous_connection()
    {
        using var app = new TestApp(postgres);
        var owner = await OwnerClientAsync(app);

        await AssertRejectedAsync(app, token: new string('z', 43));
        await AssertRejectedAsync(app, token: "not-a-token");
        await AssertRejectedAsync(app);

        // ยกเลิกผ่านเส้นทางจริงของ ticket 07
        var voided = await OpenSessionAsync(app, owner);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/sessions/{voided.Id}/void", null)).StatusCode);
        await AssertRejectedAsync(app, token: voided.Token);

        var paid = await OpenSessionAsync(app, owner);
        await MarkPaidAsync(app, paid.Id);
        await AssertRejectedAsync(app, token: paid.Token);

        // ตัวควบคุม: token ของรอบที่ Open ยังเชื่อมต่อได้และได้รับ group menu
        var open = await OpenSessionAsync(app, owner);
        await using var customer = await ConnectAsync(app, token: open.Token);
        Assert.Equal(HubConnectionState.Connected, customer.Hub.State);
    }

    // ------------------------------------------------------------------ GET /api/orders

    [Fact]
    public async Task The_board_lists_open_rounds_by_status_oldest_first_and_history_by_thai_date()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);
        var menu = await CreateMenuAsync(owner);
        var tableA = await OpenSessionAsync(app, owner);
        var tableB = await OpenSessionAsync(app, owner);

        var pending = await SubmitAsync(app, tableA, Line(menu.MeatId, 2, "ไม่ติดมัน"), Line(menu.DrinkId, 1));
        var preparing = await SubmitAsync(app, tableB, Line(menu.MeatId, 1));
        var cancelled = await SubmitAsync(app, tableA, Line(menu.DrinkId, 1));
        await SetOrderAsync(app, preparing, OrderStatus.Preparing);
        await SetOrderAsync(app, cancelled, OrderStatus.Cancelled);

        // ค่าเริ่มต้น = Pending, Preparing, Ready ของรอบที่ Open เรียงเก่าสุดก่อน
        var board = (await JsonAsync(await kitchen.GetAsync("/api/orders"))).EnumerateArray().ToList();
        Assert.Contains(board, o => IdOf(o) == pending);
        Assert.Contains(board, o => IdOf(o) == preparing);
        Assert.DoesNotContain(board, o => IdOf(o) == cancelled);
        Assert.All(board, o => Assert.Contains(o.GetProperty("status").GetString(),
            new[] { "Pending", "Preparing", "Ready" }));
        var times = board.Select(o => o.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderBy(t => t), times);

        var card = board.Single(o => IdOf(o) == pending);
        Assert.Equal(tableA.TableName, card.GetProperty("tableName").GetString());
        Assert.Equal(1, card.GetProperty("number").GetInt32());
        Assert.Equal("Pending", card.GetProperty("status").GetString());
        var lines = card.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([menu.MeatName, menu.DrinkName], lines.Select(l => l.GetProperty("name").GetString()));
        Assert.Equal(["Buffet", "AlaCarte"], lines.Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal([2, 1], lines.Select(l => l.GetProperty("quantity").GetInt32()));
        Assert.Equal("ไม่ติดมัน", lines[0].GetProperty("note").GetString());

        // สถานะเดียว / สถานะที่ไม่อยู่บนบอร์ด
        var onlyPreparing = (await JsonAsync(await kitchen.GetAsync("/api/orders?status=Preparing")))
            .EnumerateArray().ToList();
        Assert.Contains(onlyPreparing, o => IdOf(o) == preparing);
        Assert.DoesNotContain(onlyPreparing, o => IdOf(o) == pending);
        Assert.All(onlyPreparing, o => Assert.Equal("Preparing", o.GetProperty("status").GetString()));

        var onlyCancelled = (await JsonAsync(await kitchen.GetAsync("/api/orders?status=cancelled")))
            .EnumerateArray().Single(o => IdOf(o) == cancelled);
        Assert.Equal("วัตถุดิบหมด", onlyCancelled.GetProperty("cancelReason").GetString());

        // Ready เป็นสถานะสุดท้าย: หลุดจากบอร์ดเมื่อโต๊ะชำระเงินแล้ว แต่ยังอยู่ในประวัติของวันนั้น
        await SetOrderAsync(app, preparing, OrderStatus.Ready);
        Assert.Contains((await JsonAsync(await kitchen.GetAsync("/api/orders"))).EnumerateArray(),
            o => IdOf(o) == preparing && o.GetProperty("status").GetString() == "Ready");
        await MarkPaidAsync(app, tableB.Id);
        Assert.DoesNotContain((await JsonAsync(await kitchen.GetAsync("/api/orders"))).EnumerateArray(),
            o => IdOf(o) == preparing);
        var readyAt = await app.WithDbAsync(db =>
            db.Orders.AsNoTracking().Where(o => o.Id == preparing).Select(o => o.CreatedAt).SingleAsync());
        var today = Bkk.DateOf(readyAt).ToString("yyyy-MM-dd");
        Assert.Contains((await JsonAsync(await kitchen.GetAsync($"/api/orders?status=Ready&date={today}")))
            .EnumerateArray(), o => IdOf(o) == preparing);

        // วันตามเวลาไทย: 2020-01-15T17:30Z = 00:30 ของวันที่ 16 ที่กรุงเทพฯ
        await SetOrderAsync(app, pending, OrderStatus.Pending, new DateTimeOffset(2020, 1, 15, 17, 30, 0, TimeSpan.Zero));
        Assert.Contains((await JsonAsync(await kitchen.GetAsync("/api/orders?date=2020-01-16"))).EnumerateArray(),
            o => IdOf(o) == pending);
        Assert.DoesNotContain((await JsonAsync(await kitchen.GetAsync("/api/orders?date=2020-01-15")))
            .EnumerateArray(), o => IdOf(o) == pending);
        Assert.DoesNotContain((await JsonAsync(await kitchen.GetAsync($"/api/orders?date={today}")))
            .EnumerateArray(), o => IdOf(o) == pending);
        // บอร์ดไม่สนวันที่: รอบที่ยังเปิดข้ามเที่ยงคืนยังเห็นออเดอร์ค้างของตัวเอง และยังเรียงเก่าสุดก่อน
        var after = (await JsonAsync(await kitchen.GetAsync("/api/orders"))).EnumerateArray().ToList();
        Assert.Contains(after, o => IdOf(o) == pending);
        var afterTimes = after.Select(o => o.GetProperty("createdAt").GetDateTimeOffset()).ToList();
        Assert.Equal(afterTimes.OrderBy(t => t), afterTimes);

        // รายการเป็นการอ่านล้วน: มีแค่การประกาศจากการส่งออเดอร์สามรอบ ไม่มี event จากการ list
        Assert.Equal(3, notifier.Events.Count(e => e.Event == "orderCreated"));
    }

    [Fact]
    public async Task The_list_refuses_unknown_filters_and_is_for_staff_only()
    {
        var notifier = new RecordingNotifier();
        using var app = new TestApp(postgres, services => services.AddSingleton<ILiveNotifier>(notifier));
        var owner = await OwnerClientAsync(app);
        var kitchen = await KitchenClientAsync(app);

        foreach (var url in new[] { "/api/orders?status=Served", "/api/orders?status=1", "/api/orders?date=16-01-2020" })
        {
            var response = await kitchen.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await JsonAsync(response);
            Assert.Equal(ErrorCodes.ValidationFailed, body.GetProperty("code").GetString());
            Assert.Contains(body.GetProperty("message").GetString()!, m => m >= '\u0e00' && m <= '\u0e7f');
        }

        Assert.Equal(HttpStatusCode.OK, (await kitchen.GetAsync("/api/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/orders")).StatusCode);

        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/orders")).StatusCode);
    }
}
