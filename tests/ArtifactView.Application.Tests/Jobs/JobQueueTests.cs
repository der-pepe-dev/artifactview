using ArtifactView.Application.Jobs;
using System.Threading.Tasks;

namespace ArtifactView.Application.Tests.Jobs;

public sealed class JobQueueTests
{
    [Test]
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

        await Assert.That(first.Priority).IsEqualTo(JobPriority.SelectedItemRender);
        await Assert.That(second.Priority).IsEqualTo(JobPriority.VisibleGridRows);
        await Assert.That(third.Priority).IsEqualTo(JobPriority.MaintenanceAndGlobalIndexing);
    }

    [Test]
    public async Task Count_reflects_enqueued_jobs()
    {
        var queue = new JobQueue();

        await Assert.That(queue.Count).IsEqualTo(0);

        queue.Enqueue(new BackgroundJob(JobPriority.VisibleGridRows, "a", _ => Task.CompletedTask));
        queue.Enqueue(new BackgroundJob(JobPriority.NearbyFilmstrip,  "b", _ => Task.CompletedTask));

        await Assert.That(queue.Count).IsEqualTo(2);
    }
}