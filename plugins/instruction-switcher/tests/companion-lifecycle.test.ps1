param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot "..\companion\InstructionSwitcherCompanion.exe")
)

$ErrorActionPreference = "Stop"

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) {
        throw "$Label expected '$Expected', got '$Actual'"
    }
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $resolvedAssembly)) {
    throw "Companion assembly was not found: $resolvedAssembly"
}

$assembly = [Reflection.Assembly]::LoadFile($resolvedAssembly)
$lifecycle = $assembly.GetType("InstructionSwitcherCompanion.CodexLifecycle", $true)
$arguments = $assembly.GetType("InstructionSwitcherCompanion.CompanionArguments", $true)

$isOpenWindow = $lifecycle.GetMethod("IsOpenWindow", [Reflection.BindingFlags]"Public,Static")
$shouldExit = $lifecycle.GetMethod("ShouldExit", [Reflection.BindingFlags]"Public,Static")
$isPrimary = $lifecycle.GetMethod("IsPrimaryProcessCommandLine", [Reflection.BindingFlags]"Public,Static")
$shouldRestartFocusTracker = $lifecycle.GetMethod("ShouldRestartFocusTracker", [Reflection.BindingFlags]"Public,Static")
$resolveRuntimeRoot = $arguments.GetMethod("ResolveRuntimeRoot", [Reflection.BindingFlags]"Public,Static")
$orderType = $assembly.GetType("InstructionSwitcherCompanion.EnabledInstructionOrder", $true)
$moveOrder = $orderType.GetMethod("Move", [Reflection.BindingFlags]"Public,Static")

function Assert-Sequence($Expected, $Actual, [string]$Label) {
    $expectedText = [String]::Join("|", [string[]]$Expected)
    $actualText = [String]::Join("|", [string[]]$Actual)
    if ($expectedText -ne $actualText) {
        throw "$Label expected '$expectedText', got '$actualText'"
    }
}

function Move-Order([string[]]$Order, [string]$Id, [int]$TargetIndex) {
    $arguments = [object[]]::new(3)
    $arguments[0] = $Order
    $arguments[1] = $Id
    $arguments[2] = $TargetIndex
    return [string[]]$moveOrder.Invoke($null, $arguments)
}

Assert-Equal $false ($isOpenWindow.Invoke($null, [object[]]@([IntPtr]::Zero, $true, $false))) "background process"
Assert-Equal $false ($isOpenWindow.Invoke($null, [object[]]@([IntPtr]1, $false, $false))) "hidden window"
Assert-Equal $true ($isOpenWindow.Invoke($null, [object[]]@([IntPtr]1, $true, $false))) "visible window"
Assert-Equal $true ($isOpenWindow.Invoke($null, [object[]]@([IntPtr]1, $false, $true))) "minimized window"

$lastSeen = [DateTime]::SpecifyKind([DateTime]"2026-08-10T00:00:00", [DateTimeKind]::Utc)
Assert-Equal $false ($shouldExit.Invoke($null, [object[]]@($lastSeen, $lastSeen.AddMilliseconds(14999)))) "exit grace"
Assert-Equal $true ($shouldExit.Invoke($null, [object[]]@($lastSeen, $lastSeen.AddSeconds(15)))) "exit threshold"

$mainCommand = '"C:\Program Files\OpenAI\Codex\ChatGPT.exe" --remote-debugging-port=54743'
$rendererCommand = '"C:\Program Files\OpenAI\Codex\ChatGPT.exe" --type=renderer --remote-debugging-port=54743'
Assert-Equal $true ($isPrimary.Invoke($null, [object[]]@($mainCommand))) "main process command"
Assert-Equal $false ($isPrimary.Invoke($null, [object[]]@($rendererCommand))) "renderer command"

Assert-Equal $false ($shouldRestartFocusTracker.Invoke($null, [object[]]@($true, 54743, 54743))) "focus tracker keeps matching port"
Assert-Equal $true ($shouldRestartFocusTracker.Invoke($null, [object[]]@($true, 54743, 59067))) "focus tracker follows changed port"
Assert-Equal $true ($shouldRestartFocusTracker.Invoke($null, [object[]]@($false, 0, 59067))) "focus tracker starts when missing"
Assert-Equal $false ($shouldRestartFocusTracker.Invoke($null, [object[]]@($true, 54743, 0))) "focus tracker survives temporary discovery failure"

Assert-Sequence @("tdd", "review", "concise") (Move-Order @("review", "tdd", "concise") "tdd" 0) "drag to front"
Assert-Sequence @("tdd", "concise", "review") (Move-Order @("review", "tdd", "concise") "review" 2) "drag to end"
Assert-Sequence @("review", "tdd") (Move-Order @("review", "tdd") "missing" 0) "unknown drag source"

$legacyRoot = Join-Path ([IO.Path]::GetTempPath()) "instruction-switcher-legacy-runtime"
$resolveArguments = [object[]]::new(1)
$resolveArguments[0] = [string[]]@("--watch", $legacyRoot)
Assert-Equal ([IO.Path]::GetFullPath($legacyRoot)) ($resolveRuntimeRoot.Invoke($null, $resolveArguments)) "legacy watch argument"

$launcher = Join-Path $PSScriptRoot "..\companion\companion-start.ps1"
if ((Get-Content -Raw -LiteralPath $launcher).Contains("--watch")) {
    throw "companion-start.ps1 still requests permanent watch mode"
}

Write-Output "Companion lifecycle checks passed."
