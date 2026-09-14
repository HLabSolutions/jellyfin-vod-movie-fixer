using Jellyfin.Plugin.VodMovieFixer.HostedServices;
using Jellyfin.Plugin.VodMovieFixer.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.VodMovieFixer;

/// <summary>
/// Registra i servizi del plugin nel container DI del server.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHttpClient<TmdbClient>();
        serviceCollection.AddSingleton<VodMovieDetectionService>();
        serviceCollection.AddHostedService<LibraryScanHookService>();
    }
}
