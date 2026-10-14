using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Options for Umbraco Compose dictionary ingestion, bound from <c>Umbraco:Compose:Dictionary</c>.
/// </summary>
public sealed class UmbracoComposeDictionaryOptions
{
    /// <summary>
    /// The collection to ingest dictionary items into. Use a dedicated collection so dictionary
    /// entries never mix with content, and so collection-wide deletes stay safe.
    /// </summary>
    [Required]
    public string CollectionAlias { get; set; } = default!;

    /// <summary>
    /// The type schema alias used for dictionary entries.
    /// </summary>
    public string TypeSchemaAlias { get; set; } = "dictionaryItem";

    /// <summary>
    /// Whether to create/update the type schema in Compose on first use.
    /// Requires the <c>{environment}:typeschema:write</c> scope. Disable it if you manage the schema yourself.
    /// </summary>
    public bool RegisterTypeSchema { get; set; } = true;

    /// <summary>
    /// Whether to re-upsert every dictionary item on startup. This backfills existing items on
    /// first install and repairs anything missed while the site was down.
    /// </summary>
    public bool SyncAllOnStartup { get; set; } = true;

    /// <summary>
    /// The maximum number of entries per ingestion request.
    /// </summary>
    [Range(1, 1000)]
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How many times a failed batch is retried (with backoff) before it is given up on.
    /// </summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>
    /// Whether the options are usable. Empty strings count as missing.
    /// </summary>
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(CollectionAlias) &&
        !string.IsNullOrWhiteSpace(TypeSchemaAlias);
}

/// <summary>
/// A dictionary item translation as stored in Umbraco Compose. One entry exists per culture variant.
/// </summary>
internal sealed class ComposeDictionaryItem
{
    /// <summary>The dictionary key, e.g. <c>Footer.Copyright</c>.</summary>
    public required string Key { get; init; }

    /// <summary>The translated value for the entry's variant.</summary>
    public required string Value { get; init; }

    /// <summary>The key of the parent dictionary item, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ParentId { get; init; }

    /// <summary>
    /// The keys of all ancestor dictionary items. Used for subtree deletes (<c>ancestors_some</c>).
    /// </summary>
    public required Guid[] Ancestors { get; init; }

    /// <summary>When the dictionary item was last updated in Umbraco.</summary>
    public DateTime UpdateDate { get; init; }
}

/// <summary>
/// A dictionary item that has changed and needs to be reconciled with Compose.
/// </summary>
/// <param name="Key">The dictionary item key (Guid, not the string ItemKey, so renames are plain upserts).</param>
/// <param name="IncludeDescendants">Whether descendants must be re-ingested too, e.g. after a move.</param>
internal sealed record DictionaryChange(Guid Key, bool IncludeDescendants);

/// <summary>
/// A queue item describing dictionary changes.
/// </summary>
/// <param name="Changes">The changed items.</param>
/// <param name="FullSync">Whether to reconcile every dictionary item.</param>
internal sealed record DictionaryIngestQueueItem(
    IReadOnlyCollection<DictionaryChange> Changes,
    bool FullSync = false)
{
    /// <summary>A unique id, used as the scope enlistment key.</summary>
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>How many times this item has failed so far.</summary>
    public int Attempt { get; init; }
}
