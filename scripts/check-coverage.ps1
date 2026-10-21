<#
.SYNOPSIS
Fails when line coverage in a Cobertura report is below the agreed thresholds.
.DESCRIPTION
Thresholds are per assembly plus a total. Generated EF migrations are excluded when the report is produced
(backend/coverlet.runsettings), so they are not counted here. Raise thresholds over time, never lower them.
#>
param(
    [Parameter(Mandatory)][string]$CoberturaFile,
    [hashtable]$Thresholds = @{ "G54.Api" = 25; "G54.BLL" = 75; "G54.DAL" = 78; "Total" = 73 }
)

[xml]$report = Get-Content -LiteralPath $CoberturaFile -Raw
$totalLines = 0
$totalCovered = 0
$failed = $false
Write-Host ("{0,-12} {1,8} {2,8} {3,8} {4,10}" -f "Assembly", "Lines", "Covered", "Percent", "Threshold")
foreach ($package in $report.coverage.packages.package) {
    # Class-level lines only: methods repeat the same lines inside <methods>, which would count them twice.
    $lines = @($package.SelectNodes("classes/class/lines/line"))
    $covered = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
    $totalLines += $lines.Count
    $totalCovered += $covered
    $percent = if ($lines.Count -gt 0) { [math]::Round(100.0 * $covered / $lines.Count, 1) } else { 100.0 }
    $threshold = $Thresholds[$package.name]
    $verdict = ""
    if ($null -ne $threshold -and $percent -lt $threshold) { $failed = $true; $verdict = "  FAIL" }
    Write-Host ("{0,-12} {1,8} {2,8} {3,7}% {4,9}%{5}" -f $package.name, $lines.Count, $covered, $percent, $threshold, $verdict)
}

$totalPercent = if ($totalLines -gt 0) { [math]::Round(100.0 * $totalCovered / $totalLines, 1) } else { 0 }
$totalVerdict = ""
if ($totalPercent -lt $Thresholds["Total"]) { $failed = $true; $totalVerdict = "  FAIL" }
Write-Host ("{0,-12} {1,8} {2,8} {3,7}% {4,9}%{5}" -f "Total", $totalLines, $totalCovered, $totalPercent, $Thresholds["Total"], $totalVerdict)

if ($failed) { exit 1 }
exit 0
