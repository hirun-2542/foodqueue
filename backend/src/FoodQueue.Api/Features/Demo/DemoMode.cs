namespace FoodQueue.Api.Features.Demo;

/// <summary>
/// สวิตช์เดียวของ Demo Mode (spec 109): <c>Demo:Enabled</c> ใน config
/// เมื่อปิด — endpoint ของ demo ทุกตัวตอบ 404 (ไม่ใช่แค่ไม่ map) และไม่มี seed/bot
/// (compose.yaml ส่งค่ามาจาก <c>DEMO_ENABLED</c>; เทสต์ backend ปิดไว้ใน <c>TestApp</c>)
/// </summary>
public static class DemoMode
{
    public const string ConfigKey = "Demo:Enabled";

    /// <summary>ไม่ระบุ/ค่าไม่ใช่บูลีน = ปิด (ปลอดภัยกว่าเปิดโดยไม่ตั้งใจ)</summary>
    public static bool IsEnabled(IConfiguration config) =>
        bool.TryParse(config[ConfigKey], out var enabled) && enabled;
}
