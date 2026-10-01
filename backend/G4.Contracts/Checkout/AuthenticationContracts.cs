namespace G4.Contracts.Checkout;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, DateTime ExpiresAt, int UserId, string Username, string Role);
