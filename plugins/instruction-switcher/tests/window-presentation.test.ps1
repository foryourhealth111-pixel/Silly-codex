param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot "..\companion\InstructionSwitcherCompanion.exe")
)

$ErrorActionPreference = "Stop"

function Assert-True([bool]$Value, [string]$Label) {
    if (-not $Value) { throw "$Label expected true" }
}

$resolvedAssembly = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $resolvedAssembly)) {
    throw "Companion assembly was not found: $resolvedAssembly"
}

$assembly = [Reflection.Assembly]::LoadFile($resolvedAssembly)
$instanceFlags = [Reflection.BindingFlags]"Instance,NonPublic,Public"
$companion = $assembly.GetType("InstructionSwitcherCompanion.CompanionForm", $true)
$manager = $assembly.GetType("InstructionSwitcherCompanion.LibraryManagerForm", $true)
$theme = $assembly.GetType("InstructionSwitcherCompanion.CompanionTheme", $true)

$onLoad = $companion.GetMethod("OnLoad", $instanceFlags)
$onShown = $companion.GetMethod("OnShown", $instanceFlags)
$showPreferred = $companion.GetMethod("ShowPreferred", $instanceFlags)
$prepareManager = $manager.GetMethod("PrepareForModalDisplay", $instanceFlags)
$managerCreateParams = $manager.GetProperty("CreateParams", $instanceFlags)
$compositedStyle = $theme.GetField("CompositedWindowStyle", [Reflection.BindingFlags]"Public,Static")

Assert-True ($null -ne $onLoad -and $onLoad.DeclaringType -eq $companion) "companion pre-show initialization"
Assert-True ($null -ne $onShown -and $onShown.DeclaringType -eq $companion) "companion shown lifecycle"
Assert-True ($null -ne $showPreferred) "companion restore presentation"
Assert-True ($null -ne $prepareManager) "manager modal preparation"
Assert-True ($null -ne $managerCreateParams -and $managerCreateParams.DeclaringType -eq $manager) "manager create params override"
Assert-True ($null -ne $compositedStyle) "manager composited style"
Assert-True ([int]$compositedStyle.GetValue($null) -eq 0x02000000) "composited style value"

Add-Type -ReferencedAssemblies @(
    "System.dll",
    "System.Windows.Forms.dll"
) -TypeDefinition @'
using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

public static class WindowPresentationProbe
{
    public static string PrepareManager(string assemblyPath, string temporaryRoot)
    {
        string result = null;
        Exception failure = null;
        var thread = new Thread(new ThreadStart(delegate
        {
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var managerType = assembly.GetType(
                    "InstructionSwitcherCompanion.LibraryManagerForm", true);
                object instance = Activator.CreateInstance(
                    managerType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new object[] {
                        temporaryRoot + "\\missing-config.json",
                        temporaryRoot,
                        temporaryRoot + "\\sessions"
                    },
                    null);
                using (var form = (Form)instance)
                {
                    bool before = form.IsHandleCreated;
                    managerType.GetMethod("PrepareForModalDisplay").Invoke(form, null);
                    const BindingFlags flags = BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic;
                    var createParams = (CreateParams)managerType
                        .GetProperty("CreateParams", flags)
                        .GetValue(form, null);
                    result = String.Format("{0};{1};{2}", before,
                        form.IsHandleCreated,
                        (createParams.ExStyle & 0x02000000) != 0);
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
        }));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new InvalidOperationException(failure.ToString());
        return result;
    }
}
'@

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "instruction-switcher-window-presentation-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory((Join-Path $temporaryRoot "sessions")) | Out-Null
try {
    $managerState = [WindowPresentationProbe]::PrepareManager($resolvedAssembly, $temporaryRoot)
    Assert-True ($managerState -eq "False;True;True") "manager pre-show handle and composited style"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        [IO.Directory]::Delete($temporaryRoot, $true)
    }
}

Write-Output "Window presentation checks passed."
