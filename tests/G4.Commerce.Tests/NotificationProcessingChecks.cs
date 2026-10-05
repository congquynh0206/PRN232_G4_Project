using G4.Domain.Rules;

public static class NotificationProcessingChecks
{
    public static void CheckRetryRules()
    {
        var now = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
        if (EmailRetryRules.NextAttemptAt(1, now) != now.AddSeconds(30) ||
            EmailRetryRules.NextAttemptAt(2, now) != now.AddMinutes(2) ||
            EmailRetryRules.NextAttemptAt(3, now) != null)
            throw new Exception("Email retry must stop after three attempts, with 30s and 120s waits");
        foreach (var invalid in new[] { 0, 4 })
        {
            try { EmailRetryRules.NextAttemptAt(invalid, now); throw new Exception("Invalid retry count accepted"); }
            catch (ArgumentOutOfRangeException) { }
        }
        Console.WriteLine("Notification retry policy checks passed");
    }
}
