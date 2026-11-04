using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IAuthenticationService
{
    Task<bool> ValidatePasswordAsync(
        int userId,
        string role,
        string password,
        CancellationToken ct = default
    );

    Task<User?> ValidateCredentialsAsync(
        string email,
        string password,
        CancellationToken ct = default
    );
}
