<# Builds matching Windows x64 installer, portable app, SDK and developer-kit releases. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release'),
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$GitHubRepository = 'michaelyu-code/ProtoBench'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'Build releases on Windows (WinUI/x64).' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$parent = [IO.Path]::GetDirectoryName($destination)
if (!$parent -or (Test-Path -LiteralPath $destination)) { throw 'Choose a new child output directory; existing releases are never overwritten.' }
$null = New-Item -ItemType Directory -Path $parent -Force
$temporaryRoots = @()
function New-Staging([string]$Kind) {
    $path = Join-Path $parent ('.release-' + $Kind + '-' + [guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $path
    return $path
}
function Write-Checksums([string]$Directory) {
    $lines = Get-ChildItem -LiteralPath $Directory -File -Recurse | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object FullName | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/')
    }
    [IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS.txt'), ($lines -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
}
try {
    Push-Location -LiteralPath $repository
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Could not restore the pinned Velopack packaging tool.' }
    }
    finally { Pop-Location }
    # The kit builder requires a nonexistent output directory.
    $kit = Join-Path $parent ('.release-kit-' + [guid]::NewGuid().ToString('N'))
    $temporaryRoots += $kit
    $null = & (Join-Path $PSScriptRoot 'Build-DeveloperKit.ps1') -OutputDirectory $kit -Configuration Release -SelfContainedApp -InstallerSupport -EnableUpdates -GitHubRepository $GitHubRepository -Version $Version -NoZip
    $sdk = New-Staging 'sdk'; $temporaryRoots += $sdk
    $output = New-Staging 'output'; $temporaryRoots += $output
    $installer = New-Staging 'installer'; $temporaryRoots += $installer
    foreach ($item in Get-ChildItem -LiteralPath $kit -Force) {
        if ($item.Name -notin @('app', 'SHA256SUMS.txt')) { Copy-Item -LiteralPath $item.FullName -Destination $sdk -Recurse }
    }
    $sdkVersionsPath = Join-Path $sdk 'versions.json'
    $sdkVersions = Get-Content -LiteralPath $sdkVersionsPath -Raw | ConvertFrom-Json
    $sdkVersions.appIncluded = $false; $sdkVersions.appSelfContained = $false
    [IO.File]::WriteAllText($sdkVersionsPath, ($sdkVersions | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    Write-Checksums $sdk
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $kit 'app'), (Join-Path $output "ProtoBench-$Version-win-x64.zip"))
    [IO.Compression.ZipFile]::CreateFromDirectory($sdk, (Join-Path $output "ProtoBench-SDK-$Version.zip"))
    [IO.Compression.ZipFile]::CreateFromDirectory($kit, (Join-Path $output "ProtoBench-DeveloperKit-$Version.zip"))
    Push-Location -LiteralPath $repository
    try {
        # Keep installed binaries separate from %LOCALAPPDATA%/ProtoBench user data.
        & dotnet tool run vpk -- pack --packId ProtoBench.Desktop --packTitle ProtoBench --packVersion $Version `
            --packDir (Join-Path $kit 'app') --mainExe ProtoBench.exe --runtime win-x64 `
            --outputDir $installer --icon (Join-Path $repository 'src/ProtoBench.Desktop/Assets/ProtoBench.ico') --noPortable --skip-updates --yes
        if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
    }
    finally { Pop-Location }
    $setup = @(Get-ChildItem -LiteralPath $installer -Filter '*Setup.exe' -File)
    if ($setup.Count -ne 1) { throw 'Expected exactly one Windows setup executable.' }
    Copy-Item -LiteralPath $setup[0].FullName -Destination (Join-Path $output "ProtoBench-$Version-win-x64-Setup.exe")
    $feedPath = Join-Path $installer 'releases.win.json'
    if (-not (Test-Path -LiteralPath $feedPath -PathType Leaf)) { throw 'Velopack update feed is missing.' }
    $feed = Get-Content -LiteralPath $feedPath -Raw | ConvertFrom-Json
    $packages = @(Get-ChildItem -LiteralPath $installer -Filter '*.nupkg' -File)
    if ($packages.Count -eq 0 -or @($feed.Assets).Count -eq 0) { throw 'Velopack update packages are missing.' }
    foreach ($asset in $feed.Assets) {
        if ($asset.FileName -notin $packages.Name -or $asset.PackageId -ne 'ProtoBench.Desktop' -or $asset.Version -ne $Version) {
            throw 'Velopack update feed does not match the release packages.'
        }
    }
    Copy-Item -LiteralPath $feedPath -Destination $output
    foreach ($package in $packages) { Copy-Item -LiteralPath $package.FullName -Destination $output }
    $manifest = @{ version = $Version; releaseNotes = "ProtoBench $Version. See GitHub Releases for release notes." } | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $output 'latest.json'), $manifest, [Text.UTF8Encoding]::new($false))
    Write-Checksums $output
    [IO.Directory]::Move($output, $destination)
    Write-Host "Release packages: $destination"
    Get-ChildItem -LiteralPath $destination -File | Select-Object Name, Length
}
finally {
    foreach ($temporaryRoot in $temporaryRoots) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved) -match '^\.release-(kit|sdk|output|installer)-[a-f0-9]{32}$') {
            if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
        }
    }
}
