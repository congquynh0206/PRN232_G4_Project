using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace G4.Infrastructure.Notifications;

// Snapshot only short business descriptions; never copy addresses, evidence or provider errors.
internal static class BellNotificationText
{
    internal sealed record Content(string Title, string Message);

    public static Content? Describe(EntityEntry entry)
    {
        bool Changed(params string[] properties) =>
            entry.State == EntityState.Added
            || properties.Any(name => entry.Property(name).IsModified);
        bool HasRelated<T>(Func<T, bool> predicate)
            where T : class =>
            entry
                .Context.ChangeTracker.Entries<T>()
                .Any(e =>
                    e.State is EntityState.Added or EntityState.Modified && predicate(e.Entity)
                );

        return entry.Entity switch
        {
            OrderTable x
                when entry.State == EntityState.Modified
                    && Changed(nameof(x.Status))
                    && !HasRelated<Payment>(p => p.OrderId == x.Id)
                    && !HasRelated<ShippingInfo>(s => s.OrderId == x.Id)
                    && !HasRelated<ReturnRequest>(r => r.OrderId == x.Id) => new(
                "Đơn hàng có cập nhật",
                Order(x.Id, Status(x.Status))
            ),
            Payment x when Changed(nameof(x.Status)) => new(
                "Cập nhật thanh toán",
                Order(x.OrderId, Status(x.Status))
            ),
            Refund x when Changed(nameof(x.Status)) => new(
                "Cập nhật hoàn tiền",
                Order(x.OrderId, Status(x.Status))
            ),
            ReturnRequest x when Changed(nameof(x.Status)) => new(
                "Cập nhật yêu cầu trả hàng",
                Order(x.OrderId, Status(x.Status))
            ),
            ShippingInfo x when Changed(nameof(x.Status), nameof(x.ShipperId)) => new(
                entry.State == EntityState.Added ? "Vận đơn mới" : "Cập nhật vận chuyển",
                Order(
                    x.OrderId,
                    $"{(x.Direction == "Return" ? "Chiều trả hàng" : "Chiều giao hàng")}: "
                        + Status(x.Status)
                )
            ),
            Dispute x when Changed(nameof(x.Status), nameof(x.Proposal), nameof(x.Outcome)) => new(
                "Cập nhật tranh chấp",
                Order(x.OrderId, "Hồ sơ tranh chấp vừa được cập nhật.")
            ),
            DisputeEntry x
                when entry.State == EntityState.Added
                    && !HasRelated<Dispute>(d =>
                        d.Id == x.DisputeId
                        && entry.Context.Entry(d).Property(nameof(d.Status)).IsModified
                    ) => new(
                "Cập nhật tranh chấp",
                "Hồ sơ tranh chấp có phản hồi hoặc bằng chứng mới."
            ),
            SellerAccount x
                when entry.State == EntityState.Modified
                    && Changed(
                        nameof(x.ProcessingBalance),
                        nameof(x.AvailableBalance),
                        nameof(x.OnHoldBalance),
                        nameof(x.NegativeBalance),
                        nameof(x.Status)
                    )
                    && !HasRelated<SellerPayout>(p => p.SellerAccountId == x.Id)
                    && !HasRelated<SellerDebtEvent>(d => d.SellerAccountId == x.Id) => new(
                "Ví người bán có cập nhật",
                "Số dư hoặc trạng thái ví vừa thay đổi."
            ),
            SellerPayout x when Changed(nameof(x.Status)) => new(
                "Cập nhật yêu cầu rút tiền",
                $"Yêu cầu rút tiền: {Status(x.Status)}"
            ),
            SellerDebtEvent when entry.State == EntityState.Added => new(
                "Cập nhật khoản nợ",
                "Khoản nợ hoặc tình trạng hạn chế bán vừa thay đổi."
            ),
            SellerLevelEvaluation x
                when entry.State == EntityState.Added && x.PreviousLevel != x.ResultLevel => new(
                "Cấp người bán thay đổi",
                $"Cấp người bán chuyển từ {x.PreviousLevel} sang {x.ResultLevel}."
            ),
            SellerOrderFeedback when entry.State == EntityState.Added => new(
                "Đánh giá người bán mới",
                "Đã ghi nhận đánh giá người bán cho một đơn hàng."
            ),
            Review when entry.State == EntityState.Added => new(
                "Đánh giá sản phẩm mới",
                "Đã ghi nhận đánh giá sản phẩm trong đơn hàng."
            ),
            AddressChangeRequest x
                when Changed(
                    nameof(x.Status),
                    nameof(x.BuyerConfirmedAt),
                    nameof(x.SellerConfirmedAt),
                    nameof(x.ShipperDecidedAt)
                ) => new(
                "Cập nhật đổi địa chỉ",
                Order(x.OrderId, $"Yêu cầu đổi địa chỉ: {Status(x.Status)}")
            ),
            _ => null,
        };
    }

    private static string Order(int? id, string text) => id > 0 ? $"Đơn #{id} · {text}" : text;

    private static string Status(string? status) =>
        status switch
        {
            "AwaitingPayment" => "Chờ thanh toán",
            "Pending" or "Requested" => "Đang chờ xử lý",
            "Verifying" => "Đang xác minh, chưa xác nhận thành công",
            "Succeeded" => "Thành công",
            "Failed" => "Thất bại",
            "Paid" => "Đã thanh toán",
            "Preparing" => "Đang chuẩn bị hàng",
            "Shipping" or "InTransit" => "Đang vận chuyển",
            "CancelRequested" => "Đang chờ duyệt hủy",
            "Cancelled" => "Đã hủy",
            "Expired" => "Đã hết hạn",
            "Closed" or "Completed" => "Đã hoàn tất",
            "Approved" => "Đã chấp thuận",
            "Rejected" => "Đã từ chối",
            "LabelCreated" => "Đã tạo nhãn, chờ nhận vận đơn",
            "PickedUp" => "Đã lấy hàng",
            "OutForDelivery" => "Đang giao đến người nhận",
            "Delivered" => "Đã giao đến người nhận",
            "DeliveryFailed" => "Giao hàng thất bại",
            "ReattemptScheduled" => "Đã lên lịch giao lại",
            "ReturningToSender" => "Đang chuyển hoàn",
            "ReturnedToSeller" => "Người bán đã nhận hàng chuyển hoàn",
            "ReturnShipping" => "Đang trả hàng về người bán",
            "ReceivedBySeller" => "Người bán đã nhận hàng trả",
            "RefundPending" => "Đang xử lý hoàn tiền",
            "RefundFailed" => "Hoàn tiền chưa thành công",
            "Refunded" => "Đã hoàn tiền",
            "Created" => "Đã tạo yêu cầu",
            "InProgress" => "Đang xử lý",
            "FundsSent" => "Đã gửi ngân hàng, chờ xác nhận",
            "Returned" => "Ngân hàng đã trả lại tiền",
            _ => "Thông tin vừa được cập nhật",
        };
}
