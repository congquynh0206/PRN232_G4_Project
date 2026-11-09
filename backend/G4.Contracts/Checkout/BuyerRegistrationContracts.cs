namespace G4.Contracts.Checkout;

// Same fields as an eBay personal account: name, email and password.
public sealed record BuyerRegistrationInput(
    string FirstName,
    string LastName,
    string Email,
    string Password
);
