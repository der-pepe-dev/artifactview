namespace ArtifactView.Application.Jobs;

// Thread-safe priority queue for background jobs.
// Lower JobPriority value = higher urgency (processed first).
public sealed class JobQueue
{
    private readonly PriorityQueue<BackgroundJob, int> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _lock = new();

    public void Enqueue(BackgroundJob job)
    {
        lock (_lock)
            _queue.Enqueue(job, (int)job.Priority);
        _signal.Release();
    }

    public async ValueTask<BackgroundJob> DequeueAsync(CancellationToken cancellationToken)
    {
        await _signal.WaitAsync(cancellationToken);
        lock (_lock)
        {
            if (_queue.TryDequeue(out var job, out _))
                return job;
        }
        throw new InvalidOperationException("Queue signal released but queue was empty.");
    }

    public int Count
    {
        get { lock (_lock) return _queue.Count; }
    }
}
