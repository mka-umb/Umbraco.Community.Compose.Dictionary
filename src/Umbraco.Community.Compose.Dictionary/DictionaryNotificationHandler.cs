using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Compose.Integrations.UmbracoCms.Core;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Queues dictionary changes for ingestion once the surrounding Umbraco scope has completed.
/// </summary>
internal sealed class DictionaryNotificationHandler(
    ICoreScopeProvider coreScopeProvider,
    ChannelWriter<DictionaryIngestQueueItem> writer,
    IOptions<UmbracoComposeOptions> composeOptions,
    IOptions<UmbracoComposeDictionaryOptions> dictionaryOptions,
    ILogger<DictionaryNotificationHandler> logger) :
        INotificationHandler<DictionaryItemSavedNotification>,
        INotificationHandler<DictionaryItemDeletedNotification>,
        INotificationHandler<DictionaryItemMovedNotification>
{
    public void Handle(DictionaryItemSavedNotification notification) =>
        Enqueue(notification.SavedEntities.Select(static x => new DictionaryChange(x.Key, IncludeDescendants: false)));

    public void Handle(DictionaryItemDeletedNotification notification) =>
        Enqueue(notification.DeletedEntities.Select(static x => new DictionaryChange(x.Key, IncludeDescendants: false)));

    // A move changes the ancestry of the whole subtree, so descendants must be re-ingested.
    public void Handle(DictionaryItemMovedNotification notification) =>
        Enqueue(notification.MoveInfoCollection.Select(static x => new DictionaryChange(x.Entity.Key, IncludeDescendants: true)));

    private void Enqueue(IEnumerable<DictionaryChange> changes)
    {
        if (!composeOptions.Value.IsValid || !dictionaryOptions.Value.IsValid)
        {
            logger.LogDebug("Skipping dictionary ingestion - Compose or dictionary options are not valid.");
            return;
        }

        DictionaryChange[] changeArray = [.. changes];
        if (changeArray.Length == 0)
        {
            return;
        }

        DictionaryIngestQueueItem item = new(changeArray);
        IScopeContext? scopeContext = coreScopeProvider.Context;

        if (scopeContext is null)
        {
            writer.TryWrite(item);
            return;
        }

        // Enlist's completion callback is a synchronous Action<bool, T>. Keep it synchronous:
        // an async lambda here would become async void (the issue in the shared DeferredActions).
        // TryWrite on an unbounded channel never blocks and only fails once the channel is completed.
        scopeContext.Enlist(
            $"composeDictionaryIngest:{item.Id}",
            () => item,
            (completed, queued) =>
            {
                if (completed && queued is not null)
                {
                    writer.TryWrite(queued);
                }
            });
    }
}

/// <summary>
/// Queues a full dictionary sync at startup. This backfills existing items after first install and
/// repairs anything missed while the site was down (failed batches, in-memory items lost on shutdown).
/// </summary>
internal sealed class DictionarySyncOnStartupComponent(
    ChannelWriter<DictionaryIngestQueueItem> writer,
    IRuntimeState runtimeState,
    IServerRoleAccessor serverRoleAccessor,
    IOptions<UmbracoComposeOptions> composeOptions,
    IOptions<UmbracoComposeDictionaryOptions> dictionaryOptions,
    ILogger<DictionarySyncOnStartupComponent> logger) : IAsyncComponent
{
    public Task InitializeAsync(bool isRestarting, CancellationToken cancellationToken)
    {
        if (runtimeState.Level < RuntimeLevel.Run)
        {
            return Task.CompletedTask;
        }

        // In a load-balanced setup only non-subscriber servers sync, matching content ingestion.
        if (serverRoleAccessor.CurrentServerRole is ServerRole.Subscriber)
        {
            logger.LogDebug("Skipping dictionary sync - current server role is 'Subscriber'.");
            return Task.CompletedTask;
        }

        if (!composeOptions.Value.IsValid || !dictionaryOptions.Value.IsValid)
        {
            logger.LogWarning(
                "Umbraco Compose dictionary ingestion is installed but not configured. " +
                "Set Umbraco:Compose:Dictionary:CollectionAlias (and the Umbraco:Compose credentials) to enable it.");
            return Task.CompletedTask;
        }

        if (dictionaryOptions.Value.SyncAllOnStartup)
        {
            writer.TryWrite(new DictionaryIngestQueueItem([], FullSync: true));
        }

        return Task.CompletedTask;
    }

    public Task TerminateAsync(bool isRestarting, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
