using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace G4.Infrastructure.Authentication;
public sealed class SellerRegistrationService(ApplicationDbContext db, SellerIdentityHasher hasher,
    IConfiguration config, IHostEnvironment environment, TimeProvider clock) : ISellerRegistrationService
{
    public const string Denied = "Không thể đăng ký hoặc cập nhật thông tin người bán này.";
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private void Available() { if (!environment.IsDevelopment()) throw new UnauthorizedAccessException("Đăng ký định danh giả lập chỉ dùng trong Development."); }
    private static void Key(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new ArgumentException("Cần mã yêu cầu tối đa 100 ký tự.");
    }
    public async Task<SellerRegistrationResult> RegisterAsync(SellerRegistrationInput input, CancellationToken ct = default)
    {
        Available(); Key(input.Key);
        var username = input.Username?.Trim() ?? "";
        var shop = input.StoreName?.Trim() ?? "";
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        if (username.Length is < 2 or > 100 || shop.Length is < 2 or > 100 || email.Length > 100
            || !MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ArgumentException("Tên người dùng và shop cần 2–100 ký tự; email cần hợp lệ và tối đa 100 ký tự.");
        if (input.Password == null || input.Password.Length is < 8 or > 128 || !input.Password.Any(char.IsLetter)
            || !input.Password.Any(char.IsDigit) || input.Password != input.ConfirmPassword)
            throw new ArgumentException("Mật khẩu cần 8–128 ký tự, có chữ và số; xác nhận mật khẩu phải khớp.");
        var identity = hasher.Match(input.Identity);
        var fingerprint = hasher.Sign("registration", username, email, input.Password, shop, identity.Fingerprint);
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await SellerRegistryGate.LockAsync(db, ct);
        await hasher.EnsureKeyAsync(db, ct);
        await EnsureDemoAsync(ct);
        var existing = await db.SellerRegistrations.SingleOrDefaultAsync(x => x.RequestKey == input.Key, ct);
        if (existing != null)
        {
            if (existing.RequestFingerprint != fingerprint) throw new InvalidOperationException("Mã đăng ký đã được dùng với nội dung khác.");
            if (tx != null) await tx.CommitAsync(ct);
            return Result(existing);
        }
        await CheckIdentityAsync(identity, null, ct);
        if (await db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email, ct))
            throw new InvalidOperationException("Email đã được sử dụng. Hãy đăng nhập hoặc chọn email khác.");
        var salt = RandomNumberGenerator.GetBytes(24);
        var password = Rfc2898DeriveBytes.Pbkdf2(input.Password, salt, 210000, HashAlgorithmName.SHA256, 32);
        var user = new User { Username = username, Email = email, Role = "seller",
            Password = $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(password)}" };
        db.Users.Add(user); await db.SaveChangesAsync(ct);
        var savedIdentity = NewIdentity(user.Id, identity, input.Key, fingerprint);
        db.SellerIdentities.Add(savedIdentity); await db.SaveChangesAsync(ct);
        db.Stores.Add(new Store { SellerId = user.Id, StoreName = shop });
        db.SellerAccounts.Add(new SellerAccount { SellerId = user.Id, Level = 1, Status = "Active", CurrentIdentityId = savedIdentity.Id,
            IdentityVersion = 1, MonthlySalesLimit = config.GetValue("Finance:Levels:1:MonthlySalesLimit", 5000m),
            HoldDays = config.GetValue("Finance:Levels:1:HoldDays", 21), SalesMonth = new DateTime(Now.Year, Now.Month, 1), UpdatedAt = Now });
        var registration = new SellerRegistration { SellerId = user.Id, RequestKey = input.Key,
            RequestFingerprint = fingerprint, Email = email, StoreName = shop, CreatedAt = Now };
        db.SellerRegistrations.Add(registration); await db.SaveChangesAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return Result(registration);
    }
    private static SellerRegistrationResult Result(SellerRegistration r) => new(r.SellerId, r.Email, r.StoreName, "Active", r.CreatedAt);
    private SellerIdentity NewIdentity(int sellerId, SellerIdentityHasher.IdentityMatch match, string key, string requestFingerprint) => new()
    {
        SellerId = sellerId, Kind = match.Kind, MaskedValue = match.MaskedValue, Fingerprint = match.Fingerprint,
        KeyId = hasher.KeyId, RequestKey = key, RequestFingerprint = requestFingerprint, VerifiedAt = Now
    };
    private async Task CheckIdentityAsync(SellerIdentityHasher.IdentityMatch match, int? sellerId, CancellationToken ct)
    {
        if (await db.SellerBlacklist.AnyAsync(x => x.KeyId == hasher.KeyId && x.Fingerprint == match.Fingerprint, ct)
            || await db.SellerIdentities.AnyAsync(x => x.KeyId == hasher.KeyId && x.Fingerprint == match.Fingerprint && x.SellerId != sellerId, ct))
            throw new UnauthorizedAccessException(Denied);
    }
    public async Task<SellerIdentityProfile> ProfileAsync(int sellerId, CancellationToken ct = default)
    {
        var a = await db.SellerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.SellerId == sellerId, ct);
        var currentId = a?.CurrentIdentityId;
        var items = await db.SellerIdentities.AsNoTracking().Where(x => x.SellerId == sellerId).OrderByDescending(x => x.Id)
            .Select(x => new SellerIdentityView(x.Id, x.Kind, x.MaskedValue, x.VerifiedAt, x.Id == currentId)).ToListAsync(ct);
        return new(a?.IdentityVersion ?? 0, a?.PermanentSalesBlockedAt != null, items);
    }
    public async Task<SellerIdentityProfile> ChangeIdentityAsync(int sellerId, SellerIdentityChangeInput input, CancellationToken ct = default)
    {
        Available(); Key(input.Key);
        var identity = hasher.Match(input.Identity);
        var fingerprint = hasher.Sign("identity-change", identity.Fingerprint, input.ExpectedVersion.ToString());
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await SellerRegistryGate.SellerAsync(db, sellerId, ct);
        await new G4.Infrastructure.Finance.SellerFinanceService(db, config, clock).GetDebtAsync(sellerId, ct);
        await SellerRegistryGate.LockAsync(db, ct);
        await hasher.EnsureKeyAsync(db, ct); await EnsureDemoAsync(ct);
        var a = await db.SellerAccounts.SingleOrDefaultAsync(x => x.SellerId == sellerId, ct) ?? throw new KeyNotFoundException("Hãy mở tài chính để khởi tạo tài khoản seller.");
        await db.Entry(a).ReloadAsync(ct);
        var existing = await db.SellerIdentities.SingleOrDefaultAsync(x => x.SellerId == sellerId && x.RequestKey == input.Key, ct);
        if (existing != null)
        {
            if (existing.RequestFingerprint != fingerprint) throw new InvalidOperationException("Mã cập nhật định danh đã được dùng với nội dung khác.");
        }
        else
        {
            if (a.PermanentSalesBlockedAt != null) throw new UnauthorizedAccessException(Denied);
            if (a.IdentityVersion != input.ExpectedVersion) throw new InvalidOperationException("Hồ sơ đã thay đổi. Hãy tải lại trước khi cập nhật.");
            await CheckIdentityAsync(identity, sellerId, ct);
            var same = await db.SellerIdentities.SingleOrDefaultAsync(x => x.KeyId == hasher.KeyId && x.Fingerprint == identity.Fingerprint, ct);
            if (same != null) throw new InvalidOperationException("Định danh này đã có trong lịch sử của bạn. Hãy dùng định danh mới.");
            var row = NewIdentity(sellerId, identity, input.Key, fingerprint);
            db.SellerIdentities.Add(row); await db.SaveChangesAsync(ct);
            a.CurrentIdentityId = row.Id; a.IdentityVersion++; a.UpdatedAt = Now;
            await db.SaveChangesAsync(ct);
        }
        var result = await ProfileAsync(sellerId, ct);
        if (tx != null) await tx.CommitAsync(ct);
        return result;
    }
    private async Task EnsureDemoAsync(CancellationToken ct)
    {
        var match = hasher.Match(new("CCCD", "999000000002"));
        if (!await db.SellerBlacklist.AnyAsync(x => x.KeyId == hasher.KeyId && x.Fingerprint == match.Fingerprint, ct))
        {
            // Reserved synthetic identity; never derived from an existing account.
            if (await db.SellerIdentities.AnyAsync(x => x.KeyId == hasher.KeyId && x.Fingerprint == match.Fingerprint, ct))
                throw new InvalidOperationException("Định danh mẫu đã có chủ; không thể tạo fixture blacklist.");
            db.SellerBlacklist.Add(new SellerBlacklistEntry { Kind = match.Kind, MaskedValue = match.MaskedValue,
                Fingerprint = match.Fingerprint, KeyId = hasher.KeyId, IsDemo = true, Reason = "Dữ liệu blacklist giả lập Development", CreatedAt = Now });
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task<IReadOnlyList<SellerRegistrationSample>> SamplesAsync(CancellationToken ct = default)
    {
        Available();
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
        await SellerRegistryGate.LockAsync(db, ct); await hasher.EnsureKeyAsync(db, ct); await EnsureDemoAsync(ct);
        if (tx != null) await tx.CommitAsync(ct);
        return [new("valid", "Seller mới hợp lệ", new("CCCD", "999" + RandomNumberGenerator.GetInt32(3, 1000000000).ToString("D9"))),
            new("invalid", "Định danh không hợp lệ", new("CCCD", "1234")),
            new("blacklisted", "Định danh đã thuộc blacklist giả lập", new("CCCD", "999000000002"))];
    }
}
