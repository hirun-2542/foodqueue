using FoodQueue.Api.Data.Entities;

namespace FoodQueue.Api.Contracts;

// DTO ที่ใช้ร่วมกันหลายฟีเจอร์ — แก้ที่นี่ที่เดียวเมื่อรูปทรงเปลี่ยน
// (ทุกตัว serialize เป็น camelCase และ enum เป็น string ตาม Program.cs)

public record TableDto(int Id, string Name, int Seats, string? Zone, int SortOrder, bool IsActive);

public record PackageDto(int Id, string Name, int Rank, decimal AdultPrice, decimal ChildPrice,
    int DurationMinutes, bool IsActive);

public record CategoryDto(int Id, string Name, int SortOrder, int ItemCount);

public record MenuItemDto(int Id, int CategoryId, string CategoryName, string Name, string? Description,
    string? ImagePath, string Kind, int? MinPackageRank, decimal Price, bool IsAvailable, bool IsActive, int SortOrder);

public record SettingsDto(string Name, string Address, string Phone, string? TaxId, string? PromptPayId,
    string? BannerImagePath, decimal ServiceChargePercent, decimal VatPercent, int MaxQtyPerItemPerOrder,
    int MaxItemsPerOrder, int LastOrderWarningMinutes);

public record OrderItemDto(string Name, string Kind, decimal UnitPrice, int Quantity, string? Note);

public record OrderDto(int Id, int SessionId, string TableName, int Number, string Status, string? CancelReason,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<OrderItemDto> Items);

public record AdjustmentDto(int Id, string Description, decimal Amount, DateTimeOffset CreatedAt);

public record AlaCarteLineDto(string Name, int Quantity, decimal UnitPrice, decimal Amount);

public record BillDto(
    int Adults,
    int Children,
    decimal AdultPrice,
    decimal ChildPrice,
    decimal BuffetTotal,
    IReadOnlyList<AlaCarteLineDto> AlaCarteLines,
    decimal AlaCarteTotal,
    IReadOnlyList<AdjustmentDto> Adjustments,
    decimal AdjustmentTotal,
    decimal Subtotal,
    decimal ServiceChargePercent,
    decimal ServiceCharge,
    decimal VatPercent,
    decimal Vat,
    decimal Total);

public record PaymentDto(string Method, decimal AmountReceived, decimal Change, string ReceiptNo,
    DateTimeOffset PaidAt, decimal Total);

public record SessionDetailDto(int Id, int TableId, string TableName, string? Zone, string PackageName,
    int PackageRank, int Adults, int Children, DateTimeOffset OpenedAt, DateTimeOffset EndsAt, string Status,
    string QrUrl, bool IsDemoBot, int? MergedIntoSessionId, IReadOnlyList<OrderDto> Orders,
    IReadOnlyList<AdjustmentDto> Adjustments, BillDto Bill, PaymentDto? Payment);

public record SessionSummaryDto(int Id, int TableId, string TableName, string PackageName, int Adults,
    int Children, DateTimeOffset OpenedAt, DateTimeOffset EndsAt, string Status);

public record TableOverviewDto(int Id, string Name, string? Zone, int Seats, bool IsActive, int? SessionId,
    string? PackageName, int? Adults, int? Children, DateTimeOffset? OpenedAt, DateTimeOffset? EndsAt,
    int? MinutesUsed, int? OrdersWaiting, decimal? RunningTotal);

public record PrintDataDto(SettingsDto Settings, int SessionId, string TableName, string PackageName,
    int Adults, int Children, DateTimeOffset OpenedAt, DateTimeOffset EndsAt, BillDto Bill, PaymentDto? Payment,
    string QrUrl);

/// <summary>มุมมองเดิมของ session สำหรับ owner ที่รวมบิลแล้ว: sub = session ที่ถูกนำไปรวม</summary>
public record CustomerSessionDto(string TableName, string? Zone, string PackageName, int PackageRank, int Adults,
    int Children, DateTimeOffset OpenedAt, DateTimeOffset EndsAt, int LastOrderWarningMinutes,
    int MaxQtyPerItemPerOrder, int MaxItemsPerOrder, DateTimeOffset ServerNow, string? BannerImagePath,
    string? ShopName);

public record CustomerMenuDto(IReadOnlyList<CustomerCategoryDto> Categories);

public record CustomerCategoryDto(int Id, string Name, int SortOrder, IReadOnlyList<CustomerMenuItemDto> Items);

