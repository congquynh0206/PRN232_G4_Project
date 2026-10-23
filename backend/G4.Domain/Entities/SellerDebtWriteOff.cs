namespace G4.Domain.Entities;

public sealed class SellerDebtWriteOff
{
    public long Id { get; set; }
    public int SellerAccountId { get; set; }
    public int CycleNumber { get; set; }
    public decimal OriginalAmount { get; set; }
    public DateTime DebtStartedAt { get; set; }
    public DateTime DebtDueAt { get; set; }
    public DateTime EligibleAt { get; set; }
    public DateTime LastRecoveryAt { get; set; }
    public int RecoveryGraceSeconds { get; set; }
    public int BlacklistedIdentities { get; set; }
    public string Actor { get; set; } = "BackgroundWorker";
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class SellerWriteOffRecovery
{
    public long Id { get; set; }
    public long WriteOffId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class SellerBlacklistEntry
{
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string MaskedValue { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string KeyId { get; set; } = "";
    public int? SourceSellerId { get; set; }
    public long? WriteOffId { get; set; }
    public bool IsDemo { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
