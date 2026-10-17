using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodQueue.Api.Data;
using FoodQueue.Api.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Endpoint สำหรับเทสต์เท่านั้น ใช้พิสูจน์ policy `OwnerOnly` และ `KitchenAccess` โดยไม่ต้องประดิษฐ์
/// endpoint ของจริงขึ้นมา (ticket 03 จะเพิ่ม endpoint `OwnerOnly` ตัวแรกของผลิตภัณฑ์) — module นี้อยู่ใน
/// โปรเจกต์เทสต์ จึงไม่ถูกค้นเจอด้วย reflection ของ <see cref="IEndpointModule.Discover"/>
/// </summary>
public sealed class TestOnlyOwnerOnlyModule : IEndpointModule
{
    public const string OwnerOnlyPath = "/api/test-only/owner-only";
    public const string KitchenAccessPath = "/api/test-only/kitchen-access";

    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet(OwnerOnlyPath, () => new { ok = true }).RequireAuthorization("OwnerOnly");
        app.MapGet(KitchenAccessPath, () => new { ok = true }).RequireAuthorization("KitchenAccess");
    }
}

/// <summary>
/// ต่อ endpoint ของเทสต์เข้าไปใน pipeline ตัวจริง "หลัง" แอปสร้าง pipeline ของตัวเองเสร็จ (next(app) ก่อน)
/// จึงวิ่งผ่าน middleware authorization ตัวจริงของแอป และไม่แตะ endpoint ที่แอปจับคู่ได้อยู่แล้ว
/// </summary>
public sealed class TestOnlyEndpointsFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        next(app);
        app.UseEndpoints(endpoints => new TestOnlyOwnerOnlyModule().Map(endpoints));
    };
}

[Collection(ApiCollection.Name)]
public class AuthTests(PostgresFixture postgres)
{
    private static TestApp NewAppWithOwnerOnlyProbe(PostgresFixture postgres) =>
        new(postgres, services => services.AddSingleton<IStartupFilter, TestOnlyEndpointsFilter>());

