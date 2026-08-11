param(
    [string]$OutputPath = (Join-Path $PSScriptRoot "..\companion\InstructionSwitcherCompanion.exe")
)

$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$compiler = Join-Path $framework "csc.exe"
$manifest = Join-Path $PSScriptRoot "..\companion\app.manifest"
$source = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot "..\companion") -Filter "*.cs" |
    Sort-Object Name |
    ForEach-Object { $_.FullName }
$output = [IO.Path]::GetFullPath($OutputPath)

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null

if (-not (Test-Path -LiteralPath $compiler)) {
    throw ".NET Framework C# compiler was not found: $compiler"
}
if (-not (Test-Path -LiteralPath $manifest)) {
    throw "Companion application manifest was not found: $manifest"
}

$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Management.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll"
) | ForEach-Object { "/reference:$(Join-Path $framework $_)" }

& $compiler /nologo /target:winexe /optimize+ /platform:anycpu "/win32manifest:$manifest" "/out:$output" $references $source
if ($LASTEXITCODE -ne 0) {
    throw "Companion build failed with exit code $LASTEXITCODE"
}

Write-Output $output
