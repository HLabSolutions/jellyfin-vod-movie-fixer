using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.VodMovieFixer.Services;

/// <summary>
/// Interroga l'API pubblica di TMDb per capire se un titolo classificato come "serie" (dal provider)
/// corrisponde in realtà a un film. Usa una semplice euristica basata su popolarità/similarità del titolo:
/// TMDb non ha un endpoint dedicato a "questo titolo è più probabile che sia un film o una serie", quindi
/// interroghiamo entrambe le ricerche (film e serie TV) e confrontiamo i risultati migliori.
/// </summary>
public class TmdbClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3/";

    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="httpClient">Client HTTP iniettato dal container DI.</param>
    /// <param name="logger">Logger.</param>
    public TmdbClient(HttpClient httpClient, ILogger<TmdbClient> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(BaseUrl);
        _logger = logger;
    }

    /// <summary>
    /// Determina se il titolo indicato corrisponde su TMDb più a un film che a una serie TV.
    /// </summary>
    /// <param name="title">Titolo da cercare (di norma il nome della "serie" candidata).</param>
    /// <param name="year">Anno di produzione, se noto.</param>
    /// <param name="cancellationToken">Token di cancellazione.</param>
    /// <returns>Esito della verifica.</returns>
    public async Task<TmdbMovieCheckResult> IsLikelyMovieAsync(string title, int? year, CancellationToken cancellationToken)
    {
        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Nessuna API key TMDb configurata: impossibile confermare '{Title}', verrà saltata.", title);
            return TmdbMovieCheckResult.Unknown;
        }

        var movieResult = await SearchBestAsync(apiKey, "search/movie", "year", title, year, cancellationToken).ConfigureAwait(false);
        var tvResult = await SearchBestAsync(apiKey, "search/tv", "first_air_date_year", title, year, cancellationToken).ConfigureAwait(false);

        if (movieResult is null && tvResult is null)
        {
            _logger.LogInformation("Nessun risultato TMDb (né film né serie) per '{Title}'.", title);
            return TmdbMovieCheckResult.Unknown;
        }

        var normalizedQuery = Normalize(title);
        var movieIsExactTitleMatch = movieResult is not null && Normalize(movieResult.DisplayTitle) == normalizedQuery;
        var tvIsExactTitleMatch = tvResult is not null && Normalize(tvResult.DisplayTitle) == normalizedQuery;

        // Preferiamo una corrispondenza esatta del titolo; a parità, vince chi ha più popolarità.
        bool isMovie;
        if (movieIsExactTitleMatch && !tvIsExactTitleMatch)
        {
            isMovie = true;
        }
        else if (tvIsExactTitleMatch && !movieIsExactTitleMatch)
        {
            isMovie = false;
        }
        else
        {
            var moviePopularity = movieResult?.Popularity ?? -1;
            var tvPopularity = tvResult?.Popularity ?? -1;
            isMovie = moviePopularity >= tvPopularity && movieResult is not null;
        }

        _logger.LogDebug(
            "TMDb per '{Title}': film='{Movie}' (pop={MoviePop}), serie='{Tv}' (pop={TvPop}) => {Verdict}",
            title,
            movieResult?.DisplayTitle,
            movieResult?.Popularity,
            tvResult?.DisplayTitle,
            tvResult?.Popularity,
            isMovie ? "FILM" : "SERIE");

        return isMovie ? TmdbMovieCheckResult.Movie : TmdbMovieCheckResult.Series;
    }

    private async Task<TmdbSearchItem?> SearchBestAsync(string apiKey, string endpoint, string yearParamName, string title, int? year, CancellationToken cancellationToken)
    {
        var best = await SearchAsync(apiKey, endpoint, yearParamName, title, year, cancellationToken).ConfigureAwait(false);
        if (best is not null)
        {
            return best;
        }

        // TMDb a volte non trova nulla se l'anno non combacia esattamente: ritentiamo senza filtro anno.
        if (year.HasValue)
        {
            return await SearchAsync(apiKey, endpoint, yearParamName, title, null, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<TmdbSearchItem?> SearchAsync(string apiKey, string endpoint, string yearParamName, string title, int? year, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(title);
        var url = $"{endpoint}?api_key={Uri.EscapeDataString(apiKey)}&query={query}&include_adult=false";
        if (year.HasValue)
        {
            url += $"&{yearParamName}={year.Value}";
        }

        try
        {
            var response = await _httpClient.GetFromJsonAsync<TmdbSearchResponse>(url, cancellationToken).ConfigureAwait(false);
            return response?.Results?.OrderByDescending(r => r.Popularity).FirstOrDefault();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Chiamata TMDb a {Endpoint} fallita per '{Title}'.", endpoint, title);
            return null;
        }
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var formD = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in formD)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class TmdbSearchResponse
    {
        [JsonPropertyName("results")]
        public TmdbSearchItem[]? Results { get; set; }
    }

    private sealed class TmdbSearchItem
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }

        public string DisplayTitle => Title ?? Name ?? string.Empty;
    }
}

/// <summary>
/// Esito della verifica euristica su TMDb.
/// </summary>
public enum TmdbMovieCheckResult
{
    /// <summary>
    /// Non è stato possibile determinare nulla (nessuna API key, nessun risultato, errore di rete).
    /// </summary>
    Unknown,

    /// <summary>
    /// Il titolo corrisponde più a un film.
    /// </summary>
    Movie,

    /// <summary>
    /// Il titolo corrisponde più a una serie TV (probabilmente è una serie vera, non va toccata).
    /// </summary>
    Series
}
