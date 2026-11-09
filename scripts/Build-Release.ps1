<# Builds matching self-contained Windows x64 app, SDK and complete developer-kit release archives. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
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
    # The kit builder requires a nonexistent output directory.
    $kit = Join-Path $parent ('.release-kit-' + [guid]::NewGuid().ToString('N'))
    $temporaryRoots += $kit
    $null = & (Join-Path $PSScriptRoot 'Build-DeveloperKit.ps1') -OutputDirectory $kit -Configuration Release -SelfContainedApp -Version $Version -NoZip
    $sdk = New-Staging 'sdk'; $temporaryRoots += $sdk
    $output = New-Staging 'output'; $temporaryRoots += $output
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
    Write-Checksums $output
    [IO.Directory]::Move($output, $destination)
    Write-Host "Release archives: $destination"
    Get-ChildItem -LiteralPath $destination -File | Select-Object Name, Length
}
finally {
    foreach ($temporaryRoot in $temporaryRoots) {
        $resolved = [IO.Path]::GetFullPath($temporaryRoot)
        if ([IO.Path]::GetDirectoryName($resolved) -eq $parent -and [IO.Path]::GetFileName($resolved) -match '^\.release-(kit|sdk|output)-[a-f0-9]{32}$') {
            if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
        }
    }
}
