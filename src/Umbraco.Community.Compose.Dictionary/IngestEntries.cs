using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umbraco.Community.Compose.Dictionary;

// Wire format for the Compose ingestion API.
//
// These mirror the entry types in Umbraco.Compose.Integrations.UmbracoCms.Ingestion (18.0.2), so Compose
// receives exactly what the official content ingestion sends. They are kept local on purpose: depending
// on that package would tie this package to its release cycle and would also install its
// umbracoComposeIngestQueue table, because Umbraco discovers its package migration plan automatically.
// If the Compose ingestion API changes, update these types.

/// <summary>
/// Base class for ingestion actions.
/// </summary>
[JsonConverter(typeof(IngestEntryJsonConverter))]
internal abstract class IngestEntry
{
    /// <summary>The action to perform.</summary>
    public abstract string Action { get; }

    /// <summary>The id of the entry.</summary>
    public string? Id { get; set; }

    /// <summary>The variant of the entry.</summary>
    public string? Variant { get; set; }
}

/// <summary>
/// Upserts one dictionary item variant.
/// </summary>
internal sealed class UpsertDictionaryEntry : IngestEntry
{
    /// <inheritdoc />
    public override string Action => "upsert";

    /// <summary>The type schema alias.</summary>
    public required string Type { get; set; }

    /// <summary>The entry data.</summary>
    public required ComposeDictionaryItem Data { get; set; }
}

/// <summary>
/// Deletes one entry (optionally a single variant of it).
/// </summary>
internal sealed class DeleteEntry : IngestEntry
{
    /// <inheritdoc />
    public override string Action => "delete";
}

/// <summary>
/// Deletes every entry matching a filter.
/// </summary>
internal sealed class DeleteWhereEntry : IngestEntry
{
    /// <inheritdoc />
    public override string Action => "delete";

    /// <summary>The properties and values to match.</summary>
    public required Dictionary<string, object?> Where { get; init; }
}

/// <summary>
/// Serializes the concrete entry type rather than the base class.
/// </summary>
internal sealed class IngestEntryJsonConverter : JsonConverter<IngestEntry>
{
    public override IngestEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("Ingest entries are write-only.");

    public override void Write(Utf8JsonWriter writer, IngestEntry value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
