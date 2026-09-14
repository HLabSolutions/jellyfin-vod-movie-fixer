using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.VodMovieFixer.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VodMovieFixer.Services;

/// <summary>
/// Logica principale: individua le "serie" con una sola stagione e un solo episodio (il pattern con cui
/// alcuni provider IPTV/VOD espongono i film), le conferma su TMDb e, se confermate, sostituisce la voce
/// Series/Season/Episode nella libreria di Jellyfin con una voce Movie che punta allo stesso file .strm
/// (il file fisico non viene mai spostato né toccato).
/// </summary>
public class VodMovieDetectionService
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IDirectoryService _directoryService;
    private readonly TmdbClient _tmdbClient;
    private readonly ILogger<VodMovieDetectionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VodMovieDetectionService"/> class.
    /// </summary>
    public VodMovieDetectionService(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IDirectoryService directoryService,
        TmdbClient tmdbClient,
        ILogger<VodMovieDetectionService> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _directoryService = directoryService;
        _tmdbClient = tmdbClient;
        _logger = logger;
    }

    /// <summary>
    /// Esegue una scansione delle librerie configurate e converte (o simula la conversione di) i film
    /// mascherati da serie che trova.
    /// </summary>
    /// <param name="progress">Avanzamento (0-100).</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <returns>Numero di elementi convertiti (o rilevati, in modalità simulazione).</returns>
    public async Task<int> RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var folderIds = GetTargetFolderIds(config);
        if (folderIds.Count == 0)
        {
            _logger.LogWarning(
                "Nessuna libreria configurata (o nessun nome corrispondente a una libreria esistente): nessuna azione eseguita. " +
                "Configura i nomi delle librerie in Dashboard > Plugin > VOD Movie Fixer.");
            progress.Report(100);
            return 0;
        }

        var seriesList = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Series },
            Recursive = true,
            AncestorIds = folderIds.ToArray()
        }).OfType<Series>().ToList();

        _logger.LogInformation("Analisi di {Count} serie nelle librerie configurate...", seriesList.Count);

        var convertedCount = 0;
        for (var i = 0; i < seriesList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var series = seriesList[i];
            progress.Report(100.0 * i / Math.Max(seriesList.Count, 1));

            var episode = GetSingleCandidateEpisodeOrNull(series);
            if (episode is null)
            {
                continue;
            }

            TmdbMovieCheckResult verdict;
            try
            {
                verdict = await _tmdbClient.IsLikelyMovieAsync(series.Name, series.ProductionYear, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Verifica TMDb fallita per '{Name}', elemento saltato.", series.Name);
                continue;
            }

            if (verdict != TmdbMovieCheckResult.Movie)
            {
                _logger.LogDebug("'{Name}' ha un solo episodio ma non è stato confermato come film su TMDb ({Verdict}), saltato.", series.Name, verdict);
                continue;
            }

            _logger.LogInformation(
                "{Prefix}Film mascherato da serie rilevato: '{Name}' ({Path})",
                config.DryRun ? "[SIMULAZIONE] " : string.Empty,
                series.Name,
                episode.Path);

            if (config.DryRun)
            {
                convertedCount++;
                continue;
            }

            try
            {
                await ConvertSeriesToMovieAsync(series, episode, cancellationToken).ConfigureAwait(false);
                convertedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Conversione in film fallita per '{Name}'.", series.Name);
            }
        }

        progress.Report(100);
        _logger.LogInformation("Completato: {Count} elementi convertiti (o rilevati in simulazione).", convertedCount);
        return convertedCount;
    }

    /// <summary>
    /// Ritorna l'unico episodio della serie se questa ha esattamente 1 stagione e 1 episodio con un file
    /// reale associato (il pattern tipico di un film esposto come serie), altrimenti null.
    /// </summary>
    private Episode? GetSingleCandidateEpisodeOrNull(Series series)
    {
        var episodeCount = _libraryManager.GetCount(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            AncestorIds = new[] { series.Id }
        });

        if (episodeCount != 1)
        {
            return null;
        }

        var episode = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Episode },
            Recursive = true,
            AncestorIds = new[] { series.Id }
        }).OfType<Episode>().FirstOrDefault();

        if (episode is null || string.IsNullOrEmpty(episode.Path) || episode.IsVirtualItem)
        {
            return null;
        }

        return episode;
    }

    private async Task ConvertSeriesToMovieAsync(Series series, Episode episode, CancellationToken cancellationToken)
    {
        var parent = _libraryManager.GetItemById(series.ParentId);
        if (parent is null)
        {
            _logger.LogWarning("Impossibile trovare il genitore di '{Name}' nella libreria, conversione annullata.", series.Name);
            return;
        }

        var movie = new Movie
        {
            Id = _libraryManager.GetNewItemId(episode.Path, typeof(Movie)),
            Name = series.Name,
            OriginalTitle = series.OriginalTitle,
            ProductionYear = series.ProductionYear,
            PremiereDate = series.PremiereDate,
            Path = episode.Path,
            ParentId = series.ParentId,
            DateCreated = series.DateCreated
        };

        var seasons = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Season },
            Recursive = true,
            AncestorIds = new[] { series.Id }
        }).OfType<Season>().ToList();

        // DeleteFileLocation = false: rimuoviamo solo le voci dal database di Jellyfin, il file .strm fisico resta intatto.
        var deleteOptions = new DeleteOptions { DeleteFileLocation = false };

        _libraryManager.DeleteItem(episode, deleteOptions, series, false);
        foreach (var season in seasons)
        {
            _libraryManager.DeleteItem(season, deleteOptions, series, false);
        }

        _libraryManager.DeleteItem(series, deleteOptions, parent, true);

        _libraryManager.CreateItem(movie, parent);

        await _providerManager.RefreshFullItem(
            movie,
            new MetadataRefreshOptions(_directoryService)
            {
                MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
                ImageRefreshMode = MetadataRefreshMode.FullRefresh,
                ReplaceAllMetadata = true,
                ForceSave = true
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Convertito in film: '{Name}' -> {Path}", movie.Name, movie.Path);
    }

    private List<Guid> GetTargetFolderIds(PluginConfiguration config)
    {
        var configuredNames = config.LibraryNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (configuredNames.Count == 0)
        {
            return new List<Guid>();
        }

        var ids = new List<Guid>();
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (configuredNames.Contains(folder.Name) && Guid.TryParse(folder.ItemId, out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
