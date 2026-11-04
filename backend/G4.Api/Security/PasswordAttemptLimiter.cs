using System.Threading.RateLimiting;

namespace G4.Api.Security;

// Shared across all protected actions for one user; independent of the normal API quotas.
public sealed class PasswordAttemptLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<int> limiter;

    public PasswordAttemptLimiter(IConfiguration configuration)
    {
        var permits = configuration.GetValue("PasswordConfirmation:PermitLimit", 5);
        var seconds = configuration.GetValue("PasswordConfirmation:WindowSeconds", 60);
        if (permits < 1 || seconds < 1)
            throw new InvalidOperationException("Invalid password confirmation limit.");
        limiter = PartitionedRateLimiter.Create<int, int>(userId =>
            RateLimitPartition.GetSlidingWindowLimiter(
                userId,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromSeconds(seconds),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            )
        );
    }

    public RateLimitLease Acquire(int userId) => limiter.AttemptAcquire(userId);

    public void Dispose() => limiter.Dispose();
}
