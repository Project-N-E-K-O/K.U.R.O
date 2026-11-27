# run_export_local.ps1 - ExportCsv task entry for THIS machine (invoked by .vscode/tasks.json ExportCsv task)
# Equivalent to run_export.ps1, but with the local Godot path.
# Kept as a script file because inline PowerShell in tasks.json broke on nested quote parsing
# (inner single quotes '\.tmp$' collided with the outer -Command quoting).
# NOTE: comments are ASCII-only on purpose - Windows PowerShell 5.1 reads BOM-less UTF-8 .ps1 as ANSI.

$GodotPath   = "D:\Godot\godot-editor-windows-mono\godot-4.2-4.5.1-stable-mono.exe"
$ScriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = (Resolve-Path (Join-Path $ScriptDir "..\..")).Path
$DataDir     = Join-Path $ProjectRoot "data"

if (-not (Test-Path $GodotPath)) {
    Write-Error "Godot not found: $GodotPath"
    exit 1
}

Write-Host "=== ExportCsv: Starting ===" -ForegroundColor Cyan
Write-Host "Project: $ProjectRoot"

# Run Godot headless; ExportCsv.gd writes data/*.csv.tmp, renamed to .csv after the process exits
# (the .tmp indirection avoids file-lock conflicts when the editor holds the CSVs).
$Process = Start-Process -FilePath $GodotPath `
              -ArgumentList @("--headless", "--quit", "--path", $ProjectRoot, "-s", "res://scripts/tools/ExportCsv.gd") `
              -Wait -NoNewWindow -PassThru
if ($Process.ExitCode -ne 0) { exit $Process.ExitCode }

Get-ChildItem "$DataDir\*.csv.tmp" | ForEach-Object {
    Move-Item $_.FullName ($_.FullName -replace '\.tmp$','') -Force
}

Write-Host "=== ExportCsv: Done ===" -ForegroundColor Green
