using System.IO;
using System.Windows;
using ArtifactView.Application.Jobs;
using ArtifactView.Application.Plugins;
using ArtifactView.Application.Settings;
using ArtifactView.Application.Workflows;
using ArtifactView.App.ViewModels;
using ArtifactView.Infrastructure.Cache;
using ArtifactView.Infrastructure.Metadata;
using ArtifactView.Infrastructure.Plugins;
using ArtifactView.Infrastructure.Sources;
using Microsoft.Extensions.Logging;

namespace ArtifactView.App;

public partial class App : System.Windows.Application
{
    private JobScheduler? _jobScheduler;
    private LocalCacheDb? _cache;
    private BlobStore? _blobStore;
    private ILoggerFactory? _loggerFactory;
    private PluginRegistry? _pluginRegistry;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddDebug().SetMinimumLevel(LogLevel.Debug));
        _loggerFactory = loggerFactory;

        var settingsStore = new AppSettingsStore();
        var settings = settingsStore.Load();

        Directory.CreateDirectory(settings.CacheDirectory);
        Directory.CreateDirectory(settings.PluginsDirectory);

        _cache = new LocalCacheDb(Path.Combine(settings.CacheDirectory, "local.db"));
        _blobStore = new BlobStore(Path.Combine(settings.CacheDirectory, "blobs"));

        var jobQueue = new JobQueue();
        var jobScheduler = new JobScheduler(jobQueue, loggerFactory.CreateLogger<JobScheduler>());
        _jobScheduler = jobScheduler;

        _pluginRegistry = new PluginRegistry(new PluginLoader());
        _pluginRegistry.Load(settings.PluginsDirectory, settings.PluginPolicy);
        loggerFactory.CreateLogger<App>().LogInformation(
            "Plugin registry loaded: {Count} permitted plugin(s) under policy {Policy}",
            _pluginRegistry.Permitted.Count, settings.PluginPolicy);

        var sourceProvider = new FileSystemSourceProvider();
        var workflow = new FolderOpenWorkflow(
            sourceProvider,
            _cache,
            loggerFactory.CreateLogger<FolderOpenWorkflow>());

        var backupWorkflow = new IPhoneBackupOpenWorkflow(
            loggerFactory.CreateLogger<IPhoneBackupOpenWorkflow>());

        var diskImageWorkflow = new DiskImageOpenWorkflow(
            loggerFactory.CreateLogger<DiskImageOpenWorkflow>());

        var viewModel = new ShellViewModel(workflow, backupWorkflow, diskImageWorkflow, jobScheduler, settingsStore,
            new ImageMetadataExtractor(), _blobStore, loggerFactory, _pluginRegistry);

        // Restore last session or fast-open from command-line argument.
        if (e.Args.Length > 0)
        {
            var arg    = e.Args[0];
            var isFile = File.Exists(arg);
            var folder = Directory.Exists(arg) ? arg : Path.GetDirectoryName(arg);
            if (folder is not null)
            {
                // Load the folder, then — if the argument was a specific file — auto-select
                // it so the viewer opens immediately without further user interaction.
                var loadTask = viewModel.LoadFolderAsync(folder);
                if (isFile)
                {
                    var targetPath = arg;
                    loadTask.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            _loggerFactory?.CreateLogger<App>()
                                .LogError(t.Exception, "Startup folder-load failed for {Folder}", folder);
                            return;
                        }
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            var match = viewModel.Items.FirstOrDefault(
                                r => string.Equals(r.LogicalPath, targetPath, StringComparison.OrdinalIgnoreCase));
                            if (match is not null)
                                viewModel.SelectedItem = match;
                        });
                    }, TaskContinuationOptions.None);
                }
            }
        }
        else if (settings.LastFolderPath is not null && Directory.Exists(settings.LastFolderPath))
        {
            _ = viewModel.LoadFolderAsync(settings.LastFolderPath);
        }

        new MainWindow(viewModel, settingsStore).Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_jobScheduler is not null)
        {
            // Allow up to 3 seconds for in-progress jobs to finish before forcing shutdown.
            try
            {
                await _jobScheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3.0));
            }
            catch (Exception ex)
            {
                if (ex is not TimeoutException)
                    System.Diagnostics.Debug.WriteLine($"[App] Shutdown error: {ex.GetType().Name}: {ex.Message}");
            }
        }
        _cache?.Dispose();
        _loggerFactory?.Dispose();
        base.OnExit(e);
    }
}

