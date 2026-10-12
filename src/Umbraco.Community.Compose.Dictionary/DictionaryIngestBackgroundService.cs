using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Compose.Integrations.UmbracoCms.Core;
using Umbraco.Compose.Integrations.UmbracoCms.Core.Json;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Reads dictionary queue items, coalesces bursts (e.g. a uSync import saving hundreds of items),
/// and sends them to Compose in batches. Unlike the content ingestion service, a failed batch is
/// retried with backoff instead of being dropped, and no exception can escape the loop.
/// </summary>
internal sealed class DictionaryIngestBackgroundService(
    Channel<DictionaryIngestQueueItem> channel,
    IHttpClientFactory httpClientFactory,
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<UmbracoComposeDictionaryOptions> options,
    IOptions<UmbracoComposeOptions> composeOptions,
    ILogger<DictionaryIngestBackgroundService> logger) : BackgroundService
{
    internal const string IngestionClientName = "UmbracoComposeDictionaryIngestion";
    internal const string ManagementClientName = "UmbracoComposeDictionaryManagement";

    private const int MaxItemsPerMerge = 500;
    private static readonly TimeSpan s_coalesceWindow = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromMinutes(5);

    // Matches the serializer settings content ingestion uses, so the wire format is identical.
    private static readonly JsonSerializerOptions s_ingestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    private static readonly JsonSerializerOptions s_managementJsonOptions = new(JsonSerializerDefaults.Web);

    private bool _typeSchemaRegistered;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                // Give bulk operations a moment to finish so they end up in one request.
                await Task.Delay(s_coalesceWindow, stoppingToken).ConfigureAwait(false);

                List<DictionaryIngestQueueItem> pending = [];
                while (pending.Count < MaxItemsPerMerge && channel.Reader.TryRead(out DictionaryIngestQueueItem? next))
                {
                    pending.Add(next);
                }

                if (pending.Count == 0)
                {
                    continue;
                }

                DictionaryIngestQueueItem item = Merge(pending);

                try
                {
                    await ProcessAsync(item, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    ScheduleRetry(item, ex, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down. Anything still queued is recovered by the full sync on next startup.
        }
    }

    private async Task ProcessAsync(DictionaryIngestQueueItem item, CancellationToken cancellationToken)
    {
        UmbracoComposeDictionaryOptions currentOptions = options.CurrentValue;

        if (!composeOptions.Value.IsValid || !currentOptions.IsValid)
        {
            logger.LogDebug("Skipping dictionary ingestion - Compose or dictionary options are not valid.");
            return;
        }

        await EnsureTypeSchemaAsync(currentOptions, cancellationToken).ConfigureAwait(false);

        List<IngestEntry> entries;
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            DictionaryIngestQueueItemProcessor processor = scope.ServiceProvider
                .GetRequiredService<DictionaryIngestQueueItemProcessor>();

            entries = await processor.ProcessAsync(item, cancellationToken)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        if (entries.Count == 0)
        {
            return;
        }

        HttpClient client = httpClientFactory.CreateClient(IngestionClientName);

        foreach (IngestEntry[] chunk in entries.Chunk(currentOptions.BatchSize))
        {
            using HttpResponseMessage response = await client
                .PutAsJsonAsync(currentOptions.CollectionAlias, chunk, s_ingestJsonOptions, cancellationToken)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, "ingest dictionary entries", cancellationToken).ConfigureAwait(false);
        }

        logger.LogDebug("Ingested {Count} dictionary entr(ies) into '{Collection}'", entries.Count, currentOptions.CollectionAlias);
    }

    private async Task EnsureTypeSchemaAsync(UmbracoComposeDictionaryOptions currentOptions, CancellationToken cancellationToken)
    {
        if (_typeSchemaRegistered || !currentOptions.RegisterTypeSchema)
        {
            return;
        }

        JsonSchema schema = JsonSchemaGenerator.Generate<ComposeDictionaryItem>(new JsonSchemaGeneratorOptions
        {
            DefaultSchema = "https://umbracocompose.com/v1/schema",
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        object[] body =
        [
            new
            {
                typeSchemaAlias = currentOptions.TypeSchemaAlias,
                description = "Umbraco CMS dictionary item",
                schema
            }
        ];

        HttpClient client = httpClientFactory.CreateClient(ManagementClientName);
        using HttpResponseMessage response = await client
            .PutAsJsonAsync("type-schemas", body, s_managementJsonOptions, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "register the dictionary type schema", cancellationToken).ConfigureAwait(false);

        _typeSchemaRegistered = true;
        logger.LogInformation("Registered Compose type schema '{Alias}'", currentOptions.TypeSchemaAlias);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Failed to {operation}. Status code: {(int)response.StatusCode}. Response: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    private void ScheduleRetry(DictionaryIngestQueueItem item, Exception exception, CancellationToken stoppingToken)
    {
        int attempt = item.Attempt + 1;

        if (attempt >= options.CurrentValue.MaxAttempts)
        {
            logger.LogError(
                exception,
                "Giving up on dictionary ingestion after {Attempts} attempt(s) ({Changes} change(s), full sync: {FullSync}). " +
                "Restart with SyncAllOnStartup enabled to reconcile.",
                attempt,
                item.Changes.Count,
                item.FullSync);
            return;
        }

        TimeSpan delay = TimeSpan.FromSeconds(Math.Min(s_maxRetryDelay.TotalSeconds, 5 * Math.Pow(2, attempt)));
        logger.LogWarning(exception, "Dictionary ingestion failed (attempt {Attempt}), retrying in {Delay}", attempt, delay);

        _ = RequeueLaterAsync(item with { Attempt = attempt }, delay, stoppingToken);
    }

    private async Task RequeueLaterAsync(DictionaryIngestQueueItem item, TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            channel.Writer.TryWrite(item);
        }
        catch (OperationCanceledException)
        {
            // Shutting down; the startup full sync covers it.
        }
    }

    /// <summary>
    /// Merges queued items into one. The processor re-reads current state, so merging is always safe.
    /// The lowest attempt count wins, so fresh edits are never given up on early because they were
    /// merged with a batch that is already retrying.
    /// </summary>
    private static DictionaryIngestQueueItem Merge(List<DictionaryIngestQueueItem> items)
    {
        if (items.Count == 1)
        {
            return items[0];
        }

        DictionaryChange[] changes =
        [
            .. items
                .SelectMany(static i => i.Changes)
                .GroupBy(static c => c.Key)
                .Select(static g => new DictionaryChange(g.Key, g.Any(static c => c.IncludeDescendants)))
        ];

        return new DictionaryIngestQueueItem(changes, items.Any(static i => i.FullSync))
        {
            Attempt = items.Min(static i => i.Attempt)
        };
    }
}
