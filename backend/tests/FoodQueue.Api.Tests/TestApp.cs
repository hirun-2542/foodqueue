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
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "api";
}

public sealed class TestApp(PostgresFixture postgres, Action<IServiceCollection>? testServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", postgres.ConnectionString);
        builder.UseSetting("Demo:Enabled", "false");
        builder.UseEnvironment("Testing");
        // Program.cs สร้าง PhysicalFileProvider จากค่านี้ตรง ๆ ซึ่งต้องเป็น absolute path
        // (appsettings.json ตั้ง "uploads" แบบ relative ให้ container ที่ WORKDIR=/app)
        builder.UseSetting("App:UploadsPath", Path.Combine(Path.GetTempPath(), "foodqueue-tests-uploads"));

        // เทสต์ที่ต้องเพิ่ม endpoint เฉพาะทางเข้ามาใน pipeline ตัวจริงใช้ทางนี้
        if (testServices is not null)
        {
            builder.ConfigureTestServices(testServices);
        }
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
