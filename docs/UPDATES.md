# Updates (disabled by default)

`UpdatesEnabled` is a build-time feature flag, defaulting to `false`. Ordinary builds hide the Updates section in Settings, skip Velopack bootstrapping, and reject update checks, downloads, restart and download-page actions at the service boundary. There are no startup checks or background downloads, even when enabled. No live endpoints are configured or releases published by this change.

## Enable later

Build/publish with these MSBuild properties, preferably supplied through a private release `.props` file or CI arguments:

```powershell
dotnet publish src/ProtoBench.Desktop -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:Version=1.1.0 -p:UpdatesEnabled=true -p:UpdateManifestUrl=https://your-host/latest.json -p:UpdateDownloadPageUrl=https://your-host/download -p:UpdateFeedUrl=https://your-host/updates -o artifacts/publish
```

Replace all three example URLs with real HTTPS URLs. Enabled builds fail when any URL property is omitted. The service also validates HTTPS URLs before any action. The values are compiled into assembly metadata; changing a local user setting cannot enable this feature. Build without these properties (or with `-p:UpdatesEnabled=false`) to disable it again.

The manifest for ordinary folder/ZIP copies is a small JSON file (maximum 64 KiB, 30-second request timeout):

```json
{
  "version": "1.1.0",
  "releaseNotes": "Describe the changes in this release."
}
```

Use stable semantic versions. Equal or older versions do not offer an update; invalid versions and prereleases produce a check error. Release notes are displayed as plain text. The download button always opens the configured page, never an executable URL received from the manifest. Users of ordinary folder copies download and replace their application manually.

## Velopack releases

Velopack 1.2.161 is pinned as a dependency. The custom WinUI entry point runs its bootstrap before initializing the UI, only in enabled builds. Automatic application of downloaded updates on startup is disabled: users explicitly download, then select Restart to update and confirm that open workspace inputs will be closed.

Install the matching `vpk` tool in your release environment. After publishing an enabled build, package it, for example:

```powershell
vpk pack --packId ProtoBench --packVersion 1.1.0 --packDir artifacts/publish --mainExe ProtoBench.exe --outputDir artifacts/releases
```

Keep `packVersion` and the app's `Version` property identical, and keep `packId` stable across releases. Publish the generated feed and update packages at `UpdateFeedUrl`. The SDK expects a Velopack release feed there, not a GitHub release webpage. A GitHub-specific source would require a different source adapter. Publish the installer and a separate ZIP of the ordinary publish folder on the download page. Update `latest.json` only after the release artifacts are available.

Velopack-managed installations use the Velopack feed for version checks and downloads. Ordinary publish-folder copies use the JSON manifest. Both expose Open download page, including after errors. Checks and downloads happen only after the user clicks the corresponding button.

Before a real rollout, sign the installer/app, verify certificate trust on a clean machine, and test two enabled versions end to end: install v1, publish v2, check, download, cancel restart, then restart and verify v2 and preserved local settings. Also test the folder-copy download flow and unavailable feeds. No real installer upgrade or download-page launch has been exercised in this development change.
