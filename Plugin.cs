using System.Collections.Concurrent;
using Gelato.Config;
using Gelato.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Gelato;

public class GelatoPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<GelatoPlugin> _log;
    private readonly GelatoManager _manager;

    // MULTIlato: cache is keyed by (userId, instanceId) instead of userId.
    private ConcurrentDictionary<(Guid UserId, Guid InstanceId), PluginConfiguration> UserConfigs
    { get; } = new();

    private readonly GelatoStremioProviderFactory _stremioFactory;
    public PalcoCacheService PalcoCache { get; }

    public GelatoPlugin(
        IApplicationPaths applicationPaths,
        GelatoManager manager,
        IXmlSerializer xmlSerializer,
        ILogger<GelatoPlugin> log,
        GelatoStremioProviderFactory stremioFactory,
        PalcoCacheService palcoCache
    )
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _log = log;
        _manager = manager;
        _stremioFactory = stremioFactory;
        PalcoCache = palcoCache;

        // MULTIlato: wrap a legacy single-manifest config into instance #1.
        if (Configuration.MigrateLegacyInstance())
        {
            SaveConfiguration();
            _log.LogInformation("MULTIlato: migrated legacy single manifest into instance list");
        }
    }

    public static GelatoPlugin? Instance { get; private set; }

    public static new event Action<PluginConfiguration>? ConfigurationChanged;

    public override string Name => "MULTIlato";

    // New GUID so MULTIlato installs as a distinct plugin (do not run next to stock Gelato).
    public override Guid Id => Guid.Parse("7A31C9D4-52E6-4A0B-9B1F-3D8E6F2C41AA");

    public override string Description =>
        "Gelato fork: multiple AIOStreams manifests, each with its own movie/series library.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var prefix = GetType().Namespace;
        yield return new PluginPageInfo
        {
            Name = "config",
            EnableInMainMenu = true,
            EmbeddedResourcePath = prefix + ".Config.config.html",
        };
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var cfg = (PluginConfiguration)configuration;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISABLE_P2P")))
        {
            cfg.P2PEnabled = false;
        }

        // MULTIlato: seed every instance's folders on save so libraries can be added right away.
        foreach (var inst in cfg.UsableInstances())
        {
            _manager.SeedInstanceFolders(inst);
        }

        base.UpdateConfiguration(cfg);
        _manager.ClearCache();
        _stremioFactory.ClearCache();
        UserConfigs.Clear();

        try
        {
            ConfigurationChanged?.Invoke(cfg);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error while invoking ConfigurationChanged event");
        }
    }

    /// <summary>
    /// Back-compat entry point used by existing call sites: resolves the DEFAULT instance.
    /// </summary>
    public PluginConfiguration GetConfig(Guid userId)
    {
        var inst = Configuration.DefaultInstance();
        return GetConfig(userId, inst?.Id ?? Guid.Empty);
    }

    /// <summary>
    /// Effective config for one user on one manifest instance.
    /// </summary>
    public PluginConfiguration GetConfig(Guid userId, Guid instanceId)
    {
        try
        {
            return UserConfigs.GetOrAdd(
                (userId, instanceId),
                key =>
                {
                    var root = Instance?.Configuration ?? new PluginConfiguration();
                    var cfg = root;

                    if (key.UserId != Guid.Empty)
                    {
                        var userConfig = root.UserConfigs.FirstOrDefault(u =>
                            u.UserId == key.UserId
                        );
                        cfg = userConfig?.ApplyOverrides(root) ?? root;
                    }

                    // Overlay the instance's url/paths onto the effective cfg
                    // so all downstream reads of cfg.Url/MoviePath/SeriesPath keep working.
                    var inst =
                        root.Instances.FirstOrDefault(i => i.Id == key.InstanceId && i.IsUsable)
                        ?? root.DefaultInstance();

                    if (inst is not null)
                    {
                        cfg = cfg.ShallowCloneForInstance(inst); // overlay instance url/paths
                    }

                    var stremio = _stremioFactory.Create(cfg);
                    cfg.Stremio = stremio;
                    cfg.MovieFolder = _manager.TryGetMovieFolder(cfg);
                    cfg.SeriesFolder = _manager.TryGetSeriesFolder(cfg);
                    return cfg;
                }
            );
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error getting config");
            return new PluginConfiguration();
        }
    }

    /// <summary>
    /// All enabled instances' effective configs for a user (search fan-out, catalog import).
    /// Order = configured order = priority order for dedupe.
    /// </summary>
    public IReadOnlyList<PluginConfiguration> GetConfigs(Guid userId)
    {
        return Configuration
            .UsableInstances()
            .Select(i => GetConfig(userId, i.Id))
            .ToList();
    }

    /// <summary>
    /// Effective config for the instance that owns a given library item.
    /// </summary>
    public PluginConfiguration GetConfigForItem(Guid userId, BaseItem? item)
    {
        var inst = StremioInstanceResolver.ResolveForItem(Configuration, item);
        return GetConfig(userId, inst?.Id ?? Guid.Empty);
    }
}
