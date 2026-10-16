# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.1] - 2026-10-16

### Changed
- Removed the "Building and publishing" section from the README (documentation only; no code changes).

## [1.0.0] - 2026-10-12

### Added
- Initial release: ingest Umbraco CMS dictionary items into Umbraco Compose, one entry per culture
  variant.
- Reconciles on save, move and delete of dictionary items; subtree deletes via `ancestors_some`.
- Full re-sync on startup to backfill existing items and recover anything missed while down.
- Burst coalescing and batched ingestion with retry/backoff.
- Optional type-schema registration (`RegisterTypeSchema`).
- Works automatically with or without the official Compose integration: the package registers its
  Compose client under a dedicated OpenIddict registration id and resolves tokens by that id, so it
  does not collide with the official integration's registration and is unaffected by other OpenIddict
  clients (e.g. Slack, Umbraco Cloud).

[Unreleased]: https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/releases/tag/v1.0.0
