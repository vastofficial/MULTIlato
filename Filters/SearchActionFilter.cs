using Gelato.Config;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Gelato.Filters;

public class SearchActionFilter(
    IDtoService dtoService,
    GelatoManager manager,
    ILogger<SearchActionFilter> log
) : IAsyncActionFilter, IOrderedFilter
{
    public int Order => 1;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext ctx,
        ActionExecutionDelegate next
    )
    {
        ctx.TryGetUserId(out var userId);
        var cfg = GelatoPlugin.Instance!.GetConfig(userId);
        if (
            cfg.DisableSearch
            || !ctx.IsApiSearchAction()
            || !ctx.TryGetActionArgument<string>("searchTerm", out var searchTerm)
        )
        {
            await next();
            return;
        }

        // Strip "local:" prefix if present and pass through to default handler
        if (searchTerm.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            ctx.ActionArguments["searchTerm"] = searchTerm[6..].Trim();
            await next();
            return;
        }

        // Handle Stremio search
        var requestedTypes = GetRequestedItemTypes(ctx);
        if (requestedTypes.Count == 0)
        {
            await next();
            return;
        }

        // MULTIlato: fan out search across every usable manifest instance.
        var instanceConfigs = new List<PluginConfiguration>();
        foreach (var instCfg in GelatoPlugin.Instance!.GetConfigs(userId))
        {
            if (instCfg.Stremio is not null && await instCfg.Stremio.IsReady())
            {
                instanceConfigs.Add(instCfg);
            }
        }

        if (instanceConfigs.Count == 0)
        {
            await next();
            return;
        }

        ctx.TryGetActionArgument("startIndex", out var start, 0);
        ctx.TryGetActionArgument("limit", out var limit, 25);

        var metas = await SearchMetasAsync(searchTerm, requestedTypes, instanceConfigs, cfg);

        log.LogInformation(
            "Intercepted /Items search \"{Query}\" types=[{Types}] start={Start} limit={Limit} results={Results}",
            searchTerm,
            string.Join(",", requestedTypes),
            start,
            limit,
            metas.Count
        );

        var dtos = ConvertMetasToDtos(metas);
        var paged = dtos.Skip(start).Take(limit).ToArray();

        ctx.Result = new OkObjectResult(
            new QueryResult<BaseItemDto> { Items = paged, TotalRecordCount = dtos.Count }
        );
    }

    private HashSet<BaseItemKind> GetRequestedItemTypes(ActionExecutingContext ctx)
    {
        var requested = new HashSet<BaseItemKind>([BaseItemKind.Movie, BaseItemKind.Series]);

        // Already parsed as BaseItemKind[] by model binder
        if (
            ctx.TryGetActionArgument<BaseItemKind[]>("includeItemTypes", out var includeTypes)
            && includeTypes is { Length: > 0 }
        )
        {
            requested = new HashSet<BaseItemKind>(includeTypes);
            // Only keep Movie and Series
            requested.IntersectWith([BaseItemKind.Movie, BaseItemKind.Series]);
        }

        // Remove excluded types
        if (
            ctx.TryGetActionArgument<BaseItemKind[]>("excludeItemTypes", out var excludeTypes)
            && excludeTypes is { Length: > 0 }
        )
        {
            requested.ExceptWith(excludeTypes);
        }

        // If mediaTypes=Video, exclude Series
        if (
            ctx.TryGetActionArgument<MediaType[]>("mediaTypes", out var mediaTypes)
            && mediaTypes.Contains(MediaType.Video)
        )
        {
            requested.Remove(BaseItemKind.Series);
        }

        return requested;
    }

    private async Task<List<StremioMeta>> SearchMetasAsync(
        string searchTerm,
        HashSet<BaseItemKind> requestedTypes,
        List<PluginConfiguration> instanceConfigs,
        PluginConfiguration globalCfg
    )
    {
        var tasks = new List<Task<IReadOnlyList<StremioMeta>>>();

        foreach (var cfg in instanceConfigs)
        {
            if (requestedTypes.Contains(BaseItemKind.Movie) && cfg.MovieFolder is not null)
            {
                tasks.Add(cfg.Stremio!.SearchAsync(searchTerm, StremioMediaType.Movie));
            }

            if (requestedTypes.Contains(BaseItemKind.Series) && cfg.SeriesFolder is not null)
            {
                tasks.Add(cfg.Stremio!.SearchAsync(searchTerm, StremioMediaType.Series));
            }
        }

        if (tasks.Count == 0)
        {
            log.LogWarning(
                "No movie/series folder found on any instance, please add your gelato paths to a library and rescan. skipping search"
            );
            return [];
        }

        // MULTIlato: merge results from every instance, deduping by (type, stremio id).
        var results = (await Task.WhenAll(tasks))
            .SelectMany(r => r)
            .GroupBy(m => (m.Type, m.Id))
            .Select(g => g.First())
            .ToList();

        var filterUnreleased = globalCfg.FilterUnreleased;
        var bufferDays = globalCfg.FilterUnreleasedBufferDays;

        if (filterUnreleased)
        {
            results = results.Where(x => x.IsReleased(bufferDays)).ToList();
        }

        return results;
    }

    private List<BaseItemDto> ConvertMetasToDtos(List<StremioMeta> metas)
    {
        // theres a reason i initally disabled all fields but forgot....
        // infuse breaks if we do a small subset. Not sure which field it needs. Prolly mediasources
        var options = new DtoOptions { EnableImages = true, EnableUserData = false };

        var dtos = new List<BaseItemDto>(metas.Count);

        foreach (var meta in metas)
        {
            var baseItem = manager.IntoBaseItem(meta);
            if (baseItem is null)
                continue;

            var dto = dtoService.GetBaseItemDto(baseItem, options);
            var stremioUri = StremioUri.FromBaseItem(baseItem);
            dto.Id = stremioUri.ToGuid();
            dtos.Add(dto);

            manager.SaveStremioMeta(dto.Id, meta);
        }

        return dtos;
    }
}
