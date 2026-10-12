# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-12

### Added
- Initial release: ingest Umbraco CMS dictionary items into Umbraco Compose, one entry per culture
  variant.
- Reconciles on save, move and delete of dictionary items; subtree deletes via `ancestors_some`.
- Full re-sync on startup to backfill existing items and recover anything missed while down.
- Burst coalescing and batched ingestion with retry/backoff.
- Optional type-schema registration (`RegisterTypeSchema`).
- `RegisterComposeAuthentication` option (default `true`) that works standalone or alongside the
  official Compose integration; duplicate authentication registrations are de-duplicated
  automatically.
- Authentication handler that resolves the Compose client registration by explicit issuer, so it
  works even when other OpenIddict clients (e.g. Slack, Umbraco Cloud) are registered.

[Unreleased]: https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/mka-umb/Umbraco.Community.Compose.Dictionary/releases/tag/v1.0.0
