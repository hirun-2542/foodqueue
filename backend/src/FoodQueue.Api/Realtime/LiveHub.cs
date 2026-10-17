using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;

namespace FoodQueue.Api.Realtime;

/// <summary>
/// Hub เดียวสำหรับครัว เจ้าของร้าน และลูกค้า
/// ลูกค้าเข้าด้วย session token ที่ต้องเป็น session Open เท่านั้น และเห็นเฉพาะ group ของตัวเองกับ menu
/// </summary>
[AllowAnonymous]
public class LiveHub(AppDbContext db) : Hub<ILiveClient>
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

            await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(session.Id));
            await Groups.AddToGroupAsync(Context.ConnectionId, MenuGroup);
            await base.OnConnectedAsync();
            return;
        }

        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            if (Context.User.IsInRole("Owner"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, OwnerGroup);
            }

            if (Context.User.IsInRole("Owner") || Context.User.IsInRole("Kitchen"))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, KitchenGroup);
                await Groups.AddToGroupAsync(Context.ConnectionId, MenuGroup);
            }
        }

        await base.OnConnectedAsync();
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
