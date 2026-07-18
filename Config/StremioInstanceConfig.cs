using System.Xml.Serialization;
using MediaBrowser.Controller.Entities;

namespace Gelato.Config;

/// <summary>
/// One AIOStreams manifest with its own library roots.
/// Serialized into the plugin XML config by Jellyfin's IXmlSerializer.
/// </summary>
public class StremioInstanceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name, e.g. "Main", "Kids", "4K".</summary>
    public string Name { get; set; } = "Default";

    /// <summary>AIOStreams manifest or base URL.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Filesystem path for movie items of this instance.</summary>
    public string MoviePath { get; set; } = string.Empty;

    /// <summary>Filesystem path for series items of this instance.</summary>
    public string SeriesPath { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [XmlIgnore]
    public bool IsUsable =>
        Enabled
        && !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(MoviePath)
        && !string.IsNullOrWhiteSpace(SeriesPath);
}

/// <summary>
/// Extensions for PluginConfiguration.Instances.
/// </summary>
public static class StremioInstanceMigration
{
    /// <summary>
    /// Wraps the legacy single Url/MoviePath/SeriesPath into instance #1
    /// so existing Gelato installs keep working untouched.
    /// Returns true if a migration happened (caller should SaveConfiguration).
    /// </summary>
    public static bool MigrateLegacyInstance(this PluginConfiguration cfg)
    {
        if (cfg.Instances.Count > 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(cfg.Url))
        {
            return false;
        }

        cfg.Instances.Add(
            new StremioInstanceConfig
            {
                Name = "Default",
                Url = cfg.Url,
                MoviePath = cfg.MoviePath,
                SeriesPath = cfg.SeriesPath,
                Enabled = true,
            }
        );
        return true;
    }

    public static StremioInstanceConfig? DefaultInstance(this PluginConfiguration cfg) =>
        cfg.Instances.FirstOrDefault(i => i.IsUsable);

    public static IReadOnlyList<StremioInstanceConfig> UsableInstances(
        this PluginConfiguration cfg
    ) => cfg.Instances.Where(i => i.IsUsable).ToList();
}

/// <summary>
/// Resolves which instance owns a library item.
/// Priority: explicit GelatoData stamp -> longest path-prefix match -> default.
/// </summary>
public static class StremioInstanceResolver
{
    public const string GelatoDataKey = "instanceId";

    public static StremioInstanceConfig? ResolveForItem(
        PluginConfiguration root,
        BaseItem? item
    )
    {
        if (item is null)
        {
            return root.DefaultInstance();
        }

        // 1) explicit stamp written at insert time
        var stamped = item.GelatoData<Guid?>(GelatoDataKey);
        if (stamped is { } id && id != Guid.Empty)
        {
            var byId = root.Instances.FirstOrDefault(i => i.Id == id && i.IsUsable);
            if (byId is not null)
            {
                return byId;
            }
        }

        // 2) longest path-prefix match against instance roots
        var path = item.Path;
        if (!string.IsNullOrWhiteSpace(path))
        {
            StremioInstanceConfig? best = null;
            var bestLen = -1;
            foreach (var inst in root.Instances.Where(i => i.IsUsable))
            {
                foreach (var rootPath in new[] { inst.MoviePath, inst.SeriesPath })
                {
                    if (string.IsNullOrWhiteSpace(rootPath))
                    {
                        continue;
                    }

                    var normalized = rootPath.TrimEnd('/', '\\');
                    if (
                        path.StartsWith(normalized + '/', StringComparison.Ordinal)
                        || path.StartsWith(normalized + '\\', StringComparison.Ordinal)
                        || string.Equals(path, normalized, StringComparison.Ordinal)
                        // season paths look like "{seriesPath}/{name}:{seasonIdx}"
                        || path.StartsWith(normalized + ':', StringComparison.Ordinal)
                    )
                    {
                        if (normalized.Length > bestLen)
                        {
                            best = inst;
                            bestLen = normalized.Length;
                        }
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        // 3) walk up: episodes/seasons/stream rows may carry synthetic paths;
        //    their parent chain ends under an instance root
        var parent = item.GetParent();
        if (parent is not null && parent.Id != item.Id)
        {
            var viaParent = ResolveForItem(root, parent);
            if (viaParent is not null)
            {
                return viaParent;
            }
        }

        return root.DefaultInstance();
    }
}
