namespace G4.Domain.Rules;

public static class EmailRetryRules
{
    public static DateTime? NextAttemptAt(int attemptsInCycle, DateTime now) => attemptsInCycle switch
    {
        1 => now.AddSeconds(30),
        2 => now.AddMinutes(2),
        3 => null,
        _ => throw new ArgumentOutOfRangeException(nameof(attemptsInCycle))
    };
}
