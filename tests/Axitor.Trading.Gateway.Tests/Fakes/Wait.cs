using System.Diagnostics;

namespace Axitor.Trading.Gateway.Tests.Fakes;

/// <summary>Polls a condition for tests that wait on background work.</summary>
internal static class Wait
{
    private const int StepMillis = 5;
    private const string TimeoutMessage = "Condition was not met within 10 s.";

    /// <summary>Longest a test waits for anything.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    /// <summary>Polls every 5 ms until the condition holds. Fails the test after the limit.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (!condition())
        {
            if (Limit <= elapsed.Elapsed)
            {
                throw new TimeoutException(TimeoutMessage);
            }

            await Task.Delay(StepMillis, TestContext.Current.CancellationToken);
        }
    }
}
