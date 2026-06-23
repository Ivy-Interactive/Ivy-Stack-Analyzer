<#
.SYNOPSIS
    Drives the /review-stack-analyse skill over the URLs in repos.txt.

.DESCRIPTION
    Pops the first URL from repos.txt (removing it from the file) and launches
    Claude Code headlessly with the prompt "/review-stack-analyse <url>". Repeats
    until -Count reviews have been started, running up to -Parallel at a time.

    Each review runs in the repository root (so the project-scoped skill resolves)
    and its output is captured under src/.review-logs/. Any disagreements the skill
    finds are written to src/.suggestions/ (git-ignored).

.PARAMETER Parallel
    How many reviews to run concurrently. Default: 1.

.PARAMETER Count
    How many reviews to run in total. Default: 1.

.PARAMETER ReposFile
    Path to the URL list. Default: repos.txt next to this script.

.PARAMETER RequirePermissions
    By default reviews run with --dangerously-skip-permissions so the unattended
    batch can clone repos and run the reference tools. Pass this switch to run
    Claude with normal permission prompts instead.

.EXAMPLE
    ./RunReview.ps1 -Count 10 -Parallel 3
#>
[CmdletBinding()]
param(
    [int]$Parallel = 1,
    [int]$Count = 1,
    [string]$ReposFile = (Join-Path $PSScriptRoot 'repos.txt'),
    [switch]$RequirePermissions
)

$ErrorActionPreference = 'Stop'

if ($Parallel -lt 1) { throw '-Parallel must be >= 1.' }
if ($Count -lt 1) { throw '-Count must be >= 1.' }
if (-not (Test-Path -LiteralPath $ReposFile)) { throw "Repos file not found: $ReposFile" }
if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {
    throw "The 'claude' CLI was not found on PATH. Install Claude Code first."
}

$repoRoot = Split-Path -Parent $PSScriptRoot          # skill is scoped to the repo root
$logDir = Join-Path $PSScriptRoot '.review-logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Pops the first non-empty URL from the file and rewrites it without that line.
function Pop-NextUrl {
    $lines = @(Get-Content -LiteralPath $ReposFile)
    $idx = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim().Length -gt 0) { $idx = $i; break }
    }
    if ($idx -lt 0) { return $null }
    $url = $lines[$idx].Trim()
    $remaining = for ($i = 0; $i -lt $lines.Count; $i++) { if ($i -ne $idx) { $lines[$i] } }
    Set-Content -LiteralPath $ReposFile -Value $remaining
    return $url
}

function Format-LogName([string]$u) { ($u -replace '[^\w\.\-]', '_').Trim('_') }

$claudeArgs = @()
if (-not $RequirePermissions) { $claudeArgs += '--dangerously-skip-permissions' }

$running = [System.Collections.ArrayList]::new()
$dispatched = 0
$completed = 0

Write-Host "Running $Count review(s), $Parallel at a time, from $ReposFile" -ForegroundColor Cyan

while ($dispatched -lt $Count -or $running.Count -gt 0) {
    # Launch while we have spare capacity and budget remaining.
    while ($running.Count -lt $Parallel -and $dispatched -lt $Count) {
        $url = Pop-NextUrl
        if (-not $url) {
            Write-Warning "repos.txt is empty after $dispatched dispatched; stopping new launches."
            $Count = $dispatched
            break
        }
        $dispatched++
        $log = Join-Path $logDir ("{0}.log" -f (Format-LogName $url))
        $prompt = "/review-stack-analyse $url"
        Write-Host ("[{0}/{1}] start: {2}" -f $dispatched, $Count, $url) -ForegroundColor Green
        $proc = Start-Process -FilePath 'claude' `
            -ArgumentList (@('-p', $prompt) + $claudeArgs) `
            -WorkingDirectory $repoRoot `
            -RedirectStandardOutput $log `
            -RedirectStandardError "$log.err" `
            -NoNewWindow -PassThru
        [void]$running.Add([pscustomobject]@{ Proc = $proc; Url = $url; Log = $log })
    }

    if ($running.Count -eq 0) { break }

    Start-Sleep -Seconds 2

    $finished = @($running | Where-Object { $_.Proc.HasExited })
    foreach ($f in $finished) {
        $completed++
        $status = if ($f.Proc.ExitCode -eq 0) { 'ok' } else { "exit $($f.Proc.ExitCode)" }
        Write-Host ("  done [{0}]: {1} ({2}) -> {3}" -f $status, $f.Url, $completed, $f.Log)
        [void]$running.Remove($f)
    }
}

Write-Host "Finished: $completed review(s) completed." -ForegroundColor Cyan
