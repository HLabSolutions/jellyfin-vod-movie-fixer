using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.VodMovieFixer.Configuration;

/// <summary>
/// Configurazione del plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the TMDb API key (v3 auth) usata per confermare se una "serie" candidata è in realtà un film.
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets i nomi (esatti, case-insensitive) delle librerie Jellyfin da analizzare, separati da virgola.
    /// Se vuoto, non viene analizzata nessuna libreria (comportamento sicuro di default).
    /// </summary>
    public string LibraryNames { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether, in modalità simulazione, il plugin si limita a scrivere nei log
    /// quali serie convertirebbe, senza modificare realmente la libreria.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether eseguire automaticamente la correzione al termine di ogni
    /// scansione della libreria (task "Scansiona libreria multimediale").
    /// </summary>
    public bool RunAfterLibraryScan { get; set; }
}
