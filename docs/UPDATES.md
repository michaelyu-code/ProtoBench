# GitHub Releases updates

Release builds created by `Build-Release.ps1` enable updates from the public GitHub repository `michaelyu-code/ProtoBench`. The workflow passes its own `GITHUB_REPOSITORY` so forks publish and check their own releases. No access token is embedded in the application. Ordinary development builds and standalone developer-kit builds keep updates disabled unless explicitly enabled.

## User flow

An update-enabled app checks GitHub Releases once in the background when its main window first opens. If a newer version exists, a dismissible **Update available** banner shows its version. **View update** opens Settings with the download option ready. An unavailable feed does not interrupt startup; a manual check can be retried later. Disabled development builds do not make this request.

Open **Settings → Updates → Check for updates** to check again. Downloads and restart still require explicit user actions; there are no automatic downloads or installations. Opening Settings while the startup check is running shares that check instead of starting a second request.

- **Installed app:** Velopack's `GithubSource` reads stable GitHub Releases and their `releases.win.json` assets. When a newer version exists, choose **Download update**, then **Restart to update**. Restart requires confirmation because open workspace inputs close. Downloaded updates are not automatically applied at startup.
- **Portable ZIP:** ProtoBench reads `releases/latest/download/latest.json`, compares semantic versions, and offers the existing **Open download page** action. Download and replace the portable copy manually. Equal or older versions do not offer an update. The manifest must contain a stable version.
- **Errors:** The download page remains available if checking or downloading fails.

GitHub prereleases are excluded from the installed updater. GitHub's `releases/latest` URL directs portable users to the latest stable release. There is no preview update channel. Public GitHub checks are unauthenticated and subject to GitHub's rate limits.

## Release assets

Every release build creates the installer, portable ZIP, SDK ZIP and developer-kit ZIP, plus:

- `ProtoBench.Desktop-VERSION-full.nupkg`: compiled application update package.
- `releases.win.json`: Velopack's Windows release feed, referencing that package.
- `latest.json`: small version manifest for portable copies.
- `SHA256SUMS.txt`: checksums of all published files.

The workflow retains the feed and package as a separate updates artifact and uploads all assets to the tagged GitHub Release before publishing its draft. Each release includes a complete package, so updates work without downloading or building older releases. Delta packages are not currently generated. No additional source-code hosting or update server is needed.

The package ID `ProtoBench.Desktop` must stay stable, and the app version, release tag version and Velopack package version must match. User settings and plugins under `%LOCALAPPDATA%/ProtoBench` are separate from the installed application. Builds that previously disabled update checks must be replaced once with an update-enabled build; they cannot discover updates while that feature is disabled.

## Build configuration

```powershell
./scripts/Build-Release.ps1 -Version 2.0.7 -GitHubRepository michaelyu-code/ProtoBench -OutputDirectory artifacts/release-2.0.7
```

`Build-Release.ps1` enables updates and supplies these compiled MSBuild values:

| Property | Value |
| --- | --- |
| `UpdatesEnabled` | `true` |
| `UpdateFeedUrl` | `https://github.com/michaelyu-code/ProtoBench` |
| `UpdateDownloadPageUrl` | `https://github.com/michaelyu-code/ProtoBench/releases/latest` |
| `UpdateManifestUrl` | `https://github.com/michaelyu-code/ProtoBench/releases/latest/download/latest.json` |

`Build-DeveloperKit.ps1 -EnableUpdates -GitHubRepository owner/repository` also enables checks in its test app. Without `-EnableUpdates` it explicitly compiles with updates disabled. A local user setting cannot enable or redirect updates. Enabled builds require all three HTTPS URLs. Generic HTTPS Velopack feed directories remain supported through `SimpleWebSource`; public `github.com/owner/repository` URLs select `GithubSource` with prereleases disabled.

Velopack 1.2.161 is pinned both in the app dependency and `.config/dotnet-tools.json`. Release packaging restores the local tool. The entry point runs the bootstrap before logging or WinUI initialization when installation support or updates are enabled. Installer startup support is independent of update checks.

## Verification

Automated tests cover GitHub stable-release selection and package URL resolution with simulated GitHub responses, semantic version comparisons for portable copies, invalid feeds, disabled builds, download failures, and explicit restart gating. Local release packaging verifies feed/package versions and includes all update artifacts. A live two-version install/download/restart cycle against a newly published GitHub Release still requires a release smoke test. Building packages does not install them or publish a release.
