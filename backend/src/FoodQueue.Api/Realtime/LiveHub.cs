using FoodQueue.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;

namespace FoodQueue.Api.Realtime;

/// <summary>
/// Hub เดียวสำหรับครัว เจ้าของร้าน และลูกค้า
/// ลูกค้าเข้าด้วย session token ที่ต้องเป็น session Open เท่านั้น และเห็นเฉพาะ group ของตัวเองกับ menu
/// Staff เข้า group ตาม role (Owner: owner+kitchen+menu, Kitchen: kitchen+menu)
/// การเชื่อมต่อที่ไม่มีทั้ง token และ role ของพนักงานถูกตัดทันที — ไม่มีใครควรค้างอยู่โดยไม่มี group
/// การเชื่อมต่อของลูกค้าถูกจดไว้ใน <see cref="CustomerConnections"/> เพื่อให้การปิดรอบตัดการเชื่อมต่อได้ (ticket 18)
/// </summary>
[AllowAnonymous]
public class LiveHub(AppDbContext db, IHubContext<LiveHub, ILiveClient> hubContext) : Hub<ILiveClient>
{
    public const string KitchenGroup = "kitchen";
    public const string OwnerGroup = "owner";
    public const string MenuGroup = "menu";

    public static string SessionGroup(int sessionId) => $"session-{sessionId}";

    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Query["token"].ToString();
        if (!string.IsNullOrEmpty(token))
        {
            var session = await db.TableSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Token == token && s.Status == SessionStatus.Open);
            if (session is null)
            {
                Context.Abort();
                return;
            }

            CustomerConnections.For(hubContext).Add(session.Id, Context);
            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
            await Groups.AddToGroupAsync(Context.ConnectionId, MenuGroup);
            await base.OnConnectedAsync();
            return;
        }

        var user = Context.User;
        var isOwner = user?.Identity?.IsAuthenticated == true && user.IsInRole(StaffRoles.Owner);
        var isKitchen = user?.Identity?.IsAuthenticated == true && user.IsInRole(StaffRoles.Kitchen);
        if (!isOwner && !isKitchen)
        {
            Context.Abort();
            return;
        }

        // เจ้าของร้านอยู่ใน group kitchen ด้วย event ที่ส่งถึง "kitchen+owner" จึงส่งที่ group kitchen
        // ครั้งเดียว (ดู LiveNotifier) ไม่งั้นจอของเจ้าของร้านจะได้ event เดียวกันสองครั้ง
        if (isOwner)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OwnerGroup);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, KitchenGroup);
        await Groups.AddToGroupAsync(Context.ConnectionId, MenuGroup);

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        CustomerConnections.For(hubContext).Remove(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}

public interface ILiveClient
{
    Task OrderCreated(object order);
    Task OrderStatusChanged(object order);
    Task OrderEdited(object order);
    Task SessionChanged(object session);
    Task MenuItemChanged(object payload);
    Task SessionClosed(object payload);
}