public record CustomerMenuItemDto(int Id, string Name, string? ImagePath, string Kind, decimal Price,
    int? MinPackageRank, bool IsAvailable);

public record CustomerOrdersDto(IReadOnlyList<CustomerOrderItemDto> Items);

/// <summary>
/// คำตอบของ <c>POST /api/s/{token}/orders</c> — คำยืนยันว่าส่งแล้ว ไม่มีเลขรอบและไม่มีสถานะ
/// โดยเจตนา: ลูกค้าไม่เห็นเลขรอบ (spec "ลูกค้าไม่เห็นบิล ยอดรวม สถานะรายรอบ หรือเลขรอบ")
/// </summary>
public record CustomerOrderResultDto(DateTimeOffset SubmittedAt, int ItemCount);

public record CustomerOrderItemDto(int MenuItemId, string Name, string Kind, int Quantity, string? Note, string? ImagePath);

public record ReportSummaryDto(
    decimal Revenue,
    int BillCount,
    int GuestCount,
    decimal AvgPerGuest,
    /// <summary>
    /// ยอดขายของเมื่อวาน "ถึงเวลาเดียวกัน" ของวันนี้ — เฉพาะช่วง <c>today</c> (ช่วงอื่นเป็น null)
    /// แถบ KPI ของหน้าภาพรวมเทียบกับตัวเลขนี้ ไม่ได้ให้ไคลเอนต์เดาเอง · null = ยังเทียบไม่ได้
    /// (เช่น ยังไม่มียอดเมื่อวานถึงเวลานี้) แล้วหน้าจอไม่แสดงบรรทัดเทียบ
    /// </summary>
    decimal? PreviousRevenue,
    IReadOnlyList<PackageMixRow> PackageMix,
    IReadOnlyList<TopItemRow> TopItems,
    IReadOnlyList<HourlyRow> Hourly,
    IReadOnlyList<PaymentMixRow> PaymentMix,
    IReadOnlyList<DailyRow> Daily);

public record PackageMixRow(string Name, int Sessions, int Guests, decimal Revenue);
public record TopItemRow(string Name, int Quantity);
public record HourlyRow(int Hour, decimal Revenue);
public record PaymentMixRow(string Method, int Count, decimal Total);
public record DailyRow(DateOnly Date, decimal Revenue, int Bills);

public static class DtoMapping
{
    public static TableDto ToDto(this Table t) => new(t.Id, t.Name, t.Seats, t.Zone, t.SortOrder, t.IsActive);

    public static PackageDto ToDto(this Package p) =>
        new(p.Id, p.Name, p.Rank, p.AdultPrice, p.ChildPrice, p.DurationMinutes, p.IsActive);

    public static MenuItemDto ToDto(this MenuItem m) => new(m.Id, m.CategoryId, m.Category?.Name ?? "", m.Name,
        m.Description, m.ImagePath, m.Kind.ToString(), m.MinPackageRank, m.Price, m.IsAvailable, m.IsActive,
        m.SortOrder);

    public static OrderDto ToDto(this Order o) => new(o.Id, o.TableSessionId, o.TableSession?.Table?.Name ?? "",
        o.Number, o.Status.ToString(), o.CancelReason, o.CreatedAt, o.UpdatedAt,
        o.Items.Select(i => new OrderItemDto(i.NameSnapshot, i.KindSnapshot.ToString(), i.UnitPrice, i.Quantity,
            i.Note)).ToList());

    public static AdjustmentDto ToDto(this BillAdjustment a) =>
        new(a.Id, a.Description, a.Amount, a.CreatedAt);

    public static PaymentDto ToDto(this Payment p) =>
        new(p.Method.ToString(), p.AmountReceived, p.Change, p.ReceiptNo, p.PaidAt, p.Total);

    public static SessionSummaryDto ToSummaryDto(this TableSession s) => new(s.Id, s.TableId,
        s.Table?.Name ?? "", s.PackageNameSnapshot, s.AdultCount, s.ChildCount, s.OpenedAt, s.EndsAt,
        s.Status.ToString());

    public static SettingsDto ToDto(this RestaurantSettings s) => new(s.Name, s.Address, s.Phone, s.TaxId,
        s.PromptPayId, s.BannerImagePath, s.ServiceChargePercent, s.VatPercent, s.MaxQtyPerItemPerOrder,
        s.MaxItemsPerOrder, s.LastOrderWarningMinutes);
}
