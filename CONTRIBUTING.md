# Contributing

Thanks for your interest in improving **Umbraco.Community.Compose.Dictionary**. This is an
independent community package (not maintained by Umbraco HQ). Contributions of all sizes are welcome.

## Getting started

Requirements:

- .NET 10 SDK
- An Umbraco 17 site to test against (the package has no standalone runtime)

Build:

```shell
dotnet build src/Umbraco.Community.Compose.Dictionary -c Debug
```

> The **Release** configuration treats warnings as errors. Build `-c Release` before opening a PR to
> catch anything CI will flag.

## Testing against a real site

The package only does something inside a running Umbraco instance, so the practical loop is a local
site that references the project:

1. Create or use an Umbraco 17 site.
2. Reference this project from the site while developing:
   ```shell
   dotnet add path/to/YourSite.csproj reference src/Umbraco.Community.Compose.Dictionary/Umbraco.Community.Compose.Dictionary.csproj
   ```
3. Call `.AddUmbracoComposeDictionaryIngestion()` after `AddComposers()` and configure
   `Umbraco:Compose` (see the README).
4. Edit/move/delete dictionary items and confirm they reconcile with your Compose collection.

Do **not** commit a local `ProjectReference` or any `appsettings.local.json` / secrets.

## Pull requests

- Branch off `main`; keep PRs focused.
- Match the surrounding code style (`.editorconfig` is enforced); keep comment density and naming
  consistent with the existing files.
- Update `CHANGELOG.md` under `## [Unreleased]`.
- Describe what you changed and how you verified it.

## Reporting bugs

Open a GitHub issue with the Umbraco version, the package version, relevant logs, and steps to
reproduce. For **security** issues, do not open a public issue — see [SECURITY.md](SECURITY.md).
