using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.VodMovieFixer.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.VodMovieFixer.Api;

/// <summary>
/// Endpoint usati dalla pagina di configurazione del plugin per l'assegnazione manuale dei candidati
/// che il rilevamento automatico su TMDb non riesce a confermare come film.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("VodMovieFixer")]
public class VodMovieFixerController : ControllerBase
{
    private readonly VodMovieDetectionService _detectionService;
    private readonly TmdbClient _tmdbClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="VodMovieFixerController"/> class.
    /// </summary>
    public VodMovieFixerController(VodMovieDetectionService detectionService, TmdbClient tmdbClient)
    {
        _detectionService = detectionService;
        _tmdbClient = tmdbClient;
    }

    /// <summary>
    /// Ritorna lo stato corrente della ricerca dei candidati da assegnare manualmente (in corso, ultimo
    /// risultato o errore). Risponde subito: non fa chiamate a TMDb, legge solo l'ultimo risultato tenuto
    /// in memoria dal server. Per avviare (o far ripartire) la ricerca vera e propria usa
    /// <see cref="StartCandidatesScan"/>.
    /// </summary>
    [HttpGet("Candidates")]
    public ActionResult<CandidatesScanStatus> GetCandidates()
    {
        return Ok(_detectionService.GetCandidatesStatus());
    }

    /// <summary>
    /// Avvia in background la ricerca dei candidati da assegnare manualmente. La ricerca interroga TMDb
    /// per ogni "serie" con 1 sola stagione/episodio e può richiedere minuti su librerie grandi: per
    /// questo non blocca la richiesta HTTP (evitando timeout su eventuali reverse proxy) ma gira in
    /// background, e il risultato si legge poi con <see cref="GetCandidates"/>.
    /// </summary>
    [HttpPost("Candidates/Scan")]
    public ActionResult StartCandidatesScan()
    {
        _detectionService.StartCandidatesScan();
        return Accepted();
    }

    /// <summary>
    /// Cerca film su TMDb, per far scegliere all'amministratore quello corretto.
    /// </summary>
    /// <param name="query">Testo da cercare.</param>
    /// <param name="year">Anno, se noto.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    [HttpGet("TmdbSearch")]
    public async Task<ActionResult<IReadOnlyList<TmdbMovieResult>>> SearchTmdb([FromQuery] string query, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(Array.Empty<TmdbMovieResult>());
        }

        return Ok(await _tmdbClient.SearchMoviesAsync(query, year, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Converte manualmente il candidato indicato nel film TMDb scelto dall'amministratore.
    /// </summary>
    /// <param name="request">Id della serie candidata e id TMDb scelto.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    [HttpPost("Assign")]
    public async Task<ActionResult> Assign([FromBody] AssignMovieRequest request, CancellationToken cancellationToken)
    {
        var success = await _detectionService.AssignMovieAsync(request.SeriesId, request.TmdbId, cancellationToken).ConfigureAwait(false);
        return success ? Ok() : NotFound();
    }
}

/// <summary>
/// Corpo della richiesta di assegnazione manuale.
/// </summary>
public class AssignMovieRequest
{
    /// <summary>
    /// Gets or sets l'id della serie candidata.
    /// </summary>
    public Guid SeriesId { get; set; }

    /// <summary>
    /// Gets or sets l'id TMDb del film scelto manualmente.
    /// </summary>
    public int TmdbId { get; set; }
}
