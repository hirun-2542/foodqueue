using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Orders;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Features.Customer;

/// <summary>
/// คำขอส่งออเดอร์จากหน้าลูกค้า — <c>Items</c> เป็น nullable เพื่อให้ค่าที่หายไปได้ข้อความไทยของเรา
/// (ลำดับการตรวจที่เหลือเป็นของ <see cref="OrderService"/>)
/// </summary>
public sealed record SubmitOrderRequest(IReadOnlyList<OrderLineRequest>? Items);

/// <summary>
/// ออเดอร์ของลูกค้าด้วย session token (ไม่มี auth): ส่งออเดอร์หนึ่งรอบ และอ่านรายการรวมของโต๊ะ
/// ตรรกะการส่งทั้งหมดอยู่ใน <see cref="OrderService.CreateAsync"/> ซึ่ง Demo Bot (ticket 25) เรียกตัวเดียวกัน
///
/// คำตอบที่ส่งกลับหน้าลูกค้าคือ <see cref="CustomerOrderResultDto"/> (คำยืนยัน) ไม่ใช่ <c>OrderDto</c>:
/// ลูกค้าไม่เห็นเลขรอบตาม spec ส่วน <c>OrderDto</c> ที่มีเลขรอบเป็นของ realtime/ครัวและ Demo Bot
/// ไม่มีเส้นทางของ cart ที่นี่: ตะกร้าเป็นของไคลเอนต์ (localStorage ต่อ token) ตาม spec
/// </summary>
public sealed class CustomerOrderEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/s/{token}/orders", ListAsync).AllowAnonymous();
        app.MapPost("/api/s/{token}/orders", SubmitAsync).AllowAnonymous();
    }

    /// <summary>
    /// "ออเดอร์ของโต๊ะ" (spec 20–22): ทุกบรรทัดของรอบนี้ (รวมที่เพื่อนร่วมโต๊ะสั่ง) ยุบเป็นรายการเดียว —
    /// รวมจำนวนต่อ (เมนู, หมายเหตุ) เพื่อให้หมายเหตุไม่หายไปกับแถวที่ไม่มีหมายเหตุ, เรียงตามที่สั่งครั้งแรก
    /// ไม่มีรอบ ไม่มีสถานะ ไม่มียอดบิล; ออเดอร์ที่ถูกยกเลิกและบรรทัดที่ถูกนำออก (จำนวน 0) ไม่ถูกนับ
    /// เห็นเฉพาะรอบของ token นี้เท่านั้น แม้รอบนี้ถูกรวมบิลกับโต๊ะอื่น
    /// </summary>
    private static async Task<IResult> ListAsync(string token, HttpContext http, CancellationToken ct)
    {
        var session = await OrderService.From(http.RequestServices).RequireByTokenAsync(token, ct);
        var db = http.RequestServices.GetRequiredService<AppDbContext>();

        var lines = await db.OrderItems.AsNoTracking()
            .Where(i => i.Order!.TableSessionId == session.Id
                        && i.Order.Status != OrderStatus.Cancelled
                        && i.Quantity > 0)
            .OrderBy(i => i.Id)
            .Select(i => new { i.MenuItemId, i.NameSnapshot, i.KindSnapshot, i.Quantity, i.Note })
            .ToListAsync(ct);

        var menuIds = lines.Select(l => l.MenuItemId).Distinct().ToList();
        var images = await db.MenuItems.AsNoTracking()
            .Where(m => menuIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.ImagePath, ct);

        // GroupBy ของ LINQ-to-Objects รักษาลำดับที่พบครั้งแรก → แถวเรียงตามบรรทัดแรกที่สั่ง
        var items = lines
            .GroupBy(l => (l.MenuItemId, l.Note))
            .Select(g =>
            {
                var first = g.First();
                return new CustomerOrderItemDto(first.MenuItemId, first.NameSnapshot, first.KindSnapshot.ToString(),
                    g.Sum(l => l.Quantity), first.Note, images.GetValueOrDefault(first.MenuItemId));
            })
            .ToList();

        return Results.Ok(new CustomerOrdersDto(items));
    }

    private static async Task<IResult> SubmitAsync(string token, [FromBody] SubmitOrderRequest request,
        HttpContext http, CancellationToken ct)
    {
        var orders = OrderService.From(http.RequestServices);

        // token ที่ไม่รู้จัก → SESSION_NOT_FOUND, รอบที่ Paid/Voided → SESSION_CLOSED (ก่อนตรวจอย่างอื่น)
        var session = await orders.RequireByTokenAsync(token, ct);
        var order = await orders.CreateAsync(session, request.Items, ct);

        var result = new CustomerOrderResultDto(order.CreatedAt, order.Items.Sum(i => i.Quantity));
        return Results.Created($"/api/s/{token}/orders/{order.Id}", result);
    }
}
