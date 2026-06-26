using Microsoft.Extensions.Logging;

namespace ArtifactView.Application.Jobs;

// Processes jobs from the queue one at a time in priority order.
// Runs on a dedicated background task; safe to dispose when shutting down.
public sealed class JobScheduler : IAsyncDisposable
{
    private readonly JobQueue _queue;
    private readonly ILogger<JobScheduler> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public JobScheduler(JobQueue queue, ILogger<JobScheduler> logger)
    {
        _queue = queue;
        _logger = logger;
        _loop = Task.Run(ProcessLoopAsync);
    }

    public void Enqueue(BackgroundJob job) => _queue.Enqueue(job);

    private async Task ProcessLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.DequeueAsync(_cts.Token);
                _logger.LogDebug("Starting job '{Description}' (priority {Priority})", job.Description, job.Priority);
                await job.Work(_cts.Token);
            }
            catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background job failed.");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _loop;
        _cts.Dispose();
    }
}
