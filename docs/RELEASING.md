# Building and publishing ProtoBench

GitHub Actions builds the app and SDK on every push to `main`, pull request, and manual workflow run. It builds/tests the solution on Windows, then uploads downloadable package artifacts. Dependencies are restored with .NET 10; official GitHub actions are pinned to commit IDs.

Push a version tag to publish a GitHub Release after the Windows build and tests pass:

```powershell
git tag -a v2.0.1 -m "ProtoBench 2.0.1"
git push origin v2.0.1
```

Tags use `vMAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as `v2.1.0-rc.1`. Tags containing a suffix create prereleases. The release job creates a draft, uploads all verified packages, then publishes it. Rerunning a tag build can replace its assets. The workflow uses GitHub's built-in token; no personal access token or repository secret needs to be configured. Only the release job gets repository write permission.

Release downloads:

- `ProtoBench-VERSION-win-x64.zip`: Windows x64 app, including .NET and Windows App SDK runtime files. Extract and run `ProtoBench.exe`.
- `ProtoBench-SDK-VERSION.zip`: portable/WinUI SDK contracts, props imports, source contracts, documentation, packaging tools, and standalone sample plugins.
- `ProtoBench-DeveloperKit-VERSION.zip`: the app and SDK together, built from the same revision.
- `SHA256SUMS.txt`: SHA-256 hashes of the three release archives. The SDK and developer kit also contain internal file checksums.

Build the same packages locally on Windows:

```powershell
./scripts/Build-Release.ps1 -Version 2.0.1 -OutputDirectory artifacts/release-2.0.1
```

Output directories must not already exist. `Build-DeveloperKit.ps1` also accepts `-SelfContainedApp` and `-Version` for custom packaging. Release builds retain SDK assembly identity `2.0.0.0` for extension compatibility while using the requested package/file version.

Publishing here means GitHub Releases downloads. In-app update checks remain disabled, as in ordinary builds; publishing a release does not silently enable them. See [UPDATES.md](UPDATES.md) before configuring an updater feed.

The optional >2 GiB integration test is excluded from normal CI. To run it explicitly, set `PROTOBENCH_LARGE_LOG_TEST=1` and run the application tests on a machine with adequate disk space. Normal streaming and bounded-memory regression tests still run in CI.