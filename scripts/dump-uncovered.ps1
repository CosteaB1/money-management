# Dumps uncovered hand-written lines (with source text) from the merged Cobertura report.
# Usage: powershell -NoProfile -File scripts/dump-uncovered.ps1 [filter-substring]
param([string]$Filter = "")

$ErrorActionPreference = "Stop"
[xml]$x = Get-Content "$PSScriptRoot/../CoverageMerged/Cobertura.xml"
$byfile = @{}
foreach ($cls in $x.SelectNodes("//class")) {
  $fn = $cls.filename
  foreach ($ln in $cls.SelectNodes(".//line")) {
    if ($ln.hits -eq "0") {
      if (-not $byfile.ContainsKey($fn)) { $byfile[$fn] = New-Object System.Collections.Generic.HashSet[int] }
      [void]$byfile[$fn].Add([int]$ln.number)
    }
  }
}
$total = 0
foreach ($fn in ($byfile.Keys | Sort-Object)) {
  if ($Filter -and ($fn -notlike "*$Filter*")) { continue }
  if ($fn -like "*\obj\*") { continue }
  $nums = $byfile[$fn] | Sort-Object
  $total += $nums.Count
  "===== $fn ($($nums.Count)) ====="
  if (Test-Path -LiteralPath $fn) {
    $lines = Get-Content -LiteralPath $fn
    foreach ($n in $nums) { "{0,4}: {1}" -f $n, $lines[$n-1] }
  } else {
    $nums -join ", "
  }
}
"TOTAL (filtered): $total"
