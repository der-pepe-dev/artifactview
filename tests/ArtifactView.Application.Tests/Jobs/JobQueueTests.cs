using ArtifactView.Application.Jobs;
using Xunit;

namespace ArtifactView.Application.Tests.Jobs;

public sealed class JobQueueTests
{
    [Fact]
    public async Task Dequeues_jobs_in_priority_order()
    {
        var queue = new JobQueue();

        // Enqueue in reverse priority order to confirm ordering, not insertion order.
        queue.Enqueue(new BackgroundJob(JobPriority.MaintenanceAndGlobalIndexing, "maintenance", _ => Task.CompletedTask));
        queue.Enqueue(new BackgroundJob(JobPriority.VisibleGridRows,              "grid rows",   _ => Task.CompletedTask));
        queue.Enqueue(new BackgroundJob(JobPriority.SelectedItemRender,           "render",      _ => Task.CompletedTask));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first  = await queue.DequeueAsync(cts.Token);
        var second = await queue.DequeueAsync(cts.Token);
        var third  = await queue.DequeueAsync(cts.Token);

        Assert.Equal(JobPriority.SelectedItemRender,           first.Priority);
        Assert.Equal(JobPriority.VisibleGridRows,              second.Priority);
        Assert.Equal(JobPriority.MaintenanceAndGlobalIndexing, third.Priority);
    }

    [Fact]
    public void Count_reflects_enqueued_jobs()
    {
        var queue = new JobQueue();

        Assert.Equal(0, queue.Count);

        queue.Enqueue(new BackgroundJob(JobPriority.VisibleGridRows, "a", _ => Task.CompletedTask));
        queue.Enqueue(new BackgroundJob(JobPriority.NearbyFilmstrip,  "b", _ => Task.CompletedTask));

        Assert.Equal(2, queue.Count);
    }
}
