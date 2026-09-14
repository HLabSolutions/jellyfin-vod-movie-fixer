using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.VodMovieFixer.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VodMovieFixer.HostedServices;

/// <summary>
/// Si aggancia al completamento del task nativo di scansione libreria ("Scansiona libreria multimediale",
/// chiave interna "RefreshLibrary") e, se abilitato in configurazione, esegue automaticamente la
/// correzione dei film mascherati da serie subito dopo ogni scansione.
/// </summary>
public class LibraryScanHookService : IHostedService
{
    private const string LibraryScanTaskKey = "RefreshLibrary";

    private readonly ITaskManager _taskManager;
    private readonly VodMovieDetectionService _detectionService;
    private readonly ILogger<LibraryScanHookService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScanHookService"/> class.
    /// </summary>
    public LibraryScanHookService(ITaskManager taskManager, VodMovieDetectionService detectionService, ILogger<LibraryScanHookService> logger)
    {
        _taskManager = taskManager;
        _detectionService = detectionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskCompleted += OnTaskCompleted;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskCompleted -= OnTaskCompleted;
        return Task.CompletedTask;
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        if (Plugin.Instance?.Configuration.RunAfterLibraryScan != true)
        {
            return;
        }

        if (!string.Equals(e.Result.Key, LibraryScanTaskKey, StringComparison.Ordinal))
        {
            return;
        }

        if (e.Result.Status != TaskCompletionStatus.Completed)
        {
            return;
        }

        // Non blocchiamo l'evento del TaskManager: la conversione può richiedere chiamate di rete a TMDb.
        _ = RunSafelyAsync();
    }

    private async Task RunSafelyAsync()
    {
        try
        {
            _logger.LogInformation("Scansione libreria completata: avvio la correzione automatica dei film mascherati da serie.");
            await _detectionService.RunAsync(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correzione automatica post-scansione fallita.");
        }
    }
}
