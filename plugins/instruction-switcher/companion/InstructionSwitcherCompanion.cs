using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace InstructionSwitcherCompanion
{
    internal static class PathHelpers
    {
        public static string NormalizeDirectory(string value)
        {
            string full = Path.GetFullPath(value);
            string root = Path.GetPathRoot(full) ?? full;
            while (full.Length > root.Length &&
                (full.EndsWith("\\", StringComparison.Ordinal) || full.EndsWith("/", StringComparison.Ordinal)))
                full = full.Substring(0, full.Length - 1);
            return full;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            CompanionArguments.WaitForRestartSource(args);
            string runtimeRoot = CompanionArguments.ResolveRuntimeRoot(args);
            runtimeRoot = PathHelpers.NormalizeDirectory(runtimeRoot);
            Directory.CreateDirectory(runtimeRoot);

            bool created;
            using (var mutex = new Mutex(true, MutexName(runtimeRoot), out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CompanionForm(runtimeRoot));
            }
        }

        private static string MutexName(string runtimeRoot)
        {
            byte[] digest;
            using (var sha = SHA256.Create())
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(
                    PathHelpers.NormalizeDirectory(runtimeRoot).ToUpperInvariant()));
            var suffix = new StringBuilder();
            for (int i = 0; i < 8; i++) suffix.Append(digest[i].ToString("x2"));
            return @"Local\CodexInstructionSwitcherCompanion.v2." + suffix;
        }
    }

    internal static class CompanionArguments
    {
        private const string RestartSwitch = "--restart-after";

        public static void WaitForRestartSource(string[] args)
        {
            if (args.Length < 3 || !String.Equals(args[0], RestartSwitch,
                StringComparison.OrdinalIgnoreCase)) return;
            int processId;
            if (!Int32.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                out processId) || processId <= 0) return;
            try
            {
                using (Process source = Process.GetProcessById(processId))
                    source.WaitForExit(5000);
            }
            catch
            {
                // The source process may already be gone.
            }
        }

        public static string ResolveRuntimeRoot(string[] args)
        {
            // Older installations may still pass --watch before the runtime root.
            int runtimeArgument = args.Length > 0 &&
                String.Equals(args[0], RestartSwitch, StringComparison.OrdinalIgnoreCase) ? 2 :
                args.Length > 0 && String.Equals(args[0], "--watch", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            return args.Length > runtimeArgument
                ? Path.GetFullPath(args[runtimeArgument])
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex",
                    "instruction-switcher",
                    "runtime");
        }
    }

    internal static class CodexLifecycle
    {
        public const int ExitDelaySeconds = 15;

        public static bool IsOpenWindow(IntPtr handle, bool visible, bool minimized)
        {
            return handle != IntPtr.Zero && (visible || minimized);
        }

        public static bool ShouldExit(DateTime lastWindowSeenUtc, DateTime nowUtc)
        {
            return nowUtc - lastWindowSeenUtc >= TimeSpan.FromSeconds(ExitDelaySeconds);
        }

        public static bool IsPrimaryProcessCommandLine(string commandLine)
        {
            return !String.IsNullOrWhiteSpace(commandLine) &&
                !Regex.IsMatch(commandLine, @"(?:^|\s)--type(?:=|\s)", RegexOptions.IgnoreCase);
        }

        public static bool ShouldRestartFocusTracker(bool isRunning, int runningPort, int discoveredPort)
        {
            if (discoveredPort <= 0) return false;
            return !isRunning || runningPort != discoveredPort;
        }
    }

    internal static class EnabledInstructionOrder
    {
        public static string[] Move(string[] order, string id, int targetIndex)
        {
            var next = new List<string>(order ?? new string[0]);
            if (String.IsNullOrWhiteSpace(id)) return next.ToArray();

            int sourceIndex = next.FindIndex(item =>
                String.Equals(item, id, StringComparison.OrdinalIgnoreCase));
            if (sourceIndex < 0) return next.ToArray();

            string moved = next[sourceIndex];
            next.RemoveAt(sourceIndex);
            targetIndex = Math.Max(0, Math.Min(targetIndex, next.Count));
            next.Insert(targetIndex, moved);
            return next.ToArray();
        }
    }

    internal sealed class FocusSnapshot
    {
        public int version { get; set; }
        public bool available { get; set; }
        public string key { get; set; }
        public string sessionId { get; set; }
        public string rowId { get; set; }
        public string title { get; set; }
        public string mapping { get; set; }
        public string reason { get; set; }
        public string observedAt { get; set; }
    }

    internal sealed class HookAcknowledgement
    {
        public int version { get; set; }
        public string key { get; set; }
        public string revision { get; set; }
        public string observedAt { get; set; }
    }

    internal sealed class StateFileLock : IDisposable
    {
        private const int WaitMilliseconds = 10000;
        private const int RetryMilliseconds = 25;
        private const int StaleMilliseconds = 60000;
        private readonly string lockPath;
        private readonly string token;
        private readonly FileStream stream;
        private bool disposed;

        private StateFileLock(string lockPath, string token, FileStream stream)
        {
            this.lockPath = lockPath;
            this.token = token;
            this.stream = stream;
        }

        public static StateFileLock Acquire(string target)
        {
            string lockPath = target + ".lock";
            string directory = Path.GetDirectoryName(lockPath);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(WaitMilliseconds);

            while (true)
            {
                FileStream stream = null;
                bool created = false;
                string token = Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N");
                try
                {
                    stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
                    created = true;
                    byte[] data = Encoding.UTF8.GetBytes(token + "\n");
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                    return new StateFileLock(lockPath, token, stream);
                }
                catch (IOException error)
                {
                    if (stream != null) stream.Dispose();
                    if (created)
                    {
                        try { File.Delete(lockPath); } catch { }
                        throw;
                    }
                    if (!File.Exists(lockPath)) throw;
                    TryRemoveStale(lockPath);
                    if (DateTime.UtcNow >= deadline)
                        throw new IOException("状态锁等待超时，请稍后重试", error);
                    Thread.Sleep(RetryMilliseconds);
                }
            }
        }

        private static void TryRemoveStale(string lockPath)
        {
            try
            {
                var info = new FileInfo(lockPath);
                if (info.Exists && (DateTime.UtcNow - info.LastWriteTimeUtc).TotalMilliseconds > StaleMilliseconds)
                    info.Delete();
            }
            catch
            {
                // Another writer may own or remove the lock.
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                stream.Dispose();
            }
            finally
            {
                try
                {
                    if (File.Exists(lockPath) &&
                        String.Equals(File.ReadAllText(lockPath, Encoding.UTF8).Trim(), token,
                            StringComparison.Ordinal))
                        File.Delete(lockPath);
                }
                catch
                {
                    // Lock cleanup is best effort after the handle is released.
                }
            }
        }
    }

    internal sealed class WindowPosition
    {
        public int version { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public string theme { get; set; }
        public string view { get; set; }
        public string language { get; set; }
        public WindowPlacement expanded { get; set; }
        public WindowPlacement bubble { get; set; }
    }

    internal sealed class WindowPlacement
    {
        public int x { get; set; }
        public int y { get; set; }
        public string screen { get; set; }
        public string horizontalEdge { get; set; }
        public string verticalEdge { get; set; }
        public int marginX { get; set; }
        public int marginY { get; set; }
    }

    internal enum CompanionViewMode
    {
        Expanded,
        Bubble
    }

    internal enum CompanionDisplayState
    {
        Expanded,
        Bubble,
        UserHidden,
        ContextSuppressed
    }

    internal sealed class TaskItem
    {
        public SessionDescriptor Descriptor { get; private set; }

        public TaskItem(SessionDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public override string ToString()
        {
            string project = String.IsNullOrWhiteSpace(Descriptor.project)
                ? "Codex task"
                : Descriptor.project;
            string shortKey = Descriptor.key != null && Descriptor.key.Length > 6
                ? Descriptor.key.Substring(0, 6)
                : Descriptor.key;
            DateTime updated;
            string timestamp = DateTime.TryParse(
                Descriptor.updatedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out updated)
                ? updated.ToLocalTime().ToString("MM-dd HH:mm")
                : UiText.T("时间未知");
            return project + "  ·  " + timestamp + "  ·  " + shortKey;
        }
    }

    internal sealed class PresetItem
    {
        public PresetDto Preset { get; private set; }
        private readonly string label;

        public PresetItem(PresetDto preset, string label = null)
        {
            Preset = preset;
            this.label = label;
        }

        public override string ToString()
        {
            return Preset == null ? (label ?? UiText.T("自定义")) : Preset.name;
        }
    }

    internal sealed class CompanionForm : Form
    {
        private const int WmNcLButtonDown = 0x00A1;
        private const int HtCaption = 0x0002;
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;

        private readonly string runtimeRoot;
        private readonly string sessionRoot;
        private readonly string stateRoot;
        private readonly string acknowledgementRoot;
        private readonly string positionFile;
        private readonly string focusFile;
        private readonly string configFile;
        private readonly JavaScriptSerializer json = CreateSerializer();
        private readonly List<SessionDescriptor> sessions = new List<SessionDescriptor>();
        private readonly Dictionary<string, CheckBox> profileChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, int> descriptorFailures = new Dictionary<string, int>();
        private readonly ToolTip tips = new ToolTip();
        private readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private Icon trayIcon;

        private ComboBox taskPicker;
        private ComboBox presetPicker;
        private CheckBox followLatest;
        private ToolStripMenuItem savePresetItem;
        private ToolStripMenuItem updatePresetItem;
        private ToolStripMenuItem undoItem;
        private ThemedButton manageButton;
        private Label pathLabel;
        private ThemedStatusLabel statusLabel;
        private Label presetStatusLabel;
        private ThemedStatusLabel followStatusLabel;
        private Label enabledCountLabel;
        private FlowLayoutPanel profileList;
        private Panel expandedSurface;
        private BubbleControl bubbleSurface;
        private Panel headerPanel;
        private Panel taskSection;
        private Panel presetSection;
        private Panel instructionSection;
        private Panel statusPanel;
        private ThemeTransitionLayer themeTransitionLayer;
        private ThemedButton collapseButton;
        private ThemedButton presetMenuButton;
        private ThemedButton footerMenuButton;
        private ContextMenuStrip trayMenu;
        private ContextMenuStrip presetMenu;
        private ContextMenuStrip footerMenu;
        private readonly System.Windows.Forms.Timer transitionTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer themeTimer = new System.Windows.Forms.Timer();
        private string activeKey;
        private string focusedKey;
        private SessionDescriptor focusedDescriptor;
        private bool focusConfirmed;
        private string focusReason;
        private Process focusTracker;
        private int focusTrackerPort;
        private volatile bool codexWindowCheckRunning;
        private DateTime nextCodexWindowCheck = DateTime.MinValue;
        private string descriptorSignature = "";
        private string profileSignature = "";
        private string librarySignature = "";
        private string stateSignature = "";
        private string acknowledgementSignature = "";
        private string currentRevision;
        private string acknowledgedRevision;
        private string undoKey;
        private string undoAppliedRevision;
        private string lastStateError;
        private string lastAcknowledgementError;
        private HashSet<string> committedEnabled = new HashSet<string>();
        private List<string> committedOrder = new List<string>();
        private string committedPresetId;
        private SettingsDto library = new SettingsDto {
            version = 3, command = "/choose", instructions = new InstructionDto[0], presets = new PresetDto[0]
        };
        private bool libraryReady;
        private SessionState undoState;
        private DateTime undoExpiresAt;
        private bool stateLoaded;
        private bool selectionConfirmed;
        private bool suppressProfileChange;
        private bool suppressPresetChange;
        private bool presetPickerRebuildPending;
        private bool managerOpen;
        private bool allowExit;
        private ThemeMode themeMode = CompanionTheme.DetectSystemTheme();
        private CompanionViewMode preferredView = CompanionViewMode.Expanded;
        private CompanionDisplayState displayState = CompanionDisplayState.Expanded;
        private WindowPosition windowPosition = new WindowPosition { version = 2 };
        private Size expandedWindowSize;
        private bool initialPlacementApplied;
        private bool transitioning;
        private bool themeTransitioning;
        private DateTime themeTransitionStartedAt;
        private Rectangle transitionTargetBounds;
        private DateTime transitionStartedAt;
        private Action transitionCompleted;
        private TransitionPhase transitionPhase;
        private bool transitionModeApplied;
        private bool bubbleDragging;
        private Point bubbleMouseDown;
        private Point bubbleWindowStart;
        private InstructionToggle profileDragSource;
        private Point profileDragStart;
        private bool profileDragInProgress;
        private DateTime lastCodexSeen = DateTime.UtcNow;

        private enum TransitionPhase
        {
            None,
            FadingOut,
            FadingIn
        }

        private const double TransitionFadeOutMilliseconds = 84D;
        private const double TransitionFadeInMilliseconds = 120D;
        private const double ThemeTransitionMilliseconds = 220D;

        private static JavaScriptSerializer CreateSerializer()
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            return serializer;
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(
            int left, int top, int right, int bottom, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string replacement, int flags);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ClassStyle |= 0x00020000;
                return value;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (!initialPlacementApplied) ApplyInitialPlacement();
        }

        public CompanionForm(string runtimeRoot)
        {
            this.runtimeRoot = PathHelpers.NormalizeDirectory(runtimeRoot);
            sessionRoot = Path.Combine(this.runtimeRoot, "sessions");
            DirectoryInfo configDirectory = Directory.GetParent(this.runtimeRoot);
            if (configDirectory == null) throw new InvalidOperationException("运行目录无效");
            stateRoot = Path.Combine(configDirectory.FullName, "sessions");
            acknowledgementRoot = Path.Combine(this.runtimeRoot, "acks");
            positionFile = Path.Combine(this.runtimeRoot, "window-position.json");
            focusFile = Path.Combine(this.runtimeRoot, "focus.json");
            configFile = Path.Combine(configDirectory.FullName, "config.json");
            Directory.CreateDirectory(sessionRoot);
            Directory.CreateDirectory(stateRoot);

            LoadWindowPreferences();
            UiText.Current = UiText.Parse(windowPosition.language);
            BuildWindow();
            BuildTray();
            RefreshLibrary();
            RefreshSessions();
            TrackCodex();
            RefreshFocus();

            pollTimer.Interval = 300;
            pollTimer.Tick += delegate { Poll(); };
            pollTimer.Start();

            transitionTimer.Interval = 16;
            transitionTimer.Tick += AnimateWindowTransition;
            themeTimer.Interval = 16;
            themeTimer.Tick += AnimateThemeTransition;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!initialPlacementApplied) ApplyInitialPlacement();
            CompanionTheme.ApplyWindow(this, themeMode);
            TrackCodex();
            if (!IsCodexForeground() && displayState != CompanionDisplayState.UserHidden)
                SuppressForContext();
            else
                TopMost = true;
        }

        private void BuildWindow()
        {
            Text = "Instruction Switcher";
            ClientSize = new Size(400, 660);
            MinimumSize = Size;
            MaximumSize = Size;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = false;
            Font = CompanionTheme.UiFont(9F, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            KeyPreview = true;
            DoubleBuffered = true;
            Padding = new Padding(1);

            expandedSurface = new Panel { Dock = DockStyle.Fill };
            bubbleSurface = new BubbleControl { Dock = DockStyle.Fill, Visible = false };
            bubbleSurface.MouseDown += BubbleMouseDown;
            bubbleSurface.MouseMove += BubbleMouseMove;
            bubbleSurface.MouseUp += BubbleMouseUp;
            bubbleSurface.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space) return;
                ExpandFromBubble();
                e.Handled = true;
            };
            tips.SetToolTip(bubbleSurface, UiText.T("展开指令面板"));

            headerPanel = new Panel { Dock = DockStyle.Top, Height = 62 };
            var brand = new ThemedButton {
                Glyph = GlyphKind.Sliders,
                Kind = ThemedButtonKind.Secondary,
                Location = new Point(14, 14),
                Size = new Size(34, 34),
                ShowBorder = true,
                TabStop = false
            };
            brand.Cursor = Cursors.SizeAll;
            var title = MakeLabel("Silly codex", 58, 18, 238, 25, true);
            title.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            collapseButton = new ThemedButton {
                Kind = ThemedButtonKind.Ghost,
                Glyph = GlyphKind.Collapse,
                Location = new Point(354, 14),
                Size = new Size(34, 34),
                ShowBorder = false,
                TabStop = false
            };
            collapseButton.Click += delegate { CollapseToBubble(); };
            tips.SetToolTip(collapseButton, UiText.T("折叠为悬浮球"));
            AttachDrag(headerPanel);
            AttachDrag(brand);
            AttachDrag(title);
            headerPanel.Controls.Add(brand);
            headerPanel.Controls.Add(title);
            headerPanel.Controls.Add(collapseButton);
            headerPanel.Resize += delegate {
                collapseButton.Left = Math.Max(0, headerPanel.ClientSize.Width - collapseButton.Width - 12);
                title.Width = Math.Max(80, collapseButton.Left - title.Left - 8);
            };

            var body = new Panel { Dock = DockStyle.Fill };
            taskSection = new Panel { Dock = DockStyle.Top, Height = 138 };
            var taskHeading = MakeLabel(UiText.T("当前任务"), 16, 10, 140, 22, true);
            followStatusLabel = new ThemedStatusLabel {
                Text = UiText.T("正在识别"),
                Tone = StatusTone.Warning,
                Location = new Point(220, 10),
                Size = new Size(164, 22),
                TextAlign = ContentAlignment.MiddleRight
            };
            taskPicker = new ThemedComboBox {
                Location = new Point(16, 38),
                Size = new Size(368, 30),
                IntegralHeight = false,
                DropDownHeight = 180
            };
            taskPicker.SelectionChangeCommitted += TaskChanged;
            followLatest = new ThemedCheckBox {
                Text = UiText.T("自动跟随 Codex 当前任务"),
                Checked = true,
                Location = new Point(16, 101),
                Size = new Size(368, 28),
                ThemeMode = themeMode
            };
            followLatest.CheckedChanged += FollowChanged;
            pathLabel = MakeLabel(UiText.T("等待 Codex 任务"), 16, 70, 368, 26, false);
            ((ThemedLabel)pathLabel).Role = ThemedLabelRole.Secondary;
            pathLabel.AutoEllipsis = true;
            taskSection.Controls.Add(taskHeading);
            taskSection.Controls.Add(followStatusLabel);
            taskSection.Controls.Add(taskPicker);
            taskSection.Controls.Add(pathLabel);
            taskSection.Controls.Add(followLatest);
            taskSection.Resize += delegate {
                followStatusLabel.Left = Math.Max(156, taskSection.ClientSize.Width - followStatusLabel.Width - 16);
                taskPicker.Width = Math.Max(160, taskSection.ClientSize.Width - 32);
                pathLabel.Width = taskPicker.Width;
            };
            AddSectionDivider(taskSection);

            presetSection = new Panel { Dock = DockStyle.Top, Height = 104 };
            presetSection.Controls.Add(MakeLabel(UiText.T("配置预设"), 16, 10, 160, 22, true));
            enabledCountLabel = MakeLabel(UiText.CountEnabled(0), 224, 10, 160, 22, false);
            enabledCountLabel.TextAlign = ContentAlignment.MiddleRight;
            ((ThemedLabel)enabledCountLabel).Role = ThemedLabelRole.Secondary;
            presetPicker = new ThemedComboBox {
                Location = new Point(16, 38),
                Size = new Size(322, 30),
                IntegralHeight = false,
                DropDownHeight = 180
            };
            presetPicker.SelectionChangeCommitted += PresetChanged;
            presetPicker.DropDown += delegate { Activate(); };
            presetPicker.DropDownClosed += delegate {
                if (!presetPickerRebuildPending) return;
                presetPickerRebuildPending = false;
                BeginInvoke((MethodInvoker)delegate { RebuildPresetPicker(); });
            };
            presetMenuButton = new ThemedButton {
                Kind = ThemedButtonKind.Secondary,
                Glyph = GlyphKind.More,
                Location = new Point(346, 36),
                Size = new Size(38, 34),
                TabStop = false
            };
            presetMenu = new ContextMenuStrip();
            savePresetItem = new ToolStripMenuItem(UiText.T("保存为新预设"));
            updatePresetItem = new ToolStripMenuItem(UiText.T("更新当前预设"));
            undoItem = new ToolStripMenuItem(UiText.T("撤销最近一次应用"));
            savePresetItem.Click += SavePreset;
            updatePresetItem.Click += UpdatePreset;
            undoItem.Click += UndoPreset;
            undoItem.Enabled = false;
            presetMenu.Items.Add(savePresetItem);
            presetMenu.Items.Add(updatePresetItem);
            presetMenu.Items.Add(new ToolStripSeparator());
            presetMenu.Items.Add(undoItem);
            presetMenuButton.Click += delegate {
                Activate();
                BeginInvoke((MethodInvoker)delegate {
                    presetMenu.Show(presetMenuButton,
                        new Point(presetMenuButton.Width, presetMenuButton.Height),
                        ToolStripDropDownDirection.BelowLeft);
                });
            };
            tips.SetToolTip(presetMenuButton, UiText.T("保存、更新或撤销配置预设"));
            presetStatusLabel = MakeLabel("", 16, 72, 368, 24, false);
            ((ThemedLabel)presetStatusLabel).Role = ThemedLabelRole.Secondary;
            presetStatusLabel.AutoEllipsis = true;
            presetSection.Controls.Add(enabledCountLabel);
            presetSection.Controls.Add(presetPicker);
            presetSection.Controls.Add(presetMenuButton);
            presetSection.Controls.Add(presetStatusLabel);
            presetSection.Resize += delegate {
                presetMenuButton.Left = Math.Max(16, presetSection.ClientSize.Width - presetMenuButton.Width - 16);
                presetPicker.Width = Math.Max(120, presetMenuButton.Left - presetPicker.Left - 8);
                enabledCountLabel.Left = Math.Max(176, presetSection.ClientSize.Width - enabledCountLabel.Width - 16);
                presetStatusLabel.Width = Math.Max(120, presetSection.ClientSize.Width - 32);
            };
            AddSectionDivider(presetSection);

            instructionSection = new Panel { Dock = DockStyle.Fill };
            var instructionHeader = new Panel { Dock = DockStyle.Top, Height = 43 };
            instructionHeader.Controls.Add(MakeLabel(UiText.T("启用指令"), 16, 9, 170, 26, true));
            manageButton = new ThemedButton {
                Text = UiText.T("设置"),
                Glyph = GlyphKind.Settings,
                Kind = ThemedButtonKind.Ghost,
                Location = new Point(264, 6),
                Size = new Size(120, 32),
                ShowBorder = false,
                TabStop = false,
                AccessibleName = UiText.T("设置")
            };
            manageButton.Click += OpenManager;
            tips.SetToolTip(manageButton, UiText.T("管理指令、配置预设、语言、主题和数据目录"));
            instructionHeader.Controls.Add(manageButton);
            instructionHeader.Resize += delegate {
                manageButton.Left = Math.Max(16, instructionHeader.ClientSize.Width - manageButton.Width - 16);
            };
            profileList = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                AllowDrop = true,
                Padding = new Padding(16, 0, 16, 0),
                Margin = new Padding(0)
            };
            profileList.SizeChanged += delegate { ResizeProfileRows(); };
            profileList.DragEnter += ProfileDragEnter;
            profileList.DragOver += ProfileDragOver;
            profileList.DragDrop += ProfileDragDrop;
            instructionSection.Controls.Add(profileList);
            instructionSection.Controls.Add(instructionHeader);

            statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            statusLabel = new ThemedStatusLabel {
                Text = UiText.T("等待任务状态"),
                Dock = DockStyle.Fill,
                Padding = new Padding(15, 0, 0, 0),
                AutoEllipsis = true
            };
            statusLabel.TextChanged += delegate {
                UpdateHeaderSummaries();
                UpdateBubbleStatus();
            };
            footerMenuButton = new ThemedButton {
                Kind = ThemedButtonKind.Ghost,
                Glyph = GlyphKind.More,
                Dock = DockStyle.Right,
                Width = 48,
                ShowBorder = false,
                TabStop = false
            };
            footerMenu = new ContextMenuStrip();
            footerMenu.Items.Add(UiText.T("隐藏到托盘"), null, delegate { HideForUser(); });
            footerMenu.Items.Add(new ToolStripSeparator());
            footerMenu.Items.Add(UiText.T("退出"), null, delegate { ExitApplication(); });
            footerMenuButton.Click += delegate {
                footerMenu.Show(footerMenuButton, new Point(footerMenuButton.Width - footerMenu.Width, 0));
            };
            tips.SetToolTip(footerMenuButton, UiText.T("更多选项"));
            statusPanel.Controls.Add(statusLabel);
            statusPanel.Controls.Add(footerMenuButton);

            body.Controls.Add(instructionSection);
            body.Controls.Add(presetSection);
            body.Controls.Add(taskSection);
            expandedSurface.Controls.Add(body);
            expandedSurface.Controls.Add(statusPanel);
            expandedSurface.Controls.Add(headerPanel);
            Controls.Add(expandedSurface);
            Controls.Add(bubbleSurface);
            themeTransitionLayer = new ThemeTransitionLayer { Dock = DockStyle.Fill, Visible = false };
            Controls.Add(themeTransitionLayer);

            KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape && displayState == CompanionDisplayState.Expanded)
                {
                    CollapseToBubble();
                    e.Handled = true;
                }
            };
            ResizeEnd += delegate { SavePosition(); };
            FormClosing += HandleFormClosing;
            ApplyTheme();
        }

        private void AddSectionDivider(Panel panel)
        {
            panel.Paint += delegate(object sender, PaintEventArgs e) {
                ThemePalette palette = CompanionTheme.Palette(themeMode);
                using (var pen = new Pen(palette.Border))
                    e.Graphics.DrawLine(pen, 16, panel.ClientSize.Height - 1,
                        Math.Max(16, panel.ClientSize.Width - 16), panel.ClientSize.Height - 1);
            };
        }

        private Label MakeLabel(string text, int x, int y, int width, int height, bool bold)
        {
            return new ThemedLabel {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font(Font.FontFamily, 9F, bold ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private void AttachDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left || displayState != CompanionDisplayState.Expanded || transitioning) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLButtonDown, new IntPtr(HtCaption), IntPtr.Zero);
                EnsureCurrentBoundsVisible();
                SavePosition();
            };
        }

        private void ProfileMouseDown(object sender, MouseEventArgs e)
        {
            profileDragSource = null;
            if (e.Button != MouseButtons.Left) return;
            var toggle = sender as InstructionToggle;
            if (toggle == null || !CanStartProfileDrag(toggle) || !toggle.HitTestDragHandle(e.Location)) return;
            profileDragSource = toggle;
            profileDragStart = e.Location;
        }

        private void ProfileMouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || profileDragSource == null ||
                !Object.ReferenceEquals(profileDragSource, sender) ||
                !CanStartProfileDrag(profileDragSource)) return;
            int thresholdX = Math.Max(1, SystemInformation.DragSize.Width / 2);
            int thresholdY = Math.Max(1, SystemInformation.DragSize.Height / 2);
            if (Math.Abs(e.Location.X - profileDragStart.X) < thresholdX &&
                Math.Abs(e.Location.Y - profileDragStart.Y) < thresholdY) return;

            string id = profileDragSource.Tag as string;
            if (String.IsNullOrWhiteSpace(id)) return;
            profileDragInProgress = true;
            try
            {
                profileDragSource.DoDragDrop(id, DragDropEffects.Move);
            }
            finally
            {
                profileDragInProgress = false;
                profileDragSource = null;
            }
        }

        private void ProfileMouseUp(object sender, MouseEventArgs e)
        {
            if (!profileDragInProgress) profileDragSource = null;
        }

        private bool CanStartProfileDrag(InstructionToggle toggle)
        {
            if (toggle == null || displayState != CompanionDisplayState.Expanded || transitioning ||
                !toggle.Enabled || !toggle.Checked || !CanEditActive()) return false;
            string id = toggle.Tag as string;
            return !String.IsNullOrWhiteSpace(id) && committedOrder.Contains(id, StringComparer.OrdinalIgnoreCase);
        }

        private string ProfileDragId(DragEventArgs e)
        {
            if (e == null) return null;
            if (e.Data.GetDataPresent(typeof(string))) return e.Data.GetData(typeof(string)) as string;
            if (e.Data.GetDataPresent(DataFormats.StringFormat))
                return e.Data.GetData(DataFormats.StringFormat) as string;
            return null;
        }

        private bool IsProfileDragAllowed(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || profileDragSource == null ||
                !CanStartProfileDrag(profileDragSource)) return false;
            string sourceId = profileDragSource.Tag as string;
            return String.Equals(sourceId, id, StringComparison.OrdinalIgnoreCase);
        }

        private void ProfileDragEnter(object sender, DragEventArgs e)
        {
            ProfileDragOver(sender, e);
        }

        private void ProfileDragOver(object sender, DragEventArgs e)
        {
            string id = ProfileDragId(e);
            if (!IsProfileDragAllowed(id))
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Move;
            ScrollProfileList(new Point(e.X, e.Y));
        }

        private void ProfileDragDrop(object sender, DragEventArgs e)
        {
            string id = ProfileDragId(e);
            if (!IsProfileDragAllowed(id))
            {
                e.Effect = DragDropEffects.None;
                return;
            }

            int targetIndex = ProfileDropIndex(e.X, e.Y, id);
            string[] next = EnabledInstructionOrder.Move(committedOrder.ToArray(), id, targetIndex);
            if (next.SequenceEqual(committedOrder, StringComparer.OrdinalIgnoreCase))
            {
                ArrangeProfileControls(committedOrder);
                return;
            }

            SessionDescriptor descriptor = ActiveDescriptor();
            try
            {
                CommitEnabledOrder(descriptor, next);
            }
            catch (Exception error)
            {
                ArrangeProfileControls(committedOrder);
                statusLabel.Text = UiText.Error("保存失败，已恢复：") + UiText.Error(error.Message);
            }
        }

        private int ProfileDropIndex(int screenX, int screenY, string draggedId)
        {
            Point point = profileList.PointToClient(new Point(screenX, screenY));
            List<InstructionToggle> targets = profileList.Controls.Cast<Control>()
                .OfType<InstructionToggle>()
                .Where(item => item.Checked && !String.Equals(item.Tag as string, draggedId,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            for (int i = 0; i < targets.Count; i++)
            {
                if (point.Y < targets[i].Top + targets[i].Height / 2) return i;
            }
            return targets.Count;
        }

        private void ScrollProfileList(Point screenPoint)
        {
            if (profileList == null || !profileList.VerticalScroll.Visible) return;
            Point point = profileList.PointToClient(screenPoint);
            int edge = CompanionTheme.Scale(profileList, 24);
            int step = CompanionTheme.Scale(profileList, 28);
            int current = Math.Abs(profileList.AutoScrollPosition.Y);
            int desired = current;
            if (point.Y < edge) desired -= step;
            else if (point.Y > profileList.ClientSize.Height - edge) desired += step;
            int maximum = Math.Max(0, profileList.VerticalScroll.Maximum -
                profileList.VerticalScroll.LargeChange + 1);
            desired = Math.Max(0, Math.Min(maximum, desired));
            if (desired != current) profileList.AutoScrollPosition = new Point(0, desired);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateWindowRegion();
            ResizeProfileRows();
        }

        private void UpdateWindowRegion()
        {
            if (Width <= 0 || Height <= 0 || IsDisposed) return;
            bool bubble = bubbleSurface != null && bubbleSurface.Visible;
            int diameter = bubble ? Math.Min(Width, Height) : CompanionTheme.Scale(this, 32);
            IntPtr handle = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, diameter, diameter);
            Region next = Region.FromHrgn(handle);
            DeleteObject(handle);
            Region previous = Region;
            Region = next;
            if (previous != null) previous.Dispose();
        }

        private void ResizeProfileRows()
        {
            if (profileList == null || profileList.IsDisposed) return;
            int scrollbar = profileList.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int width = Math.Max(120, profileList.ClientSize.Width - profileList.Padding.Horizontal - scrollbar - 2);
            foreach (Control control in profileList.Controls) control.Width = width;
        }

        private void ArrangeProfileControls(IEnumerable<string> enabledOrder)
        {
            if (profileList == null || profileList.IsDisposed) return;
            var desired = new List<Control>();
            var used = new HashSet<Control>();
            foreach (string id in enabledOrder ?? new string[0])
            {
                CheckBox check;
                if (profileChecks.TryGetValue(id, out check) && used.Add(check)) desired.Add(check);
            }
            foreach (InstructionDto profile in library.instructions ?? new InstructionDto[0])
            {
                CheckBox check;
                if (profileChecks.TryGetValue(profile.id, out check) && used.Add(check)) desired.Add(check);
            }
            foreach (Control control in profileList.Controls.Cast<Control>().ToArray())
                if (used.Add(control)) desired.Add(control);

            profileList.SuspendLayout();
            try
            {
                for (int i = 0; i < desired.Count; i++) profileList.Controls.SetChildIndex(desired[i], i);
            }
            finally
            {
                profileList.ResumeLayout(true);
            }
            ResizeProfileRows();
        }

        private List<string> BuildEnabledOrder(IEnumerable<string> preferredOrder)
        {
            var enabled = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in preferredOrder ?? new string[0])
            {
                CheckBox check;
                if (String.IsNullOrWhiteSpace(id) || !profileChecks.TryGetValue(id, out check) ||
                    !check.Checked || !seen.Add(id)) continue;
                enabled.Add(id);
            }
            foreach (InstructionDto profile in library.instructions ?? new InstructionDto[0])
            {
                CheckBox check;
                if (profileChecks.TryGetValue(profile.id, out check) && check.Checked && seen.Add(profile.id))
                    enabled.Add(profile.id);
            }
            return enabled;
        }

        private void ChangeTheme(ThemeMode requestedMode)
        {
            if (themeMode == requestedMode) return;
            if (transitioning) FinishTransition();
            if (themeTransitioning) FinishThemeTransition();
            ThemeMode previousMode = themeMode;
            ThemePalette previousPalette = CompanionTheme.Palette(previousMode);
            Bitmap frame = CaptureThemeFrame();
            bool animate = Visible && displayState != CompanionDisplayState.UserHidden &&
                themeTransitionLayer != null;
            if (animate)
            {
                themeTransitionLayer.SetFrame(frame, previousPalette.Window);
                themeTransitionLayer.Visible = true;
                themeTransitionLayer.BringToFront();
                themeTransitionLayer.Update();
            }
            themeMode = requestedMode;
            ApplyThemeCore();
            if (animate)
            {
                ThemePalette targetPalette = CompanionTheme.Palette(themeMode);
                themeTransitionLayer.SetTargetFrame(CaptureThemeSurfaceFrame(), targetPalette.Window);
                themeTransitionStartedAt = DateTime.UtcNow;
                themeTransitioning = true;
                themeTimer.Start();
            }
            else if (frame != null)
            {
                frame.Dispose();
            }
            SavePosition();
        }

        private void ApplyTheme()
        {
            FinishThemeTransition();
            ApplyThemeCore();
        }

        private void ApplyThemeCore()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            CompanionTheme.Apply(this, themeMode);
            BackColor = palette.BorderStrong;
            if (expandedSurface != null) expandedSurface.BackColor = palette.Window;
            if (headerPanel != null) headerPanel.BackColor = palette.Surface;
            if (taskSection != null) taskSection.BackColor = palette.Window;
            if (presetSection != null) presetSection.BackColor = palette.Window;
            if (instructionSection != null) instructionSection.BackColor = palette.Window;
            if (profileList != null) profileList.BackColor = palette.Window;
            if (statusPanel != null) statusPanel.BackColor = palette.Surface;
            if (presetMenu != null) CompanionTheme.ApplyToolStrip(presetMenu, themeMode);
            if (footerMenu != null) CompanionTheme.ApplyToolStrip(footerMenu, themeMode);
            if (trayMenu != null) CompanionTheme.ApplyToolStrip(trayMenu, themeMode);
            if (trayMenu != null && trayIcon != null)
            {
                Icon previous = trayIcon;
                trayIcon = CreateTrayIcon();
                tray.Icon = trayIcon;
                previous.Dispose();
            }
            CompanionTheme.ApplyWindow(this, themeMode);
            UpdateFollowStatus();
            UpdateBubbleStatus();
            Invalidate(true);
            UpdateWindowRegion();
        }

        private Bitmap CaptureThemeFrame()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return null;
            Bitmap frame = new Bitmap(ClientSize.Width, ClientSize.Height);
            try
            {
                DrawToBitmap(frame, new Rectangle(Point.Empty, ClientSize));
                if (FrameHasContent(frame)) return frame;
            }
            catch
            {
                // The fallback below captures the visible client surface directly.
            }
            try
            {
                using (Graphics graphics = Graphics.FromImage(frame))
                    graphics.CopyFromScreen(PointToScreen(Point.Empty), Point.Empty, ClientSize);
                return frame;
            }
            catch
            {
                frame.Dispose();
                return null;
            }
        }

        private Bitmap CaptureThemeSurfaceFrame()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return null;
            Bitmap frame = new Bitmap(ClientSize.Width, ClientSize.Height);
            try
            {
                using (Graphics graphics = Graphics.FromImage(frame)) graphics.Clear(BackColor);
                Control surface = expandedSurface != null && expandedSurface.Visible
                    ? (Control)expandedSurface
                    : bubbleSurface != null && bubbleSurface.Visible ? bubbleSurface : null;
                if (surface == null)
                {
                    frame.Dispose();
                    return null;
                }
                surface.DrawToBitmap(frame, surface.Bounds);
                if (FrameHasContent(frame)) return frame;
            }
            catch
            {
            }
            frame.Dispose();
            return null;
        }

        private bool FrameHasContent(Bitmap frame)
        {
            if (frame == null || frame.Width <= 0 || frame.Height <= 0) return false;
            Color first = frame.GetPixel(0, 0);
            int[] samples = { 1, 3, 5, 7, 9 };
            foreach (int step in samples)
            {
                int x = Math.Min(frame.Width - 1, frame.Width * step / 10);
                int y = Math.Min(frame.Height - 1, frame.Height * (10 - step) / 10);
                Color sample = frame.GetPixel(x, y);
                if (Math.Abs(sample.R - first.R) + Math.Abs(sample.G - first.G) +
                    Math.Abs(sample.B - first.B) > 12) return true;
            }
            return false;
        }

        private void AnimateThemeTransition(object sender, EventArgs e)
        {
            if (!themeTransitioning || themeTransitionLayer == null)
            {
                themeTimer.Stop();
                return;
            }
            double progress = Math.Max(0D, Math.Min(1D,
                (DateTime.UtcNow - themeTransitionStartedAt).TotalMilliseconds /
                ThemeTransitionMilliseconds));
            if (progress >= 1D)
            {
                FinishThemeTransition();
                return;
            }
            themeTransitionLayer.FrameOpacity = Math.Max(1,
                (int)Math.Round(255D * (1D - EaseTransition(progress))));
        }

        private void FinishThemeTransition()
        {
            themeTimer.Stop();
            themeTransitioning = false;
            if (themeTransitionLayer != null) themeTransitionLayer.ClearFrame();
        }

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(UiText.T("显示面板"), null, delegate { ShowPreferred(CompanionViewMode.Expanded, true); });
            trayMenu.Items.Add(UiText.T("显示悬浮球"), null, delegate { ShowPreferred(CompanionViewMode.Bubble, true); });
            trayMenu.Items.Add(UiText.T("设置"), null, OpenSettingsFromTray);
            trayMenu.Items.Add(UiText.T("隐藏"), null, delegate { HideForUser(); });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(UiText.T("退出"), null, delegate { ExitApplication(); });
            trayIcon = CreateTrayIcon();
            tray.Icon = trayIcon;
            tray.Text = "Instruction Switcher";
            tray.ContextMenuStrip = trayMenu;
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowPreferred(CompanionViewMode.Expanded, true); };
            CompanionTheme.ApplyToolStrip(trayMenu, themeMode);
        }

        private void OpenSettingsFromTray(object sender, EventArgs e)
        {
            ShowPreferred(CompanionViewMode.Expanded, true);
            BeginInvoke((MethodInvoker)delegate { OpenManager(null, EventArgs.Empty); });
        }

        private void ChangeLanguage(UiLanguage language)
        {
            if (UiText.Current == language) return;
            FinishThemeTransition();
            FinishTransition();
            SavePosition();
            UiText.Current = language;
            windowPosition.language = UiText.Code(language);
            SavePosition();

            pollTimer.Stop();
            transitionTimer.Stop();
            themeTimer.Stop();
            if (tray != null) tray.Visible = false;
            allowExit = true;
            BeginInvoke((MethodInvoker)delegate {
                string executable = Application.ExecutablePath;
                string arguments = "--restart-after " +
                    Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture) + " " +
                    QuoteArgument(runtimeRoot);
                Process.Start(new ProcessStartInfo {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false
                });
                Close();
            });
        }

        private Icon CreateTrayIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                ThemePalette palette = CompanionTheme.Palette(themeMode);
                using (var background = new SolidBrush(palette.Window))
                using (var border = new Pen(palette.BorderStrong, 1.5F))
                {
                    graphics.FillEllipse(background, 2, 2, 27, 27);
                    graphics.DrawEllipse(border, 2, 2, 27, 27);
                }
                CompanionTheme.DrawGlyph(graphics, GlyphKind.Sliders, new Rectangle(8, 8, 16, 16),
                    palette.Text, palette.Window, 1.4F);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private void Poll()
        {
            try
            {
                if (!managerOpen)
                {
                    UpdatePanelVisibility();
                    RefreshLibrary();
                    RefreshSessions();
                    RefreshFocus();
                    RefreshState();
                    RefreshAcknowledgement();
                    if (undoState != null && DateTime.UtcNow > undoExpiresAt)
                    {
                        ClearUndo();
                    }
                }
            }
            catch (Exception error)
            {
                statusLabel.Text = UiText.Error("读取失败：") + UiText.Error(error.Message);
            }

            TrackCodex();
        }

        private void RefreshFocus()
        {
            FocusSnapshot snapshot;
            string reason;
            bool confirmed = TryReadFocus(out snapshot, out reason);
            if (!confirmed)
            {
                bool changed = focusConfirmed || !String.Equals(focusReason, reason, StringComparison.Ordinal);
                focusConfirmed = false;
                focusReason = reason;
                if (followLatest.Checked)
                {
                    string fallback = sessions.Count > 0 ? sessions[0].key : null;
                    if (!String.Equals(activeKey, fallback, StringComparison.OrdinalIgnoreCase)) changed = true;
                    activeKey = fallback;
                    selectionConfirmed = false;
                }
                if (changed)
                {
                    RebuildTaskPicker();
                    ApplyDescriptor();
                }
                return;
            }

            SessionDescriptor descriptor = sessions.FirstOrDefault(item =>
                String.Equals(item.key, snapshot.key, StringComparison.OrdinalIgnoreCase));
            if (descriptor == null) descriptor = ReadDescriptorForKey(snapshot.key);
            if (descriptor == null && focusedDescriptor != null &&
                String.Equals(focusedDescriptor.key, snapshot.key, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(focusedDescriptor.source, "focus", StringComparison.OrdinalIgnoreCase))
            {
                descriptor = focusedDescriptor;
                if (!String.IsNullOrWhiteSpace(snapshot.title)) descriptor.project = snapshot.title;
                descriptor.updatedAt = snapshot.observedAt;
            }
            if (descriptor == null) descriptor = FocusDescriptor(snapshot);

            string previousIdentity = DescriptorIdentity(focusedDescriptor);
            bool focusChanged = !focusConfirmed ||
                !String.Equals(focusedKey, snapshot.key, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(previousIdentity, DescriptorIdentity(descriptor), StringComparison.Ordinal);
            focusedKey = snapshot.key;
            focusedDescriptor = descriptor;
            focusConfirmed = true;
            focusReason = null;
            if (followLatest.Checked)
            {
                if (!String.Equals(activeKey, focusedKey, StringComparison.OrdinalIgnoreCase)) focusChanged = true;
                activeKey = focusedKey;
                selectionConfirmed = true;
            }
            if (focusChanged)
            {
                RebuildTaskPicker();
                ApplyDescriptor();
            }
        }

        private bool TryReadFocus(out FocusSnapshot snapshot, out string reason)
        {
            snapshot = null;
            reason = UiText.Error("前台任务探测尚未就绪");
            try
            {
                if (!File.Exists(focusFile)) return false;
                snapshot = json.Deserialize<FocusSnapshot>(File.ReadAllText(focusFile, Encoding.UTF8));
                if (snapshot == null || snapshot.version != 1)
                {
                    reason = UiText.Error("前台任务探测数据无效");
                    return false;
                }
                if (!snapshot.available)
                {
                    reason = UiText.Error("暂时无法确认 Codex 前台任务");
                    return false;
                }
                DateTime observed;
                if (!DateTime.TryParse(snapshot.observedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out observed))
                {
                    reason = UiText.Error("前台任务探测时间无效");
                    return false;
                }
                double age = (DateTime.UtcNow - observed.ToUniversalTime()).TotalSeconds;
                if (age < -5 || age > 5)
                {
                    reason = UiText.Error("前台任务探测已断开");
                    return false;
                }
                if (String.IsNullOrWhiteSpace(snapshot.sessionId) ||
                    !IsValidSessionKey(snapshot.key) ||
                    !String.Equals(SessionKey(snapshot.sessionId), snapshot.key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    reason = UiText.Error("前台任务映射校验失败");
                    return false;
                }
                return true;
            }
            catch
            {
                reason = UiText.Error("前台任务探测读取失败");
                return false;
            }
        }

        private SessionDescriptor ReadDescriptorForKey(string key)
        {
            try
            {
                if (!IsValidSessionKey(key)) return null;
                var file = new FileInfo(Path.Combine(sessionRoot, key.ToLowerInvariant() + ".json"));
                if (!file.Exists) return null;
                SessionDescriptor descriptor = json.Deserialize<SessionDescriptor>(
                    File.ReadAllText(file.FullName, Encoding.UTF8));
                return ValidDescriptor(file, descriptor) ? descriptor : null;
            }
            catch
            {
                return null;
            }
        }

        private void RefreshLibrary()
        {
            string next = LibraryStore.Signature(configFile);
            if (next == librarySignature) return;
            try
            {
                library = LibraryStore.Load(configFile, Path.GetDirectoryName(configFile));
                librarySignature = next;
                libraryReady = true;
                profileSignature = "";
                lastStateError = null;
                if (presetPicker != null) RebuildPresetPicker();
                if (profileList != null) ApplyDescriptor();
            }
            catch (Exception error)
            {
                libraryReady = false;
                librarySignature = "unavailable";
                SetProfileEditingEnabled(false);
                statusLabel.Text = UiText.IsEnglish ? "Instruction library read failed: " + UiText.Error(error.Message) : "指令库读取失败：" + error.Message;
            }
        }

        private SessionDescriptor FocusDescriptor(FocusSnapshot snapshot)
        {
            return new SessionDescriptor {
                version = 3,
                key = snapshot.key,
                project = String.IsNullOrWhiteSpace(snapshot.title) ? "Codex task" : snapshot.title,
                cwd = "",
                instructions = library.instructions,
                presets = library.presets,
                defaultPresetId = library.defaultPresetId,
                profiles = (library.instructions ?? new InstructionDto[0])
                    .Select(item => new ProfileDto { id = item.id, label = item.name }).ToArray(),
                source = "focus",
                updatedAt = snapshot.observedAt
            };
        }

        private string DescriptorIdentity(SessionDescriptor descriptor)
        {
            if (descriptor == null) return "";
            return descriptor.version + "|" + descriptor.key + "|" + descriptor.project + "|" +
                descriptor.cwd + "|" + descriptor.source;
        }

        private string SessionKey(string sessionId)
        {
            byte[] digest;
            using (var sha = SHA256.Create())
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
            var key = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) key.Append(value.ToString("x2"));
            return key.ToString();
        }

        private void RefreshSessions()
        {
            var files = new DirectoryInfo(sessionRoot)
                .GetFiles("*.json")
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(24)
                .ToArray();
            string nextSignature = String.Join("|", files.Select(file =>
                file.Name + ":" + file.LastWriteTimeUtc.Ticks + ":" + file.Length));
            if (nextSignature == descriptorSignature) return;

            var nextSessions = new List<SessionDescriptor>();
            bool complete = true;
            foreach (FileInfo file in files)
            {
                string failureKey = file.FullName + ":" + file.LastWriteTimeUtc.Ticks + ":" + file.Length;
                try
                {
                    var descriptor = json.Deserialize<SessionDescriptor>(File.ReadAllText(file.FullName, Encoding.UTF8));
                    if (!ValidDescriptor(file, descriptor))
                    {
                        if (!CanSkipDescriptor(failureKey)) complete = false;
                        continue;
                    }
                    descriptorFailures.Remove(failureKey);
                    nextSessions.Add(descriptor);
                }
                catch
                {
                    if (!CanSkipDescriptor(failureKey)) complete = false;
                }
            }

            if (!complete) return;
            descriptorFailures.Clear();
            descriptorSignature = nextSignature;
            sessions.Clear();
            sessions.AddRange(nextSessions);
            if (!String.IsNullOrWhiteSpace(focusedKey))
            {
                SessionDescriptor registered = sessions.FirstOrDefault(item =>
                    String.Equals(item.key, focusedKey, StringComparison.OrdinalIgnoreCase));
                if (registered != null) focusedDescriptor = registered;
            }

            if (sessions.Count == 0 && focusedDescriptor == null)
            {
                activeKey = null;
                selectionConfirmed = false;
                RebuildTaskPicker();
                ApplyDescriptor();
                return;
            }

            bool activeExists = DescriptorForKey(activeKey) != null;
            if (followLatest.Checked)
            {
                if (focusConfirmed && focusedDescriptor != null)
                {
                    activeKey = focusedDescriptor.key;
                    selectionConfirmed = true;
                }
                else if (sessions.Count > 0)
                {
                    activeKey = sessions[0].key;
                    selectionConfirmed = false;
                }
            }
            else if (!activeExists && sessions.Count > 0)
            {
                activeKey = sessions[0].key;
                selectionConfirmed = false;
            }
            RebuildTaskPicker();
            ApplyDescriptor();
        }

        private bool CanSkipDescriptor(string failureKey)
        {
            int failures;
            descriptorFailures.TryGetValue(failureKey, out failures);
            failures++;
            descriptorFailures[failureKey] = failures;
            return failures >= 3;
        }

        private bool ValidDescriptor(FileInfo file, SessionDescriptor descriptor)
        {
            if (descriptor == null || (descriptor.version != 1 && descriptor.version != 2 && descriptor.version != 3) ||
                !IsValidSessionKey(descriptor.key)) return false;
            if (!String.Equals(Path.GetFileNameWithoutExtension(file.Name), descriptor.key,
                StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private void RebuildTaskPicker()
        {
            taskPicker.Items.Clear();
            int selected = -1;
            List<SessionDescriptor> visible = VisibleSessions();
            for (int i = 0; i < visible.Count; i++)
            {
                taskPicker.Items.Add(new TaskItem(visible[i]));
                if (String.Equals(visible[i].key, activeKey, StringComparison.OrdinalIgnoreCase)) selected = i;
            }
            taskPicker.SelectedIndex = selected;
        }

        private List<SessionDescriptor> VisibleSessions()
        {
            var visible = new List<SessionDescriptor>();
            if (focusedDescriptor != null) visible.Add(focusedDescriptor);
            visible.AddRange(sessions.Where(item => focusedDescriptor == null ||
                !String.Equals(item.key, focusedDescriptor.key, StringComparison.OrdinalIgnoreCase)));
            return visible;
        }

        private void RebuildPresetPicker()
        {
            if (presetPicker == null) return;
            if (presetPicker.DroppedDown)
            {
                presetPickerRebuildPending = true;
                return;
            }
            presetPickerRebuildPending = false;
            suppressPresetChange = true;
            presetPicker.Items.Clear();
            presetPicker.Items.Add(new PresetItem(null, UiText.T("自定义")));
            int selected = 0;
            PresetDto[] presets = library.presets ?? new PresetDto[0];
            for (int i = 0; i < presets.Length; i++)
            {
                presetPicker.Items.Add(new PresetItem(presets[i]));
                if (String.Equals(presets[i].id, committedPresetId, StringComparison.Ordinal)) selected = i + 1;
            }
            presetPicker.SelectedIndex = selected;
            suppressPresetChange = false;
            PresetDto current = presets.FirstOrDefault(item => String.Equals(item.id, committedPresetId, StringComparison.Ordinal));
            if (presetStatusLabel != null)
                presetStatusLabel.Text = (current == null ? UiText.T("自定义配置") : current.name) + " · " + UiText.CountItems(committedEnabled.Count);
            if (updatePresetItem != null) updatePresetItem.Enabled = CanEditActive() && presets.Length > 0;
            if (savePresetItem != null) savePresetItem.Enabled = CanEditActive();
            presetPicker.Enabled = CanEditActive() && presets.Length > 0;
        }

        private void TaskChanged(object sender, EventArgs e)
        {
            var item = taskPicker.SelectedItem as TaskItem;
            if (item == null) return;
            activeKey = item.Descriptor.key;
            selectionConfirmed = true;
            followLatest.Checked = false;
            ApplyDescriptor();
        }

        private void FollowChanged(object sender, EventArgs e)
        {
            if (followLatest.Checked)
            {
                if (focusConfirmed && focusedDescriptor != null)
                {
                    activeKey = focusedDescriptor.key;
                    selectionConfirmed = true;
                }
                else
                {
                    selectionConfirmed = false;
                    if (sessions.Count > 0) activeKey = sessions[0].key;
                }
                RebuildTaskPicker();
            }
            ApplyDescriptor();
        }

        private void PresetChanged(object sender, EventArgs e)
        {
            if (suppressPresetChange) return;
            PresetItem selected = presetPicker.SelectedItem as PresetItem;
            if (selected == null || selected.Preset == null || !CanEditActive())
            {
                RebuildPresetPicker();
                return;
            }
            ApplyPresetToCurrentTask(selected.Preset);
        }

        private void ApplyPresetToCurrentTask(PresetDto preset)
        {
            if (preset == null || !CanEditActive())
            {
                RebuildPresetPicker();
                statusLabel.Text = UiText.Error("配置预设已导入，当前任务暂不可编辑");
                return;
            }
            SessionDescriptor descriptor = ActiveDescriptor();
            try
            {
                undoState = new SessionState {
                    version = 3,
                    enabled = committedOrder.ToArray(),
                    activePresetId = committedPresetId,
                    revision = currentRevision,
                    updatedAt = DateTime.UtcNow.ToString("o")
                };
                SessionState state = WriteState(StatePathForKey(descriptor.key),
                    preset.instructionIds ?? new string[0], currentRevision, preset.id);
                ApplyStateSnapshot(descriptor, state, true);
                stateSignature = StateSignature(descriptor);
                undoExpiresAt = DateTime.UtcNow.AddSeconds(10);
                undoKey = descriptor.key;
                undoAppliedRevision = state.revision;
                undoItem.Enabled = true;
                UpdateStatus();
            }
            catch (Exception error)
            {
                ClearUndo();
                RebuildPresetPicker();
                statusLabel.Text = UiText.Error("应用失败：") + UiText.Error(error.Message);
            }
        }

        private void SavePreset(object sender, EventArgs e)
        {
            if (!CanEditActive() || !libraryReady) return;
            SessionDescriptor initialDescriptor = ActiveDescriptor();
            if (initialDescriptor == null) return;
            string taskKey = initialDescriptor.key;
            string expectedRevision = currentRevision;
            string[] order = committedOrder.ToArray();
            string expectedLibrarySignature = librarySignature;
            string name;
            if (!NamePromptForm.Ask(this, UiText.T("保存为配置预设"), UiText.T("新配置预设"), themeMode, out name)) return;
            if (!CanEditActive() || !MatchesTaskSnapshot(taskKey, expectedRevision, order))
            {
                statusLabel.Text = UiText.Error("任务或状态已更新，请重新确认写入目标");
                return;
            }
            string id = LibraryStore.NewId("preset");
            string now = DateTime.UtcNow.ToString("o");
            SettingsDto next;
            try
            {
                next = CloneLibrary();
                var presets = (next.presets ?? new PresetDto[0]).ToList();
                presets.Add(new PresetDto {
                    id = id,
                    name = name,
                    instructionIds = order,
                    createdAt = now,
                    updatedAt = now
                });
                next.presets = presets.ToArray();
            }
            catch (Exception error)
            {
                statusLabel.Text = UiText.Error("保存预设失败：") + UiText.Error(error.Message);
                return;
            }
            TryCommitPresetAndState(next, taskKey, expectedRevision, order, id,
                (UiText.IsEnglish ? "Saved preset " + UiText.Quote(name) : "已保存配置预设“" + name + "”"), expectedLibrarySignature);
        }

        private void UpdatePreset(object sender, EventArgs e)
        {
            if (!CanEditActive() || !libraryReady || (library.presets ?? new PresetDto[0]).Length == 0) return;
            SessionDescriptor initialDescriptor = ActiveDescriptor();
            if (initialDescriptor == null) return;
            string taskKey = initialDescriptor.key;
            string expectedRevision = currentRevision;
            string[] order = committedOrder.ToArray();
            string expectedLibrarySignature = librarySignature;
            PresetDto target = (library.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                String.Equals(item.id, committedPresetId, StringComparison.Ordinal));
            if (target == null && !PresetSelectionForm.SelectPreset(this, library.presets, themeMode, out target)) return;
            string updateMessage = UiText.IsEnglish
                ? "Save the current task's " + committedOrder.Count + " enabled instructions to " + UiText.Quote(target.name) + "?"
                : "将当前任务的 " + committedOrder.Count + " 条指令写入“" + target.name + "”？";
            if (MessageBox.Show(this, updateMessage,
                UiText.T("更新配置预设"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!CanEditActive() || !MatchesTaskSnapshot(taskKey, expectedRevision, order))
            {
                statusLabel.Text = UiText.Error("任务或状态已更新，请重新确认写入目标");
                return;
            }
            SettingsDto next;
            try
            {
                next = CloneLibrary();
                PresetDto changed = next.presets.First(item => String.Equals(item.id, target.id, StringComparison.Ordinal));
                changed.instructionIds = order;
                changed.updatedAt = DateTime.UtcNow.ToString("o");
            }
            catch (Exception error)
            {
                statusLabel.Text = UiText.Error("更新预设失败：") + UiText.Error(error.Message);
                return;
            }
            TryCommitPresetAndState(next, taskKey, expectedRevision, order, target.id,
                (UiText.IsEnglish ? "Updated preset " + UiText.Quote(target.name) : "已更新配置预设“" + target.name + "”"), expectedLibrarySignature);
        }

        private bool MatchesTaskSnapshot(string taskKey, string revision, string[] order)
        {
            SessionDescriptor descriptor = ActiveDescriptor();
            return descriptor != null && String.Equals(descriptor.key, taskKey, StringComparison.OrdinalIgnoreCase) &&
                String.Equals(currentRevision, revision, StringComparison.Ordinal) &&
                committedOrder.SequenceEqual(order ?? new string[0], StringComparer.Ordinal);
        }

        private bool TryCommitPresetAndState(SettingsDto next, string taskKey, string expectedRevision,
            string[] order, string presetId, string successText, string expectedLibrarySignature)
        {
            ConfigCommit commit;
            try
            {
                commit = LibraryStore.Save(configFile, Path.GetDirectoryName(configFile), next, expectedLibrarySignature);
            }
            catch (Exception error)
            {
                statusLabel.Text = UiText.Error("预设保存失败：") + UiText.Error(error.Message);
                return false;
            }

            try
            {
                WriteState(StatePathForKey(taskKey), order, expectedRevision, presetId);
            }
            catch (Exception error)
            {
                RecoverPresetCommit(commit, error);
                return false;
            }

            try
            {
                librarySignature = "";
                RefreshLibrary();
                if (!libraryReady)
                {
                    statusLabel.Text = successText + (UiText.IsEnglish ? "; " : "；") +
                        UiText.Error("任务状态已更新，指令库重新读取失败");
                    return true;
                }
                SessionDescriptor active = ActiveDescriptor();
                if (active != null && String.Equals(active.key, taskKey, StringComparison.OrdinalIgnoreCase))
                {
                    stateSignature = "";
                    RefreshState();
                    if (!stateLoaded || !String.IsNullOrWhiteSpace(lastStateError))
                    {
                        statusLabel.Text = successText + (UiText.IsEnglish ? "; " : "；") +
                            UiText.Error("任务状态已更新，重新读取失败");
                        return true;
                    }
                }
            }
            catch (Exception error)
            {
                statusLabel.Text = successText + (UiText.IsEnglish ? "; " : "；") +
                    UiText.Error("配置和任务状态已保存，界面刷新失败：") + UiText.Error(error.Message);
                return true;
            }
            statusLabel.Text = successText;
            return true;
        }

        private void RecoverPresetCommit(ConfigCommit commit, Exception stateError)
        {
            bool restored = false;
            string rollbackError = null;
            try
            {
                restored = LibraryStore.TryRollback(commit);
            }
            catch (Exception error)
            {
                rollbackError = error.Message;
            }

            librarySignature = "";
            RefreshLibrary();
            RefreshState();
            if (restored)
            {
                statusLabel.Text = UiText.Error("任务状态发生变化，预设配置已恢复：") + UiText.Error(stateError.Message);
            }
            else if (!String.IsNullOrWhiteSpace(rollbackError))
            {
                statusLabel.Text = UiText.Error("预设已保存，任务状态未更新，配置回滚失败：") + UiText.Error(rollbackError);
            }
            else
            {
                statusLabel.Text = UiText.Error("预设已保存，任务状态未更新；配置已被其他窗口修改，保留当前配置：") + UiText.Error(stateError.Message);
            }
        }

        private void UndoPreset(object sender, EventArgs e)
        {
            SessionDescriptor activeDescriptor = ActiveDescriptor();
            if (undoState == null || DateTime.UtcNow > undoExpiresAt || !CanEditActive() ||
                activeDescriptor == null || !String.Equals(undoKey, activeDescriptor.key, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(currentRevision, undoAppliedRevision, StringComparison.Ordinal))
            {
                ClearUndo();
                return;
            }
            try
            {
                string active = MatchingPreset(undoState.enabled, undoState.activePresetId);
                SessionState state = WriteState(StatePathForKey(activeDescriptor.key), undoState.enabled, currentRevision, active);
                ApplyStateSnapshot(activeDescriptor, state, true);
                stateSignature = StateSignature(activeDescriptor);
                statusLabel.Text = UiText.Error("已撤销最近一次预设应用");
            }
            catch (Exception error)
            {
                statusLabel.Text = UiText.Error("撤销失败：") + UiText.Error(error.Message);
            }
            finally
            {
                ClearUndo();
            }
        }

        private void ClearUndo()
        {
            undoState = null;
            undoKey = null;
            undoAppliedRevision = null;
            if (undoItem != null) undoItem.Enabled = false;
        }

        private SettingsDto CloneLibrary()
        {
            return json.Deserialize<SettingsDto>(json.Serialize(library));
        }

        private void OpenManager(object sender, EventArgs e)
        {
            if (managerOpen) return;
            managerOpen = true;
            string importedPresetId = null;
            bool settingsApplied = false;
            UiLanguage requestedLanguage = UiText.Current;
            ThemeMode requestedTheme = themeMode;
            try
            {
                using (var manager = new LibraryManagerForm(configFile, Path.GetDirectoryName(configFile), stateRoot, themeMode))
                {
                    manager.TopMost = true;
                    manager.PrepareForModalDisplay();
                    TopMost = false;
                    manager.ShowDialog(this);
                    importedPresetId = manager.ImportedPresetIdToApply;
                    settingsApplied = manager.SettingsApplied;
                    requestedLanguage = manager.RequestedLanguage;
                    requestedTheme = manager.RequestedThemeMode;
                }
            }
            finally
            {
                managerOpen = false;
                if (!allowExit && !IsDisposed)
                {
                    librarySignature = "";
                    RefreshLibrary();
                    ApplyDescriptor();
                    UpdatePanelVisibility();
                    if (!String.IsNullOrWhiteSpace(importedPresetId))
                    {
                        PresetDto imported = (library.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                            String.Equals(item.id, importedPresetId, StringComparison.Ordinal));
                        if (imported != null) ApplyPresetToCurrentTask(imported);
                    }
                    if (settingsApplied)
                    {
                        ChangeTheme(requestedTheme);
                        ChangeLanguage(requestedLanguage);
                    }
                }
            }
        }

        private SessionDescriptor ActiveDescriptor()
        {
            return DescriptorForKey(activeKey);
        }

        private SessionDescriptor DescriptorForKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key)) return null;
            if (focusedDescriptor != null &&
                String.Equals(focusedDescriptor.key, key, StringComparison.OrdinalIgnoreCase))
                return focusedDescriptor;
            return sessions.FirstOrDefault(item =>
                String.Equals(item.key, key, StringComparison.OrdinalIgnoreCase));
        }

        private void ApplyDescriptor()
        {
            SessionDescriptor descriptor = ActiveDescriptor();
            if (descriptor == null)
            {
                ClearUndo();
                pathLabel.Text = UiText.T("等待 Codex 任务");
                tips.SetToolTip(pathLabel, "");
                selectionConfirmed = false;
                profileSignature = "";
                stateSignature = "";
                acknowledgementSignature = "";
                currentRevision = null;
                acknowledgedRevision = null;
                committedEnabled.Clear();
                committedOrder.Clear();
                committedPresetId = null;
                stateLoaded = false;
                BuildProfiles(null);
                RebuildPresetPicker();
                statusLabel.Text = UiText.T("等待任务状态");
                return;
            }

            string location = String.IsNullOrWhiteSpace(descriptor.cwd)
                ? UiText.T("当前任务") + ": " + (String.IsNullOrWhiteSpace(descriptor.project) ? "Codex task" : descriptor.project)
                : descriptor.cwd;
            pathLabel.Text = location;
            tips.SetToolTip(pathLabel, location);
            string nextProfileSignature = descriptor.key + "|" + librarySignature + "|" + String.Join("|",
                (library.instructions ?? new InstructionDto[0]).Select(item => item.id + ":" + item.name)) + "|" +
                String.Join("|", (library.presets ?? new PresetDto[0]).Select(item => item.id + ":" + item.name + ":" +
                    String.Join(",", item.instructionIds ?? new string[0])));
            if (nextProfileSignature != profileSignature || !stateLoaded || stateSignature == "missing")
            {
                profileSignature = nextProfileSignature;
                stateSignature = "";
                acknowledgementSignature = "";
                currentRevision = null;
                acknowledgedRevision = null;
                committedEnabled.Clear();
                committedOrder.Clear();
                committedPresetId = null;
                stateLoaded = false;
                BuildProfiles(descriptor);
            }
            SetProfileEditingEnabled(CanEditActive());
            UpdateStatus();
        }

        private void BuildProfiles(SessionDescriptor descriptor)
        {
            suppressProfileChange = true;
            profileChecks.Clear();
            foreach (Control control in profileList.Controls.Cast<Control>().ToArray())
            {
                profileList.Controls.Remove(control);
                control.Dispose();
            }

            if (descriptor == null)
            {
                profileList.Controls.Add(MakePlaceholder(UiText.T("打开或恢复一个 Codex 任务")));
                suppressProfileChange = false;
                return;
            }

            if (!libraryReady)
            {
                stateLoaded = false;
                profileList.Controls.Add(MakePlaceholder(UiText.T("指令库暂不可用")));
                suppressProfileChange = false;
                SetProfileEditingEnabled(false);
                return;
            }

            InstructionDto[] profiles = library.instructions ?? new InstructionDto[0];
            SessionState state;
            string error;
            if (TryReadState(descriptor, out state, out error))
            {
                ApplyStateSnapshot(descriptor, state, false);
                stateSignature = StateSignature(descriptor);
            }
            else
            {
                stateLoaded = false;
                lastStateError = error;
                committedEnabled.Clear();
                committedOrder.Clear();
            }

            profiles = profiles.Where(ShouldShowInstruction).ToArray();

            if (profiles.Length == 0)
            {
                profileList.Controls.Add(MakePlaceholder(UiText.T("自定义列表中没有可显示的指令")));
                suppressProfileChange = false;
            }
            else
            {
                foreach (InstructionDto profile in profiles)
                {
                    string summary = InstructionSummary(profile);
                    if (String.Equals(profile.origin, "preset-package", StringComparison.Ordinal))
                        summary = UiText.T("随预设导入") + (summary.Length == 0 ? "" : " · " + summary);
                    var check = new InstructionToggle {
                        TitleText = profile.name,
                        SummaryText = summary,
                        Tag = profile.id,
                        Checked = committedEnabled.Contains(profile.id),
                        Enabled = false,
                        Size = new Size(336, 64),
                        Margin = new Padding(0),
                        ThemeMode = themeMode,
                        AllowDrop = true
                    };
                    check.CheckedChanged += ProfileChanged;
                    check.MouseDown += ProfileMouseDown;
                    check.MouseMove += ProfileMouseMove;
                    check.MouseUp += ProfileMouseUp;
                    check.DragEnter += ProfileDragEnter;
                    check.DragOver += ProfileDragOver;
                    check.DragDrop += ProfileDragDrop;
                    profileChecks[profile.id] = check;
                    profileList.Controls.Add(check);
                    tips.SetToolTip(check, profile.name + " (" + profile.id + ")\n" +
                        (UiText.IsEnglish ? "Drag the left handle to reorder when enabled" : "启用后可拖动左侧手柄调整顺序"));
                }
                suppressProfileChange = false;
            }
            ArrangeProfileControls(committedOrder);
            ResizeProfileRows();
            acknowledgementSignature = "";
            RefreshAcknowledgement();
            SetProfileEditingEnabled(CanEditActive());
            UpdateStatus();
        }

        private string InstructionSummary(InstructionDto instruction)
        {
            try
            {
                string body = LibraryStore.ReadBody(Path.GetDirectoryName(configFile), instruction);
                string first = body.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
                first = Regex.Replace(first, @"^[#>*\-\s]+", "");
                return first.Length > 72 ? first.Substring(0, 72) + "…" : first;
            }
            catch
            {
                return UiText.T("正文暂不可用");
            }
        }

        private bool ShouldShowInstruction(InstructionDto instruction)
        {
            return instruction != null && (instruction.showInCustomPicker != false || committedEnabled.Contains(instruction.id));
        }

        private bool ProfileControlsMatchVisibility()
        {
            var expected = new HashSet<string>(
                (library.instructions ?? new InstructionDto[0]).Where(ShouldShowInstruction).Select(item => item.id),
                StringComparer.OrdinalIgnoreCase);
            return expected.Count == profileChecks.Count && expected.All(id => profileChecks.ContainsKey(id));
        }

        private Label MakePlaceholder(string text)
        {
            var label = MakeLabel(text, 0, 0, 336, 54, false);
            ((ThemedLabel)label).Role = ThemedLabelRole.Secondary;
            return label;
        }

        private bool CanInitializeDefault(SessionDescriptor descriptor)
        {
            bool focused = focusConfirmed && String.Equals(focusedKey, descriptor.key, StringComparison.OrdinalIgnoreCase);
            bool manual = !followLatest.Checked && selectionConfirmed &&
                String.Equals(activeKey, descriptor.key, StringComparison.OrdinalIgnoreCase);
            return focused || manual;
        }

        private string MatchingPreset(string[] enabled, string preferredId)
        {
            enabled = enabled ?? new string[0];
            PresetDto preferred = (library.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                String.Equals(item.id, preferredId, StringComparison.Ordinal));
            if (preferred != null && enabled.SequenceEqual(preferred.instructionIds ?? new string[0], StringComparer.Ordinal))
                return preferred.id;
            PresetDto match = (library.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                enabled.SequenceEqual(item.instructionIds ?? new string[0], StringComparer.Ordinal));
            return match == null ? null : match.id;
        }

        private bool TryReadState(SessionDescriptor descriptor, out SessionState state, out string error)
        {
            state = null;
            error = null;
            if (!libraryReady)
            {
                error = UiText.T("指令库暂不可用");
                return false;
            }
            try
            {
                string target = StatePathForKey(descriptor.key);
                if (!File.Exists(target))
                {
                    string legacy = LegacyStatePath(descriptor);
                    if (legacy == null || !File.Exists(legacy))
                    {
                        PresetDto defaultPreset = (library.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                            String.Equals(item.id, library.defaultPresetId, StringComparison.Ordinal));
                        if (defaultPreset != null && CanInitializeDefault(descriptor))
                        {
                            state = WriteState(target, defaultPreset.instructionIds ?? new string[0], null, defaultPreset.id);
                            return true;
                        }
                        state = new SessionState { version = 3, enabled = new string[0], activePresetId = null };
                        return true;
                    }
                    target = legacy;
                }
                state = json.Deserialize<SessionState>(File.ReadAllText(target, Encoding.UTF8));
                if (state == null || state.enabled == null ||
                    (state.version != 1 && state.version != 2 && state.version != 3))
                    throw new InvalidDataException("状态文件格式无效");
                var valid = new HashSet<string>((library.instructions ?? new InstructionDto[0]).Select(item => item.id),
                    StringComparer.Ordinal);
                string[] normalized = state.enabled.Where(id => valid.Contains(id)).Distinct().ToArray();
                string active = MatchingPreset(normalized, state.activePresetId);
                bool needsUpgrade = state.version != 3 || String.IsNullOrWhiteSpace(state.revision) ||
                    !state.enabled.SequenceEqual(normalized, StringComparer.Ordinal) ||
                    !String.Equals(state.activePresetId, active, StringComparison.Ordinal);
                if (needsUpgrade)
                {
                    string canonical = StatePathForKey(descriptor.key);
                    string expected = String.Equals(target, canonical, StringComparison.OrdinalIgnoreCase)
                        ? state.revision : null;
                    state = WriteState(canonical, normalized, expected, active);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private void RefreshState()
        {
            if (!libraryReady) return;
            SessionDescriptor descriptor = ActiveDescriptor();
            if (descriptor == null) return;
            string next = StateSignature(descriptor);
            if (next == stateSignature) return;

            SessionState state;
            string error;
            if (!TryReadState(descriptor, out state, out error))
            {
                stateLoaded = false;
                lastStateError = error;
                SetProfileEditingEnabled(false);
                UpdateStatus();
                return;
            }
            stateSignature = next;
            ApplyStateSnapshot(descriptor, state, true);
            if (undoState != null && (!String.Equals(undoKey, descriptor.key, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(undoAppliedRevision, currentRevision, StringComparison.Ordinal))) ClearUndo();
            SetProfileEditingEnabled(CanEditActive());
            UpdateStatus();
        }

        private void ApplyStateSnapshot(SessionDescriptor descriptor, SessionState state, bool updateChecks)
        {
            var valid = new HashSet<string>(
                (library.instructions ?? new InstructionDto[0]).Select(profile => profile.id),
                StringComparer.OrdinalIgnoreCase);
            committedOrder = (state.enabled ?? new string[0]).Where(id => valid.Contains(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            committedEnabled = new HashSet<string>(committedOrder, StringComparer.OrdinalIgnoreCase);
            committedPresetId = MatchingPreset(committedOrder.ToArray(), state.activePresetId);
            currentRevision = state.revision;
            stateLoaded = true;
            lastStateError = null;
            RebuildPresetPicker();
            if (!updateChecks) return;

            if (!ProfileControlsMatchVisibility())
            {
                BuildProfiles(descriptor);
                return;
            }

            suppressProfileChange = true;
            foreach (KeyValuePair<string, CheckBox> item in profileChecks)
                item.Value.Checked = committedEnabled.Contains(item.Key);
            suppressProfileChange = false;
            ArrangeProfileControls(committedOrder);
        }

        private void ProfileChanged(object sender, EventArgs e)
        {
            if (suppressProfileChange) return;
            SessionDescriptor descriptor = ActiveDescriptor();
            if (descriptor == null || !CanEditActive())
            {
                RestoreCommittedSelection();
                return;
            }

            try
            {
                CommitEnabledOrder(descriptor, committedOrder);
            }
            catch (Exception error)
            {
                RestoreCommittedSelection();
                statusLabel.Text = UiText.IsEnglish ? "Save failed; restored: " + UiText.Error(error.Message) : "保存失败，已恢复：" + error.Message;
            }
        }

        private void CommitEnabledOrder(SessionDescriptor descriptor, IEnumerable<string> preferredOrder)
        {
            if (descriptor == null || !CanEditActive())
                throw new InvalidOperationException(UiText.Error("当前任务处于只读状态"));
            List<string> enabled = BuildEnabledOrder(preferredOrder);
            string activePreset = MatchingPreset(enabled.ToArray(), null);
            SessionState state = WriteState(StatePathForKey(descriptor.key), enabled.ToArray(),
                currentRevision, activePreset);
            committedOrder = enabled;
            committedEnabled = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            committedPresetId = state.activePresetId;
            currentRevision = state.revision;
            stateLoaded = true;
            stateSignature = StateSignature(descriptor);
            ClearUndo();
            ArrangeProfileControls(committedOrder);
            SetProfileEditingEnabled(CanEditActive());
            RebuildPresetPicker();
            UpdateStatus();
        }

        private SessionState WriteState(string target, string[] enabled, string expectedRevision, string activePresetId)
        {
            target = Path.GetFullPath(target);
            string prefix = Path.GetFullPath(stateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !target.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("状态文件路径无效");
            using (StateFileLock.Acquire(target))
            {
                string actualRevision = null;
                if (File.Exists(target))
                {
                    SessionState current = json.Deserialize<SessionState>(File.ReadAllText(target, Encoding.UTF8));
                    if (current == null || current.enabled == null ||
                        (current.version != 1 && current.version != 2 && current.version != 3))
                        throw new InvalidDataException("状态文件格式无效");
                    actualRevision = current.revision;
                }
                if (!String.Equals(actualRevision, expectedRevision, StringComparison.Ordinal))
                    throw new InvalidOperationException("状态已在其他窗口更新，请重新选择任务");

                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temp = target + "." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N") + ".tmp";
                var state = new SessionState {
                    version = 3,
                    revision = Guid.NewGuid().ToString("D"),
                    enabled = (enabled ?? new string[0]).Distinct(StringComparer.Ordinal).ToArray(),
                    activePresetId = activePresetId,
                    updatedAt = DateTime.UtcNow.ToString("o")
                };
                try
                {
                    File.WriteAllText(temp, json.Serialize(state), new UTF8Encoding(false));
                    if (!MoveFileEx(temp, target, MoveFileReplaceExisting | MoveFileWriteThrough))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                return state;
            }
        }

        private void RefreshAcknowledgement()
        {
            SessionDescriptor descriptor = ActiveDescriptor();
            if (descriptor == null) return;
            string target = AcknowledgementPathForKey(descriptor.key);
            string next = FileSignature(target);
            if (next == acknowledgementSignature) return;
            try
            {
                if (!File.Exists(target))
                {
                    acknowledgedRevision = null;
                    lastAcknowledgementError = null;
                    acknowledgementSignature = next;
                    UpdateStatus();
                    return;
                }
                HookAcknowledgement acknowledgement = json.Deserialize<HookAcknowledgement>(
                    File.ReadAllText(target, Encoding.UTF8));
                if (acknowledgement == null || acknowledgement.version != 1 ||
                    !String.Equals(acknowledgement.key, descriptor.key, StringComparison.OrdinalIgnoreCase) ||
                    String.IsNullOrWhiteSpace(acknowledgement.revision))
                    throw new InvalidDataException("Hook 回执格式无效");
                acknowledgedRevision = acknowledgement.revision;
                lastAcknowledgementError = null;
                acknowledgementSignature = next;
                UpdateStatus();
            }
            catch (Exception error)
            {
                lastAcknowledgementError = error.Message;
                UpdateStatus();
            }
        }

        private bool CanEditActive()
        {
            SessionDescriptor descriptor = ActiveDescriptor();
            bool exactFocus = followLatest.Checked && focusConfirmed &&
                String.Equals(activeKey, focusedKey, StringComparison.OrdinalIgnoreCase);
            bool manual = !followLatest.Checked && selectionConfirmed;
            return libraryReady && descriptor != null && (exactFocus || manual) && stateLoaded &&
                String.Equals(activeKey, descriptor.key, StringComparison.OrdinalIgnoreCase);
        }

        private void SetProfileEditingEnabled(bool enabled)
        {
            foreach (CheckBox check in profileChecks.Values)
            {
                check.Enabled = enabled;
                var toggle = check as InstructionToggle;
                if (toggle != null) toggle.AllowReorder = enabled;
            }
            if (presetPicker != null) presetPicker.Enabled = enabled && (library.presets ?? new PresetDto[0]).Length > 0;
            if (savePresetItem != null) savePresetItem.Enabled = enabled;
            if (updatePresetItem != null) updatePresetItem.Enabled = enabled && (library.presets ?? new PresetDto[0]).Length > 0;
        }

        private void RestoreCommittedSelection()
        {
            suppressProfileChange = true;
            foreach (KeyValuePair<string, CheckBox> item in profileChecks)
                item.Value.Checked = committedEnabled.Contains(item.Key);
            suppressProfileChange = false;
            ArrangeProfileControls(committedOrder);
            SetProfileEditingEnabled(CanEditActive());
        }

        private bool IsValidSessionKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key) || key.Length != 64) return false;
            foreach (char value in key)
            {
                bool digit = value >= '0' && value <= '9';
                bool lower = value >= 'a' && value <= 'f';
                bool upper = value >= 'A' && value <= 'F';
                if (!digit && !lower && !upper) return false;
            }
            return true;
        }

        private bool IsValidProfileId(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 64) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char value = id[i];
                bool letter = value >= 'a' && value <= 'z';
                bool digit = value >= '0' && value <= '9';
                bool extra = value == '_' || value == '-';
                if (!(letter || digit || (i > 0 && extra))) return false;
            }
            return true;
        }

        private string StatePathForKey(string key)
        {
            if (!IsValidSessionKey(key)) throw new InvalidOperationException("会话标识无效");
            return Path.Combine(stateRoot, key.ToLowerInvariant() + ".json");
        }

        private string LegacyStatePath(SessionDescriptor descriptor)
        {
            if (descriptor == null || descriptor.version != 1 ||
                String.IsNullOrWhiteSpace(descriptor.stateFile)) return null;
            try
            {
                string target = Path.GetFullPath(descriptor.stateFile);
                string expected = descriptor.key.ToLowerInvariant() + ".json";
                if (!String.Equals(Path.GetFileName(target), expected, StringComparison.OrdinalIgnoreCase))
                    return null;
                return target;
            }
            catch
            {
                return null;
            }
        }

        private string StateSignature(SessionDescriptor descriptor)
        {
            string canonical = StatePathForKey(descriptor.key);
            string signature = FileSignature(canonical);
            if (signature != "missing") return "canonical:" + signature;
            string legacy = LegacyStatePath(descriptor);
            return legacy == null ? "missing" : "legacy:" + FileSignature(legacy);
        }

        private string AcknowledgementPathForKey(string key)
        {
            if (!IsValidSessionKey(key)) throw new InvalidOperationException("会话标识无效");
            return Path.Combine(acknowledgementRoot, key.ToLowerInvariant() + ".json");
        }

        private string FileSignature(string file)
        {
            try
            {
                var info = new FileInfo(file);
                return info.Exists ? info.LastWriteTimeUtc.Ticks + ":" + info.Length : "missing";
            }
            catch
            {
                return "unavailable";
            }
        }

        private void UpdateStatus()
        {
            int count = committedEnabled.Count;
            UpdateHeaderSummaries();
            if (!String.IsNullOrWhiteSpace(lastStateError))
            {
                statusLabel.Text = UiText.IsEnglish ? "State read failed: " + UiText.Error(lastStateError) : "状态读取失败：" + lastStateError;
                return;
            }
            if (followLatest.Checked && !focusConfirmed)
            {
                statusLabel.Text = UiText.IsEnglish ? "Detecting active task · Read-only preview · " + UiText.CountItems(count) : "正在识别前台任务 · 只读预览 · " + count + " 项";
                return;
            }
            if (followLatest.Checked && focusConfirmed)
            {
                if (!String.IsNullOrWhiteSpace(lastAcknowledgementError))
                {
                    statusLabel.Text = UiText.IsEnglish ? "Following current task · Hook acknowledgement error" : "已跟随当前任务 · Hook 回执异常";
                    return;
                }
                if (!String.IsNullOrWhiteSpace(currentRevision) &&
                    String.Equals(currentRevision, acknowledgedRevision, StringComparison.Ordinal))
                {
                    statusLabel.Text = UiText.IsEnglish ? "Following · Hook read the state · " + UiText.CountItems(count) : "已跟随 · Hook 已读取 · " + count + " 项";
                    return;
                }
                if (!String.IsNullOrWhiteSpace(currentRevision))
                {
                    statusLabel.Text = UiText.IsEnglish ? "Following · Saved · " + UiText.CountItems(count) : "已跟随 · 已保存 · " + count + " 项";
                    return;
                }
                statusLabel.Text = UiText.IsEnglish ? "Following current task · " + UiText.CountItems(count) : "已跟随当前任务 · " + count + " 项";
                return;
            }
            if (!selectionConfirmed)
            {
                statusLabel.Text = UiText.Error("请选择写入目标");
                return;
            }
            if (!stateLoaded)
            {
                statusLabel.Text = UiText.T("等待任务状态");
                return;
            }
            if (!String.IsNullOrWhiteSpace(lastAcknowledgementError))
            {
                statusLabel.Text = UiText.Error("Hook 回执读取失败");
                return;
            }
            if (!String.IsNullOrWhiteSpace(currentRevision) &&
                String.Equals(currentRevision, acknowledgedRevision, StringComparison.Ordinal))
            {
                statusLabel.Text = UiText.IsEnglish ? "Hook read the state · " + UiText.CountItems(count) : "Hook 已读取 · " + count + " 项";
                return;
            }
            if (!String.IsNullOrWhiteSpace(currentRevision))
            {
                statusLabel.Text = UiText.IsEnglish ? "Saved; waiting for the next message · " + UiText.CountItems(count) : "已保存，等待下一条消息 · " + count + " 项";
                return;
            }
            statusLabel.Text = count == 0 ? UiText.Error("尚未启用指令") :
                (UiText.IsEnglish ? "Confirmed · " + UiText.CountItems(count) : "已确认 · " + count + " 项");
        }

        private void UpdateHeaderSummaries()
        {
            if (enabledCountLabel != null)
                enabledCountLabel.Text = UiText.CountEnabled(committedEnabled.Count);
            UpdateFollowStatus();
        }

        private void UpdateFollowStatus()
        {
            if (followStatusLabel == null || followLatest == null) return;
            if (followLatest.Checked && focusConfirmed)
            {
                followStatusLabel.Text = UiText.T("已准确跟随");
                followStatusLabel.Tone = StatusTone.Accent;
            }
            else if (followLatest.Checked)
            {
                followStatusLabel.Text = UiText.T("正在识别");
                followStatusLabel.Tone = StatusTone.Warning;
            }
            else if (selectionConfirmed)
            {
                followStatusLabel.Text = UiText.T("手动目标");
                followStatusLabel.Tone = StatusTone.Neutral;
            }
            else
            {
                followStatusLabel.Text = UiText.T("请选择任务");
                followStatusLabel.Tone = StatusTone.Warning;
            }
        }

        private void UpdateBubbleStatus()
        {
            if (bubbleSurface == null) return;
            string value = statusLabel == null ? "" : statusLabel.Text ?? "";
            if (value.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("异常", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("错误", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0)
                bubbleSurface.StatusTone = StatusTone.Danger;
            else if (value.IndexOf("等待", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("识别", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("只读", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("waiting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("detecting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("read-only", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("unsaved", StringComparison.OrdinalIgnoreCase) >= 0)
                bubbleSurface.StatusTone = StatusTone.Warning;
            else
                bubbleSurface.StatusTone = StatusTone.Accent;
            string tooltip = UiText.IsEnglish
                ? "Open instruction panel · " + UiText.CountEnabled(committedEnabled.Count)
                : "展开指令面板 · " + committedEnabled.Count + " 条指令已启用";
            tips.SetToolTip(bubbleSurface, tooltip);
            bubbleSurface.AccessibleDescription = tooltip;
            bubbleSurface.Invalidate();
        }

        private void TrackCodex()
        {
            DateTime now = DateTime.UtcNow;
            if (codexWindowCheckRunning || now < nextCodexWindowCheck) return;
            codexWindowCheckRunning = true;
            nextCodexWindowCheck = now.AddSeconds(1);
            ThreadPool.QueueUserWorkItem(delegate {
                bool hasOpenWindow = false;
                int debugPort = 0;
                try
                {
                    hasOpenWindow = HasOpenCodexWindow();
                    if (hasOpenWindow) debugPort = FindCodexDebugPort();
                }
                catch
                {
                    hasOpenWindow = false;
                }

                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    codexWindowCheckRunning = false;
                    nextCodexWindowCheck = DateTime.MinValue;
                    return;
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate {
                        CompleteCodexWindowCheck(hasOpenWindow, debugPort);
                    });
                }
                catch
                {
                    codexWindowCheckRunning = false;
                }
            });
        }

        private void CompleteCodexWindowCheck(bool hasOpenWindow, int debugPort)
        {
            codexWindowCheckRunning = false;
            DateTime now = DateTime.UtcNow;
            if (hasOpenWindow)
            {
                lastCodexSeen = now;
                EnsureFocusTracker(debugPort);
                return;
            }
            StopFocusTracker();
            if (CodexLifecycle.ShouldExit(lastCodexSeen, now)) ExitApplication();
        }

        private void EnsureFocusTracker(int port)
        {
            try
            {
                bool trackerRunning = false;
                if (focusTracker != null)
                {
                    trackerRunning = !focusTracker.HasExited;
                    if (!trackerRunning)
                    {
                        focusTracker.Dispose();
                        focusTracker = null;
                        focusTrackerPort = 0;
                    }
                }
                if (!CodexLifecycle.ShouldRestartFocusTracker(
                    trackerRunning, focusTrackerPort, port)) return;
                if (trackerRunning) StopFocusTracker();

                string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus-tracker.mjs");
                if (!File.Exists(script)) return;
                int parentPid;
                using (Process current = Process.GetCurrentProcess()) parentPid = current.Id;
                var start = new ProcessStartInfo {
                    FileName = "node.exe",
                    Arguments = QuoteArgument(script) + " " + QuoteArgument(runtimeRoot) + " " +
                        port.ToString(CultureInfo.InvariantCulture) + " " +
                        parentPid.ToString(CultureInfo.InvariantCulture),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                focusTracker = Process.Start(start);
                focusTrackerPort = focusTracker == null ? 0 : port;
            }
            catch
            {
                focusReason = UiText.Error("前台任务探测器启动失败");
                if (focusTracker != null) focusTracker.Dispose();
                focusTracker = null;
                focusTrackerPort = 0;
            }
        }

        private int FindCodexDebugPort()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE Name = 'ChatGPT.exe'"))
                using (ManagementObjectCollection processes = searcher.Get())
                {
                    foreach (ManagementBaseObject process in processes)
                    {
                        string commandLine = process["CommandLine"] as string;
                        if (!CodexLifecycle.IsPrimaryProcessCommandLine(commandLine)) continue;
                        Match match = Regex.Match(commandLine,
                            @"--remote-debugging-port(?:=|\s+)(\d+)", RegexOptions.IgnoreCase);
                        int port;
                        if (match.Success && Int32.TryParse(match.Groups[1].Value,
                            NumberStyles.None, CultureInfo.InvariantCulture, out port) &&
                            port > 0 && port <= 65535) return port;
                    }
                }
            }
            catch
            {
                return 0;
            }
            return 0;
        }

        private string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void StopFocusTracker()
        {
            if (focusTracker == null) return;
            try
            {
                if (!focusTracker.HasExited) focusTracker.Kill();
            }
            catch
            {
                // The child may have exited between the status check and termination.
            }
            finally
            {
                focusTracker.Dispose();
                focusTracker = null;
                focusTrackerPort = 0;
            }
        }

        private bool HasOpenCodexWindow()
        {
            Process[] processes = Process.GetProcessesByName("ChatGPT");
            try
            {
                foreach (Process process in processes)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (CodexLifecycle.IsOpenWindow(
                        handle,
                        IsWindowVisible(handle),
                        IsIconic(handle))) return true;
                }
                return false;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        private bool IsCodexForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || IsIconic(foreground)) return false;
            uint processId;
            GetWindowThreadProcessId(foreground, out processId);
            if (processId == 0) return false;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                    return String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private bool IsCompanionForeground()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;
            uint foregroundProcessId;
            GetWindowThreadProcessId(foreground, out foregroundProcessId);
            using (Process current = Process.GetCurrentProcess())
                return foregroundProcessId == current.Id;
        }

        private bool HasUsableCodexWindow()
        {
            Process[] processes = Process.GetProcessesByName("ChatGPT");
            try
            {
                foreach (Process process in processes)
                {
                    IntPtr handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && IsWindowVisible(handle) && !IsIconic(handle)) return true;
                }
                return false;
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }

        private void UpdatePanelVisibility()
        {
            if (OwnedForms.Any(form => form.Visible)) return;
            if ((taskPicker != null && taskPicker.DroppedDown) ||
                (presetPicker != null && presetPicker.DroppedDown) ||
                (presetMenu != null && presetMenu.Visible) ||
                (footerMenu != null && footerMenu.Visible) ||
                (trayMenu != null && trayMenu.Visible)) return;

            bool codexForeground = IsCodexForeground();
            bool companionForeground = IsCompanionForeground();
            bool contextActive = codexForeground || (companionForeground && HasUsableCodexWindow());
            if (!contextActive)
            {
                if (displayState != CompanionDisplayState.UserHidden && Visible)
                    SuppressForContext();
                return;
            }

            if (codexForeground) lastCodexSeen = DateTime.UtcNow;
            if (displayState == CompanionDisplayState.ContextSuppressed && (codexForeground || companionForeground))
                ShowPreferred(preferredView, false);
            else if (!Visible && displayState != CompanionDisplayState.UserHidden && codexForeground)
                ShowPreferred(preferredView, false);
            else if (Visible && displayState != CompanionDisplayState.UserHidden)
                TopMost = true;
        }

        private void ShowPreferred(CompanionViewMode mode, bool activate)
        {
            if (allowExit) return;
            preferredView = mode;
            if (displayState == CompanionDisplayState.UserHidden ||
                displayState == CompanionDisplayState.ContextSuppressed)
                displayState = mode == CompanionViewMode.Bubble
                    ? CompanionDisplayState.Bubble : CompanionDisplayState.Expanded;

            if (!Visible)
            {
                SuspendLayout();
                try
                {
                    ApplyInitialPlacementIfNeeded();
                    SetVisualMode(mode, false);
                    Rectangle placement = mode == CompanionViewMode.Bubble
                        ? ResolvePlacement(windowPosition.bubble, Size, true)
                        : ResolvePlacement(windowPosition.expanded, expandedWindowSize, false);
                    Location = placement.Location;
                    displayState = mode == CompanionViewMode.Bubble
                        ? CompanionDisplayState.Bubble : CompanionDisplayState.Expanded;
                    WindowState = FormWindowState.Normal;
                    TopMost = true;
                    CompanionTheme.ApplyWindow(this, themeMode);
                }
                finally
                {
                    ResumeLayout(true);
                }
                Show();
            }
            else if (mode == CompanionViewMode.Bubble && displayState == CompanionDisplayState.Expanded)
            {
                CollapseToBubble();
            }
            else if (mode == CompanionViewMode.Expanded && displayState == CompanionDisplayState.Bubble)
            {
                ExpandFromBubble();
            }

            WindowState = FormWindowState.Normal;
            TopMost = true;
            if (activate)
            {
                BringToFront();
                Activate();
            }
            SavePosition();
        }

        private void HideForUser()
        {
            FinishThemeTransition();
            FinishTransition();
            if (Visible) SavePosition();
            displayState = CompanionDisplayState.UserHidden;
            TopMost = false;
            Hide();
        }

        private void SuppressForContext()
        {
            if (displayState == CompanionDisplayState.UserHidden) return;
            FinishThemeTransition();
            FinishTransition();
            if (Visible) SavePosition();
            displayState = CompanionDisplayState.ContextSuppressed;
            TopMost = false;
            Hide();
        }

        private void CollapseToBubble()
        {
            if (displayState != CompanionDisplayState.Expanded || transitioning) return;
            preferredView = CompanionViewMode.Bubble;
            SaveCurrentPlacement(false);
            Rectangle target = BubbleBoundsFromPanel();
            StartTransition(target, delegate {
                SetVisualMode(CompanionViewMode.Bubble, false);
                displayState = CompanionDisplayState.Bubble;
                SavePosition();
            });
        }

        private void ExpandFromBubble()
        {
            if (displayState != CompanionDisplayState.Bubble || transitioning) return;
            preferredView = CompanionViewMode.Expanded;
            SaveCurrentPlacement(true);
            Rectangle target = ExpandedBoundsFromBubble();
            StartTransition(target, delegate {
                SetVisualMode(CompanionViewMode.Expanded, false);
                displayState = CompanionDisplayState.Expanded;
                SavePosition();
            });
        }

        private void SetVisualMode(CompanionViewMode mode, bool preserveSize)
        {
            if (!preserveSize)
            {
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
            }
            if (mode == CompanionViewMode.Expanded)
            {
                if (expandedWindowSize.IsEmpty) expandedWindowSize = Size;
                expandedSurface.Visible = true;
                bubbleSurface.Visible = false;
                expandedSurface.BringToFront();
                if (!preserveSize) Size = expandedWindowSize;
                MinimumSize = Size;
                MaximumSize = Size;
            }
            else
            {
                Size bubbleSize = BubbleSize();
                bubbleSurface.Visible = true;
                expandedSurface.Visible = false;
                bubbleSurface.BringToFront();
                if (!preserveSize) Size = bubbleSize;
                MinimumSize = Size;
                MaximumSize = Size;
            }
            UpdateWindowRegion();
            UpdateBubbleStatus();
        }

        private Size BubbleSize()
        {
            int diameter = CompanionTheme.Scale(this, 58);
            return new Size(diameter, diameter);
        }

        private void StartTransition(Rectangle target, Action completed)
        {
            FinishTransition();
            MinimumSize = Size.Empty;
            MaximumSize = Size.Empty;
            transitionTargetBounds = ClampRectangle(target);
            transitionStartedAt = DateTime.UtcNow;
            transitionCompleted = completed;
            transitionPhase = TransitionPhase.FadingOut;
            transitionModeApplied = false;
            transitioning = true;
            Opacity = 1D;
            transitionTimer.Start();
        }

        private void AnimateWindowTransition(object sender, EventArgs e)
        {
            if (!transitioning)
            {
                transitionTimer.Stop();
                return;
            }
            double duration = transitionPhase == TransitionPhase.FadingOut
                ? TransitionFadeOutMilliseconds : TransitionFadeInMilliseconds;
            double progress = Math.Max(0D, Math.Min(1D,
                (DateTime.UtcNow - transitionStartedAt).TotalMilliseconds / duration));
            double eased = EaseTransition(progress);

            if (transitionPhase == TransitionPhase.FadingOut)
            {
                Opacity = 1D - eased;
                if (progress < 1D) return;

                ApplyTransitionTarget();
                transitionPhase = TransitionPhase.FadingIn;
                transitionStartedAt = DateTime.UtcNow;
                Opacity = 0D;
                return;
            }

            Opacity = eased;
            if (progress < 1D) return;

            Opacity = 1D;
            transitionTimer.Stop();
            transitioning = false;
            transitionPhase = TransitionPhase.None;
            transitionModeApplied = false;
        }

        private double EaseTransition(double progress)
        {
            return 1D - Math.Pow(1D - progress, 3D);
        }

        private void ApplyTransitionTarget()
        {
            if (transitionModeApplied) return;
            SuspendLayout();
            try
            {
                Bounds = transitionTargetBounds;
                transitionModeApplied = true;
                Action done = transitionCompleted;
                transitionCompleted = null;
                if (done != null) done();
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        private void FinishTransition()
        {
            FinishThemeTransition();
            if (!transitioning)
            {
                Opacity = 1D;
                return;
            }
            transitionTimer.Stop();
            ApplyTransitionTarget();
            Opacity = 1D;
            transitioning = false;
            transitionPhase = TransitionPhase.None;
            transitionModeApplied = false;
        }

        private void BubbleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || transitioning) return;
            bubbleDragging = false;
            bubbleMouseDown = Cursor.Position;
            bubbleWindowStart = Location;
            bubbleSurface.Capture = true;
        }

        private void BubbleMouseMove(object sender, MouseEventArgs e)
        {
            if (!bubbleSurface.Capture || transitioning) return;
            Point current = Cursor.Position;
            int threshold = SystemInformation.DragSize.Width;
            if (!bubbleDragging && (Math.Abs(current.X - bubbleMouseDown.X) > threshold ||
                Math.Abs(current.Y - bubbleMouseDown.Y) > threshold)) bubbleDragging = true;
            if (!bubbleDragging) return;
            Location = new Point(bubbleWindowStart.X + current.X - bubbleMouseDown.X,
                bubbleWindowStart.Y + current.Y - bubbleMouseDown.Y);
            EnsureCurrentBoundsVisible();
        }

        private void BubbleMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            bubbleSurface.Capture = false;
            bool dragged = bubbleDragging;
            bubbleDragging = false;
            if (dragged) SavePosition();
            else ExpandFromBubble();
        }

        private void ApplyInitialPlacement()
        {
            ApplyInitialPlacementIfNeeded();
            if (preferredView == CompanionViewMode.Bubble)
            {
                SetVisualMode(CompanionViewMode.Bubble, false);
                Location = ResolvePlacement(windowPosition.bubble, Size, true).Location;
                displayState = CompanionDisplayState.Bubble;
            }
            else
            {
                SetVisualMode(CompanionViewMode.Expanded, false);
                Location = ResolvePlacement(windowPosition.expanded, Size, false).Location;
                displayState = CompanionDisplayState.Expanded;
            }
            initialPlacementApplied = true;
            EnsureCurrentBoundsVisible();
            ApplyTheme();
        }

        private void ApplyInitialPlacementIfNeeded()
        {
            if (!expandedWindowSize.IsEmpty) return;
            expandedWindowSize = Size;
            if (expandedWindowSize.Width < 200 || expandedWindowSize.Height < 300)
                expandedWindowSize = new Size(400, 660);
        }

        private Rectangle BubbleBoundsFromPanel()
        {
            Size size = BubbleSize();
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            bool right = Math.Abs(area.Right - Bounds.Right) <= Math.Abs(Bounds.Left - area.Left);
            int x = right ? Bounds.Right - size.Width : Bounds.Left;
            int y = Bounds.Bottom - size.Height;
            return ClampRectangle(new Rectangle(x, y, size.Width, size.Height));
        }

        private Rectangle ExpandedBoundsFromBubble()
        {
            ApplyInitialPlacementIfNeeded();
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            bool right = Bounds.Left + Bounds.Width / 2 >= area.Left + area.Width / 2;
            bool bottom = Bounds.Top + Bounds.Height / 2 >= area.Top + area.Height / 2;
            int x = right ? Bounds.Right - expandedWindowSize.Width : Bounds.Left;
            int y = bottom ? Bounds.Bottom - expandedWindowSize.Height : Bounds.Top;
            return ClampRectangle(new Rectangle(x, y, expandedWindowSize.Width, expandedWindowSize.Height));
        }

        private Rectangle ClampRectangle(Rectangle rectangle)
        {
            Screen screen = Screen.FromRectangle(rectangle);
            Rectangle area = screen.WorkingArea;
            int x = Math.Max(area.Left, Math.Min(rectangle.Left, area.Right - rectangle.Width));
            int y = Math.Max(area.Top, Math.Min(rectangle.Top, area.Bottom - rectangle.Height));
            return new Rectangle(x, y, rectangle.Width, rectangle.Height);
        }

        private void EnsureCurrentBoundsVisible()
        {
            if (!Visible || Width <= 0 || Height <= 0) return;
            Rectangle clamped = ClampRectangle(Bounds);
            if (clamped.Location != Location) Location = clamped.Location;
        }

        private void SaveCurrentPlacement(bool bubble)
        {
            if (!Visible) return;
            WindowPlacement placement = CapturePlacement(Bounds);
            if (bubble) windowPosition.bubble = placement;
            else windowPosition.expanded = placement;
        }

        private WindowPlacement CapturePlacement(Rectangle bounds)
        {
            Screen screen = Screen.FromRectangle(bounds);
            Rectangle area = screen.WorkingArea;
            int leftMargin = Math.Max(0, bounds.Left - area.Left);
            int rightMargin = Math.Max(0, area.Right - bounds.Right);
            int topMargin = Math.Max(0, bounds.Top - area.Top);
            int bottomMargin = Math.Max(0, area.Bottom - bounds.Bottom);
            return new WindowPlacement {
                x = bounds.Left,
                y = bounds.Top,
                screen = screen.DeviceName,
                horizontalEdge = leftMargin <= rightMargin ? "left" : "right",
                verticalEdge = topMargin <= bottomMargin ? "top" : "bottom",
                marginX = Math.Min(leftMargin, rightMargin),
                marginY = Math.Min(topMargin, bottomMargin)
            };
        }

        private Rectangle ResolvePlacement(WindowPlacement placement, Size size, bool bubble)
        {
            ApplyInitialPlacementIfNeeded();
            if (placement == null)
            {
                IntPtr foreground = GetForegroundWindow();
                Screen targetScreen = foreground == IntPtr.Zero ? Screen.PrimaryScreen : Screen.FromHandle(foreground);
                Rectangle area = targetScreen.WorkingArea;
                int margin = CompanionTheme.Scale(this, bubble ? 24 : 18);
                return ClampRectangle(new Rectangle(area.Right - size.Width - margin,
                    area.Bottom - size.Height - margin, size.Width, size.Height));
            }
            Screen screen = Screen.AllScreens.FirstOrDefault(item =>
                String.Equals(item.DeviceName, placement.screen, StringComparison.OrdinalIgnoreCase));
            if (screen == null) screen = Screen.FromPoint(new Point(placement.x, placement.y));
            Rectangle areaForScreen = screen.WorkingArea;
            int x = placement.x;
            int y = placement.y;
            if (String.Equals(placement.horizontalEdge, "left", StringComparison.OrdinalIgnoreCase))
                x = areaForScreen.Left + placement.marginX;
            else if (String.Equals(placement.horizontalEdge, "right", StringComparison.OrdinalIgnoreCase))
                x = areaForScreen.Right - size.Width - placement.marginX;
            if (String.Equals(placement.verticalEdge, "top", StringComparison.OrdinalIgnoreCase))
                y = areaForScreen.Top + placement.marginY;
            else if (String.Equals(placement.verticalEdge, "bottom", StringComparison.OrdinalIgnoreCase))
                y = areaForScreen.Bottom - size.Height - placement.marginY;
            return ClampRectangle(new Rectangle(x, y, size.Width, size.Height));
        }

        private void LoadWindowPreferences()
        {
            try
            {
                if (!File.Exists(positionFile)) return;
                WindowPosition saved = json.Deserialize<WindowPosition>(File.ReadAllText(positionFile, Encoding.UTF8));
                if (saved == null) return;
                windowPosition = saved;
                if (saved.expanded == null && (saved.x != 0 || saved.y != 0))
                    saved.expanded = new WindowPlacement { x = saved.x, y = saved.y };
                if (String.Equals(saved.theme, "dark", StringComparison.OrdinalIgnoreCase)) themeMode = ThemeMode.Dark;
                else if (String.Equals(saved.theme, "light", StringComparison.OrdinalIgnoreCase)) themeMode = ThemeMode.Light;
                if (String.Equals(saved.view, "bubble", StringComparison.OrdinalIgnoreCase)) preferredView = CompanionViewMode.Bubble;
            }
            catch
            {
                windowPosition = new WindowPosition { version = 2 };
            }
        }

        private void HandleFormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowExit) return;
            e.Cancel = true;
            HideForUser();
        }

        private void ExitApplication()
        {
            allowExit = true;
            pollTimer.Stop();
            transitionTimer.Stop();
            themeTimer.Stop();
            FinishThemeTransition();
            StopFocusTracker();
            SavePosition();
            tray.Visible = false;
            tray.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
            Close();
            Application.ExitThread();
        }

        private void SavePosition()
        {
            try
            {
                Directory.CreateDirectory(runtimeRoot);
                if (Visible && displayState == CompanionDisplayState.Expanded) SaveCurrentPlacement(false);
                if (Visible && displayState == CompanionDisplayState.Bubble) SaveCurrentPlacement(true);
                windowPosition.version = 2;
                windowPosition.theme = themeMode == ThemeMode.Dark ? "dark" : "light";
                windowPosition.view = preferredView == CompanionViewMode.Bubble ? "bubble" : "expanded";
                windowPosition.language = UiText.Code(UiText.Current);
                WindowPlacement legacy = windowPosition.expanded ?? windowPosition.bubble;
                if (legacy != null)
                {
                    windowPosition.x = legacy.x;
                    windowPosition.y = legacy.y;
                }
                File.WriteAllText(positionFile, json.Serialize(windowPosition), new UTF8Encoding(false));
            }
            catch
            {
                // Window placement is optional state.
            }
        }

    }
}
