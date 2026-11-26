<#
.SYNOPSIS
Runs the actual SDK HTML sample against the native WinUI/WebView2 host (requires a Windows desktop session).
#>
[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repository 'tests/ProtoBench.WebViewSmoke/ProtoBench.WebViewSmoke.csproj'
& dotnet build $project -c $Configuration -p:Platform=x64 -m:1 --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Native WebView smoke test build failed.' }
$output = Join-Path $repository 'artifacts/webview-smoke'
$null = New-Item -ItemType Directory -Path $output -Force
$report = Join-Path $output ('report-' + [guid]::NewGuid().ToString('N') + '.json')
$executable = Join-Path $repository "tests/ProtoBench.WebViewSmoke/bin/x64/$Configuration/net10.0-windows10.0.26100.0/win-x64/ProtoBench.WebViewSmoke.exe"
$process = Start-Process -FilePath $executable -ArgumentList ('"' + $report + '"') -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(45000)) {
    Stop-Process -Id $process.Id
    throw 'Native WebView smoke test exceeded 45 seconds.'
}
if (-not (Test-Path -LiteralPath $report)) { throw "Native WebView smoke test exited without a report (exit $($process.ExitCode))." }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
$result.observations | Write-Output
if ($process.ExitCode -ne 0 -or -not $result.success) { throw "Native WebView smoke test failed: $($result.error)" }
Write-Output "Native WebView smoke test passed. Report and screenshot: $report"
