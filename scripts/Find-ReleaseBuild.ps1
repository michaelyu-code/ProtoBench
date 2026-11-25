<# Finds existing verified packages for a tagged commit; never starts another build. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$Commit,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [ValidateRange(0, 3600)][int]$TimeoutSeconds = 3000,
    [ValidateRange(0, 60)][int]$PollSeconds = 15
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-GitHubJson([string]$Endpoint) {
    $json = & gh api $Endpoint
    if ($LASTEXITCODE -ne 0) { throw "GitHub API request failed: $Endpoint" }
    return ($json | ConvertFrom-Json)
}

$timer = [Diagnostics.Stopwatch]::StartNew()
while ($true) {
    $response = Get-GitHubJson "repos/$Repository/actions/workflows/build-and-release.yml/runs?event=push&branch=main&head_sha=$Commit&per_page=100"
    $run = @($response.workflow_runs | Where-Object {
        $_.head_sha -ieq $Commit -and $_.head_branch -ceq 'main' -and $_.event -ceq 'push'
    } | Sort-Object id -Descending | Select-Object -First 1)
    if ($run.Count -gt 0 -and $run[0].status -eq 'completed') {
        $build = $run[0]
        if ($build.conclusion -ne 'success') {
            throw "Main build $($build.id) finished with '$($build.conclusion)'. Rerun that build successfully, then rerun this release."
        }
        $prefix = "ProtoBench-$Version-$($build.run_number)"
        $artifacts = (Get-GitHubJson "repos/$Repository/actions/runs/$($build.id)/artifacts?per_page=100").artifacts
        foreach ($kind in @('portable', 'installer', 'updates', 'sdk')) {
            $name = "$prefix-$kind"
            $matching = @($artifacts | Where-Object { $_.name -ceq $name -and !$_.expired -and $_.size_in_bytes -gt 0 })
            if ($matching.Count -ne 1) {
                throw "Main build $($build.id) has no usable '$name' artifact. Packages must match the tag version and are retained for 14 days. Rerun the main build, then rerun this release."
            }
        }
        Write-Host "Reusing verified packages from main build $($build.id) for commit $Commit."
        return [pscustomobject]@{ Id = $build.id; ArtifactName = $prefix }
    }
    if ($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
        throw "Timed out waiting for a successful main push build of $Commit. Push the tagged commit to main and rerun this release once its build passes."
    }
    Write-Host "Waiting for the main push build of $Commit; no additional build will be started."
    Start-Sleep -Seconds $PollSeconds
}
