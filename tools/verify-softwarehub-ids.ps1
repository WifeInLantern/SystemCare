# Verifies every winget package Id in SoftwareHubCatalog.cs actually resolves on the
# winget source, so the Software Hub's one-click installs won't fail on a bad Id.
$ErrorActionPreference = 'Stop'

$catalog = Join-Path $PSScriptRoot '..\src\SystemCare\Services\SoftwareHubCatalog.cs'
if (-not (Test-Path $catalog)) { Write-Host "Cannot find $catalog" -ForegroundColor Red; exit 1 }

$text = Get-Content -Raw $catalog
$ids  = [regex]::Matches($text, 'Id = "([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } |
        Select-Object -Unique

Write-Host ""
Write-Host "Verifying $($ids.Count) winget IDs from SoftwareHubCatalog.cs ..." -ForegroundColor Cyan
Write-Host "(each one runs 'winget show' - this takes a couple of minutes)" -ForegroundColor DarkGray
Write-Host ""

$missing = @()
foreach ($id in $ids) {
    winget show --id $id --exact --source winget --disable-interactivity --accept-source-agreements > $null 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host ("  OK        {0}" -f $id) -ForegroundColor Green
    } else {
        Write-Host ("  MISSING   {0}" -f $id) -ForegroundColor Red
        $missing += $id
    }
}

Write-Host ""
if ($missing.Count -eq 0) {
    Write-Host "All $($ids.Count) IDs resolve on winget - catalog is good." -ForegroundColor Green
} else {
    Write-Host "$($missing.Count) ID(s) did NOT resolve and need fixing:" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "    $_" -ForegroundColor Yellow }
}
Write-Host ""
