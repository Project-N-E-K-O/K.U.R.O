# run_export.ps1 — Export CSV data via Godot headless
# Location: scripts/tools/run_export.ps1
# Usage: powershell -ExecutionPolicy Bypass -File scripts\tools\run_export.ps1

# ── Path config ─────────────────────────────────────────────────────────────
$GodotPath   = "C:\Users\STG-PC-TC\Desktop\KURO_Project\godot-editor-windows-mono\godot-4.2-4.5.1-stable-mono.exe"
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$ProjectFile = Join-Path $ProjectRoot "project.godot"
$ExportScript = "res://scripts/tools/ExportCsv.gd"
$CsvFiles = @("items.csv", "skills.csv", "loot.csv", "characters.csv", "hotel_room_catalog.csv")

# ── Pre-checks ──────────────────────────────────────────────────────────────
if (-not (Test-Path $GodotPath)) {
    Write-Error "Godot not found: $GodotPath"
    exit 1
}
if (-not (Test-Path $ProjectFile)) {
    Write-Error "project.godot not found: $ProjectFile"
    exit 1
}

Write-Host "=== ExportCsv: Starting ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"

# ── Step 1: Remove csv-data-importer from project.godot temporarily ─────────
# Prevents file handle conflicts in headless mode
Write-Host ""
Write-Host "[Step 1] Suspending csv-data-importer plugin..." -ForegroundColor Yellow

$ProjectContent  = Get-Content $ProjectFile -Raw -Encoding UTF8
$OriginalContent = $ProjectContent

$Modified = $ProjectContent -replace '"res://addons/csv-data-importer/plugin\.cfg",?\s*', ''
$Modified = $Modified -replace 'PackedStringArray\(\s*,\s*', 'PackedStringArray('
$Modified = $Modified -replace ',\s*\)', ')'

if ($Modified -ne $ProjectContent) {
    [System.IO.File]::WriteAllText($ProjectFile, $Modified, [System.Text.Encoding]::UTF8)
    Write-Host "  OK: Plugin suspended"
} else {
    Write-Host "  Skip: Plugin entry not found (may already be disabled)"
    $OriginalContent = $null
}

# ── Step 2: Clear temporary files from earlier exports ───────────────────────
Write-Host ""
Write-Host "[Step 2] Cleaning temporary CSV files..." -ForegroundColor Yellow

$DataDir  = Join-Path $ProjectRoot "data"

foreach ($csv in $CsvFiles) {
    $tmpFp = Join-Path $DataDir "$csv.tmp"
    if (Test-Path -LiteralPath $tmpFp) { Remove-Item -LiteralPath $tmpFp -Force -ErrorAction SilentlyContinue }
}

# ── Step 3: Run Godot headless ───────────────────────────────────────────────
Write-Host ""
Write-Host "[Step 3] Running Godot headless export..." -ForegroundColor Yellow

$GodotArgs = @(
    "--path", $ProjectRoot,
    "--headless",
    "--quit",
    "-s", $ExportScript
)

$TempLog = [System.IO.Path]::GetTempFileName()
$TempErr = [System.IO.Path]::GetTempFileName()

# Noise patterns that Godot headless always emits on exit — not real errors
$GodotNoise = "^(WARNING: ObjectDB|ERROR: \d+ resources still in use|\s+at: ObjectDB|\s+at: ResourceCache|\s+at: VariantUtilityFunctions)"

$Process = Start-Process -FilePath $GodotPath `
                         -ArgumentList $GodotArgs `
                         -Wait -PassThru `
                         -NoNewWindow `
                         -RedirectStandardOutput $TempLog `
                         -RedirectStandardError  $TempErr

# Print Godot stdout (our script prints)
Get-Content $TempLog | ForEach-Object { Write-Host "  $_" }

# Print Godot stderr — but filter out the known headless noise
$RealErrors = Get-Content $TempErr | Where-Object { $_ -notmatch $GodotNoise }
if ($RealErrors) {
    $RealErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
}

Remove-Item $TempLog, $TempErr -ErrorAction SilentlyContinue

$ExitCode = $Process.ExitCode
Write-Host "  Godot exit code: $ExitCode"

if ($ExitCode -ne 0) {
    Write-Warning "  Non-zero exit code from Godot. Check output above."
}

# ── Step 3.5: Rename .tmp → .csv (Godot writes .tmp to avoid file-lock issues) ─
Write-Host ""
Write-Host "[Step 3.5] Committing .tmp files..." -ForegroundColor Yellow

$Committed = @()
foreach ($csv in $CsvFiles) {
    if ($ExitCode -ne 0) { break }
    $fp    = Join-Path $DataDir $csv
    $tmpFp = "$fp.tmp"
    if (!(Test-Path -LiteralPath $tmpFp)) { continue }
    try {
        Move-Item -LiteralPath $tmpFp -Destination $fp -Force -ErrorAction Stop
        $Committed += $csv
        Write-Host "  OK: $csv"
    } catch {
        Write-Host "  ERROR: Could not replace $csv" -ForegroundColor Red
    }
}

# ── Step 4: Restore project.godot ───────────────────────────────────────────
Write-Host ""
Write-Host "[Step 4] Restoring plugin config..." -ForegroundColor Yellow

if ($OriginalContent -ne $null) {
    [System.IO.File]::WriteAllText($ProjectFile, $OriginalContent, [System.Text.Encoding]::UTF8)
    Write-Host "  OK: Restored"
} else {
    Write-Host "  Skip: Nothing to restore"
}

# ── Step 5: Verify output files ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Step 5] Checking output files..." -ForegroundColor Yellow

$AllOk = $true
foreach ($csv in $CsvFiles) {
    $fp = Join-Path $DataDir $csv
    if ($csv -in $Committed) {
        $size  = (Get-Item $fp).Length
        $lines = (Get-Content $fp | Measure-Object -Line).Lines
        Write-Host ("  OK: {0,-14} {1,6} bytes  {2} rows" -f $csv, $size, $lines) -ForegroundColor Green
    } else {
        Write-Host "  NOT UPDATED: $csv" -ForegroundColor Red
        $AllOk = $false
    }
}

Write-Host ""
if ($AllOk) {
    Write-Host "=== Export complete ===" -ForegroundColor Green
} else {
    Write-Host "=== Some files missing. Check Godot output above. ===" -ForegroundColor Red
    exit 1
}
