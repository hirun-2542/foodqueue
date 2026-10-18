using FoodQueue.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Seam 1 ของ spec: แอป backend จริง + Postgres จริง (Testcontainers) + in-process host
/// เทสต์ปิด Demo และ seed เฉพาะข้อมูลที่แต่ละเคสต้องใช้
///
/// ฐานข้อมูล: template หนึ่งตัวที่ migrate แล้ว (สร้างครั้งเดียวต่อคอลเลกชัน) และแต่ละ TestApp ได้
/// ฐานข้อมูลของตัวเองที่โคลนจาก template — เทสต์จึงไม่แย่งกันใช้พื้นที่ identifier ที่มีขอบเขตจริง
/// ในผลิตภัณฑ์ (package Rank ไม่ซ้ำและจำกัด 1–99, ชื่อโต๊ะไม่ซ้ำ) ซึ่งเคยทำให้เทสต์ผ่านตอนรันเดี่ยว
/// แต่ล้มตอนรันทั้งชุด ฐานข้อมูลที่โคลนมาถูกทิ้งไปพร้อม container ตอนจบ
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();
    private DatabaseTemplate? _template;
    private string? _templateConnectionString;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // template: ฐานข้อมูลว่างหนึ่งตัว ให้แอปจริง migrate แล้ว seed settings ครั้งเดียว
        _template = new DatabaseTemplate(_container.GetConnectionString());
        _templateConnectionString = _template.CreateEmptyTemplate();

        // migrate ตรง ๆ บน template: deterministic และรอจน schema เสร็จก่อนมี clone ใด ๆ
        // (การ bootstrap ด้วยแอปจริงจะเริ่ม host แบบ lazy แล้ว clone อาจถูกสร้างก่อน migrate จบ)
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_templateConnectionString)
            .Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            if (!await db.Settings.AnyAsync())
            {
                db.Settings.Add(new FoodQueue.Api.Data.Entities.RestaurantSettings
                {
                    Id = 1,
                    Name = "ร้านชาบู",
                    Address = "",
                    Phone = "",
                });
                await db.SaveChangesAsync();
            }
        }

        // pool ต้องถูกปล่อยก่อน ไม่งั้น CREATE DATABASE ... WITH TEMPLATE จะถูกปฏิเสธ
        _template.ReleaseTemplateConnections();
    }

    /// <summary>ฐานข้อมูลใหม่สำหรับหนึ่ง host (schema + settings สำเนามาจาก template)</summary>
    public string NewDatabase() =>
        _template?.CreateClone() ?? throw new InvalidOperationException("fixture ยังไม่พร้อม");

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}

public sealed class TestApp : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly Action<IServiceCollection>? _testServices;

    /// <summary>connection string ของฐานข้อมูลที่ host นี้ใช้จริง (เทสต์ racing ต้องล็อกในฐานเดียวกัน)</summary>
    public string ConnectionString => _connectionString;

    public TestApp(PostgresFixture postgres, Action<IServiceCollection>? testServices = null)
        : this(postgres.NewDatabase(), testServices)
    {
    }

    public TestApp(string connectionString, Action<IServiceCollection>? testServices = null)
    {
        _connectionString = connectionString;
        _testServices = testServices;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Demo:Enabled", "false");
        builder.UseEnvironment("Testing");
        // Program.cs สร้าง PhysicalFileProvider จากค่านี้ตรง ๆ ซึ่งต้องเป็น absolute path
        // (appsettings.json ตั้ง "uploads" แบบ relative ให้ container ที่ WORKDIR=/app)
        builder.UseSetting("App:UploadsPath", Path.Combine(Path.GetTempPath(), "foodqueue-tests-uploads"));

        // เทสต์ที่ต้องเพิ่ม endpoint เฉพาะทางเข้ามาใน pipeline ตัวจริงใช้ทางนี้
        if (_testServices is not null)
        {
            builder.ConfigureTestServices(_testServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // connection string ไม่ซ้ำกันในแต่ละ host จึงได้ pool แยกกัน ถ้าไม่คืน pool ตอนจบ
        // 167 host จะเปิด connection ค้างจน Postgres ปฏิเสธ ("too many clients")
        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    public IServiceScope NewScope() => Services.CreateScope();

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = NewScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

[Collection(ApiCollection.Name)]
public class SmokeTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Health_endpoint_reports_ok_when_database_is_reachable()
    {
        using var app = new TestApp(postgres);
        var client = app.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Initial_migration_is_applied_and_settings_row_exists()
    {
        using var app = new TestApp(postgres);
        _ = app.CreateClient();

        var settings = await app.WithDbAsync(db => db.Settings.SingleAsync());

        Assert.Equal(1, settings.Id);
    }
}
