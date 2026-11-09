<#
.SYNOPSIS
Builds a plugin project and packages only the files declared in its plugin.json.
.EXAMPLE
./tools/Build-PluginPackage.ps1 -ProjectPath ./samples/ExamplePlugin/ExamplePlugin.csproj
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../packages'),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $destination -Force
$buildRoot = Join-Path $destination ('.plugin-build-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $buildRoot
$temporaryPackage = Join-Path $destination ('.plugin-package-' + [guid]::NewGuid().ToString('N') + '.zip')
try {
    & dotnet build $project -c $Configuration -p:Platform=x64 -o $buildRoot --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
    $manifestPath = Join-Path $buildRoot 'plugin.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Build output needs plugin.json.' }
    if ((Get-Item -LiteralPath $manifestPath).Length -gt 65536) { throw 'Manifest exceeds 64 KiB.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.apiVersion -ne 2) { throw 'Unsupported manifest/API version.' }
    if ($manifest.id -cnotmatch '^[a-z0-9][a-z0-9.-]{0,79}$' -or $manifest.id.EndsWith('.') -or
        $manifest.version -notmatch '^\d+\.\d+(?:\.\d+){0,2}$') { throw 'Invalid plugin ID or numeric version.' }
    $files = @($manifest.files)
    if ($files.Count -lt 1 -or $files.Count -gt 512 -or $files -cnotcontains $manifest.entryAssembly -or
        -not $manifest.entryAssembly.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid Files or EntryAssembly.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [long]$total = 0
    foreach ($relative in @('plugin.json') + $files) {
        if ($relative -isnot [string] -or [string]::IsNullOrWhiteSpace($relative) -or $relative.Length -gt 240 -or
            $relative.Contains('\')) { throw 'Invalid relative package path.' }
        foreach ($part in $relative.Split('/')) {
            if ([string]::IsNullOrWhiteSpace($part) -or $part.StartsWith('.') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or
                $part -match '[\x00-\x1f<>:"|?*]' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)') {
                throw "Unsafe package path: $relative"
            }
        }
        if (-not $seen.Add($relative)) { throw "Duplicate/reserved package path: $relative" }
        $file = Join-Path $buildRoot $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing declared file: $relative" }
        $total += (Get-Item -LiteralPath $file).Length
        if ($total -gt 128MB) { throw 'Expanded plugin exceeds 128 MiB.' }
    }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Create($temporaryPackage)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            foreach ($relative in @('plugin.json') + $files) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $buildRoot $relative), $relative) | Out-Null
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    if ((Get-Item -LiteralPath $temporaryPackage).Length -gt 128MB) { throw 'Compressed plugin exceeds 128 MiB.' }
    $package = Join-Path $destination ($manifest.id + '-' + $manifest.version + '.nstplugin')
    Move-Item -LiteralPath $temporaryPackage -Destination $package -Force
    Write-Host "Plugin package: $package"
    Write-Output $package
}
finally {
    # Remove only the uniquely named scratch paths created by this invocation.
    $resolvedBuild = [IO.Path]::GetFullPath($buildRoot)
    if ([IO.Path]::GetDirectoryName($resolvedBuild) -eq $destination.TrimEnd('\', '/') -and
        [IO.Path]::GetFileName($resolvedBuild) -match '^\.plugin-build-[a-f0-9]{32}$') {
        if (Test-Path -LiteralPath $resolvedBuild) { Remove-Item -LiteralPath $resolvedBuild -Recurse -Force }
    }
    if (Test-Path -LiteralPath $temporaryPackage) { Remove-Item -LiteralPath $temporaryPackage -Force }
}
