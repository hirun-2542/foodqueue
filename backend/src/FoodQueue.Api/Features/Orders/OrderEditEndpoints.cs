using Microsoft.AspNetCore.Mvc;

namespace FoodQueue.Api.Features.Orders;

public sealed record EditOrderRequest(string? Reason, IReadOnlyList<OrderEditLine>? Items);

/// <summary>
/// <c>POST /api/orders/{id}/edit</c> <c>{ "reason": "ลูกค้าเอาออก", "items": [{ "itemId": 12, "quantity": 0 }] }</c>
/// — <c>OwnerOnly</c> (ครัวยกเลิกได้ แต่แก้รายการไม่ได้); <c>quantity</c> คือจำนวนใหม่ของบรรทัดนั้น (0 = นำออก)
/// กฎทั้งหมดอยู่ใน <see cref="OrderEditService"/>; คืน <c>OrderDto</c> รูปเดียวกับ event <c>orderEdited</c>
/// </summary>
public sealed class OrderEditEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/api/orders/{id:int}/edit", EditAsync).RequireAuthorization("OwnerOnly");

    private static async Task<IResult> EditAsync(int id, [FromBody] EditOrderRequest? request, HttpContext http,
        CancellationToken ct)
    {
        var order = await OrderEditService.From(http.RequestServices)
            .EditAsync(id, request?.Items, request?.Reason, ct);
        return order is null ? Results.NotFound() : Results.Ok(order);
    }
}
