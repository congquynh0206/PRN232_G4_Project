using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;

namespace G4.Infrastructure.Authentication;
public sealed class SellerIdentityHasher
{
    private readonly byte[] key;
    public string KeyId { get; }
    public SellerIdentityHasher(IConfiguration config)
    {
        KeyId = config["SellerIdentity:KeyId"] ?? "v1";
        if (!Regex.IsMatch(KeyId, "^[a-zA-Z0-9_-]{1,40}$")) throw new InvalidOperationException("SellerIdentity:KeyId chưa hợp lệ.");
        try { key = Convert.FromBase64String(config["SellerIdentity:HmacKey"] ?? ""); }
        catch (FormatException) { throw new InvalidOperationException("SellerIdentity:HmacKey cần là khóa Base64 tối thiểu 32 byte."); }
        if (key.Length < 32) throw new InvalidOperationException("Cần cấu hình SellerIdentity:HmacKey tối thiểu 32 byte ngoài database.");
    }
    public string Sign(string purpose, params string[] values) => Convert.ToHexString(HMACSHA256.HashData(key,
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { purpose }.Concat(values)))));
    public IdentityMatch Match(G4.Contracts.Checkout.SellerIdentityInput input)
    {
        if (input == null || input.Value == null || input.Value.Length > 100 || (input.BankCode?.Length ?? 0) > 30)
            throw new ArgumentException("Thông tin định danh chưa hợp lệ.");
        var kind = input.Kind?.Trim().ToUpperInvariant() ?? "";
        var value = Regex.Replace(input.Value.Trim().ToUpperInvariant(), @"\s+", "");
        var bank = Regex.Replace((input.BankCode ?? "").Trim().ToUpperInvariant(), @"\s+", "");
        if (kind == "CCCD" && !Regex.IsMatch(value, "^[0-9]{12}$")
            || kind == "PASSPORT" && !Regex.IsMatch(value, "^[A-Z]{1,3}[0-9]{6,9}$")
            || kind == "BANK" && (!Regex.IsMatch(value, "^[0-9]{6,30}$") || !Regex.IsMatch(bank, "^[A-Z0-9]{2,12}$"))
            || kind is not ("CCCD" or "PASSPORT" or "BANK"))
            throw new ArgumentException("Xác minh giả lập thất bại: CCCD cần 12 số; Passport gồm 1–3 chữ và 6–9 số; tài khoản ngân hàng cần 6–30 số và mã ngân hàng 2–12 ký tự.");
        if (kind != "BANK") bank = "";
        var masked = (kind == "BANK" ? bank + " · " : kind + " · ") + new string('*', value.Length - 4) + value[^4..];
        return new(kind, masked, Sign("identity", kind, bank, value));
    }
    public async Task EnsureKeyAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var verifier = Sign("key-verifier", KeyId);
        var keys = await db.SellerIdentityKeys.ToListAsync(ct);
        if (keys.Any(k => k.Id != KeyId || k.Verifier != verifier))
            throw new InvalidOperationException("Khóa đối chiếu định danh không khớp dữ liệu hiện có. Cần khôi phục đúng khóa trước khi tiếp tục.");
        if (keys.Count == 0)
        {
            db.SellerIdentityKeys.Add(new SellerIdentityKey { Id = KeyId, Verifier = verifier });
            await db.SaveChangesAsync(ct);
        }
    }
    public static void ConfigureLocalKey(IConfiguration config, IHostEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(config["SellerIdentity:HmacKey"])) return;
        if (!env.IsDevelopment()) return; // Non-development deployments must supply their own key.
        var folder = Path.Combine(env.ContentRootPath, ".local-security");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "seller-identity.key");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        }
        catch (IOException) when (File.Exists(path)) { }
        config["SellerIdentity:HmacKey"] = File.ReadAllText(path).Trim();
    }
    public sealed record IdentityMatch(string Kind, string MaskedValue, string Fingerprint);
}

public static class SellerRegistryGate
{
    public static Task LockAsync(ApplicationDbContext db, CancellationToken ct) => LockResourceAsync(db, "commerce:seller-identities", ct);
    public static Task SellerAsync(ApplicationDbContext db, int sellerId, CancellationToken ct) => LockResourceAsync(db, $"commerce:seller-finance:{sellerId}", ct);
    private static async Task LockResourceAsync(ApplicationDbContext db, string resource, CancellationToken ct)
    {
        if (!db.Database.IsSqlServer()) return;
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = N'Exclusive',
                @LockOwner = N'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51000, 'Cannot acquire seller registry transaction lock', 1;", ct);
    }
}
