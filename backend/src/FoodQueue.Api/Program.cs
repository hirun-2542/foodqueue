using System.Text.Json;
using System.Text.Json.Serialization;
using FoodQueue.Api.Common;
using FoodQueue.Api.Data;
using FoodQueue.Api.Data.Entities;
using FoodQueue.Api.Features;
using FoodQueue.Api.Realtime;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? "Host=localhost;Database=foodqueue;Username=foodqueue;Password=foodqueue";

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "fq_auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.SlidingExpiration = true;
    // API ตอบสถานะ 401/403 ตรง ๆ ไม่ redirect ไปหน้า login
    o.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("OwnerOnly", p => p.RequireRole("Owner"))
    .AddPolicy("KitchenAccess", p => p.RequireRole("Owner", "Kitchen"));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddSignalR();
builder.Services.AddScoped<ILiveNotifier, LiveNotifier>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// ไฟล์อัปโหลด (รูปเมนู/แบนเนอร์) เสิร์ฟใต้ /api/uploads
var uploadsPath = builder.Configuration["App:UploadsPath"] ?? Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath = "/api/uploads",
});

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", async (AppDbContext db) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
        return Results.Ok(new { status = "ok" });
    }
    catch (Exception)
    {
        return Results.Json(new { status = "degraded" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapHub<LiveHub>("/hubs/live");

foreach (var module in IEndpointModule.Discover())
{
    module.Map(app);
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (!await db.Settings.AnyAsync())
    {
        db.Settings.Add(new RestaurantSettings { Id = 1, Name = "ร้านชาบู", Address = "", Phone = "" });
        await db.SaveChangesAsync();
    }
}

await StartupTasks.RunAsync(app.Services, app.Configuration);

app.Run();

public partial class Program;

/// <summary>งานตอนสตาร์ทที่แต่ละฟีเจอร์เพิ่มได้ (บัญชีเริ่มต้น, seed demo) โดยไม่ต้องแก้ Program.cs</summary>
public interface IStartupTask
{
    Task RunAsync(IServiceProvider services, IConfiguration config);
}

public static class StartupTasks
{
    public static async Task RunAsync(IServiceProvider services, IConfiguration config)
    {
        foreach (var type in typeof(Program).Assembly.GetTypes()
                     .Where(t => typeof(IStartupTask).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            var task = (IStartupTask)Activator.CreateInstance(type)!;
            await task.RunAsync(services, config);
        }
    }
}
