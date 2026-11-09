using G4.Contracts.Checkout;
using G4.Domain.Entities;

namespace G4.Application.Services;

public interface IBuyerRegistrationService
{
    Task<User> RegisterAsync(BuyerRegistrationInput input, CancellationToken ct = default);
}
