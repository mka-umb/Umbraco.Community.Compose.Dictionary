# Umbraco.Community.Compose.Dictionary

[![Build and publish](https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/actions/workflows/build.yml/badge.svg)](https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
<!-- TODO after first publish: [![NuGet](https://img.shields.io/nuget/v/Umbraco.Community.Compose.Dictionary.svg)](https://www.nuget.org/packages/Umbraco.Community.Compose.Dictionary) -->

Ingests Umbraco CMS dictionary items into [Umbraco Compose](https://docs.umbraco.com/umbraco-compose). Saving, moving or deleting a dictionary item updates Compose automatically, and existing items are backfilled on startup.

This is an independent community package. It is not part of, or maintained with, `Umbraco.Compose.Integrations`. Its only Compose dependency is `Umbraco.Compose.Integrations.UmbracoCms.Core`, which supplies authentication and endpoint URLs.

## Requirements

- Umbraco CMS 17 (.NET 10).
- An Umbraco Compose project with a collection dedicated to dictionary items.
- An API Application with the `{environment}:ingestion` scope. Add `{environment}:typeschema:write` if `RegisterTypeSchema` is on (the default).

It works with or without the official Compose CMS integration installed.

## Installation

```shell
dotnet add package Umbraco.Community.Compose.Dictionary
```

```csharp
builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddUmbracoComposeDictionaryIngestion()   // after AddComposers()
    .Build();
```

Call it after `AddComposers()`. The package registers Compose authentication itself (a standalone install needs it) under a dedicated OpenIddict registration id, so it works automatically whether or not the official Compose integration is also installed — no configuration required.

## Configuration

```json
"Umbraco": {
  "Compose": {
    "ClientId": "...",
    "ClientSecret": "...",
    "ProjectAlias": "...",
    "EnvironmentAlias": "...",
    "Region": "...",
    "Dictionary": {
      "CollectionAlias": "dictionary",
      "TypeSchemaAlias": "dictionaryItem",
      "RegisterTypeSchema": true,
      "SyncAllOnStartup": true,
      "BatchSize": 100,
      "MaxAttempts": 6
    }
  }
}
```

The `Umbraco:Compose` credentials are the same ones the official integration uses, so a site that already has it only needs the `Dictionary` section. Keep `ClientSecret` out of committed files: use `appsettings.local.json`, user secrets or Key Vault.

## Data model

Each dictionary item becomes one Compose entry per language:

- **`id`** is the dictionary item's Guid key, not its string key, so renaming a key is a plain upsert.
- **`variant`** is the language ISO code, for example `en-US`.
- **`type`** is `TypeSchemaAlias`.
- **`data`** holds `{ key, value, parentId, ancestors, updateDate }`.

Clearing a translation deletes that variant. Deleting an item deletes its subtree via `ancestors_some`.

The following example follows the query shape used by the official Compose Data Source package. Check it against your environment's GraphQL schema.

```graphql
query Dictionary($variant: String, $after: String) {
  dictionary(where: { variant: $variant }, first: 500, after: $after) {
    items { id variant ... on DictionaryItem { key value } }
    pageInfo { endCursor hasNextPage }
  }
}
```

## How it works

1. Notification handlers queue changes after the Umbraco scope completes.
2. A background service coalesces bursts (such as a uSync import) and re-reads the current state from `IDictionaryItemService`.
3. It PUTs batches to the Compose ingestion API.
4. Failed batches are retried with exponential backoff up to `MaxAttempts`. Processing reconciles against the current state, so retries are always safe.
5. A full sync on startup handles backfill and recovery. This replaces a database queue, which is cheap to do because dictionaries are small.

The request payloads mirror the official content ingestion's entry types (as of Compose integration 18.0.2). They are defined locally in `IngestEntries.cs` rather than imported, so this package does not depend on the Ingestion package or install its queue table.

## Known gaps

- **Deletes made while the site is down.** A delete whose retries all fail, or a delete still queued in memory at shutdown, is not repaired by the startup sync, because the sync only upserts.
- **Language deletion.** Deleting a language does not remove that culture's variants from Compose.
- **Type schema.** The generated schema has not been validated against every Compose schema rule. Review it in a dev environment first.

## License

MIT
