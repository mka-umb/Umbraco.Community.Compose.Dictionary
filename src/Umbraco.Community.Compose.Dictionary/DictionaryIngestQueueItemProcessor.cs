using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Reconciles dictionary changes with Compose. It always reads the current state from Umbraco rather
/// than trusting the notification payload, so retries and out-of-order processing stay correct: if an
/// item exists it is upserted, and if it no longer exists it (and its subtree) is deleted.
/// </summary>
internal sealed class DictionaryIngestQueueItemProcessor(
    IDictionaryItemService dictionaryItemService,
    ILanguageService languageService,
    IOptions<UmbracoComposeDictionaryOptions> options,
    ILogger<DictionaryIngestQueueItemProcessor> logger)
{
    public IAsyncEnumerable<IngestEntry> ProcessAsync(DictionaryIngestQueueItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return ProcessCoreAsync(item, cancellationToken);
    }

    private async IAsyncEnumerable<IngestEntry> ProcessCoreAsync(
        DictionaryIngestQueueItem item,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string[] cultures = [.. (await languageService.GetAllAsync().ConfigureAwait(false)).Select(static l => l.IsoCode)];
        string typeAlias = options.Value.TypeSchemaAlias;

        if (item.FullSync)
        {
            IDictionaryItem[] all = [.. await dictionaryItemService.GetDescendantsAsync(null, null).ConfigureAwait(false)];
            Dictionary<Guid, IDictionaryItem?> lookup = all.ToDictionary(static x => x.Key, static x => (IDictionaryItem?)x);

            logger.LogInformation("Full dictionary sync: {Count} item(s) across {Cultures} culture(s)", all.Length, cultures.Length);

            foreach (IDictionaryItem dictionaryItem in all)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid[] ancestors = await GetAncestorsAsync(dictionaryItem, lookup).ConfigureAwait(false);

                foreach (IngestEntry entry in BuildUpserts(dictionaryItem, ancestors, cultures, typeAlias))
                {
                    yield return entry;
                }
            }

            // A full sync already covers every item, so the individual changes are redundant.
            yield break;
        }

        Dictionary<Guid, IDictionaryItem?> cache = [];
        HashSet<Guid> handled = [];

        foreach (DictionaryChange change in item.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IDictionaryItem? dictionaryItem = await GetCachedAsync(change.Key, cache).ConfigureAwait(false);

            if (dictionaryItem is null)
            {
                if (!handled.Add(change.Key))
                {
                    continue;
                }

                // Deleting a parent removes its descendants in Umbraco, but only the parent raises a
                // notification, so delete the subtree by ancestry as content ingestion does.
                string id = change.Key.ToString();
                foreach (string culture in cultures)
                {
                    yield return new DeleteEntry { Id = id, Variant = culture };
                    yield return new DeleteWhereEntry
                    {
                        Where = new()
                        {
                            { "ancestors_some", new[] { id } },
                            { "variant", culture }
                        }
                    };
                }

                continue;
            }

            IEnumerable<IDictionaryItem> targets = change.IncludeDescendants
                ? [dictionaryItem, .. await dictionaryItemService.GetDescendantsAsync(dictionaryItem.Key, null).ConfigureAwait(false)]
                : [dictionaryItem];

            foreach (IDictionaryItem target in targets)
            {
                if (!handled.Add(target.Key))
                {
                    continue;
                }

                cache[target.Key] = target;
                Guid[] ancestors = await GetAncestorsAsync(target, cache).ConfigureAwait(false);

                foreach (IngestEntry entry in BuildUpserts(target, ancestors, cultures, typeAlias))
                {
                    yield return entry;
                }
            }
        }
    }

    /// <summary>
    /// Builds one upsert per culture that has a value. Cultures without a value are deleted so that
    /// clearing a translation in the backoffice also removes it from Compose. Deleting a variant that
    /// was never ingested is the same pattern content ingestion relies on for unpublish.
    /// </summary>
    private static IEnumerable<IngestEntry> BuildUpserts(
        IDictionaryItem item,
        Guid[] ancestors,
        string[] cultures,
        string typeAlias)
    {
        string id = item.Key.ToString();

        foreach (string culture in cultures)
        {
            string? value = item.Translations
                .FirstOrDefault(t => string.Equals(t.LanguageIsoCode, culture, StringComparison.OrdinalIgnoreCase))
                ?.Value;

            if (string.IsNullOrEmpty(value))
            {
                yield return new DeleteEntry { Id = id, Variant = culture };
                continue;
            }

            yield return new UpsertDictionaryEntry
            {
                Id = id,
                Variant = culture,
                Type = typeAlias,
                Data = new()
                {
                    Key = item.ItemKey,
                    Value = value,
                    ParentId = item.ParentId,
                    Ancestors = ancestors,
                    UpdateDate = item.UpdateDate
                }
            };
        }
    }

    /// <summary>
    /// Walks ParentId upwards, nearest ancestor first. Guards against cycles in corrupt data.
    /// </summary>
    private async Task<Guid[]> GetAncestorsAsync(IDictionaryItem item, Dictionary<Guid, IDictionaryItem?> cache)
    {
        List<Guid> ancestors = [];
        HashSet<Guid> visited = [item.Key];
        Guid? parentKey = item.ParentId;

        while (parentKey is { } key && visited.Add(key))
        {
            ancestors.Add(key);
            IDictionaryItem? parent = await GetCachedAsync(key, cache).ConfigureAwait(false);
            parentKey = parent?.ParentId;
        }

        return [.. ancestors];
    }

    private async Task<IDictionaryItem?> GetCachedAsync(Guid key, Dictionary<Guid, IDictionaryItem?> cache)
    {
        if (cache.TryGetValue(key, out IDictionaryItem? cached))
        {
            return cached;
        }

        IDictionaryItem? item = await dictionaryItemService.GetAsync(key).ConfigureAwait(false);
        cache[key] = item;
        return item;
    }
}
