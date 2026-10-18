using FoodQueue.Api.Common;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// คำขอรวมบิล: รายการ session ที่จะนำเข้าบิลของ <c>{id}</c> (nullable เพื่อให้ค่าที่หายไปถูกปฏิเสธด้วยข้อความไทยของเรา)
/// </summary>
public sealed record MergeSessionsRequest(IReadOnlyList<int>? SessionIds);

/// <summary>
/// <c>POST /api/sessions/{id}/merge</c> <c>{ "sessionIds": [12, 13] }</c> (ticket 22) — เจ้าของร้านเท่านั้น
/// นำ session ที่ยัง Open อื่นมาผูกเข้าบิลของ session หลัก แล้วคืนรายละเอียดของ session หลักพร้อมบิลรวมที่คำนวณใหม่
/// กฎทั้งหมดอยู่ใน <see cref="SessionMergeService"/>; การชำระบิลรวมอยู่ใน <see cref="PaymentService"/> เส้นทางเดียว
///
/// <c>POST /api/sessions/{id}/merge/preview</c> ไม่เขียนอะไร: หน้าต่าง "รวมบิล" (frame o19) ขอข้อมูลตั้งต้นและ
/// บิลรวมตัวอย่างจากที่นี่ จึงเห็นยอดเดียวกับที่จะเกิดขึ้นจริงก่อนกดยืนยัน
/// </summary>
public sealed class SessionMergeEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization("OwnerOnly");
        group.MapPost("/{id:int}/merge", MergeAsync);
        group.MapPost("/{id:int}/merge/preview", PreviewAsync);
    }

    private static async Task<IResult> MergeAsync(int id, MergeSessionsRequest? request, HttpContext http,
        CancellationToken ct)
    {
        var ids = request?.SessionIds;
        if (ids is null || ids.Count == 0)
        {
            throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "เลือกอย่างน้อยหนึ่งโต๊ะที่จะรวมเข้าบิลนี้");
        }

        if (ids.Any(one => one <= 0))
        {
            throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "รหัสรอบการนั่งต้องเป็นจำนวนเต็มบวก");
        }

        return Results.Ok(await SessionMergeService.From(http.RequestServices).MergeAsync(id, ids, ct));
    }

    private static async Task<IResult> PreviewAsync(int id, MergeSessionsRequest? request, HttpContext http,
        CancellationToken ct) =>
        Results.Ok(await SessionMergeService.From(http.RequestServices)
            .PreviewAsync(id, request?.SessionIds, ct));
}
