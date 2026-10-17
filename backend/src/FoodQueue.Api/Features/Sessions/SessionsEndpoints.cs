using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// คำขอเปิดโต๊ะ — ใช้ชื่อ <c>adultCount</c>/<c>childCount</c> ตามสัญญาที่ตรึงไว้
/// ตัวนับเป็น nullable เพื่อให้ค่าที่หายไปถูกปฏิเสธด้วยข้อความไทยของเรา
/// </summary>
public sealed record OpenSessionRequest(int? TableId, int? PackageId, int? AdultCount, int? ChildCount);

/// <summary>
/// รอบการนั่งของเจ้าของร้าน: เปิดโต๊ะพร้อม QR เฉพาะรอบ ดูรายละเอียด/รายการ/ประวัติ และยกเลิกรอบที่เปิดผิด
/// เฉพาะเจ้าของร้าน (<c>OwnerOnly</c>) — ครัวเห็นเฉพาะจอครัว ไม่เห็นข้อมูลการเงิน
/// ไม่มีเส้นทาง "ต่อเวลา" โดยเจตนา: EndsAt ถูกตรึงไว้ที่ OpenedAt + DurationMinutes ของแพ็กเกจ
/// (การปรับยอด/ย้ายโต๊ะ/รวมบิล/ชำระเงิน/ข้อมูลพิมพ์เป็นของ ticket 16, 21, 22, 18, 20 ตามลำดับ)
/// </summary>
public sealed class SessionsEndpoints : IEndpointModule
{
    /// <summary>สถานะที่ยอมรับใน query ของรายการ — ไม่มี <c>Served</c> ในระบบนี้โดยเจตนา</summary>
    private static readonly string[] AllowedStatuses =
        [nameof(SessionStatus.Open), nameof(SessionStatus.Paid), nameof(SessionStatus.Voided)];

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization("OwnerOnly");

        group.MapPost("", OpenAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("/{id:int}/void", VoidAsync);
    }

    private static async Task<IResult> OpenAsync(OpenSessionRequest request, HttpContext http,
        CancellationToken ct)
    {
        if (request.TableId is not { } tableId || request.PackageId is not { } packageId)
        {
            throw Invalid("เลือกโต๊ะและแพ็กเกจก่อนเปิดโต๊ะ");
        }

        if (request.AdultCount is not { } adults || request.ChildCount is not { } children)
        {
            throw Invalid("ระบุจำนวนผู้ใหญ่และเด็ก");
        }

        var detail = await SessionService.From(http.RequestServices)
            .OpenAsync(tableId, packageId, adults, children, ct);

        return Results.Created($"/api/sessions/{detail.Id}", detail);
    }

    private static async Task<IResult> GetAsync(int id, HttpContext http, CancellationToken ct) =>
        Results.Ok(await SessionService.From(http.RequestServices).GetDetailAsync(id, ct));

    /// <summary>
    /// รายการรอบการนั่งสำหรับหน้าประวัติ (ticket 20) และหน้าอื่นของเจ้าของร้าน
    /// <c>status</c> ไม่ระบุ = ทุกสถานะ, <c>date</c> ไม่ระบุ = ทุกวัน (รูปแบบ <c>yyyy-MM-dd</c> ตามเวลาไทย)
    /// </summary>
    private static async Task<IResult> ListAsync([FromQuery] string? status, [FromQuery] string? date,
        HttpContext http, CancellationToken ct)
    {
        SessionStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!AllowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                throw Invalid($"สถานะที่กรองได้คือ {string.Join(", ", AllowedStatuses)}");
            }

            wanted = Enum.Parse<SessionStatus>(status, ignoreCase: true);
        }

        DateOnly? day = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsed))
            {
                throw Invalid("วันที่ต้องอยู่ในรูปแบบ yyyy-MM-dd");
            }

            day = parsed;
        }

        var sessions = await SessionService.From(http.RequestServices).ListAsync(wanted, day, ct);
        return Results.Ok(sessions);
    }

    private static async Task<IResult> VoidAsync(int id, HttpContext http, CancellationToken ct) =>
        Results.Ok(await SessionService.From(http.RequestServices).VoidAsync(id, ct));

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
