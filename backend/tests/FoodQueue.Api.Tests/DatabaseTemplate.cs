using Npgsql;

namespace FoodQueue.Api.Tests;

/// <summary>
/// Creates a fresh database per test host, cloned from a migrated template.
///
/// Why this exists: every test used to share one database for the whole collection, so tests competed for
/// bounded, product-level identifier space — the package <c>Rank</c> is unique and limited to 1–99 by the
/// spec, and table names are unique. With 148 tests those limits are reachable, and a test then fails only
/// when the whole suite runs (it passes alone). Cloning per host makes each test's data its own, so growth
/// in the suite cannot make an unrelated test fail, without changing any product rule.
/// </summary>
public sealed class DatabaseTemplate
{
    private readonly string _containerConnectionString;
    private readonly string _adminConnectionString;
    private readonly string _templateName = $"fq_template_{Guid.NewGuid():N}";

    public DatabaseTemplate(string containerConnectionString)
    {
        // Pooling ปิดไว้: CREATE DATABASE ... WITH TEMPLATE ต้องไม่มี session ค้างอยู่กับฐานต้นทาง
        _containerConnectionString = containerConnectionString;
        var builder = new NpgsqlConnectionStringBuilder(containerConnectionString)
        {
            Database = "postgres",
            Pooling = false,
        };
        _adminConnectionString = builder.ConnectionString;
    }

    private string ConnectionStringFor(string database) =>
        new NpgsqlConnectionStringBuilder(_containerConnectionString) { Database = database }.ConnectionString;

    /// <summary>สร้าง template เปล่าแล้วให้ caller สร้าง schema ผ่านแอปจริงหนึ่งครั้ง</summary>
    public string CreateEmptyTemplate()
    {
        Execute($"CREATE DATABASE \"{_templateName}\"");
        return ConnectionStringFor(_templateName);
    }

    /// <summary>คืน connection string ของฐานข้อมูลใหม่ที่สำเนามาจาก template (schema พร้อมใช้)</summary>
    public string CreateClone()
    {
        var name = $"fq_test_{Guid.NewGuid():N}";
        // WITH TEMPLATE ต้องไม่มี connection ค้างอยู่กับ template
        Execute($"CREATE DATABASE \"{name}\" WITH TEMPLATE \"{_templateName}\"");
        return ConnectionStringFor(name);
    }

    /// <summary>ปล่อย connection ที่ค้างกับ template ก่อนโคลน (pool ของแอป bootstrap)</summary>
    public void ReleaseTemplateConnections() => NpgsqlConnection.ClearAllPools();

    public void DropAndFinish(IEnumerable<string> databasesInUse)
    {
        foreach (var db in databasesInUse)
        {
            Execute($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE)");
        }

        Execute($"DROP DATABASE IF EXISTS \"{_templateName}\" WITH (FORCE)");
    }

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(_adminConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
