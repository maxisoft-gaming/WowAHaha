using Microsoft.Extensions.Logging;

namespace WowAHaha;

public class ProgramWorkerSemaphore(ProgramConfigurations configurations, ILogger<ProgramWorkerSemaphore> logger)
{
    private readonly Lazy<SemaphoreSlim> _semaphore = new(() =>
    {
        var maxWorkers = configurations.MaxWorkers;
        if (maxWorkers <= 0)
        {
            logger.LogError("Invalid max workers: {MaxWorkers}", maxWorkers);
            maxWorkers = 1;
        }

        return new SemaphoreSlim(maxWorkers, maxWorkers);
    });

    public int MaxWorkers => Math.Max(configurations.MaxWorkers, 1);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _semaphore.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Release()
    {
        _semaphore.Value.Release();
    }
}