using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Content.Photos;

namespace Portfolio.Web.Services;

/// <summary>Watches the external photo manifest and retains the last valid immutable snapshot.</summary>
public sealed class ReloadingPhotoCatalog : BackgroundService, IPhotoCatalog
{
    private readonly Channel<RefreshReason> _refreshRequests = Channel.CreateBounded<RefreshReason>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly string _manifestPath;
    private readonly string _webRootPath;
    private readonly PhotoCatalogOptions _options;
    private readonly ILogger<ReloadingPhotoCatalog> _logger;
    private JsonPhotoCatalog? _current;
    private FileSystemWatcher? _watcher;
    private CatalogFileStamp? _loadedStamp;

    /// <summary>Creates a catalog whose manifest path is independent of the application image.</summary>
    public ReloadingPhotoCatalog(
        IOptions<PhotoCatalogOptions> options,
        IWebHostEnvironment environment,
        ILogger<ReloadingPhotoCatalog> logger)
    {
        _options = options.Value;
        _manifestPath = Path.GetFullPath(
            Path.IsPathRooted(_options.ManifestPath)
                ? _options.ManifestPath
                : Path.Combine(environment.ContentRootPath, _options.ManifestPath));
        _webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        _logger = logger;
    }

    /// <summary>Gets whether this process has loaded at least one valid manifest.</summary>
    public bool HasValidCatalog => Volatile.Read(ref _current) is not null;

    /// <inheritdoc />
    public string ContentRevision => Volatile.Read(ref _current)?.ContentRevision ?? string.Empty;

    /// <inheritdoc />
    public DateTimeOffset PublishedAt => Volatile.Read(ref _current)?.PublishedAt ?? default;

    /// <inheritdoc />
    public IReadOnlyList<PhotoRecord> Photos => Volatile.Read(ref _current)?.Photos ?? [];

    /// <summary>Loads the initial snapshot before the host begins accepting requests.</summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        TryReload(force: true);
        EnsureWatcher();
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Reacts to filesystem notifications with periodic reconciliation.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reconciliationTask = QueueReconciliationAsync(stoppingToken);

        try
        {
            await foreach (var reason in _refreshRequests.Reader.ReadAllAsync(stoppingToken))
            {
                if (reason is RefreshReason.FileSystem)
                {
                    await Task.Delay(_options.ChangeDebounce, stoppingToken);
                }

                var force = reason is RefreshReason.FileSystem;
                while (_refreshRequests.Reader.TryRead(out var queuedReason))
                {
                    force |= queuedReason is RefreshReason.FileSystem;
                }

                EnsureWatcher();
                TryReload(force);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
        finally
        {
            _watcher?.Dispose();
            await reconciliationTask;
        }
    }

    /// <summary>Queues a cheap filesystem reconciliation without parsing on every request.</summary>
    private async Task QueueReconciliationAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ReconciliationInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                _refreshRequests.Writer.TryWrite(RefreshReason.Reconciliation);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal hosted-service shutdown.
        }
    }

    /// <summary>Creates a directory watcher so atomic manifest renames trigger immediate reloads.</summary>
    private void EnsureWatcher()
    {
        if (_watcher is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_manifestPath);
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        var watcher = new FileSystemWatcher(directory, Path.GetFileName(_manifestPath))
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
        };
        watcher.Changed += OnManifestChanged;
        watcher.Created += OnManifestChanged;
        watcher.Deleted += OnManifestChanged;
        watcher.Renamed += OnManifestChanged;
        watcher.Error += OnWatcherError;
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;

        _logger.LogInformation("Watching photo manifest {ManifestPath}", _manifestPath);
    }

    /// <summary>Queues a debounced reload for a matching manifest event.</summary>
    private void OnManifestChanged(object sender, FileSystemEventArgs args) =>
        _refreshRequests.Writer.TryWrite(RefreshReason.FileSystem);

    /// <summary>Discards a failed watcher so reconciliation can recreate it.</summary>
    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        _logger.LogWarning(args.GetException(), "Photo manifest watcher failed; reconciliation will recreate it");
        _watcher?.Dispose();
        _watcher = null;
        _refreshRequests.Writer.TryWrite(RefreshReason.Reconciliation);
    }

    /// <summary>Loads a changed manifest and preserves the prior snapshot after any failure.</summary>
    private void TryReload(bool force)
    {
        var stamp = CatalogFileStamp.Read(_manifestPath);
        if (!force && HasValidCatalog && stamp == _loadedStamp)
        {
            return;
        }

        try
        {
            var candidate = JsonPhotoCatalog.Load(_manifestPath, _webRootPath);
            var previousRevision = Volatile.Read(ref _current)?.ContentRevision;

            Volatile.Write(ref _current, candidate);
            _loadedStamp = stamp;

            if (!string.Equals(previousRevision, candidate.ContentRevision, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Activated photo revision {PhotoRevision} with {PhotoCount} photos",
                    candidate.ContentRevision,
                    candidate.Photos.Count);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException
            or Portfolio.Web.Content.CatalogValidationException)
        {
            _logger.LogError(
                exception,
                "Rejected photo manifest {ManifestPath}; retaining revision {PhotoRevision}",
                _manifestPath,
                ContentRevision);
        }
    }

    private enum RefreshReason
    {
        FileSystem,
        Reconciliation
    }

    private readonly record struct CatalogFileStamp(long Length, DateTime LastWriteUtc)
    {
        /// <summary>Reads only bounded file metadata used by the reconciliation fallback.</summary>
        public static CatalogFileStamp? Read(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? new CatalogFileStamp(file.Length, file.LastWriteTimeUtc) : null;
        }
    }
}
