using System.Data;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace G4.Infrastructure.Checkout;
/// <summary>
/// Buyer address book. Rules: first address becomes default, at most one default per buyer,
/// deleting the default promotes a remaining address. Orders keep their own address snapshot,
/// so editing or deleting an address never changes an existing order's address or money.
/// </summary>
public sealed class BuyerAddressService(ApplicationDbContext db) : IBuyerAddressService
{
    private static BuyerAddressView View(Address a) => new(a.Id, a.FullName ?? "", a.Phone, a.Street ?? "", a.City ?? "", a.State ?? "", a.Country ?? "", a.IsDefault == true);

    private async Task<IReadOnlyList<BuyerAddressView>> ReadAsync(int buyerId, CancellationToken ct) => (await db.Addresses.AsNoTracking().Where(a => a.UserId == buyerId).OrderBy(a => a.Id)
        .ToListAsync(ct)).OrderByDescending(a => a.IsDefault == true)
        .ThenBy(a => a.Id)
        .Select(View)
        .ToArray();
    public Task<IReadOnlyList<BuyerAddressView>> ListAsync(int buyerId, CancellationToken ct = default) => ReadAsync(buyerId, ct);
    // Serialize every write of one buyer's address book so two tabs cannot create two defaults.
    private async Task<IDbContextTransaction?> LockAsync(int buyerId, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
            return null;
        var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            if (db.Database.IsSqlServer())
                await db.Users.FromSqlInterpolated($"SELECT * FROM [User] WITH (UPDLOCK,HOLDLOCK) WHERE [id]={buyerId}").AsNoTracking().ToListAsync(ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private static void Apply(Address address, BuyerAddressInput input)
    {
        address.FullName = input.FullName;
        address.Street = input.Street;
        address.City = input.City;
        address.State = input.State;
        address.Country = input.Country;
    }

    /// <summary>Guarantees exactly one default when the book is not empty. preferred wins when set.</summary>
    private static void NormalizeDefault(IReadOnlyList<Address> book, int? preferred)
    {
        if (book.Count == 0)
            return;
        var keep = book.FirstOrDefault(a => a.Id == preferred && preferred != null) ?? book.Where(a => a.IsDefault == true).OrderBy(a => a.Id)
            .FirstOrDefault() ?? book.OrderBy(a => a.Id).First();
        foreach (var a in book)
        {
            var value = ReferenceEquals(a, keep);
            if (a.IsDefault != value)
                a.IsDefault = value;
        }
    }

    private static void EnsureValid(BuyerAddressInput input)
    {
        var errors = BuyerAddressRules.Validate(input);
        if (errors.Count > 0)
            throw new ArgumentException(errors.First().Value);
    }

    public async Task<BuyerAddressBook> CreateAsync(int buyerId, BuyerAddressInput input, CancellationToken ct = default)
    {
        EnsureValid(input);
        input = BuyerAddressRules.Normalize(input);
        await using var tx = await LockAsync(buyerId, ct);
        var book = await db.Addresses.Where(a => a.UserId == buyerId).ToListAsync(ct);
        if (book.Count >= BuyerAddressRules.MaxAddresses)
            throw new InvalidOperationException($"Mỗi tài khoản chỉ lưu tối đa {BuyerAddressRules.MaxAddresses} địa chỉ. Hãy xóa bớt địa chỉ không dùng.");
        var address = new Address { UserId = buyerId, IsDefault = false };
        Apply(address, input);
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        book.Add(address);
        // The first address is always the default; later ones only when the buyer asks.
        NormalizeDefault(book, book.Count == 1 || input.IsDefault ? address.Id : null);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return new(address.Id, await ReadAsync(buyerId, ct));
    }

    public async Task<BuyerAddressBook> UpdateAsync(int buyerId, int id, BuyerAddressInput input, CancellationToken ct = default)
    {
        EnsureValid(input);
        input = BuyerAddressRules.Normalize(input);
        await using var tx = await LockAsync(buyerId, ct);
        var book = await db.Addresses.Where(a => a.UserId == buyerId).ToListAsync(ct);
        var address = book.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Không tìm thấy địa chỉ.");
        Apply(address, input);
        NormalizeDefault(book, input.IsDefault ? id : null);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return new(id, await ReadAsync(buyerId, ct));
    }

    public async Task<BuyerAddressBook> SetDefaultAsync(int buyerId, int id, CancellationToken ct = default)
    {
        await using var tx = await LockAsync(buyerId, ct);
        var book = await db.Addresses.Where(a => a.UserId == buyerId).ToListAsync(ct);
        if (book.All(a => a.Id != id))
            throw new KeyNotFoundException("Không tìm thấy địa chỉ.");
        NormalizeDefault(book, id);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return new(id, await ReadAsync(buyerId, ct));
    }

    public async Task<BuyerAddressBook> DeleteAsync(int buyerId, int id, CancellationToken ct = default)
    {
        await using var tx = await LockAsync(buyerId, ct);
        var book = await db.Addresses.Where(a => a.UserId == buyerId).ToListAsync(ct);
        var address = book.SingleOrDefault(a => a.Id == id) ?? throw new KeyNotFoundException("Không tìm thấy địa chỉ.");
        // Retain referenced rows as historical addresses. Checkout retries compare the original
        // AddressId, so clearing that FK would invalidate an otherwise identical idempotent request.
        // Unowned addresses cannot be selected, listed or edited by any buyer. No schema change.
        if (await db.OrderTables.AnyAsync(o => o.AddressId == id, ct))
        {
            address.UserId = null;
            address.User = null;
            address.IsDefault = false;
        }
        else
            db.Addresses.Remove(address);
        book.Remove(address);
        // Deleting the default promotes the oldest remaining address; no addresses means no default.
        NormalizeDefault(book, null);
        await db.SaveChangesAsync(ct);
        if (tx is not null)
            await tx.CommitAsync(ct);
        return new(null, await ReadAsync(buyerId, ct));
    }
}
