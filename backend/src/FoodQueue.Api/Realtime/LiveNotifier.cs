using Microsoft.AspNetCore.SignalR;

namespace FoodQueue.Api.Realtime;

/// <summary>
/// จุดเดียวที่ส่ง event realtime ทุกตัว ห่อ IHubContext<LiveHub>
/// ทุกเมธอดต้องถูกเรียกหลัง SaveChanges สำเร็จเท่านั้น
/// </summary>
public interface ILiveNotifier
{
    Task OrderCreated(object order, int sessionId, int? primarySessionId = null);
    Task OrderStatusChanged(object order);
    Task OrderEdited(object order, int sessionId, int? primarySessionId = null);
    Task SessionChanged(object summary, int sessionId);
    Task MenuItemChanged(int menuItemId, bool isAvailable, bool isActive);
    Task SessionClosed(int sessionId);
    Task SessionClosedMany(IEnumerable<int> sessionIds);
}

public sealed class LiveNotifier(IHubContext<LiveHub, ILiveClient> hub) : ILiveNotifier
{
    public async Task OrderCreated(object order, int sessionId, int? primarySessionId = null)
    {
        await hub.Clients.Groups(LiveHub.KitchenGroup, LiveHub.OwnerGroup).OrderCreated(order);
        await hub.Clients.Group(LiveHub.SessionGroup(sessionId)).OrderCreated(order);
        if (primarySessionId is not null && primarySessionId != sessionId)
        {
            await hub.Clients.Group(LiveHub.SessionGroup(primarySessionId.Value)).OrderCreated(order);
        }
    }

    public async Task OrderStatusChanged(object order)
    {
        // ลูกค้าไม่เห็นสถานะรายรอบ จึงส่งเฉพาะครัวและเจ้าของร้าน
        await hub.Clients.Groups(LiveHub.KitchenGroup, LiveHub.OwnerGroup).OrderStatusChanged(order);
    }

    public async Task OrderEdited(object order, int sessionId, int? primarySessionId = null)
    {
        await hub.Clients.Groups(LiveHub.KitchenGroup, LiveHub.OwnerGroup).OrderEdited(order);
        await hub.Clients.Group(LiveHub.SessionGroup(sessionId)).OrderEdited(order);
        if (primarySessionId is not null && primarySessionId != sessionId)
        {
            await hub.Clients.Group(LiveHub.SessionGroup(primarySessionId.Value)).OrderEdited(order);
        }
    }

    public async Task SessionChanged(object summary, int sessionId)
    {
        await hub.Clients.Group(LiveHub.OwnerGroup).SessionChanged(summary);
        await hub.Clients.Group(LiveHub.SessionGroup(sessionId)).SessionChanged(summary);
    }

    public async Task MenuItemChanged(int menuItemId, bool isAvailable, bool isActive)
    {
        await hub.Clients.Group(LiveHub.MenuGroup)
            .MenuItemChanged(new { menuItemId, isAvailable, isActive });
    }

    public async Task SessionClosed(int sessionId)
    {
        await hub.Clients.Group(LiveHub.SessionGroup(sessionId)).SessionClosed(new { sessionId });
        await hub.Clients.Group(LiveHub.OwnerGroup).SessionClosed(new { sessionId });
    }

    public async Task SessionClosedMany(IEnumerable<int> sessionIds)
    {
        foreach (var id in sessionIds.Distinct())
        {
            await SessionClosed(id);
        }
    }
}
