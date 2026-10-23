using G4.Contracts.Checkout;
using Microsoft.Extensions.Configuration;

namespace G4.Infrastructure.Finance;

public static class SellerLevelRules
{
    public static SellerLevelPolicy Policy(IConfiguration config)
    {
        SellerLevelTier Tier(
            int level,
            int orders,
            decimal feedback,
            decimal onTime,
            int age,
            decimal cancel,
            decimal returns,
            decimal disputes
        )
        {
            var c = config.GetSection($"Finance:LevelPromotion:Level{level}");
            var result = new SellerLevelTier(
                level,
                c.GetValue("CompletedOrders", orders),
                c.GetValue("PositiveFeedbackRate", feedback),
                c.GetValue("OnTimeDeliveryRate", onTime),
                c.GetValue("ActivityDays", age),
                c.GetValue("MaxCancellationRate", cancel),
                c.GetValue("MaxReturnRate", returns),
                c.GetValue("MaxDisputeRate", disputes)
            );
            if (
                result.CompletedOrders < 1
                || result.ActivityDays < 0
                || new[]
                {
                    result.PositiveFeedbackRate,
                    result.OnTimeDeliveryRate,
                    result.MaxCancellationRate,
                    result.MaxReturnRate,
                    result.MaxDisputeRate,
                }.Any(x => x < 0 || x > 100)
            )
                throw new InvalidOperationException("Cấu hình tiêu chí cấp seller không hợp lệ.");
            return result;
        }
        var p = new SellerLevelPolicy(
            config.GetValue("Finance:LevelPromotion:WindowDays", 90),
            config.GetValue("Finance:LevelPromotion:MinimumPaidOrders", 20),
            config.GetValue("Finance:LevelPromotion:EvaluationSeconds", 3600),
            Tier(2, 20, 90, 90, 30, 5, 10, 2),
            Tier(3, 100, 97, 95, 90, 2, 5, 1)
        );
        if (
            p.WindowDays is < 1 or > 3650
            || p.MinimumPaidOrders < 1
            || p.EvaluationSeconds is < 5 or > 2592000
        )
            throw new InvalidOperationException("Cấu hình kỳ đánh giá seller không hợp lệ.");
        if (
            p.Level3.CompletedOrders < p.Level2.CompletedOrders
            || p.Level3.ActivityDays < p.Level2.ActivityDays
            || p.Level3.PositiveFeedbackRate < p.Level2.PositiveFeedbackRate
            || p.Level3.OnTimeDeliveryRate < p.Level2.OnTimeDeliveryRate
            || p.Level3.MaxCancellationRate > p.Level2.MaxCancellationRate
            || p.Level3.MaxReturnRate > p.Level2.MaxReturnRate
            || p.Level3.MaxDisputeRate > p.Level2.MaxDisputeRate
        )
            throw new InvalidOperationException(
                "Tiêu chí cấp 3 phải chặt chẽ hơn hoặc bằng cấp 2."
            );
        return p;
    }

    public static SellerLevelDecision Decide(int current, SellerLevelMetrics m, SellerLevelPolicy p)
    {
        if (current is < 1 or > 3)
            throw new ArgumentException("Cấp hiện tại phải từ 1 đến 3.");
        SellerLevelTierProgress Check(SellerLevelTier t)
        {
            SellerLevelCriterion C(
                string code,
                string label,
                decimal? actual,
                decimal required,
                bool max = false
            ) =>
                new(
                    code,
                    label,
                    actual,
                    required,
                    max,
                    actual == null ? null
                        : max ? actual <= required
                        : actual >= required
                );
            var checks = new[]
            {
                C(
                    "sample",
                    $"Đơn đã thanh toán trong {p.WindowDays} ngày",
                    m.PaidOrders,
                    p.MinimumPaidOrders
                ),
                C("completed", "Đơn hoàn tất cộng dồn", m.CompletedOrders, t.CompletedOrders),
                C(
                    "feedback",
                    $"Phản hồi tích cực ({m.FeedbackCount} đánh giá)",
                    m.PositiveFeedbackRate,
                    t.PositiveFeedbackRate
                ),
                C(
                    "delivery",
                    $"Giao đúng hạn ({m.DeliveryCount} đơn có thời hạn)",
                    m.OnTimeDeliveryRate,
                    t.OnTimeDeliveryRate
                ),
                C("age", "Số ngày hoạt động", m.ActivityDays, t.ActivityDays),
                C(
                    "cancel",
                    "Tỷ lệ hủy đơn đã thanh toán",
                    m.CancellationRate,
                    t.MaxCancellationRate,
                    true
                ),
                C("return", "Tỷ lệ trả hàng được chấp nhận", m.ReturnRate, t.MaxReturnRate, true),
                C("dispute", "Tỷ lệ đơn có tranh chấp", m.DisputeRate, t.MaxDisputeRate, true),
                C("debt", "Số dư nợ âm", m.NegativeBalance, 0, true),
                C("restriction", "Hạn chế tài khoản", m.Restricted ? 1 : 0, 0, true),
            };
            return new(t.Level, checks.All(x => x.Passed == true), checks);
        }
        var tiers = new[] { Check(p.Level2), Check(p.Level3) };
        var next = current;
        string reason;
        if (m.NegativeBalance > 0 || m.Restricted)
        {
            next = 1;
            reason =
                "Áp dụng cấp 1 vì seller đang có nợ âm hoặc hạn chế tài khoản; quyết định này không thay đổi trạng thái khóa.";
        }
        else if (m.PaidOrders < p.MinimumPaidOrders)
            reason =
                $"Giữ cấp {current}: chưa đủ {p.MinimumPaidOrders} đơn thanh toán trong {p.WindowDays} ngày.";
        else
        {
            next = tiers.Where(x => x.Eligible).Select(x => x.Level).DefaultIfEmpty(1).Max();
            // Missing feedback/deadline evidence is unknown, never a negative rating.
            if (
                next < current
                && tiers.Single(x => x.Level == current).Criteria.All(x => x.Passed != false)
            )
                next = current;
            reason =
                next > current ? $"Đủ tất cả tiêu chí để nâng lên cấp {next}."
                : next < current
                    ? $"Hạ xuống cấp {next}: không còn đáp ứng tiêu chí giữ cấp {current}."
                : $"Giữ cấp {current}; xem tiêu chí để biết điều kiện lên hoặc giữ cấp.";
        }
        return new(current, next, reason, m, p, tiers);
    }
}
