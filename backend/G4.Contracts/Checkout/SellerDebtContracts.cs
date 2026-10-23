namespace G4.Contracts.Checkout;

public sealed record DebtRepaymentRequest(
    decimal Amount,
    decimal ExpectedDebt,
    int ExpectedCycleNumber,
    string Key,
    bool SimulateFailure = false
);

public sealed record DebtRepaymentView(
    long Id,
    int CycleNumber,
    decimal Amount,
    decimal RemainingDebt,
    string Currency,
    string Status,
    string BankReferenceId,
    DateTime CreatedAt
);

public sealed record SellerDebtSummary(
    int SellerId,
    decimal Balance,
    int CycleNumber,
    DateTime? StartedAt,
    DateTime? DueAt,
    long AgeSeconds,
    bool Overdue,
    bool Suspended,
    string AccountStatus,
    DateTime? PermanentSalesBlockedAt = null,
    DateTime? EarliestWriteOffAt = null,
    int RecoveryGraceSeconds = 0
);

public sealed record SellerProductInput(
    string Title,
    string? Description,
    decimal Price,
    decimal WeightKg,
    int Quantity,
    int? CategoryId = null
);
