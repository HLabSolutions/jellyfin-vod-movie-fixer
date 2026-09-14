using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.VodMovieFixer.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.VodMovieFixer.ScheduledTasks;

/// <summary>
/// Task pianificabile da Dashboard &gt; Programmazione attività per correggere manualmente (o on-demand)
/// i film mascherati da serie. Non ha trigger di default: viene eseguito a mano dall'amministratore,
/// oppure automaticamente al termine di ogni scansione libreria se l'opzione è abilitata nella
/// configurazione del plugin (vedi <see cref="HostedServices.LibraryScanHookService"/>).
/// </summary>
public class FixVodMoviesTask : IScheduledTask
{
    private readonly VodMovieDetectionService _detectionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="FixVodMoviesTask"/> class.
    /// </summary>
    /// <param name="detectionService">Servizio che esegue la logica di rilevamento/conversione.</param>
    public FixVodMoviesTask(VodMovieDetectionService detectionService)
    {
        _detectionService = detectionService;
    }

    /// <inheritdoc />
    public string Name => "Correggi film VOD classificati come serie";

    /// <inheritdoc />
    public string Key => "FixVodSeriesAsMovies";

    /// <inheritdoc />
    public string Description =>
        "Cerca, nelle librerie configurate, le 'serie' con una sola stagione e un solo episodio che " +
        "corrispondono a un film su TMDb, e le converte in film senza spostare i file .strm.";

    /// <inheritdoc />
    public string Category => "Libreria";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _detectionService.RunAsync(progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();
}