    private static string ConfigValue(TestApp app, string key) =>
        app.Services.GetRequiredService<IConfiguration>()[key]
        ?? throw new InvalidOperationException($"ไม่ได้ตั้งค่า {key} ใน appsettings.json");

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<string> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    private static async Task<string> ProblemMessageAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("message").GetString()!;
    }

    [Fact]
    public async Task Me_without_login_is_401_status_code_not_a_redirect()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // "ไม่ redirect": API ตอบสถานะตรง ๆ ไม่มี Location ให้เบราว์เซอร์ตามไป
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Login_with_the_seeded_owner_sets_the_cookie_and_me_returns_the_owner_role()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        var email = ConfigValue(app, "DemoSeed:OwnerEmail");
        var password = ConfigValue(app, "DemoSeed:Password");

        var login = await LoginAsync(client, email, password);

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var setCookie = login.Headers.TryGetValues("Set-Cookie", out var values) ? string.Join(";", values) : "";
        Assert.Contains("fq_auth=", setCookie);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(email, doc.RootElement.GetProperty("email").GetString());
        Assert.Equal("Owner", doc.RootElement.GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("displayName").GetString()));
    }

    [Fact]
    public async Task Login_with_the_seeded_kitchen_account_returns_the_kitchen_role()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();

        var login = await LoginAsync(client, ConfigValue(app, "DemoSeed:KitchenEmail"),
            ConfigValue(app, "DemoSeed:Password"));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("Kitchen", doc.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_400_validation_failed_with_a_thai_message()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();

        var response = await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), "รหัสผิดแน่นอน");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
        var message = await ProblemMessageAsync(response);
        Assert.Contains("รหัสผ่าน", message);
        Assert.True(message.Any(c => c >= '\u0e00' && c <= '\u0e7f'), $"ข้อความต้องเป็นภาษาไทย: {message}");

        // รหัสผิดต้องไม่ได้ cookie และต้องยังเรียก me ไม่ได้
        Assert.Null(response.Headers.Location);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_is_400_validation_failed()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();

        var response = await LoginAsync(client, "ไม่มีบัญชีนี้@demo.local", "Demo1234!");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_FAILED", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Logout_clears_the_session_so_me_is_401_again()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();
        var password = ConfigValue(app, "DemoSeed:Password");
        Assert.Equal(HttpStatusCode.NoContent,
            (await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), password)).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Owner_only_rejects_kitchen_with_403_and_accepts_owner_while_kitchen_access_takes_both()
    {
        using var app = NewAppWithOwnerOnlyProbe(postgres);
        var client = app.CreateClient();
        var password = ConfigValue(app, "DemoSeed:Password");

        var anonymous = await client.GetAsync(TestOnlyOwnerOnlyModule.OwnerOnlyPath);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await LoginAsync(client, ConfigValue(app, "DemoSeed:KitchenEmail"), password)).StatusCode);
        var asKitchen = await client.GetAsync(TestOnlyOwnerOnlyModule.OwnerOnlyPath);
        Assert.Equal(HttpStatusCode.Forbidden, asKitchen.StatusCode);
        Assert.Null(asKitchen.Headers.Location);
        // KitchenAccess ต้องเปิดให้ครัว (ต่างจาก OwnerOnly)
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TestOnlyOwnerOnlyModule.KitchenAccessPath)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TestOnlyOwnerOnlyModule.OwnerOnlyPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TestOnlyOwnerOnlyModule.KitchenAccessPath)).StatusCode);
    }

    [Fact]
    public async Task Kitchen_access_endpoint_is_still_401_without_login()
    {
        using var app = NewAppWithOwnerOnlyProbe(postgres);
        var client = app.CreateClient();

        var response = await client.GetAsync(TestOnlyOwnerOnlyModule.KitchenAccessPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Provisioning_creates_both_staff_accounts_with_their_roles_and_is_idempotent()
    {
        // สตาร์ทแอปครั้งแรกบนฐานข้อมูลนี้ (เทสต์อื่นในคอลเลกชันเดียวกันอาจสร้างไปแล้ว — ก็ยังต้องผ่าน)
        using (var app = new TestApp(postgres))
        {
            _ = app.CreateClient();
        }

        // สตาร์ทแอปครั้งที่สองบนฐานข้อมูลเดิม: ต้องไม่สร้างซ้ำ และบัญชายังล็อกอินได้
        using (var app = new TestApp(postgres))
        {
            var client = app.CreateClient();
            var password = ConfigValue(app, "DemoSeed:Password");
            Assert.Equal(HttpStatusCode.NoContent,
                (await LoginAsync(client, ConfigValue(app, "DemoSeed:OwnerEmail"), password)).StatusCode);

            var ownerEmail = ConfigValue(app, "DemoSeed:OwnerEmail");
            var kitchenEmail = ConfigValue(app, "DemoSeed:KitchenEmail");

            var emails = await app.WithDbAsync(async db =>
                await db.Users.Where(u => u.Email == ownerEmail || u.Email == kitchenEmail)
                    .Select(u => u.Email!).ToListAsync());
            Assert.Equal(2, emails.Count);
            Assert.Contains(ownerEmail, emails);
            Assert.Contains(kitchenEmail, emails);

            var roleNames = await app.WithDbAsync(async db => await db.Roles.Select(r => r.Name!).ToListAsync());
            Assert.Contains("Owner", roleNames);
            Assert.Contains("Kitchen", roleNames);

            using var scope = app.NewScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = await userManager.FindByEmailAsync(ownerEmail);
            var kitchen = await userManager.FindByEmailAsync(kitchenEmail);
            Assert.NotNull(owner);
            Assert.NotNull(kitchen);
            Assert.True(await userManager.IsInRoleAsync(owner!, "Owner"));
            Assert.True(await userManager.IsInRoleAsync(kitchen!, "Kitchen"));
            // บัญชีครัวต้องไม่ใช่เจ้าของร้าน มิฉะนั้น guard ฝั่ง owner จะพังทั้งใบ
            Assert.False(await userManager.IsInRoleAsync(kitchen!, "Owner"));
        }
    }
}
