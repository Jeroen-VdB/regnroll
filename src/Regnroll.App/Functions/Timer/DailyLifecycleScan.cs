using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Timer;

/// <summary>The daily driver of rotate-before / warn-before / expired flows (schedule: REGNROLL_TIMER_SCHEDULE).</summary>
public class DailyLifecycleScan(ILifecycleService lifecycle, ILogger<DailyLifecycleScan> logger)
{
    [Function(nameof(DailyLifecycleScan))]
    public async Task Run([TimerTrigger("%REGNROLL_TIMER_SCHEDULE%")] TimerInfo timer, CancellationToken ct)
    {
        logger.LogInformation("Daily lifecycle scan starting (past due: {PastDue}).", timer.IsPastDue);
        await lifecycle.RunAsync(ct);
    }
}
