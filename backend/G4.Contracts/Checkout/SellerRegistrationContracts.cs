namespace G4.Contracts.Checkout;
public sealed record SellerIdentityInput(string Kind, string Value, string? BankCode = null);
public sealed record SellerRegistrationInput(string Username, string Email, string Password, string ConfirmPassword,
    string StoreName, SellerIdentityInput Identity, string Key);
public sealed record SellerRegistrationResult(int SellerId, string Email, string StoreName, string Status, DateTime CreatedAt);
public sealed record SellerIdentityChangeInput(SellerIdentityInput Identity, int ExpectedVersion, string Key);
public sealed record SellerIdentityView(long Id, string Kind, string MaskedValue, DateTime VerifiedAt, bool Current);
public sealed record SellerIdentityProfile(int Version, bool PermanentlyBlocked, IReadOnlyList<SellerIdentityView> Items);
public sealed record SellerRegistrationSample(string Name, string Description, SellerIdentityInput Identity);
public sealed record SellerWriteOffView(long Id, int SellerId, int CycleNumber, decimal OriginalAmount,
    decimal RecoveredAmount, decimal RemainingWrittenOffAmount, decimal CurrentDebt, DateTime CreatedAt,
    DateTime DebtStartedAt, DateTime DebtDueAt, DateTime EligibleAt, DateTime LastRecoveryAt, int RecoveryGraceSeconds,
    int BlacklistedIdentities, string Actor, string Reason);
