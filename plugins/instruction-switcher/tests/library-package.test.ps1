param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot "..\companion\InstructionSwitcherCompanion.exe")
)

$ErrorActionPreference = "Stop"

function Assert-Equal($Expected, $Actual, [string]$Label) {
    if ($Expected -ne $Actual) {
        throw "$Label expected '$Expected', got '$Actual'"
    }
}

function Assert-True([bool]$Value, [string]$Label) {
    if (-not $Value) { throw "$Label expected true" }
}

function Invoke-Static($Method, [object[]]$Arguments) {
    $nativeArguments = [object[]]::new($Arguments.Length)
    for ($index = 0; $index -lt $Arguments.Length; $index++) {
        $nativeArguments[$index] = if ($null -eq $Arguments[$index]) {
            $null
        } else {
            $Arguments[$index].PSObject.BaseObject
        }
    }
    try {
        return $Method.Invoke($null, $nativeArguments)
    }
    catch [Reflection.TargetInvocationException] {
        throw $_.Exception.InnerException
    }
    catch {
        $argumentTypes = ($Arguments | ForEach-Object {
            if ($null -eq $_) { "null" } else { $_.GetType().FullName }
        }) -join ", "
        throw "$($Method.Name) failed for [$argumentTypes]: $($_.Exception.Message)"
    }
}

function Write-Json([string]$File, $Value) {
    $directory = [IO.Path]::GetDirectoryName($File)
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    [IO.File]::WriteAllText($File, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}

function New-Library([string]$Root, [object[]]$Instructions, [object[]]$Presets, [string]$DefaultPresetId = $null, [string]$Command = "/choose") {
    [IO.Directory]::CreateDirectory((Join-Path $Root "instructions")) | Out-Null
    $metadata = @()
    foreach ($item in $Instructions) {
        $metadata += [ordered]@{
            id = $item.id
            name = $item.name
            file = "instructions/$($item.id).md"
            origin = if ($null -eq $item.origin) { "local" } else { $item.origin }
            sourcePackageId = $item.sourcePackageId
            sourcePackageKey = $item.sourcePackageKey
            sourceContentHash = $item.sourceContentHash
            showInCustomPicker = if ($null -eq $item.showInCustomPicker) { $true } else { $item.showInCustomPicker }
            createdAt = "2026-08-11T00:00:00.000Z"
            updatedAt = "2026-08-11T00:00:00.000Z"
        }
        [IO.File]::WriteAllText((Join-Path $Root "instructions\$($item.id).md"), [string]$item.content, [Text.UTF8Encoding]::new($false))
    }
    Write-Json (Join-Path $Root "config.json") ([ordered]@{
        version = 3
        command = $Command
        defaultPresetId = $DefaultPresetId
        instructions = $metadata
        presets = $Presets
    })
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $resolvedAssembly)) {
    throw "Companion assembly was not found: $resolvedAssembly"
}

