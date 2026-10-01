using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IAuthenticationService
{
    Task<User?> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);
}
