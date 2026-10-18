namespace FoodQueue.Api.Data.Entities;

public enum MenuItemKind { Buffet, AlaCarte }

public enum SessionStatus { Open, Paid, Voided }

public enum OrderStatus { Pending, Preparing, Ready, Cancelled }

public enum PaymentMethod { Cash, PromptPay, Card }

public class Table
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Seats { get; set; } = 4;
    public string? Zone { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Package
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Rank { get; set; }
    public decimal AdultPrice { get; set; }
    public decimal ChildPrice { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public List<MenuItem> Items { get; set; } = [];
}

public class MenuItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? ImagePath { get; set; }
    public MenuItemKind Kind { get; set; }
    public int? MinPackageRank { get; set; }
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class TableSession
{
    public int Id { get; set; }
    public int TableId { get; set; }
    public Table? Table { get; set; }
    public int PackageId { get; set; }
    public Package? Package { get; set; }
    public string Token { get; set; } = "";
    public int AdultCount { get; set; }
    public int ChildCount { get; set; }
    public string PackageNameSnapshot { get; set; } = "";
    public decimal AdultPriceSnapshot { get; set; }
    public decimal ChildPriceSnapshot { get; set; }
    public int PackageRankSnapshot { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Open;
    public DateTimeOffset? ClosedAt { get; set; }
    public bool IsDemoBot { get; set; }
    /// <summary>Session ที่ถูกนำไปรวมเข้าบิลของ session หลัก (null = ไม่ได้ถูกรวม).</summary>
    public int? MergedIntoSessionId { get; set; }
    public TableSession? MergedIntoSession { get; set; }
    public List<Order> Orders { get; set; } = [];
    public List<BillAdjustment> Adjustments { get; set; } = [];
    public Payment? Payment { get; set; }
}

public class Order
{
    public int Id { get; set; }
    public int TableSessionId { get; set; }
    public TableSession? TableSession { get; set; }
    public int Number { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public string? CancelReason { get; set; }
    /// <summary>เหตุผลของการแก้ไขครั้งล่าสุด (ลดจำนวน/นำรายการออก, ticket 17) — null = ไม่เคยแก้</summary>
    public string? EditReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int MenuItemId { get; set; }
    public string NameSnapshot { get; set; } = "";
    public MenuItemKind KindSnapshot { get; set; }
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
}

public class BillAdjustment
{
    public int Id { get; set; }
    public int TableSessionId { get; set; }
    public TableSession? TableSession { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int TableSessionId { get; set; }
    public TableSession? TableSession { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal BuffetTotal { get; set; }
    public decimal AlaCarteTotal { get; set; }
    public decimal AdjustmentTotal { get; set; }
    public decimal Subtotal { get; set; }
    public decimal ServiceCharge { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public decimal AmountReceived { get; set; }
    public decimal Change { get; set; }
    public string ReceiptNo { get; set; } = "";
    public DateTimeOffset PaidAt { get; set; }
}

public class RestaurantSettings
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Phone { get; set; } = "";
    public string? TaxId { get; set; }
    public string? PromptPayId { get; set; }
    public string? BannerImagePath { get; set; }
    public decimal ServiceChargePercent { get; set; }
    public decimal VatPercent { get; set; }
    public int MaxQtyPerItemPerOrder { get; set; } = 5;
    public int MaxItemsPerOrder { get; set; } = 20;
    public int LastOrderWarningMinutes { get; set; } = 15;
}
