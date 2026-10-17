using FoodQueue.Api.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FoodQueue.Api.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Table> Tables => Set<Table>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<BillAdjustment> BillAdjustments => Set<BillAdjustment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<RestaurantSettings> Settings => Set<RestaurantSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Table>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(20).IsRequired();
            e.Property(x => x.Zone).HasMaxLength(20);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Package>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50).IsRequired();
            e.Property(x => x.AdultPrice).HasPrecision(10, 2);
            e.Property(x => x.ChildPrice).HasPrecision(10, 2);
            e.HasIndex(x => x.Rank).IsUnique();
        });

        b.Entity<Category>(e => e.Property(x => x.Name).HasMaxLength(50).IsRequired());

        b.Entity<MenuItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.HasOne(x => x.Category).WithMany(c => c.Items).HasForeignKey(x => x.CategoryId);
        });

        b.Entity<TableSession>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(43).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.Property(x => x.PackageNameSnapshot).HasMaxLength(50).IsRequired();
            e.Property(x => x.AdultPriceSnapshot).HasPrecision(10, 2);
            e.Property(x => x.ChildPriceSnapshot).HasPrecision(10, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasOne(x => x.Table).WithMany().HasForeignKey(x => x.TableId);
            e.HasOne(x => x.Package).WithMany().HasForeignKey(x => x.PackageId);
            e.HasOne(x => x.MergedIntoSession).WithMany().HasForeignKey(x => x.MergedIntoSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            // โต๊ะหนึ่งมี session ที่ยัง Open ได้ทีละหนึ่ง (partial unique index)
            e.HasIndex(x => x.TableId).IsUnique().HasFilter("\"Status\" = 'Open'");
        });

        b.Entity<Order>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CancelReason).HasMaxLength(200);
            e.HasIndex(x => new { x.TableSessionId, x.Number }).IsUnique();
            e.HasOne(x => x.TableSession).WithMany(s => s.Orders).HasForeignKey(x => x.TableSessionId);
        });

        b.Entity<OrderItem>(e =>
        {
            e.Property(x => x.NameSnapshot).HasMaxLength(80).IsRequired();
            e.Property(x => x.KindSnapshot).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UnitPrice).HasPrecision(10, 2);
            e.Property(x => x.Note).HasMaxLength(100);
            e.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId);
        });

        b.Entity<BillAdjustment>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(100).IsRequired();
            e.Property(x => x.Amount).HasPrecision(10, 2);
            e.HasOne(x => x.TableSession).WithMany(s => s.Adjustments).HasForeignKey(x => x.TableSessionId);
        });

        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ReceiptNo).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasIndex(x => x.TableSessionId).IsUnique();
            foreach (var money in new[]
                     {
                         nameof(Payment.BuffetTotal), nameof(Payment.AlaCarteTotal), nameof(Payment.AdjustmentTotal),
                         nameof(Payment.Subtotal), nameof(Payment.ServiceCharge), nameof(Payment.Vat),
                         nameof(Payment.Total), nameof(Payment.AmountReceived), nameof(Payment.Change)
                     })
            {
                e.Property(money).HasPrecision(10, 2);
            }

            e.HasOne(x => x.TableSession).WithOne(s => s.Payment).HasForeignKey<Payment>(x => x.TableSessionId);
        });

        b.Entity<RestaurantSettings>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            e.Property(x => x.TaxId).HasMaxLength(30);
            e.Property(x => x.PromptPayId).HasMaxLength(30);
            e.Property(x => x.BannerImagePath).HasMaxLength(300);
            e.Property(x => x.ServiceChargePercent).HasPrecision(5, 2);
            e.Property(x => x.VatPercent).HasPrecision(5, 2);
        });
    }
}
