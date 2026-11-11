namespace G4.Application.Integrations;

/// <summary>One autocomplete row: an opaque provider reference and the text shown to the buyer.</summary>
public sealed record AddressSuggestion(string Ref, string Display);

/// <summary>Address picked from a suggestion, already split into the form fields.</summary>
public sealed record AddressSuggestionDetail(
    string Street,
    string City,
    string State,
    string Country,
    string? PostalCode
);

/// <summary>House-number address suggestions (Vietnam only, VietMap).</summary>
public interface IAddressLookupGateway
{
    /// <summary>False when no API key is configured; callers hide the search box.</summary>
    bool Enabled { get; }

    Task<IReadOnlyList<AddressSuggestion>> SuggestAsync(string text, CancellationToken ct);

    Task<AddressSuggestionDetail?> DetailAsync(string reference, CancellationToken ct);
}

/// <summary>The provider is unreachable, out of quota or rejected the key.</summary>
public sealed class AddressLookupUnavailableException(string message) : Exception(message);
