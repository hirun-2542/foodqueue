using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace FoodQueue.Api.Features.Auth;

public sealed record LoginRequest(string? Email, string? Password);

public sealed record MeResponse(string Email, string DisplayName, string Role);

/// <summary>
/// Auth ด้วย cookie ของ ASP.NET Identity (ไม่ใช้ JWT) — 401/403 ตอบเป็นสถานะ ไม่ redirect
/// ลูกค้าไม่มีบัญชี ใช้ Session Token เท่านั้น จึงไม่มี endpoint ของ Auth ให้ลูกค้าใช้
/// </summary>
public sealed class AuthEndpoints : IEndpointModule
{
    /// <summary>ข้อความเดียวสำหรับทุกกรณีล็อกอินไม่สำเร็จ: ไม่บอกใบ้ว่าอีเมลนั้นมีอยู่จริงหรือไม่</summary>
    private const string InvalidCredentialsMessage = "อีเมลหรือรหัสผ่านไม่ถูกต้อง";

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, SignInManager<AppUser> signIn)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(request.Password))
        {
            throw Failed();
        }

        var user = await signIn.UserManager.FindByEmailAsync(email);
        if (user is null)
        {
            throw Failed();
        }

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            throw Failed();
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(SignInManager<AppUser> signIn)
    {
        // ออกได้เสมอแม้ยังไม่ได้ล็อกอิน เพื่อให้ UI เรียกซ้ำได้โดยไม่ต้องเช็คสถานะก่อน
        await signIn.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var role = StaffRoles.All.FirstOrDefault(http.User.IsInRole);
        if (role is null)
        {
            // ล็อกอินได้แต่ไม่มีบทบาทพนักงาน: ห้ามเดาเป็นครัวเพราะจะเปิดข้อมูลครัวให้คนนอก
            return Results.Forbid();
        }

        return Results.Ok(new MeResponse(user.Email ?? user.UserName ?? "", user.DisplayName, role));
    }

    private static AppException Failed() =>
        new(ErrorCodes.ValidationFailed, ErrorCodes.StatusFor(ErrorCodes.ValidationFailed),
            InvalidCredentialsMessage);
}