$assembly = [Reflection.Assembly]::LoadFile($resolvedAssembly)
$store = $assembly.GetType("InstructionSwitcherCompanion.LibraryStore", $true)
$exchange = $assembly.GetType("InstructionSwitcherCompanion.PackageExchange", $true)
$flags = [Reflection.BindingFlags]"Public,Static"
$load = $store.GetMethod("Load", $flags)
$signature = $store.GetMethod("Signature", $flags)
$createPreset = $exchange.GetMethod("CreatePresetPackage", $flags)
$createBackup = $exchange.GetMethod("CreateBackup", $flags)
$serialize = $exchange.GetMethod("SerializePackage", $flags)
$readJson = $exchange.GetMethod("ReadPackageJson", $flags)
$preview = $exchange.GetMethod("PreviewImport", $flags)
$validate = $exchange.GetMethod("ValidatePlan", $flags)
$apply = $exchange.GetMethod("ApplyImport", $flags)

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("instruction-switcher-package-test-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $sourceRoot = Join-Path $testRoot "source"
    $sourcePresets = @([ordered]@{
        id = "preset-review"
        name = "Review preset"
        instructionIds = @("review")
        origin = "local"
        sourcePackageId = $null
        sourcePackageKey = $null
        sourceContentHash = $null
        createdAt = "2026-08-11T00:00:00.000Z"
        updatedAt = "2026-08-11T00:00:00.000Z"
    })
    New-Library $sourceRoot @([pscustomobject]@{
        id = "review"; name = "Review"; content = "CHECK THE EVIDENCE"; showInCustomPicker = $true
    }) $sourcePresets "preset-review"
    $sourceConfig = Join-Path $sourceRoot "config.json"
    $sourceSettings = Invoke-Static $load ([object[]]@($sourceConfig, $sourceRoot))
    $package = Invoke-Static $createPreset ([object[]]@($sourceRoot, $sourceSettings, "preset-review"))
    $packageJson = Invoke-Static $serialize ([object[]]@($package))
    $packageData = $packageJson | ConvertFrom-Json
    Assert-Equal "instruction-switcher-package" $packageData.format "package format"
    Assert-Equal "preset" $packageData.kind "package kind"
    Assert-Equal "review" $packageData.presets[0].instructionKeys[0] "package reference"

    $targetRoot = Join-Path $testRoot "target"
    New-Library $targetRoot @() @()
    $targetConfig = Join-Path $targetRoot "config.json"
    $targetSettings = Invoke-Static $load ([object[]]@($targetConfig, $targetRoot))
    $targetSignature = Invoke-Static $signature ([object[]]@($targetConfig))
    $parsed = Invoke-Static $readJson ([object[]]@($packageJson))
    $plan = Invoke-Static $preview ([object[]]@($parsed, $targetSettings, $targetRoot, $targetSignature))
    Assert-Equal $false $plan.showPresetInstructions "preset dependency default visibility"
    Assert-Equal "create" $plan.instructions[0].selectedAction "new dependency action"
    $result = Invoke-Static $apply ([object[]]@($plan, $targetConfig, $targetRoot))
    Assert-Equal 1 $result.createdInstructions "created instruction count"
    Assert-Equal 1 $result.createdPresets "created preset count"
    $targetData = Get-Content -Raw -Encoding UTF8 $targetConfig | ConvertFrom-Json
    Assert-Equal "preset-package" $targetData.instructions[0].origin "dependency origin"
    Assert-Equal $false $targetData.instructions[0].showInCustomPicker "dependency hidden"
    Assert-Equal $targetData.instructions[0].id $targetData.presets[0].instructionIds[0] "mapped preset reference"
    Assert-Equal "CHECK THE EVIDENCE" ([IO.File]::ReadAllText((Join-Path $targetRoot $targetData.instructions[0].file))) "imported body"

    $targetSettings = Invoke-Static $load ([object[]]@($targetConfig, $targetRoot))
    $targetSignature = Invoke-Static $signature ([object[]]@($targetConfig))
    $secondPlan = Invoke-Static $preview ([object[]]@($parsed, $targetSettings, $targetRoot, $targetSignature))
    Assert-Equal "reuse" $secondPlan.instructions[0].selectedAction "repeat instruction action"
    Assert-Equal "reuse" $secondPlan.presets[0].selectedAction "repeat preset action"
    $secondPlan.instructions[0].selectedAction = "copy"
    $dependencyError = Invoke-Static $validate ([object[]]@($secondPlan))
    Assert-True (-not [String]::IsNullOrWhiteSpace([string]$dependencyError)) "changed dependency rejects stale preset reuse"
    $secondPlan.instructions[0].selectedAction = "reuse"
    $secondPlan.showPresetInstructions = $true
    [void](Invoke-Static $apply ([object[]]@($secondPlan, $targetConfig, $targetRoot)))
    $targetData = Get-Content -Raw -Encoding UTF8 $targetConfig | ConvertFrom-Json
    Assert-Equal $true $targetData.instructions[0].showInCustomPicker "recipient visibility opt-in"

    $oldBodyReference = [string]$targetData.instructions[0].file
    $updatedPackageData = $packageJson | ConvertFrom-Json
    $updatedPackageData.instructions[0].content = "CHECK THE UPDATED EVIDENCE"
    $updatedPackageJson = $updatedPackageData | ConvertTo-Json -Depth 12 -Compress
    $updatedParsed = Invoke-Static $readJson ([object[]]@($updatedPackageJson))
    $targetSettings = Invoke-Static $load ([object[]]@($targetConfig, $targetRoot))
    $targetSignature = Invoke-Static $signature ([object[]]@($targetConfig))
    $updatePlan = Invoke-Static $preview ([object[]]@($updatedParsed, $targetSettings, $targetRoot, $targetSignature))
    Assert-Equal "update" $updatePlan.instructions[0].selectedAction "package update action"
    [void](Invoke-Static $apply ([object[]]@($updatePlan, $targetConfig, $targetRoot)))
    $targetData = Get-Content -Raw -Encoding UTF8 $targetConfig | ConvertFrom-Json
    Assert-True ($oldBodyReference -ne [string]$targetData.instructions[0].file) "package update switches body reference"
    Assert-Equal "CHECK THE UPDATED EVIDENCE" ([IO.File]::ReadAllText((Join-Path $targetRoot $targetData.instructions[0].file))) "updated body"
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $targetRoot $oldBodyReference))) "old body removed after commit"

    $reuseRoot = Join-Path $testRoot "reuse"
    New-Library $reuseRoot @([pscustomobject]@{
        id = "local-review"; name = "Review"; content = "CHECK THE EVIDENCE"; showInCustomPicker = $true
    }) @()
    $reuseConfig = Join-Path $reuseRoot "config.json"
    $reuseSettings = Invoke-Static $load ([object[]]@($reuseConfig, $reuseRoot))
    $reuseSignature = Invoke-Static $signature ([object[]]@($reuseConfig))
    $reusePlan = Invoke-Static $preview ([object[]]@($parsed, $reuseSettings, $reuseRoot, $reuseSignature))
    Assert-Equal "reuse" $reusePlan.instructions[0].selectedAction "same content reuse"
    [void](Invoke-Static $apply ([object[]]@($reusePlan, $reuseConfig, $reuseRoot)))
    $reuseData = Get-Content -Raw -Encoding UTF8 $reuseConfig | ConvertFrom-Json
    Assert-Equal 1 $reuseData.instructions.Count "reuse keeps one instruction"
    Assert-Equal "local-review" $reuseData.presets[0].instructionIds[0] "preset maps to local instruction"
    Assert-Equal "local" $reuseData.instructions[0].origin "reuse preserves local origin"

    $multiRoot = Join-Path $testRoot "multi"
    $multiLocalPresets = @([ordered]@{
        id = "local-shared-preset"
        name = "Shared preset"
        instructionIds = @("local-shared")
        origin = "local"
        sourcePackageId = $null
        sourcePackageKey = $null
        sourceContentHash = $null
        createdAt = "2026-08-11T00:00:00.000Z"
        updatedAt = "2026-08-11T00:00:00.000Z"
    })
    New-Library $multiRoot @([pscustomobject]@{
        id = "local-shared"; name = "Shared"; content = "SHARED BODY"; showInCustomPicker = $true
    }) $multiLocalPresets
    $multiConfig = Join-Path $multiRoot "config.json"
    $multiPackageJson = ([ordered]@{
        format = "instruction-switcher-package"
        schemaVersion = 2
        kind = "preset"
        packageId = "multi-preset-package"
        name = "Multi preset package"
        exportedAt = "2026-08-11T00:00:00.000Z"
        instructions = @([ordered]@{
            packageKey = "shared"
            stableId = "shared"
            name = "Shared"
            content = "SHARED BODY"
        })
        presets = @(
            [ordered]@{
                packageKey = "first"
                stableId = "first"
                name = "Shared preset"
                instructionKeys = @("shared")
            },
            [ordered]@{
                packageKey = "second"
                stableId = "second"
                name = "Shared preset"
                instructionKeys = @("shared")
            }
        )
    } | ConvertTo-Json -Depth 12 -Compress)
    $multiPackage = Invoke-Static $readJson ([object[]]@($multiPackageJson))
    $multiSettings = Invoke-Static $load ([object[]]@($multiConfig, $multiRoot))
    $multiSignature = Invoke-Static $signature ([object[]]@($multiConfig))
    $multiPlan = Invoke-Static $preview ([object[]]@($multiPackage, $multiSettings, $multiRoot, $multiSignature))
    Assert-Equal "reuse" $multiPlan.presets[0].selectedAction "first shared preset reuse"
    Assert-Equal "reuse" $multiPlan.presets[1].selectedAction "second shared preset reuse"
    $multiResult = Invoke-Static $apply ([object[]]@($multiPlan, $multiConfig, $multiRoot))
    Assert-Equal 2 $multiResult.presetKeys.Count "multi preset result key count"
    Assert-Equal 2 $multiResult.presetIds.Count "multi preset result id count"
    Assert-Equal "second" $multiResult.presetKeys[1] "multi preset second key"
    Assert-Equal "local-shared-preset" $multiResult.presetIds[1] "multi preset second local id"

    $legacyRoot = Join-Path $testRoot "legacy"
    New-Library $legacyRoot @() @()
    $legacyConfig = Join-Path $legacyRoot "config.json"
    $legacyJson = (@{
        version = 1
        exportedAt = "2026-08-11T00:00:00.000Z"
        defaultPresetId = $null
        instructions = @(@{ id = "legacy"; name = "Legacy"; content = "LEGACY BODY" })
        presets = @()
    } | ConvertTo-Json -Depth 8)
    $legacyPackage = Invoke-Static $readJson ([object[]]@($legacyJson))
    Assert-Equal "legacy" $legacyPackage.kind "legacy package compatibility"
    $legacySettings = Invoke-Static $load ([object[]]@($legacyConfig, $legacyRoot))
    $legacySignature = Invoke-Static $signature ([object[]]@($legacyConfig))
    $legacyPlan = Invoke-Static $preview ([object[]]@($legacyPackage, $legacySettings, $legacyRoot, $legacySignature))
    [void](Invoke-Static $apply ([object[]]@($legacyPlan, $legacyConfig, $legacyRoot)))
    $legacyData = Get-Content -Raw -Encoding UTF8 $legacyConfig | ConvertFrom-Json
    Assert-Equal $true $legacyData.instructions[0].showInCustomPicker "legacy import remains visible"

    $backupSourceSettings = Invoke-Static $load ([object[]]@($targetConfig, $targetRoot))
    $backup = Invoke-Static $createBackup ([object[]]@($targetRoot, $backupSourceSettings))
    $backupJson = Invoke-Static $serialize ([object[]]@($backup))
    $restoreRoot = Join-Path $testRoot "restore"
    New-Library $restoreRoot @([pscustomobject]@{
        id = "extra"; name = "Extra"; content = "EXTRA"; showInCustomPicker = $true
    }) @() $null "/local"
    $restoreConfig = Join-Path $restoreRoot "config.json"
    $restoreSettings = Invoke-Static $load ([object[]]@($restoreConfig, $restoreRoot))
    $restoreSignature = Invoke-Static $signature ([object[]]@($restoreConfig))
    $backupPackage = Invoke-Static $readJson ([object[]]@($backupJson))
    $backupPlan = Invoke-Static $preview ([object[]]@($backupPackage, $restoreSettings, $restoreRoot, $restoreSignature))
    Assert-Equal $true $backupPlan.replaceLibrary "backup replacement plan"
    [void](Invoke-Static $apply ([object[]]@($backupPlan, $restoreConfig, $restoreRoot)))
    $restoreData = Get-Content -Raw -Encoding UTF8 $restoreConfig | ConvertFrom-Json
    Assert-Equal 1 $restoreData.instructions.Count "backup replaces local library"
    Assert-Equal "/choose" $restoreData.command "backup restores command"
    Assert-Equal $targetData.defaultPresetId $restoreData.defaultPresetId "backup restores default preset"

    $staleRoot = Join-Path $testRoot "stale"
    New-Library $staleRoot @() @()
    $staleConfig = Join-Path $staleRoot "config.json"
    $staleSettings = Invoke-Static $load ([object[]]@($staleConfig, $staleRoot))
    $staleSignature = Invoke-Static $signature ([object[]]@($staleConfig))
    $stalePlan = Invoke-Static $preview ([object[]]@($parsed, $staleSettings, $staleRoot, $staleSignature))
    [IO.File]::AppendAllText($staleConfig, " ")
    $staleRejected = $false
    try { [void](Invoke-Static $apply ([object[]]@($stalePlan, $staleConfig, $staleRoot))) }
    catch { $staleRejected = $true }
    Assert-True $staleRejected "stale plan rejection"
    $staleData = Get-Content -Raw -Encoding UTF8 $staleConfig | ConvertFrom-Json
    Assert-Equal 0 $staleData.instructions.Count "stale plan leaves library unchanged"

    Write-Output "Library package checks passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
