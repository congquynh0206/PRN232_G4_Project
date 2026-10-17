namespace G4.Domain.Entities;
public sealed class SellerIdentity
{
    public long Id { get; set; }
    public int SellerId { get; set; }
    public string Kind { get; set; } = "";
    public string MaskedValue { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string RequestKey { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public DateTime VerifiedAt { get; set; }
}
public sealed class SellerIdentityKey
{
    public string Id { get; set; } = "";
    public string Verifier { get; set; } = "";
}
public sealed class SellerRegistration
{
    public long Id { get; set; }
    public int SellerId { get; set; }
    public string RequestKey { get; set; } = "";
    public string RequestFingerprint { get; set; } = "";
    public string Email { get; set; } = "";
    public string StoreName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
