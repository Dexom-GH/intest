using System.Diagnostics;

namespace InTest.Runtime;

/// <summary>
/// Readiness gating. Framework-neutral.
/// Post-deploy cold start is the largest single source of flaky gates; failing here once
/// with a clear message beats N confusing test failures.
/// </summary>
public static class Readiness
{
    public static async Task WaitAsync(HttpClient client, ReadinessOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled) return;

        var deadline = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds);
        var consecutive = 0;
        var lastOutcome = "no response";

        while (deadline.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(options.Path, cancellationToken).ConfigureAwait(false);
                lastOutcome = ((int)response.StatusCode).ToString();

                if ((int)response.StatusCode == options.ExpectStatus)
                {
                    if (++consecutive >= options.ConsecutiveSuccesses) return;
                }
                else consecutive = 0;
            }
            catch (HttpRequestException ex)
            {
                lastOutcome = ex.GetType().Name;
                consecutive = 0;
            }

            if (interval > TimeSpan.Zero)
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }

        throw new ReadinessTimeoutException(
            $"Service did not become ready within {options.TimeoutSeconds}s (last response: {lastOutcome}). " +
            $"Probed '{options.Path}' expecting {options.ExpectStatus}, requiring {options.ConsecutiveSuccesses} consecutive successes.");
    }
}
