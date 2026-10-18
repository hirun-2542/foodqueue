using FoodQueue.Api.Common;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>คำขอย้ายโต๊ะ: โต๊ะปลายทาง (nullable เพื่อให้ค่าที่หายไปถูกปฏิเสธด้วยข้อความไทยของเรา)</summary>
public sealed record MoveSessionRequest(int? TableId);

/// <summary>
/// <c>POST /api/sessions/{id}/move</c> (ticket 21) — เจ้าของร้านเท่านั้น คืนรายละเอียด session พร้อมบิลที่โต๊ะใหม่
/// กฎทั้งหมดอยู่ใน <see cref="SessionMoveService"/>
/// </summary>
public sealed class SessionMoveEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGroup("/api/sessions").RequireAuthorization("OwnerOnly").MapPost("/{id:int}/move", MoveAsync);

    private static async Task<IResult> MoveAsync(int id, MoveSessionRequest request, HttpContext http,
        CancellationToken ct)
    {
        if (request.TableId is not { } tableId)
        {
            throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "เลือกโต๊ะปลายทางก่อนย้าย");
        }

        return Results.Ok(await SessionMoveService.From(http.RequestServices).MoveAsync(id, tableId, ct));
    }
}
