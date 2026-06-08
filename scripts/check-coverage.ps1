# Backend LINE-coverage gate.
#
# Parses the filtered reportgenerator TextSummary (CoverageReport/Summary.txt)
# and fails with a non-zero exit code when total line coverage is below
# -Threshold. Branch coverage is intentionally ignored.
#
# Usage:
#   powershell -NoProfile -File scripts/check-coverage.ps1 [-Threshold 99] [-SummaryPath path]
#
# Prerequisite: run the tests + reportgenerator first (see COVERAGE.md "TL;DR").
param(
    [double]$Threshold = 99,
    [string]$SummaryPath = "$PSScriptRoot/../CoverageReport/Summary.txt"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SummaryPath)) {
    Write-Error "Coverage summary not found at '$SummaryPath'. Run the coverage steps in COVERAGE.md first."
    exit 2
}

$line = Select-String -LiteralPath $SummaryPath -Pattern '^\s*Line coverage:\s*([0-9.]+)%' | Select-Object -First 1
if (-not $line) {
    Write-Error "Could not find 'Line coverage:' in '$SummaryPath'."
    exit 2
}

$actual = [double]$line.Matches[0].Groups[1].Value

if ($actual -lt $Threshold) {
    Write-Host "FAIL: line coverage $actual% is below the $Threshold% threshold." -ForegroundColor Red
    exit 1
}

Write-Host "OK: line coverage $actual% meets the $Threshold% threshold." -ForegroundColor Green
exit 0
