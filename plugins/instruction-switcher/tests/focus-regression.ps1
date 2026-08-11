$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class FocusRegressionNative
{
    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public uint ProcessId;
        public uint ThreadId;
        public string Title;
        public string ClassName;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT caretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(IntPtr handle, bool altTab);

    private static WindowInfo Describe(IntPtr handle)
    {
        uint processId;
        uint threadId = GetWindowThreadProcessId(handle, out processId);
        var title = new StringBuilder(256);
        var className = new StringBuilder(256);
        GetWindowText(handle, title, title.Capacity);
        GetClassName(handle, className, className.Capacity);
        RECT rect;
        GetWindowRect(handle, out rect);
        return new WindowInfo {
            Handle = handle,
            ProcessId = processId,
            ThreadId = threadId,
            Title = title.ToString(),
            ClassName = className.ToString(),
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };
    }

    public static WindowInfo[] TopLevel(uint processId)
    {
        var result = new List<WindowInfo>();
        EnumWindows(delegate(IntPtr handle, IntPtr unused) {
            uint owner;
            GetWindowThreadProcessId(handle, out owner);
            if (owner == processId) result.Add(Describe(handle));
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    public static WindowInfo[] Children(IntPtr parent)
    {
        var result = new List<WindowInfo>();
        EnumChildWindows(parent, delegate(IntPtr handle, IntPtr unused) {
            result.Add(Describe(handle));
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }

    public static string Text(IntPtr handle)
    {
        var text = new StringBuilder(512);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    public static IntPtr Focus(uint threadId)
    {
        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf(typeof(GuiThreadInfo)) };
        return GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    public static IntPtr Foreground()
    {
        return GetForegroundWindow();
    }

    public static bool Visible(IntPtr handle)
    {
        return IsWindowVisible(handle);
    }

    public static bool TopMost(IntPtr handle)
    {
        return (GetWindowLongPtr(handle, -20).ToInt64() & 0x00000008L) != 0;
    }

    public static long RealClick(int x, int y)
    {
        POINT previous;
        GetCursorPos(out previous);
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        SetCursorPos(previous.X, previous.Y);
        return (previous.X & 0xffffffffL) | ((long)previous.Y << 32);
    }

    public static void MakeForeground(IntPtr handle)
    {
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        SwitchToThisWindow(handle, true);
    }

    public static void RemoveTopMost(IntPtr handle)
    {
        SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static void RestoreForeground(IntPtr handle)
    {
        if (handle != IntPtr.Zero) SwitchToThisWindow(handle, true);
    }

    public static bool ShowWithoutActivation(IntPtr handle)
    {
        return ShowWindow(handle, 4);
    }

    public static bool Click(IntPtr handle)
    {
        const uint WmLButtonDown = 0x0201;
        const uint WmLButtonUp = 0x0202;
        const int MkLButton = 0x0001;
        return PostMessage(handle, WmLButtonDown, new IntPtr(MkLButton), new IntPtr(5 | (5 << 16)))
            && PostMessage(handle, WmLButtonUp, IntPtr.Zero, new IntPtr(5 | (5 << 16)));
    }

    public static bool ButtonClick(IntPtr handle)
    {
        return PostMessage(handle, 0x00F5, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool TypeText(IntPtr handle, string value)
    {
        bool posted = true;
        foreach (char character in value)
            posted = PostMessage(handle, 0x0102, new IntPtr(character), IntPtr.Zero) && posted;
        return posted;
    }

    public static bool Close(IntPtr handle)
    {
        return PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }
}
'@

function Wait-Window([uint32]$processId, [string]$title, [int]$timeoutMs = 3000) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMs)
    do {
        $window = [FocusRegressionNative]::TopLevel($processId) | Where-Object { $_.Title -eq $title } | Select-Object -First 1
        if ($null -ne $window) { return $window }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

$process = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'InstructionSwitcherCompanion.exe' -and $_.CommandLine -like '*instruction-switcher*'
} | Select-Object -First 1
if ($null -eq $process) { throw '伴随窗进程未运行' }

$processId = [uint32]$process.ProcessId
$existingManager = Wait-Window $processId '管理指令库与配置预设' 100
if ($null -ne $existingManager) { throw '管理窗口已在用户操作中，停止回归检查以避免干扰' }

$main = [FocusRegressionNative]::TopLevel($processId) | Where-Object { $_.Title -eq 'Instruction Switcher' } | Select-Object -First 1
if ($null -eq $main) { throw '伴随窗主窗口未找到' }
[FocusRegressionNative]::ShowWithoutActivation($main.Handle) | Out-Null
$manage = [FocusRegressionNative]::Children($main.Handle) | Where-Object { $_.Title -eq '管理指令库' } | Select-Object -First 1
if ($null -eq $manage) { throw '管理按钮未找到' }
[FocusRegressionNative]::ButtonClick($manage.Handle) | Out-Null

$manager = Wait-Window $processId '管理指令库与配置预设'
if ($null -eq $manager) { throw '管理窗口未打开' }

$add = [FocusRegressionNative]::Children($manager.Handle) | Where-Object { $_.Title -eq '新增指令' } | Select-Object -First 1
if ($null -eq $add) { throw '新增指令按钮未找到' }
[FocusRegressionNative]::MakeForeground($manager.Handle)
[FocusRegressionNative]::RealClick([int](($add.Left + $add.Right) / 2), [int](($add.Top + $add.Bottom) / 2)) | Out-Null
Start-Sleep -Milliseconds 250
[FocusRegressionNative]::RemoveTopMost($manager.Handle)
Start-Sleep -Milliseconds 150

$nameBox = [FocusRegressionNative]::Children($manager.Handle) |
    Where-Object { $_.ClassName -like '*EDIT*' -and $_.ClassName -notlike '*RichEdit*' -and $_.Left -gt ($manager.Left + 250) } |
    Sort-Object Top | Select-Object -First 1
if ($null -eq $nameBox) { throw '新指令名称输入框未找到' }
$initialName = [FocusRegressionNative]::Text($nameBox.Handle)
if (-not [String]::IsNullOrEmpty($initialName)) { throw "新增指令未进入空白编辑状态：$initialName" }

$previousForeground = [FocusRegressionNative]::Foreground()
[FocusRegressionNative]::MakeForeground($manager.Handle)
$clickX = [int](($nameBox.Left + $nameBox.Right) / 2)
$clickY = [int](($nameBox.Top + $nameBox.Bottom) / 2)
[FocusRegressionNative]::RealClick($clickX, $clickY) | Out-Null
Start-Sleep -Milliseconds 100
[FocusRegressionNative]::RemoveTopMost($manager.Handle)
[FocusRegressionNative]::RealClick($clickX, $clickY) | Out-Null
Start-Sleep -Milliseconds 100
$immediateFocus = [FocusRegressionNative]::Focus($manager.ThreadId)
$immediateForeground = [FocusRegressionNative]::Foreground()
$focusChangedAtMs = $null
$focusChangedForeground = $null
$focusChangedTopMost = $null
$focusChangedMainVisible = $null
$focusChangedManagerVisible = $null
$started = [Diagnostics.Stopwatch]::StartNew()
while ($started.ElapsedMilliseconds -lt 1500) {
    Start-Sleep -Milliseconds 25
    $sampleFocus = [FocusRegressionNative]::Focus($manager.ThreadId)
    if ($null -eq $focusChangedAtMs -and $sampleFocus -ne $nameBox.Handle) {
        $focusChangedAtMs = $started.ElapsedMilliseconds
        $focusChangedForeground = [FocusRegressionNative]::Foreground().ToInt64()
        $focusChangedTopMost = [FocusRegressionNative]::TopMost($main.Handle)
        $focusChangedMainVisible = [FocusRegressionNative]::Visible($main.Handle)
        $focusChangedManagerVisible = [FocusRegressionNative]::Visible($manager.Handle)
    }
}
$delayedFocus = [FocusRegressionNative]::Focus($manager.ThreadId)
$typedMarker = 'focus-check'
[FocusRegressionNative]::TypeText($nameBox.Handle, $typedMarker) | Out-Null
Start-Sleep -Milliseconds 100
$typedText = [FocusRegressionNative]::Text($nameBox.Handle)

$result = [PSCustomObject]@{
    ManagerThread = $manager.ThreadId
    InputHandle = $nameBox.Handle.ToInt64()
    ImmediateFocus = $immediateFocus.ToInt64()
    ImmediateForeground = $immediateForeground.ToInt64()
    DelayedFocus = $delayedFocus.ToInt64()
    FocusChangedAtMs = $focusChangedAtMs
    FocusChangedForeground = $focusChangedForeground
    MainTopMostAtChange = $focusChangedTopMost
    MainVisibleAtChange = $focusChangedMainVisible
    ManagerVisibleAtChange = $focusChangedManagerVisible
    FocusRetained = ($delayedFocus -eq $nameBox.Handle)
    TypedText = $typedText
    TypingAccepted = ($typedText -eq $typedMarker)
    InitialName = $initialName
}
$result | ConvertTo-Json

$cancel = [FocusRegressionNative]::Children($manager.Handle) | Where-Object { $_.Title -eq '取消' } | Select-Object -First 1
if ($null -ne $cancel) { [FocusRegressionNative]::ButtonClick($cancel.Handle) | Out-Null }
Start-Sleep -Milliseconds 150
[FocusRegressionNative]::Close($manager.Handle) | Out-Null
[FocusRegressionNative]::RestoreForeground($previousForeground)
exit ($(if ($result.FocusRetained -and $result.TypingAccepted) { 0 } else { 1 }))
