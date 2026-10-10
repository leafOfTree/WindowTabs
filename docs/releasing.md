# Publishing a release

First merge a PR whose **build** and **hosted desktop E2E** checks both passed on
its latest commit. For a broad modernization PR, describe the full scope: settings
and localization, tab rendering and DPI, input, workspaces, runtime ownership,
native resources, build tooling, tests and documentation.

Record the chosen version and release date in [CHANGELOG.md](../CHANGELOG.md),
keeping the user-facing changes. Check that [README.md](../README.md) describes
the version being released.

Tag the merged commit and push the tag, substituting the intended version below:

```powershell
git switch main
git pull --ff-only
git tag v2026.10.07
git push origin v2026.10.07
```

A `vMAJOR.MINOR.PATCH` tag, optionally with a suffix such as `-beta.1`, triggers
[the release workflow](../.github/workflows/release.yml). It:

1. Builds the standalone Release exe, embedding the tag version.
2. Runs all 13 regression suites in three parallel groups on separate Windows
   runners, without coverage instrumentation. Suites stay serial within each group.
3. Runs the isolated Release smoke test, then starts Full desktop E2E against the
   built exe as soon as the build job succeeds, without waiting for regressions.
4. Creates a **draft** GitHub Release with `WindowTabs.exe` and generated notes
   only after all preceding checks pass. A suffixed tag creates a prerelease draft.

Review the draft, include the changelog's user-facing highlights, and publish it on
GitHub. Pushing a tag prepares a draft; it does not publish the release or update
users' installed copies. Users download and replace the exe themselves.
