using System.Collections.Concurrent;

namespace G4.Infrastructure.Integrations.Shipping;

public sealed class CarrierAvailabilityState
{
    private readonly ConcurrentDictionary<string, int> _remainingFailures = new();

    public void FailNext(string key, int count) => _remainingFailures[key] = Math.Clamp(count, 0, 3);

    public bool ShouldFail(string key)
    {
        while (_remainingFailures.TryGetValue(key, out var count) && count > 0)
            if (_remainingFailures.TryUpdate(key, count - 1, count)) return true;
        return false;
    }
}
