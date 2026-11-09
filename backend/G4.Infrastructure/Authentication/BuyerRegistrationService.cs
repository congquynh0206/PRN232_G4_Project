using System.Data;
using System.Net.Mail;
using G4.Contracts.Checkout;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace G4.Infrastructure.Authentication;

public sealed class BuyerRegistrationService(
    ApplicationDbContext db,
    IHostEnvironment environment
) : IBuyerRegistrationService
{
    public const string EmailTaken = "Email đã được sử dụng. Hãy đăng nhập hoặc chọn email khác.";

    public async Task<User> RegisterAsync(
        BuyerRegistrationInput input,
        CancellationToken ct = default
    )
    {
        if (!environment.IsDevelopment())
            throw new UnauthorizedAccessException(
                "Đăng ký người mua chỉ dùng trong Development."
            );
        var firstName = input.FirstName?.Trim() ?? "";
        var lastName = input.LastName?.Trim() ?? "";
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        // User only has Username, so the full name is stored there.
        var username = $"{firstName} {lastName}";
        if (firstName.Length is < 1 or > 50 || lastName.Length is < 1 or > 50)
            throw new ArgumentException("Họ và tên mỗi phần cần 1–50 ký tự.");
        if (
            email.Length > 100
            || !MailAddress.TryCreate(email, out var address)
            || address.Address != email
        )
            throw new ArgumentException("Email cần hợp lệ và tối đa 100 ký tự.");
        if (
            input.Password == null
            || input.Password.Length is < 8 or > 128
            || !input.Password.Any(char.IsLetter)
            || !input.Password.Any(char.IsDigit)
        )
            throw new ArgumentException("Mật khẩu cần 8–128 ký tự, có cả chữ và số.");

        await using var tx =
            db.Database.IsRelational() && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                : null;
        // Same lock as seller registration, so one email cannot be taken twice at once.
        await SellerRegistryGate.LockAsync(db, ct);
        if (await db.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email, ct))
            throw new InvalidOperationException(EmailTaken);
        var user = new User
        {
            Username = username,
            Email = email,
            Role = "buyer",
            Password = PasswordHashing.Hash(input.Password),
        };
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The unique email index is the last guard if another request won the race.
            throw new InvalidOperationException(EmailTaken);
        }
        if (tx != null)
            await tx.CommitAsync(ct);
        return user;
    }
}
