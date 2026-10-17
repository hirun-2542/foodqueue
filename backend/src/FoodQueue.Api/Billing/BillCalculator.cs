using FoodQueue.Api.Contracts;
using FoodQueue.Api.Data.Entities;

namespace FoodQueue.Api.Billing;

/// <summary>
/// ตัวคำนวณบิล — ฟังก์ชันบริสุทธิ์จุดเดียวที่ทุกที่ใช้ร่วมกัน
/// (บิลฝั่งลูกค้า, หน้ารายละเอียด session, การจ่ายเงิน, ข้อมูลพิมพ์, รายงาน)
/// </summary>
public static class BillCalculator
{
    public static BillDto Compute(
        TableSession session,
        IEnumerable<Order> orders,
        IEnumerable<BillAdjustment> adjustments,
        RestaurantSettings settings,
        IEnumerable<TableSession>? mergedSessions = null)
    {
        var sessions = new List<TableSession> { session };
        if (mergedSessions is not null)
        {
            sessions.AddRange(mergedSessions);
        }

        var sessionIds = sessions.Select(s => s.Id).ToHashSet();

        var adults = sessions.Sum(s => s.AdultCount);
        var children = sessions.Sum(s => s.ChildCount);
        var buffetTotal = sessions.Sum(s => s.AdultCount * s.AdultPriceSnapshot + s.ChildCount * s.ChildPriceSnapshot);

        var paidOrders = orders
            .Where(o => sessionIds.Contains(o.TableSessionId) && o.Status != OrderStatus.Cancelled)
            .ToList();

        var lines = paidOrders
            .SelectMany(o => o.Items)
            .Where(i => i.KindSnapshot == MenuItemKind.AlaCarte)
            .GroupBy(i => new { i.NameSnapshot, i.UnitPrice })
            .Select(g => new AlaCarteLineDto(g.Key.NameSnapshot, g.Sum(i => i.Quantity), g.Key.UnitPrice,
                g.Key.UnitPrice * g.Sum(i => i.Quantity)))
            .OrderByDescending(l => l.Amount)
            .ThenBy(l => l.Name, StringComparer.Ordinal)
            .ToList();

        var alaCarteTotal = lines.Sum(l => l.Amount);

        var adjustmentRows = adjustments
            .Where(a => sessionIds.Contains(a.TableSessionId))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.Id)
            .Select(a => a.ToDto())
            .ToList();

        var adjustmentTotal = adjustmentRows.Sum(a => a.Amount);
        var subtotal = buffetTotal + alaCarteTotal + adjustmentTotal;
        var service = Round2(subtotal * settings.ServiceChargePercent / 100m);
        var vat = Round2((subtotal + service) * settings.VatPercent / 100m);

        return new BillDto(
            adults,
            children,
            session.AdultPriceSnapshot,
            session.ChildPriceSnapshot,
            buffetTotal,
            lines,
            alaCarteTotal,
            adjustmentRows,
            adjustmentTotal,
            subtotal,
            settings.ServiceChargePercent,
            service,
            settings.VatPercent,
            vat,
            subtotal + service + vat);
    }

    /// <summary>ปัด 2 ตำแหน่งแบบ AwayFromZero ตาม spec</summary>
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
