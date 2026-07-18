using Gelato.Config;

namespace Gelato.Services;

public class CatalogService(GelatoStremioProviderFactory stremioFactory)
{
    public Task<List<CatalogConfig>> GetCatalogsAsync(Guid userId) =>
        GetCatalogsAsync(userId, Guid.Empty);

    // MULTIlato: fetch catalogs from a specific manifest instance (Guid.Empty = default instance).
    public async Task<List<CatalogConfig>> GetCatalogsAsync(Guid userId, Guid instanceId)
    {
        var config = GelatoPlugin.Instance!.Configuration;
        var cfg = GelatoPlugin.Instance!.GetConfig(userId, instanceId);
        var provider = stremioFactory.Create(cfg);
        var manifest = await provider.GetManifestAsync();

        if (manifest?.Catalogs == null)
        {
            return config.Catalogs;
        }

        List<CatalogConfig> catalogs = [];

        // Merge manifest catalogs with local config
        foreach (var mCatalog in manifest.Catalogs)
        {
            if (!mCatalog.IsImportable())
                continue;

            var existing = config.Catalogs.FirstOrDefault(c =>
                c.Id == mCatalog.Id && c.Type == mCatalog.Type
            );
            if (existing == null)
            {
                existing = new CatalogConfig
                {
                    Id = mCatalog.Id,
                    Type = mCatalog.Type,
                    Name = mCatalog.Name,
                    Enabled = false,
                    MaxItems = 0, // max items to be imported from this catalog
                    CreateCollection = false,
                    Url = "",
                };
            }
            else
            {
                // Update basic info from manifest just in case
                existing.Name = mCatalog.Name;
            }
            catalogs.Add(existing);
        }
        config.Catalogs = catalogs;

        // Save if we added new ones (optional, but good for persistence)
        GelatoPlugin.Instance.SaveConfiguration();

        return config.Catalogs;
    }

    public void UpdateCatalogConfig(CatalogConfig updatedConfig)
    {
        var config = GelatoPlugin.Instance!.Configuration;
        var existing = config.Catalogs.FirstOrDefault(c =>
            c.Id == updatedConfig.Id && c.Type == updatedConfig.Type
        );

        if (existing != null)
        {
            existing.Enabled = updatedConfig.Enabled;
            existing.MaxItems = updatedConfig.MaxItems;
            existing.CreateCollection = updatedConfig.CreateCollection;
        }
        else
        {
            config.Catalogs.Add(updatedConfig);
        }

        GelatoPlugin.Instance.SaveConfiguration();
    }

    public CatalogConfig? GetCatalogConfig(string id, string type)
    {
        return GelatoPlugin.Instance!.Configuration.Catalogs.FirstOrDefault(c =>
            c.Id == id && c.Type == type
        );
    }
}
