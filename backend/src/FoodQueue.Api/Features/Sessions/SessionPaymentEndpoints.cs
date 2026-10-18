using FoodQueue.Api.Common;
using FoodQueue.Api.Data.Entities;

namespace FoodQueue.Api.Features.Sessions;

/// <summary>
/// คำขอรับชำระ: <c>method</c> = <c>Cash</c> | <c>Card</c> | <c>PromptPay</c>;
/// <c>amountReceived</c> ใช้กับเงินสดเท่านั้น (วิธีอื่นถือว่ารับเท่ากับยอดรวม จึงไม่สนค่าที่ส่งมา)
/// </summary>
public sealed record PaymentRequest(string? Method, decimal? AmountReceived);

/// <summary>
/// <c>POST /api/sessions/{id}/payment</c> (เฉพาะเจ้าของร้าน) — ปิดบิลผ่าน <see cref="PaymentService"/> ตัวเดียวกับ Demo bot
/// แล้วคืนรายละเอียด session ที่ Paid พร้อม <c>payment</c> (เลขใบเสร็จ รับเงิน เงินทอน) ให้หน้าจอแสดงผลสำเร็จได้ทันที
/// แยกไฟล์จาก <see cref="SessionsEndpoints"/> เพื่อให้แต่ละ ticket เพิ่มเส้นทางของตัวเองได้โดยไม่ชนกัน
/// </summary>
public sealed class SessionPaymentEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sessions/{id:int}/payment", PayAsync).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> PayAsync(int id, PaymentRequest request, HttpContext http,
        CancellationToken ct)
    {
        // ชื่อวิธีเท่านั้น: Enum.TryParse รับ "0"/"1" เป็นค่าตัวเลขได้ด้วย ซึ่งไม่ใช่สัญญาของสาย JSON
        if (string.IsNullOrWhiteSpace(request.Method)
            || !request.Method.Trim().All(char.IsAsciiLetter)
            || !Enum.TryParse<PaymentMethod>(request.Method.Trim(), ignoreCase: true, out var method))
        {
            throw new AppException(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
                "เลือกวิธีชำระเงิน: เงินสด PromptPay หรือบัตร");
        }

        return Results.Ok(await PaymentService.From(http.RequestServices)
            .PayAsync(id, method, request.AmountReceived, ct));
    }
}
