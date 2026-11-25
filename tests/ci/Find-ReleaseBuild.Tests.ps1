# Offline regression checks for the release gate. No network, build, or publication.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$finder = Join-Path $PSScriptRoot '../../scripts/Find-ReleaseBuild.ps1'
$commit = 'a' * 40
$otherCommit = 'b' * 40
$version = '2.0.24'
$prefix = "ProtoBench-$version-42"
$success = @{ id = 123; run_number = 42; head_sha = $commit; head_branch = 'main'; event = 'push'; status = 'completed'; conclusion = 'success' }
$packages = @('portable', 'installer', 'updates', 'sdk') | ForEach-Object {
    @{ name = "$prefix-$_"; expired = $false; size_in_bytes = 100 }
}

# Shadow gh only inside this test script; the production script still calls the CLI.
function gh([string]$Command, [string]$Endpoint) {
    if ($Command -ne 'api') { throw 'Unexpected GitHub command.' }
    $global:ReleaseBuildTest.Calls++
    $global:LASTEXITCODE = 0
    if ($Endpoint -like '*/artifacts?*') {
        return (@{ artifacts = @($global:ReleaseBuildTest.Packages) } | ConvertTo-Json -Depth 8)
    }
    if ($Endpoint -notlike '*head_sha=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa*' -or $Endpoint -notlike '*event=push&branch=main*') {
        throw 'Workflow lookup must request the exact commit and main push event.'
    }
    $index = [Math]::Min($global:ReleaseBuildTest.Poll++, $global:ReleaseBuildTest.Responses.Count - 1)
    return ($global:ReleaseBuildTest.Responses[$index] | ConvertTo-Json -Depth 8)
}

function Runs([object[]]$Items = @()) { return @{ workflow_runs = @($Items) } }
function Copy-Run([hashtable]$Changes) {
    $copy = $success.Clone()
    foreach ($key in $Changes.Keys) { $copy[$key] = $Changes[$key] }
    return $copy
}

function Assert-Case([string]$Name, [object[]]$Responses, [object[]]$Artifacts, [string]$ErrorText = '', [int]$Timeout = 0) {
    $global:ReleaseBuildTest = @{ Responses = $Responses; Packages = $Artifacts; Poll = 0; Calls = 0 }
    $caught = $null
    $result = $null
    try { $result = & $finder -Repository 'owner/repo' -Commit $commit -Version $version -TimeoutSeconds $Timeout -PollSeconds 0 }
    catch { $caught = $_ }
    if ($ErrorText) {
        if ($null -eq $caught -or $caught.Exception.Message -notlike "*$ErrorText*") {
            throw "$Name failed: expected '$ErrorText', got '$caught'."
        }
    }
    elseif ($null -ne $caught -or $result.Id -ne 123 -or $result.ArtifactName -cne $prefix) {
        throw "$Name failed: unexpected result or error '$caught'."
    }
    Write-Host "Passed: $Name"
}

try {
    Assert-Case 'Reuse a completed exact-commit main build' @((Runs @($success))) $packages
    Assert-Case 'No matching build never starts a new build' @((Runs)) $packages 'Timed out'
    Assert-Case 'Other commits cannot supply release packages' @((Runs @((Copy-Run @{ head_sha = $otherCommit })))) $packages 'Timed out'
    Assert-Case 'Pull request builds cannot supply release packages' @((Runs @((Copy-Run @{ event = 'pull_request' })))) $packages 'Timed out'
    Assert-Case 'Non-main builds cannot supply release packages' @((Runs @((Copy-Run @{ head_branch = 'feature' })))) $packages 'Timed out'
    Assert-Case 'Failed builds stop publication' @((Runs @((Copy-Run @{ conclusion = 'failure' })))) $packages "finished with 'failure'"
    Assert-Case 'Cancelled builds stop publication' @((Runs @((Copy-Run @{ conclusion = 'cancelled' })))) $packages "finished with 'cancelled'"
    Assert-Case 'All four package artifacts are required' @((Runs @($success))) @($packages | Select-Object -First 3) 'no usable'
    Assert-Case 'Expired artifacts stop publication' @((Runs @($success))) @($packages | ForEach-Object { $p = $_.Clone(); $p.expired = $true; $p }) 'no usable'
    Assert-Case 'Empty artifacts stop publication' @((Runs @($success))) @($packages | ForEach-Object { $p = $_.Clone(); $p.size_in_bytes = 0; $p }) 'no usable'
    Assert-Case 'Different package versions stop publication' @((Runs @($success))) @($packages | ForEach-Object { $p = $_.Clone(); $p.name = $p.name.Replace($version, '9.9.9'); $p }) 'no usable'
    $queued = Copy-Run @{ status = 'in_progress'; conclusion = $null }
    Assert-Case 'Wait for a main build when the tag arrives first' @((Runs), (Runs @($queued)), (Runs @($success))) $packages -Timeout 5
    if ($global:ReleaseBuildTest.Poll -ne 3) { throw 'Expected to wait for the matching build to finish.' }
    $newer = Copy-Run @{ id = 124; conclusion = 'failure' }
    Assert-Case 'Do not bypass a newer failed run' @((Runs @($success, $newer))) $packages "finished with 'failure'"
    Write-Host 'All 13 release build-selection checks passed.'
}
finally {
    Remove-Variable -Name ReleaseBuildTest -Scope Global -ErrorAction SilentlyContinue
}
