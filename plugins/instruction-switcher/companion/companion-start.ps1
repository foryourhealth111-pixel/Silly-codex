param(
    [string]$CodexHome = "",
    [string]$SwitcherHome = ""
)

$ErrorActionPreference = "SilentlyContinue"

if ([string]::IsNullOrWhiteSpace($CodexHome)) {
    $CodexHome = Join-Path $env:USERPROFILE ".codex"
}
if ([string]::IsNullOrWhiteSpace($SwitcherHome)) {
    $SwitcherHome = Join-Path $CodexHome "instruction-switcher"
}

$cacheRoot = Join-Path $CodexHome "plugins\cache\personal\instruction-switcher"
$runtimeRoot = Join-Path $SwitcherHome "runtime"
$version = Get-ChildItem -LiteralPath $cacheRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName "companion\InstructionSwitcherCompanion.exe") } |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($version) {
    $executable = Join-Path $version.FullName "companion\InstructionSwitcherCompanion.exe"
    Start-Process -FilePath $executable -ArgumentList @($runtimeRoot) -WindowStyle Hidden
}
