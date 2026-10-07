# Publishing a release

First merge a PR whose **build** and **hosted desktop E2E** checks both passed on
its latest commit. For a broad modernization PR, describe the full scope: settings
and localization, tab rendering and DPI, input, workspaces, runtime ownership,
native resources, build tooling, tests and documentation.

Update [CHANGELOG.md](../CHANGELOG.md): turn Unreleased into the chosen version and
release date, keeping the user-facing changes. Remove the upcoming-release note
from [README.md](../README.md) when those changes are published.

Tag the merged commit and push the tag, substituting the intended version below:

```powershell
git switch master
git pull --ff-only
git tag v2026.10.07
git push origin v2026.10.07
```

A `vMAJOR.MINOR.PATCH` tag, optionally with a suffix such as `-beta.1`, triggers
[the release workflow](../.github/workflows/release.yml). It:

1. Builds the standalone Release exe, embedding the tag version.
2. Runs regression tests with 50% line and branch coverage floors.
3. Runs the isolated Release smoke test.
4. Creates a **draft** GitHub Release with `WindowTabs.exe` and generated notes
   only after all preceding checks pass. A suffixed tag creates a prerelease draft.

Review the draft, include the changelog's user-facing highlights, and publish it on
GitHub. Pushing a tag prepares a draft; it does not publish the release or update
users' installed copies. Users download and replace the exe themselves.
