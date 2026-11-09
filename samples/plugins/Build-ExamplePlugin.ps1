[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ExamplePlugin/ExamplePlugin.csproj'
$output = Join-Path $repository ('artifacts/example-plugin-' + [guid]::NewGuid().ToString('N'))
& dotnet build $project -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw 'Sample plugin build failed.' }
$manifest = Get-Content -LiteralPath (Join-Path $output 'plugin.json') -Raw | ConvertFrom-Json
$package = Join-Path $repository 'artifacts/example-toolkit.nstplugin'
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Create($package)
try {
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in @('plugin.json') + @($manifest.files)) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $output $relative), $relative) | Out-Null
        }
    } finally { $archive.Dispose() }
} finally { $stream.Dispose() }
Write-Output "Import in Settings > Plugins & custom tools: $package"
