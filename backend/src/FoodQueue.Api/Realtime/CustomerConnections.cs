using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;

namespace FoodQueue.Api.Realtime;

/// <summary>
/// การเชื่อมต่อของลูกค้าที่เข้ามาด้วย token แยกตามรอบการนั่ง เพื่อให้การปิดรอบ (ชำระเงิน/ยกเลิกโต๊ะ) ตัดการเชื่อมต่อ
/// ของลูกค้ารอบนั้นได้ทันทีหลังส่ง <c>sessionClosed</c> (ticket 18) — SignalR ไม่มีคำสั่ง "ตัดทั้ง group" จากนอก hub
///
/// หนึ่งชุดต่อหนึ่ง host: ผูกกับ <c>IHubContext</c> ซึ่งเป็น singleton ของ host นั้น (Program.cs ถูก freeze จึงลงทะเบียน DI
/// ไม่ได้) — เทสต์ที่รันหลาย host ในโปรเซสเดียวจึงไม่ตัดการเชื่อมต่อของกันและกันแม้เลข session ชนกัน
/// </summary>
public sealed class CustomerConnections
{
    private static readonly ConditionalWeakTable<object, CustomerConnections> PerHost = new();

    private readonly ConcurrentDictionary<string, (int SessionId, HubCallerContext Context)> _connections = new();

    public static CustomerConnections For(IHubContext<LiveHub, ILiveClient> hub) =>
        PerHost.GetValue(hub, _ => new CustomerConnections());

    public void Add(int sessionId, HubCallerContext context) =>
        _connections[context.ConnectionId] = (sessionId, context);

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>ตัดทุกการเชื่อมต่อของลูกค้ารอบนี้ (ข้อความที่ส่งไปก่อนหน้ายังถึงก่อนการปิด)</summary>
    public void Abort(int sessionId)
    {
        foreach (var (connectionId, entry) in _connections)
        {
            if (entry.SessionId == sessionId && _connections.TryRemove(connectionId, out _))
            {
                entry.Context.Abort();
            }
        }
    }
}
