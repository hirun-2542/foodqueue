using FoodQueue.Api.Common;

namespace FoodQueue.Api.Features.Uploads;

/// <summary>
/// ที่เก็บไฟล์อัปโหลดที่ทุกฟีเจอร์ใช้ร่วมกัน (แบนเนอร์ร้าน ticket 03 · รูปเมนู ticket 05)
/// สัญญาตาม notes/00-CONTRACTS.md: jpeg/png/webp ที่นามสกุลตรงกับ content type, ไม่เกิน 2 MB,
/// บันทึกเป็น <c>{prefix}-{guid}{ext}</c> ใต้ App:UploadsPath แล้วคืนพาธสาธารณะ /api/uploads/{file}
/// ที่ Program.cs เสิร์ฟอยู่แล้ว
/// </summary>
public interface IUploadStore
{
    /// <summary>ตรวจแล้วบันทึกไฟล์ คืนพาธสาธารณะ; ไฟล์ที่ไม่ผ่านกฎถูกปฏิเสธด้วย AppException VALIDATION_FAILED</summary>
    Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken ct);

    /// <summary>ตรวจชนิดและขนาดไฟล์ก่อนบันทึก — คืน false พร้อมข้อความไทยที่ใช้แสดงต่อผู้ใช้</summary>
    bool TryValidate(IFormFile file, out string thaiError);
}

/// <summary>
/// ตัวจริงของ <see cref="IUploadStore"/> — ไฟล์อยู่ที่เดียวกับที่ Program.cs ผูกเป็น static files
/// Program.cs ถูก freeze จึงไม่มี DI registration: เรียกผ่าน <see cref="From"/> ใน handler ได้เลย
/// </summary>
public sealed class UploadStore : IUploadStore
{
    /// <summary>ข้อความเดียวกับสัญญา (ticket 05 ใช้ข้อความนี้ต่อ) — ห้ามแก้ให้ต่างจาก notes/00-CONTRACTS.md</summary>
    public const string InvalidFileMessage = "ไฟล์ต้องเป็น jpeg/png/webp และไม่เกิน 2 MB";

    public const long MaxBytes = 2 * 1024 * 1024;

    private const string PublicPrefix = "/api/uploads/";

    private static readonly Dictionary<string, string[]> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = [".jpg", ".jpeg"],
        ["image/png"] = [".png"],
        ["image/webp"] = [".webp"],
    };

    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public UploadStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    /// <summary>สร้าง store จาก service provider ของ request (ไม่ต้องลงทะเบียนใน DI)</summary>
    public static UploadStore From(IServiceProvider services) => new(
        services.GetRequiredService<IWebHostEnvironment>(),
        services.GetRequiredService<IConfiguration>());

    public bool TryValidate(IFormFile file, out string thaiError)
    {
        thaiError = InvalidFileMessage;

        if (file is null || file.Length <= 0 || file.Length > MaxBytes)
        {
            return false;
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension))
        {
            return false;
        }

        // content type อาจมี parameter ต่อท้าย (เช่น image/jpeg; charset=...) จึงตัดที่ ;
        var contentType = file.ContentType?.Split(';')[0].Trim() ?? "";
        return AllowedTypes.TryGetValue(contentType, out var extensions)
               && extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> SaveAsync(IFormFile file, string prefix, CancellationToken ct)
    {
        if (!TryValidate(file, out var thaiError))
        {
            throw new AppException(ErrorCodes.ValidationFailed,
                ErrorCodes.StatusFor(ErrorCodes.ValidationFailed), thaiError);
        }

        var directory = ResolveUploadsDirectory();
        Directory.CreateDirectory(directory);

        var name = $"{SafePrefix(prefix)}-{Guid.NewGuid()}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        var fullPath = Path.Combine(directory, name);

        await using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream, ct);
        }

        return PublicPrefix + name;
    }

    /// <summary>
    /// ลบไฟล์ที่เคยบันทึกผ่าน store นี้ (เรียกหลังเขียนค่าลงฐานข้อมูลสำเร็จแล้วเท่านั้น)
    /// พาธอื่นนอกจาก /api/uploads/{ชื่อไฟล์} ถูกเมิน เพื่อไม่ให้ลบไฟล์นอกโฟลเดอร์อัปโหลดได้
    /// </summary>
    public void Delete(string? publicPath)
    {
        if (string.IsNullOrWhiteSpace(publicPath) ||
            !publicPath.StartsWith(PublicPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var name = publicPath[PublicPrefix.Length..];
        if (name.Length == 0 || name != Path.GetFileName(name))
        {
            return;
        }

        var fullPath = Path.Combine(ResolveUploadsDirectory(), name);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }

    /// <summary>โฟลเดอร์เดียวกับที่ Program.cs ผูกเป็น /api/uploads (relative path คิดเทียบ content root)</summary>
    private string ResolveUploadsDirectory()
    {
        var configured = _configuration["App:UploadsPath"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(_environment.ContentRootPath, "uploads")
            : Path.GetFullPath(configured, _environment.ContentRootPath);
    }

    /// <summary>ชื่อไฟล์มาจากพรีฟิกซ์ที่โค้ดกำหนดเอง แต่ตัดอักขระที่ใช้หลุดโฟลเดอร์ได้ทิ้งไว้ก่อน</summary>
    private static string SafePrefix(string prefix)
    {
        var safe = new string((prefix ?? "").Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').ToArray());
        return safe.Length == 0 ? "file" : safe[..Math.Min(safe.Length, 20)];
    }
}
