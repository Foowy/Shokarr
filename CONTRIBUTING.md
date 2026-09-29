# Contributing to Shokarr

Thanks for helping out. Bug reports, feature ideas and pull requests are all welcome.
Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).

## Reporting bugs and requesting features

Open an [issue](https://github.com/Foowy/Shokarr/issues/new/choose) using the matching template.
For bugs, include your Shokarr version, Shoko Server version and relevant log lines
(remove API keys first). Security problems go through the private flow in [SECURITY.md](SECURITY.md),
not a public issue.

## Development setup

Requires the .NET 10 SDK.

```bash
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet format --verify-no-changes
```

Deploy steps for a local Shoko Server are in the [README](README.md#manual-deploy).

## Pull requests

1. Fork the repo and branch from `master`.
2. Keep each PR to one change. Add or update tests for behaviour changes.
3. Make sure the four commands above pass; CI runs the same checks plus CodeQL.
4. Fill in the PR template and describe why the change is needed.

`master` is protected, so all changes land through a PR with passing checks. PRs are squash-merged.

## Notes

- `Shoko.Abstractions` and `Asp.Versioning.*` must match what the Shoko Server host bundles,
  so Dependabot ignores `Asp.Versioning.*` and bumps of either are done by hand against the host.
- Plugin load only happens at Shoko Server startup, so restart the server after deploying a build.
