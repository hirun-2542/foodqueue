using FoodQueue.Api.Common;
using Microsoft.AspNetCore.Mvc;

namespace FoodQueue.Api.Features.Print;

/// <summary>
/// งานพิมพ์ 80mm ผ่าน browser print และประวัติการชำระ/พิมพ์ซ้ำ (ticket 20) — เฉพาะเจ้าของร้าน
/// <list type="bullet">
/// <item><c>GET /api/sessions/{id}/print-data?doc=qr|prebill|receipt</c> — ข้อมูลของเอกสาร (ไม่ระบุ doc = ไม่ตรวจเงื่อนไข)</item>
/// <item><c>GET /api/sessions/history?date=yyyy-MM-dd</c> — บิลที่ชำระในวันนั้นตามเวลาไทย (ไม่ระบุ = วันนี้)</item>
/// </list>
/// </summary>
public sealed class PrintEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sessions").RequireAuthorization("OwnerOnly");

        group.MapGet("/{id:int}/print-data", GetPrintDataAsync);
        group.MapGet("/history", ListPaidAsync);
    }

    private static async Task<IResult> GetPrintDataAsync(int id, [FromQuery] string? doc, HttpContext http,
        CancellationToken ct)
    {
        PrintDocument? document = doc?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "qr" => PrintDocument.Qr,
            "prebill" => PrintDocument.Prebill,
            "receipt" => PrintDocument.Receipt,
            _ => throw Invalid("เอกสารที่พิมพ์ได้คือ qr, prebill หรือ receipt"),
        };

        return Results.Ok(await PrintService.From(http.RequestServices).GetAsync(id, document, ct));
    }

    private static async Task<IResult> ListPaidAsync([FromQuery] string? date, HttpContext http,
        CancellationToken ct)
    {
        var day = Bkk.DateOf(DateTimeOffset.UtcNow);
        if (!string.IsNullOrWhiteSpace(date) && !DateOnly.TryParseExact(date, "yyyy-MM-dd", out day))
        {
            throw Invalid("วันที่ต้องอยู่ในรูปแบบ yyyy-MM-dd");
        }

        return Results.Ok(await PrintService.From(http.RequestServices).ListPaidAsync(day, ct));
    }

    private static AppException Invalid(string message) =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), message);
}
