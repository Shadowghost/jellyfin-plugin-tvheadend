using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace TVHeadEnd;

/// <summary>
/// Registers the plugin's services.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<HTSConnectionHandler>();
        serviceCollection.AddSingleton<ConnectionTester>();
        serviceCollection.AddSingleton<ImageCache>();
        serviceCollection.AddSingleton<ILiveTvService, LiveTvService>();
        serviceCollection.AddSingleton<IChannel, RecordingsChannel>();
    }
}
