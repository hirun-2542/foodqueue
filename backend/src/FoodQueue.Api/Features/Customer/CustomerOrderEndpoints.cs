using FoodQueue.Api.Contracts;
using FoodQueue.Api.Features.Orders;
using Microsoft.AspNetCore.Mvc;

namespace FoodQueue.Api.Features.Customer;

/// <summary>
/// คำขอส่งออเดอร์จากหน้าลูกค้า — <c>Items</c> เป็น nullable เพื่อให้ค่าที่หายไปได้ข้อความไทยของเรา
/// (ลำดับการตรวจที่เหลือเป็นของ <see cref="OrderService"/>)
/// </summary>
public sealed record SubmitOrderRequest(IReadOnlyList<OrderLineRequest>? Items);

/// <summary>
/// เส้นทางเขียนของลูกค้า: ส่งออเดอร์หนึ่งรอบด้วย session token (ไม่มี auth) — ตรรกะทั้งหมดอยู่ใน
/// <see cref="OrderService.CreateAsync"/> ซึ่ง Demo Bot (ticket 25) เรียกตัวเดียวกัน
///
/// คำตอบที่ส่งกลับหน้าลูกค้าคือ <see cref="CustomerOrderResultDto"/> (คำยืนยัน) ไม่ใช่ <c>OrderDto</c>:
/// ลูกค้าไม่เห็นเลขรอบตาม spec ส่วน <c>OrderDto</c> ที่มีเลขรอบเป็นของ realtime/ครัวและ Demo Bot
/// ไม่มีเส้นทางของ cart ที่นี่: ตะกร้าเป็นของไคลเอนต์ (localStorage ต่อ token) ตาม spec
/// </summary>
public sealed class CustomerOrderEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/s/{token}/orders", SubmitAsync).AllowAnonymous();
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
