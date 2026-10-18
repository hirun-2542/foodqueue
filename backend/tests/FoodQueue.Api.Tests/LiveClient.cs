using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Realtime;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// ไคลเอนต์ SignalR จริงที่ต่อ <c>/hubs/live</c> ของ in-process server ผ่าน WebSocket ของ TestServer
/// (ใช้ร่วมกันโดยเทสต์ realtime ของจอครัว ticket 11 และเวิร์กโฟลว์ ticket 12)
///
/// กฎ group ถูกพิสูจน์ด้วย "สิ่งที่แต่ละการเชื่อมต่อได้รับจริง" ไม่ใช่ชื่อ group
/// การพิสูจน์ว่า "ไม่ได้รับ" ใช้ barrier: หลัง event ที่ทดสอบ ส่ง <c>menuItemChanged</c> (group menu ที่ทุกคนอยู่)
/// แล้วรอจนทุกการเชื่อมต่อได้ barrier นั้น — SignalR ส่งถึงการเชื่อมต่อหนึ่งตามลำดับ ถ้าการเชื่อมต่อนั้นอยู่ใน
/// group ของ event ที่ทดสอบ event นั้นต้องมาถึงก่อน barrier เสมอ จึงไม่ต้องรอเวลาเลย
/// </summary>
public static class LiveClient
{
    /// <summary>acceptance ของจอครัว: การ์ดต้องโผล่ภายใน 5 วินาที</summary>
    public static readonly TimeSpan Within = TimeSpan.FromSeconds(5);

    public static readonly string[] AllEvents =
        ["orderCreated", "orderStatusChanged", "orderEdited", "sessionChanged", "menuItemChanged", "sessionClosed"];

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    /// <summary>cookie <c>fq_auth</c> ของบัญชีหนึ่ง สำหรับส่งไปกับทุกคำขอของไคลเอนต์ SignalR</summary>
    public static async Task<string> AuthCookieAsync(TestApp app, string emailKey)
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

    /// <summary>
    /// ชื่อบนสายคือชื่อเมธอดของ <see cref="ILiveClient"/> (<c>OrderCreated</c>) — ไคลเอนต์ JavaScript จับคู่ชื่อแบบไม่สนตัวพิมพ์
    /// จึงฟัง <c>orderCreated</c> ได้ตรงสัญญา แต่ไคลเอนต์ .NET สนตัวพิมพ์ จึงต้องลงทะเบียนด้วยชื่อบนสาย
    /// </summary>
    private static string Wire(string name) => char.ToUpperInvariant(name[0]) + name[1..];

    /// <summary>ทุก event ที่การเชื่อมต่อหนึ่งได้รับ ตามลำดับที่มาถึง</summary>
    public sealed class Inbox
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

    public sealed record Live(HubConnection Hub, Inbox Inbox) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Hub.DisposeAsync();
    }

    /// <summary>WebSocket ผ่าน TestServer ตรง ๆ (เหมือนเบราว์เซอร์หลัง Caddy) พร้อม cookie ของพนักงานถ้ามี</summary>
    public static HubConnection BuildHub(TestApp app, string? token = null, string? cookie = null)
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
    public static async Task<Live> ConnectAsync(TestApp app, string? token = null, string? cookie = null)
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
    public static Task BarrierAsync(TestApp app, params Live[] connections) => BarrierAsync(app, Within, connections);

    public static async Task BarrierAsync(TestApp app, TimeSpan timeout, params Live[] connections)
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
    public static async Task AssertRejectedAsync(TestApp app, string? token = null, string? cookie = null)
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

    public static int IdOf(JsonElement order) => order.GetProperty("id").GetInt32();
}
