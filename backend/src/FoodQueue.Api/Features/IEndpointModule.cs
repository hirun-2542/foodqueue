namespace FoodQueue.Api.Features;

/// <summary>
/// โมดูลฟีเจอร์หนึ่งตัวลงทะเบียน endpoint ของตัวเอง
/// Program.cs ค้นหา implementation ทั้งหมดด้วย reflection จึงไม่ต้องแก้ Program.cs เมื่อเพิ่มฟีเจอร์
/// </summary>
public interface IEndpointModule
{
    void Map(IEndpointRouteBuilder app);

    static IReadOnlyList<IEndpointModule> Discover()
    {
        return typeof(IEndpointModule).Assembly.GetTypes()
            .Where(t => typeof(IEndpointModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => (IEndpointModule)Activator.CreateInstance(t)!)
            .ToList();
    }
}
