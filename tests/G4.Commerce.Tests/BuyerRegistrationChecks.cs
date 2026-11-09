using G4.Contracts.Checkout;
using G4.Domain.Entities;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

internal static class BuyerRegistrationChecks
{
    public static async Task RunAsync()
    {
        await RegistersBuyerThatCanLogIn();
        await RejectsTakenEmail();
        await RejectsInvalidInput();
        await DevelopmentOnly();
        Console.WriteLine("Buyer registration checks passed");
    }

    private static BuyerRegistrationInput Input(
        string email = "New.Buyer@Example.test",
        string password = "BuyerDemo123"
    ) => new("  An ", " Nguyen ", email, password);

    private static ApplicationDbContext Db() =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static BuyerRegistrationService Service(
        ApplicationDbContext db,
        string environment = "Development"
    ) => new(db, new SellerRegistrationChecks.Host { EnvironmentName = environment });

    private static async Task RegistersBuyerThatCanLogIn()
    {
        await using var db = Db();
        var user = await Service(db).RegisterAsync(Input());
        Check(user.Role == "buyer", "new account gets the buyer role");
        Check(user.Username == "An Nguyen", "first and last name are trimmed into Username");
        Check(user.Email == "new.buyer@example.test", "email is stored lower-case");
        Check(user.Password!.StartsWith("PBKDF2$"), "password is hashed, never stored as text");
        var auth = new AuthenticationService(db);
        var login = await auth.ValidateCredentialsAsync("NEW.BUYER@example.test", "BuyerDemo123");
        Check(login?.Id == user.Id, "new buyer can log in with the chosen password");
        Check(
            await auth.ValidateCredentialsAsync("new.buyer@example.test", "Wrong12345") == null,
            "wrong password is rejected"
        );
    }

    private static async Task RejectsTakenEmail()
    {
        await using var db = Db();
        db.Users.Add(new User { Email = "seller@example.test", Role = "seller" });
        await db.SaveChangesAsync();
        var service = Service(db);
        await Reject<InvalidOperationException>(
            () => service.RegisterAsync(Input("Seller@Example.test"))
        );
        await service.RegisterAsync(Input());
        await Reject<InvalidOperationException>(() => service.RegisterAsync(Input()));
        Check(await db.Users.CountAsync() == 2, "a taken email never creates a second account");
    }

    private static async Task RejectsInvalidInput()
    {
        await using var db = Db();
        var service = Service(db);
        await Reject<ArgumentException>(() => service.RegisterAsync(Input("not-an-email")));
        await Reject<ArgumentException>(() => service.RegisterAsync(Input(password: "short1")));
        await Reject<ArgumentException>(() => service.RegisterAsync(Input(password: "lettersonly")));
        await Reject<ArgumentException>(() => service.RegisterAsync(Input(password: "12345678")));
        await Reject<ArgumentException>(
            () => service.RegisterAsync(new(" ", "Nguyen", "a@example.test", "BuyerDemo123"))
        );
        Check(!await db.Users.AnyAsync(), "invalid input creates no account");
    }

    private static async Task DevelopmentOnly()
    {
        await using var db = Db();
        await Reject<UnauthorizedAccessException>(
            () => Service(db, "Production").RegisterAsync(Input())
        );
        Check(!await db.Users.AnyAsync(), "registration is closed outside Development");
    }

    private static async Task Reject<T>(Func<Task> action)
        where T : Exception
    {
        try
        {
            await action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok)
            throw new Exception(message);
    }
}
