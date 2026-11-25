# Building and publishing ProtoBench

Text Compare uses the Microsoft Edge WebView2 Runtime for its continuous virtualized editor. The editor scripts, styles and MIT license are bundled in `Assets/TextCompareEditor`; it does not load code from a CDN or send source text to a service. The Evergreen runtime is included on Windows 11 and most updated Windows 10 installations. For machines without it, install the [WebView2 Runtime from Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) before using Text Compare.

GitHub Actions builds the app and SDK on every push to `main`, pull request, and manual workflow run. It builds/tests the solution on Windows, then uploads separate **installer**, **portable**, **updates**, and **SDK/developer kit** artifacts. Both app distributions come from the same self-contained publish output. Dependencies are restored with .NET 10; official GitHub actions are pinned to commit IDs. The local tool manifest pins the Velopack CLI to the app's Velopack package version.

The Windows build runs once for a `main` push. A tag push runs only the release workflow: it downloads that exact commit's successful main-build artifacts and publishes them, without rebuilding or repackaging. If main and the tag are pushed together, the release workflow waits for the main build (up to 50 minutes).

Set `Version` in `Directory.Build.props`, commit and push that change to `main`, then push a matching version tag:

```powershell
git push origin main
git tag -a v2.0.28 -m "ProtoBench 2.0.28"
git push origin v2.0.28
```

Tags use `vMAJOR.MINOR.PATCH`, optionally with a prerelease suffix such as `v2.1.0-rc.1`, and must match the committed package version exactly. Tags containing a suffix create prereleases. Only successful `main` push builds of the exact tagged commit are eligible; pull-request packages and builds of other commits are excluded. Failed/cancelled builds and missing/expired packages stop the release. Artifacts are retained for 14 days; rerun the main build and then the failed release workflow when needed.

The release job creates a draft, uploads all verified packages, then publishes it. Rerunning the release workflow can replace its assets. The workflows use GitHub's built-in token; no personal access token or repository secret needs to be configured. Only the release job gets repository write permission, plus read access to build artifacts.

Release downloads:

- `ProtoBench-VERSION-win-x64-Setup.exe`: Windows x64 installer. Run it to install ProtoBench.
- `ProtoBench-VERSION-win-x64.zip`: Windows x64 app, including .NET and Windows App SDK runtime files. Extract and run `ProtoBench.exe`.
- `ProtoBench-SDK-VERSION.zip`: portable/WinUI SDK contracts, props imports, source contracts, documentation, packaging tools, and standalone sample plugins.
- `ProtoBench-DeveloperKit-VERSION.zip`: the app and SDK together, built from the same revision.
- `ProtoBench.Desktop-VERSION-full.nupkg` and `releases.win.json`: application update package and Velopack feed.
- `latest.json`: version manifest for update checks in portable copies.
- `SHA256SUMS.txt`: SHA-256 hashes of all release files. The SDK and developer kit also contain internal file checksums.

Build the same packages locally on Windows:

```powershell
./scripts/Build-Release.ps1 -Version 2.0.1 -OutputDirectory artifacts/release-2.0.1
```

Output directories must not already exist. The script restores the pinned local `vpk` tool, builds and verifies the standalone SDK samples, and packages the setup executable. It fails the build if installer packaging fails. `Build-DeveloperKit.ps1` also accepts `-SelfContainedApp`, `-InstallerSupport`, and `-Version` for custom packaging. Release builds retain SDK assembly identity `2.0.0.0` for extension compatibility while using the requested package/file version.

The release app includes Velopack startup lifecycle support (`InstallerEnabled=true`) so setup can install it even with update checks disabled. The stable installer package ID is `ProtoBench.Desktop`, keeping installed binaries under `%LOCALAPPDATA%/ProtoBench.Desktop` separate from user settings, plugins and logs under `%LOCALAPPDATA%/ProtoBench`. The portable ZIP is the ordinary publish folder: extract it and run `ProtoBench.exe`, with no installation step. Both distributions include .NET and Windows App SDK runtime files and use the same user-data location. Installer signing is not configured; installers produced by this workflow are unsigned. Creating the installer does not install it on the build machine.

Release builds check GitHub Releases once at startup and show an update notice, with manual checks also available in Settings. Installed copies can download and restart to update; portable copies link to the latest stable download page. Ordinary development builds keep updates disabled. See [UPDATES.md](UPDATES.md) for the update flow and release assets.

## Uninstall and remove local data

Velopack setup registers ProtoBench in Windows **Settings → Apps → Installed apps**. Uninstalling removes installed binaries and shortcuts; local settings, plugins, custom definitions and logs remain in `%LOCALAPPDATA%/ProtoBench`.

For a clean removal, first open ProtoBench **Settings → Remove local data → Remove all local data…**. The confirmation names the folder and explains that settings, favorites, recent-file history, plugins, custom definitions and logs will be permanently deleted. Confirming closes the app and stops running work. A separate cleanup process waits for the app to exit so loaded plugin DLLs can be removed, then reports success or failure. Other ProtoBench instances must be closed. Directory links are removed without following their targets; saved files outside the local data folder are kept.

The app remains installed after clearing its data. Uninstall it through Windows Settings, or reopen it with default settings. Portable copies use the same removal action; delete their extracted application folder to remove the app itself. The uninstaller does not offer its own remove-data prompt.

The optional >2 GiB integration test is excluded from normal CI. To run it explicitly, set `PROTOBENCH_LARGE_LOG_TEST=1` and run the application tests on a machine with adequate disk space. Normal streaming and bounded-memory regression tests still run in CI.
