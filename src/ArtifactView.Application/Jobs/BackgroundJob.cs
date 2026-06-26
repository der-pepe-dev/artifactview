namespace ArtifactView.Application.Jobs;

public sealed record BackgroundJob(
    JobPriority Priority,
    string Description,
    Func<CancellationToken, Task> Work);
