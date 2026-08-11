param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\companion\InstructionSwitcherCompanion.exe')
)

$ErrorActionPreference = 'Stop'

$assemblyPath = [IO.Path]::GetFullPath($AssemblyPath)
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Companion assembly was not found: $assemblyPath"
}

Add-Type -ReferencedAssemblies @(
    'System.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll'
) -TypeDefinition @'
using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

public static class ThemeTransitionLayerProbe
{
    public static string RenderTransparentLayer(string assemblyPath)
    {
        string result = null;
        Exception failure = null;
        var thread = new Thread(new ThreadStart(delegate
        {
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var layerType = assembly.GetType(
                    "InstructionSwitcherCompanion.ThemeTransitionLayer", true);
                using (var layer = (Control)Activator.CreateInstance(layerType, true))
                {
                    layer.Size = new Size(120, 80);
                    layer.Visible = true;
                    var previousFrame = new Bitmap(layer.Width, layer.Height);
                    using (Graphics graphics = Graphics.FromImage(previousFrame))
                        graphics.Clear(Color.Black);
                    var targetFrame = new Bitmap(layer.Width, layer.Height);
                    using (Graphics graphics = Graphics.FromImage(targetFrame))
                        graphics.Clear(Color.White);
                    layerType.GetMethod("SetFrame").Invoke(
                        layer, new object[] { previousFrame, Color.Black });
                    layerType.GetMethod("SetTargetFrame").Invoke(
                        layer, new object[] { targetFrame, Color.White });
                    layerType.GetProperty("FrameOpacity").SetValue(layer, 128, null);
                    using (var bitmap = new Bitmap(layer.Width, layer.Height))
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    using (var paint = new PaintEventArgs(graphics,
                        new Rectangle(Point.Empty, layer.Size)))
                    {
                        graphics.Clear(Color.Magenta);
                        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                        layerType.GetMethod("OnPaintBackground", flags).Invoke(
                            layer, new object[] { paint });
                        layerType.GetMethod("OnPaint", flags).Invoke(
                            layer, new object[] { paint });
                        Color pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
                        layerType.GetProperty("FrameOpacity").SetValue(layer, 0, null);
                        result = String.Format("{0};{1},{2},{3}", layer.Visible,
                            pixel.R, pixel.G, pixel.B);
                    }
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

$pixel = [ThemeTransitionLayerProbe]::RenderTransparentLayer($assemblyPath)
Write-Output "Theme transition layer state: $pixel"
$parts = $pixel.Split(';')
$channels = [int[]]($parts[1].Split(','))
if ($parts[0] -ne 'False') {
    throw "Theme transition layer remained visible at zero opacity: $pixel"
}
if (($channels | Where-Object { $_ -lt 126 -or $_ -gt 129 }).Count -gt 0) {
    throw "Theme transition layer did not blend the old and new frames: $pixel"
}

Write-Output 'Theme transition layer regression passed.'
