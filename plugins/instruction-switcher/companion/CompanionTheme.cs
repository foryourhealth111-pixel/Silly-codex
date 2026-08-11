using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace InstructionSwitcherCompanion
{
    internal enum ThemeMode
    {
        Light,
        Dark
    }

    internal enum ThemedButtonKind
    {
        Ghost,
        Secondary,
        Primary,
        Danger
    }

    internal enum GlyphKind
    {
        None,
        Sliders,
        Sun,
        Moon,
        Collapse,
        More,
        Settings,
        ArrowUp,
        ArrowDown,
        Remove,
        ChevronDown,
        Folder
    }

    internal enum ThemedLabelRole
    {
        Primary,
        Secondary,
        Accent,
        Warning,
        Danger
    }

    internal enum StatusTone
    {
        Auto,
        Accent,
        Warning,
        Danger,
        Neutral
    }

    internal sealed class ThemePalette
    {
        public Color Window { get; private set; }
        public Color Surface { get; private set; }
        public Color Raised { get; private set; }
        public Color Hover { get; private set; }
        public Color Pressed { get; private set; }
        public Color Input { get; private set; }
        public Color Border { get; private set; }
        public Color BorderStrong { get; private set; }
        public Color Text { get; private set; }
        public Color SecondaryText { get; private set; }
        public Color DisabledText { get; private set; }
        public Color Selection { get; private set; }
        public Color Accent { get; private set; }
        public Color AccentText { get; private set; }
        public Color Warning { get; private set; }
        public Color Danger { get; private set; }

        public ThemePalette(Color window, Color surface, Color raised, Color hover, Color pressed,
            Color input, Color border, Color borderStrong, Color text, Color secondaryText,
            Color disabledText, Color selection, Color accent, Color accentText, Color warning,
            Color danger)
        {
            Window = window;
            Surface = surface;
            Raised = raised;
            Hover = hover;
            Pressed = pressed;
            Input = input;
            Border = border;
            BorderStrong = borderStrong;
            Text = text;
            SecondaryText = secondaryText;
            DisabledText = disabledText;
            Selection = selection;
            Accent = accent;
            AccentText = accentText;
            Warning = warning;
            Danger = danger;
        }
    }

    internal static class CompanionTheme
    {
        private const int EmSetCueBanner = 0x1501;
        private const int LbSetItemHeight = 0x01A0;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;
        private const int DwmWindowCornerPreference = 33;

        private static readonly ThemePalette LightPalette = new ThemePalette(
            Color.FromArgb(253, 253, 252), Color.FromArgb(245, 245, 242),
            Color.FromArgb(250, 250, 248), Color.FromArgb(241, 241, 237),
            Color.FromArgb(232, 232, 227), Color.White,
            Color.FromArgb(227, 227, 223), Color.FromArgb(216, 216, 211),
            Color.FromArgb(32, 32, 30), Color.FromArgb(116, 116, 111),
            Color.FromArgb(158, 158, 151), Color.FromArgb(231, 239, 235),
            Color.FromArgb(111, 157, 141), Color.FromArgb(85, 122, 110),
            Color.FromArgb(177, 139, 79), Color.FromArgb(182, 93, 93));

        private static readonly ThemePalette DarkPalette = new ThemePalette(
            Color.FromArgb(25, 25, 25), Color.FromArgb(31, 31, 31),
            Color.FromArgb(34, 34, 34), Color.FromArgb(41, 41, 41),
            Color.FromArgb(48, 48, 47), Color.FromArgb(34, 34, 34),
            Color.FromArgb(53, 53, 51), Color.FromArgb(66, 66, 63),
            Color.FromArgb(241, 241, 238), Color.FromArgb(155, 155, 149),
            Color.FromArgb(105, 105, 100), Color.FromArgb(42, 55, 51),
            Color.FromArgb(127, 174, 157), Color.FromArgb(141, 178, 165),
             Color.FromArgb(194, 160, 100), Color.FromArgb(212, 122, 122));

        private static readonly string UiFontName = ResolveUiFontName();

        private static string ResolveUiFontName()
        {
            string[] candidates = { "Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI" };
            try
            {
                using (var installed = new InstalledFontCollection())
                {
                    var names = new System.Collections.Generic.HashSet<string>(
                        installed.Families.Select(family => family.Name), StringComparer.OrdinalIgnoreCase);
                    foreach (string candidate in candidates)
                        if (names.Contains(candidate)) return candidate;
                }
            }
            catch
            {
                // Font enumeration can fail in a restricted desktop session.
            }
            return "Microsoft YaHei UI";
        }

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                return new Font(UiFontName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr handle, string subAppName, string subIdList);

        public static ThemeMode DetectSystemTheme()
        {
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return Convert.ToInt32(value) == 0 ? ThemeMode.Dark : ThemeMode.Light;
            }
            catch
            {
                return ThemeMode.Dark;
            }
        }

        public static ThemePalette Palette(ThemeMode mode)
        {
            if (SystemInformation.HighContrast)
            {
                return new ThemePalette(
                    SystemColors.Window, SystemColors.Control, SystemColors.Control,
                    SystemColors.Highlight, SystemColors.ControlDark, SystemColors.Window,
                    SystemColors.WindowFrame, SystemColors.ControlDarkDark,
                    SystemColors.WindowText, SystemColors.GrayText, SystemColors.GrayText,
                    SystemColors.Highlight, SystemColors.Highlight,
                    SystemColors.HighlightText, SystemColors.HotTrack, SystemColors.HotTrack);
            }
            return mode == ThemeMode.Dark ? DarkPalette : LightPalette;
        }

        public static int Scale(Control control, int logicalPixels)
        {
            float dpi = 96F;
            try
            {
                if (control != null && control.IsHandleCreated)
                    dpi = control.DeviceDpi;
                else
                {
                    using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero))
                        dpi = graphics.DpiX;
                }
            }
            catch
            {
                dpi = 96F;
            }
            return Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96F));
        }

        internal static void SetListItemHeight(ListBox list, int logicalPixels)
        {
            if (list == null || list.IsDisposed || !list.IsHandleCreated) return;
            try
            {
                int height = Scale(list, logicalPixels);
                list.ItemHeight = height;
                SendMessage(list.Handle, LbSetItemHeight, IntPtr.Zero, new IntPtr(height));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (ExternalException)
            {
            }
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0F, Math.Min(1F, amount));
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        public static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            var path = new GraphicsPath();
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
                return path;
            radius = Math.Max(0, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2));
            if (radius == 0)
            {
                path.AddRectangle(rectangle);
                return path;
            }
            int diameter = radius * 2;
            var arc = new Rectangle(rectangle.Left, rectangle.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rectangle.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rectangle.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rectangle.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void SetCueBanner(TextBox textBox, string text)
        {
            if (textBox == null || textBox.IsDisposed) return;
            if (!textBox.IsHandleCreated)
            {
                textBox.HandleCreated += delegate { SetCueBanner(textBox, text); };
                return;
            }
            SendMessage(textBox.Handle, EmSetCueBanner, new IntPtr(1), text ?? "");
        }

        public static void ApplyWindow(Form form, ThemeMode mode)
        {
            if (form == null || form.IsDisposed) return;
            if (!form.IsHandleCreated)
            {
                form.HandleCreated += delegate { ApplyWindow(form, mode); };
                return;
            }
            try
            {
                int dark = mode == ThemeMode.Dark && !SystemInformation.HighContrast ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref dark, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkModeLegacy, ref dark, 4);
                int rounded = 2;
                DwmSetWindowAttribute(form.Handle, DwmWindowCornerPreference, ref rounded, 4);
            }
            catch
            {
                // Older Windows versions keep the normal title bar and Region fallback.
            }
        }

        public static void Apply(Control root, ThemeMode mode)
        {
            if (root == null || root.IsDisposed) return;
            ThemePalette palette = Palette(mode);

            var themedButton = root as ThemedButton;
            var themedCheckBox = root as ThemedCheckBox;
            var themedLabel = root as ThemedLabel;
            var statusLabel = root as ThemedStatusLabel;
            var toggle = root as InstructionToggle;
            var themedTabs = root as ThemedTabControl;
            var themedList = root as ThemedListBox;
            var themedCheckedList = root as ThemedCheckedListBox;
            var themedOrderList = root as ThemedOrderListBox;
            var themedCombo = root as ThemedComboBox;
            var themedTextBox = root as ThemedTextBox;
            var bubble = root as BubbleControl;

            if (themedButton != null) themedButton.ThemeMode = mode;
            else if (themedCheckBox != null) themedCheckBox.ThemeMode = mode;
            else if (statusLabel != null) statusLabel.ThemeMode = mode;
            else if (themedLabel != null) themedLabel.ThemeMode = mode;
            else if (toggle != null) toggle.ThemeMode = mode;
            else if (themedTabs != null) themedTabs.ThemeMode = mode;
            else if (themedList != null) themedList.ThemeMode = mode;
            else if (themedCheckedList != null) themedCheckedList.ThemeMode = mode;
            else if (themedOrderList != null) themedOrderList.ThemeMode = mode;
            else if (themedCombo != null) themedCombo.ThemeMode = mode;
            else if (themedTextBox != null) themedTextBox.ThemeMode = mode;
            else if (bubble != null) bubble.ThemeMode = mode;
            else if (root is TextBoxBase)
            {
                root.BackColor = palette.Input;
                root.ForeColor = palette.Text;
                if (root is TextBox)
                    ((TextBox)root).BorderStyle = BorderStyle.FixedSingle;
            }
            else if (root is ComboBox)
            {
                root.BackColor = palette.Input;
                root.ForeColor = palette.Text;
                ((ComboBox)root).FlatStyle = FlatStyle.Flat;
            }
            else if (root is ListBox || root is CheckedListBox)
            {
                root.BackColor = palette.Input;
                root.ForeColor = palette.Text;
                ((ListBox)root).BorderStyle = BorderStyle.FixedSingle;
            }
            else if (root is Button)
            {
                var button = (Button)root;
                button.BackColor = palette.Raised;
                button.ForeColor = palette.Text;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = palette.BorderStrong;
            }
            else if (root is CheckBox || root is RadioButton)
            {
                root.BackColor = root.Parent == null ? palette.Window : root.Parent.BackColor;
                root.ForeColor = palette.Text;
            }
            else if (root is Form || root is TabPage || root is Panel ||
                root is FlowLayoutPanel || root is TableLayoutPanel || root is SplitContainer)
            {
                root.BackColor = palette.Window;
                root.ForeColor = palette.Text;
            }
            else
            {
                root.ForeColor = palette.Text;
            }

            if (root.ContextMenuStrip != null) ApplyToolStrip(root.ContextMenuStrip, mode);
            if (UsesNativeTheme(root))
            {
                if (root.IsHandleCreated) ApplyNativeTheme(root, mode);
                else root.HandleCreated += delegate { ApplyNativeTheme(root, mode); };
            }

            foreach (Control child in root.Controls) Apply(child, mode);
            root.Invalidate();
        }

        private static bool UsesNativeTheme(Control control)
        {
            var scrollable = control as ScrollableControl;
            return control is TextBoxBase || control is ListBox || control is CheckedListBox ||
                control is ComboBox || control is TabControl ||
                (scrollable != null && scrollable.AutoScroll);
        }

        private static void ApplyNativeTheme(Control control, ThemeMode mode)
        {
            if (control == null || control.IsDisposed || !control.IsHandleCreated) return;
            try
            {
                SetWindowTheme(control.Handle, mode == ThemeMode.Dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch
            {
                // Native theme names vary by Windows release.
            }
        }

        public static void ApplyToolStrip(ToolStrip toolStrip, ThemeMode mode)
        {
            if (toolStrip == null || toolStrip.IsDisposed) return;
            ThemePalette palette = Palette(mode);
            toolStrip.BackColor = palette.Surface;
            toolStrip.ForeColor = palette.Text;
            toolStrip.Renderer = new ToolStripProfessionalRenderer(new ThemeColorTable(palette));
            foreach (ToolStripItem item in toolStrip.Items)
            {
                item.BackColor = palette.Surface;
                item.ForeColor = palette.Text;
            }
        }

        public static void DrawGlyph(Graphics graphics, GlyphKind glyph, Rectangle bounds,
            Color color, Color background, float thickness)
        {
            if (glyph == GlyphKind.None || bounds.Width <= 0 || bounds.Height <= 0) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = bounds.Left + bounds.Width / 2F;
            float cy = bounds.Top + bounds.Height / 2F;
            float unit = Math.Min(bounds.Width, bounds.Height) / 20F;
            using (var pen = new Pen(color, Math.Max(1F, thickness)))
            using (var brush = new SolidBrush(color))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (glyph == GlyphKind.Sliders)
                {
                    float left = cx - 8 * unit, right = cx + 8 * unit;
                    float[] ys = { cy - 6 * unit, cy, cy + 6 * unit };
                    float[] knobs = { cx - 3 * unit, cx + 4 * unit, cx - unit };
                    for (int i = 0; i < 3; i++)
                    {
                        graphics.DrawLine(pen, left, ys[i], right, ys[i]);
                        graphics.FillEllipse(brush, knobs[i] - 2 * unit, ys[i] - 2 * unit, 4 * unit, 4 * unit);
                    }
                }
                else if (glyph == GlyphKind.Sun)
                {
                    graphics.DrawEllipse(pen, cx - 4 * unit, cy - 4 * unit, 8 * unit, 8 * unit);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = Math.PI * i / 4D;
                        graphics.DrawLine(pen,
                            cx + (float)Math.Cos(angle) * 7 * unit,
                            cy + (float)Math.Sin(angle) * 7 * unit,
                            cx + (float)Math.Cos(angle) * 10 * unit,
                            cy + (float)Math.Sin(angle) * 10 * unit);
                    }
                }
                else if (glyph == GlyphKind.Moon)
                {
                    graphics.FillEllipse(brush, cx - 7 * unit, cy - 8 * unit, 15 * unit, 16 * unit);
                    using (var mask = new SolidBrush(background))
                        graphics.FillEllipse(mask, cx - 2 * unit, cy - 9 * unit, 14 * unit, 14 * unit);
                }
                else if (glyph == GlyphKind.Collapse)
                {
                    graphics.DrawLine(pen, cx - 8 * unit, cy - 8 * unit, cx - 2 * unit, cy - 2 * unit);
                    graphics.DrawLine(pen, cx - 2 * unit, cy - 7 * unit, cx - 2 * unit, cy - 2 * unit);
                    graphics.DrawLine(pen, cx - 7 * unit, cy - 2 * unit, cx - 2 * unit, cy - 2 * unit);
                    graphics.DrawLine(pen, cx + 8 * unit, cy + 8 * unit, cx + 2 * unit, cy + 2 * unit);
                    graphics.DrawLine(pen, cx + 2 * unit, cy + 7 * unit, cx + 2 * unit, cy + 2 * unit);
                    graphics.DrawLine(pen, cx + 7 * unit, cy + 2 * unit, cx + 2 * unit, cy + 2 * unit);
                }
                else if (glyph == GlyphKind.More)
                {
                    graphics.FillEllipse(brush, cx - 8 * unit, cy - 1.5F * unit, 3 * unit, 3 * unit);
                    graphics.FillEllipse(brush, cx - 1.5F * unit, cy - 1.5F * unit, 3 * unit, 3 * unit);
                    graphics.FillEllipse(brush, cx + 5 * unit, cy - 1.5F * unit, 3 * unit, 3 * unit);
                }
                else if (glyph == GlyphKind.Settings)
                {
                    graphics.DrawEllipse(pen, cx - 4 * unit, cy - 4 * unit, 8 * unit, 8 * unit);
                    graphics.DrawEllipse(pen, cx - 8 * unit, cy - 8 * unit, 16 * unit, 16 * unit);
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = Math.PI * i / 4D;
                        graphics.DrawLine(pen,
                            cx + (float)Math.Cos(angle) * 8 * unit,
                            cy + (float)Math.Sin(angle) * 8 * unit,
                            cx + (float)Math.Cos(angle) * 10 * unit,
                            cy + (float)Math.Sin(angle) * 10 * unit);
                    }
                }
                else if (glyph == GlyphKind.ArrowUp || glyph == GlyphKind.ArrowDown)
                {
                    float direction = glyph == GlyphKind.ArrowUp ? -1F : 1F;
                    graphics.DrawLine(pen, cx, cy - 8 * unit * direction, cx, cy + 8 * unit * direction);
                    graphics.DrawLine(pen, cx, cy - 8 * unit * direction, cx - 5 * unit, cy - 3 * unit * direction);
                    graphics.DrawLine(pen, cx, cy - 8 * unit * direction, cx + 5 * unit, cy - 3 * unit * direction);
                }
                else if (glyph == GlyphKind.Remove)
                {
                    graphics.DrawLine(pen, cx - 6 * unit, cy - 6 * unit, cx + 6 * unit, cy + 6 * unit);
                    graphics.DrawLine(pen, cx + 6 * unit, cy - 6 * unit, cx - 6 * unit, cy + 6 * unit);
                }
                else if (glyph == GlyphKind.ChevronDown)
                {
                    graphics.DrawLine(pen, cx - 6 * unit, cy - 3 * unit, cx, cy + 3 * unit);
                    graphics.DrawLine(pen, cx, cy + 3 * unit, cx + 6 * unit, cy - 3 * unit);
                }
                else if (glyph == GlyphKind.Folder)
                {
                    var folder = new RectangleF(cx - 8 * unit, cy - 5 * unit, 16 * unit, 11 * unit);
                    graphics.DrawRectangle(pen, folder.X, folder.Y, folder.Width, folder.Height);
                    graphics.DrawLine(pen, folder.Left + unit, folder.Top,
                        folder.Left + 5 * unit, folder.Top - 3 * unit);
                    graphics.DrawLine(pen, folder.Left + 5 * unit, folder.Top - 3 * unit,
                        folder.Left + 9 * unit, folder.Top);
                }
            }
        }
    }

    internal sealed class ThemeColorTable : ProfessionalColorTable
    {
        private readonly ThemePalette palette;

        public ThemeColorTable(ThemePalette palette)
        {
            this.palette = palette;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground { get { return palette.Surface; } }
        public override Color ImageMarginGradientBegin { get { return palette.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return palette.Surface; } }
        public override Color ImageMarginGradientEnd { get { return palette.Surface; } }
        public override Color MenuItemSelected { get { return palette.Hover; } }
        public override Color MenuItemBorder { get { return palette.Border; } }
        public override Color MenuItemPressedGradientBegin { get { return palette.Pressed; } }
        public override Color MenuItemPressedGradientMiddle { get { return palette.Pressed; } }
        public override Color MenuItemPressedGradientEnd { get { return palette.Pressed; } }
        public override Color SeparatorDark { get { return palette.BorderStrong; } }
        public override Color SeparatorLight { get { return palette.Border; } }
    }

    internal class ThemedLabel : Label
    {
        private ThemeMode themeMode;
        private ThemedLabelRole role;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); }
        }

        public ThemedLabelRole Role
        {
            get { return role; }
            set { role = value; ApplyColors(); }
        }

        public ThemedLabel()
        {
            role = ThemedLabelRole.Primary;
            BackColor = Color.Transparent;
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            if (role == ThemedLabelRole.Secondary) ForeColor = palette.SecondaryText;
            else if (role == ThemedLabelRole.Accent) ForeColor = palette.AccentText;
            else if (role == ThemedLabelRole.Warning) ForeColor = palette.Warning;
            else if (role == ThemedLabelRole.Danger) ForeColor = palette.Danger;
            else ForeColor = palette.Text;
            Invalidate();
        }
    }

    internal sealed class ThemedStatusLabel : Label
    {
        private ThemeMode themeMode;
        private StatusTone tone;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; Invalidate(); }
        }

        public StatusTone Tone
        {
            get { return tone; }
            set { tone = value; Invalidate(); }
        }

        public ThemedStatusLabel()
        {
            tone = StatusTone.Auto;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            Color background = Parent == null ? palette.Window : Parent.BackColor;
            e.Graphics.Clear(background);
            if (String.IsNullOrWhiteSpace(Text)) return;
            StatusTone resolved = ResolveTone();
            Color color = resolved == StatusTone.Danger ? palette.Danger :
                resolved == StatusTone.Warning ? palette.Warning :
                resolved == StatusTone.Neutral ? palette.SecondaryText : palette.Accent;
            int dot = CompanionTheme.Scale(this, 7);
            int left = CompanionTheme.Scale(this, 2);
            int centerY = Height / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
                e.Graphics.FillEllipse(brush, left, centerY - dot / 2, dot, dot);
            Rectangle textBounds = new Rectangle(left + dot + CompanionTheme.Scale(this, 8), 0,
                Math.Max(0, Width - left - dot - CompanionTheme.Scale(this, 8)), Height);
            TextRenderer.DrawText(e.Graphics, Text ?? "", Font, textBounds,
                Enabled ? color : palette.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private StatusTone ResolveTone()
        {
            if (tone != StatusTone.Auto) return tone;
            string value = Text ?? "";
            if (value.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("异常", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("错误", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("无效", StringComparison.Ordinal) >= 0)
                return StatusTone.Danger;
            if (value.IndexOf("等待", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("待确认", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("尚未", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("待保存", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("只读", StringComparison.Ordinal) >= 0)
                return StatusTone.Warning;
            return StatusTone.Accent;
        }
    }

    internal sealed class ThemedCheckBox : CheckBox
    {
        private ThemeMode themeMode;
        private bool hover;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; Invalidate(); }
        }

        public ThemedCheckBox()
        {
            AutoSize = false;
            Appearance = Appearance.Normal;
            AccessibleRole = AccessibleRole.CheckButton;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            Color background = Parent == null ? palette.Window : Parent.BackColor;
            e.Graphics.Clear(background);
            if (hover && Enabled)
            {
                Rectangle hoverBounds = new Rectangle(0, 1, Math.Max(0, Width - 1), Math.Max(0, Height - 2));
                using (GraphicsPath hoverPath = CompanionTheme.RoundedPath(
                    hoverBounds, CompanionTheme.Scale(this, 6)))
                using (var hoverBrush = new SolidBrush(palette.Hover))
                    e.Graphics.FillPath(hoverBrush, hoverPath);
            }

            int box = CompanionTheme.Scale(this, 18);
            int left = CompanionTheme.Scale(this, 2);
            int top = (Height - box) / 2;
            Color fill = Checked ? palette.Accent : palette.Input;
            Color border = Checked ? palette.Accent : palette.BorderStrong;
            if (!Enabled)
            {
                fill = Checked ? CompanionTheme.Blend(background, palette.Accent, .55F) : palette.Input;
                border = palette.Border;
            }
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath boxPath = CompanionTheme.RoundedPath(
                new Rectangle(left, top, box, box), CompanionTheme.Scale(this, 5)))
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border, Math.Max(1F, CompanionTheme.Scale(this, 1))))
            {
                e.Graphics.FillPath(fillBrush, boxPath);
                e.Graphics.DrawPath(borderPen, boxPath);
            }
            if (CheckState == CheckState.Indeterminate)
            {
                using (var dash = new Pen(themeMode == ThemeMode.Dark ? palette.Window : Color.White,
                    Math.Max(2F, CompanionTheme.Scale(this, 2))))
                    e.Graphics.DrawLine(dash, left + CompanionTheme.Scale(this, 5), top + box / 2,
                        left + box - CompanionTheme.Scale(this, 5), top + box / 2);
            }
            else if (Checked)
            {
                Color markColor = themeMode == ThemeMode.Dark ? palette.Window : Color.White;
                using (var mark = new Pen(markColor, Math.Max(1.5F, CompanionTheme.Scale(this, 1))))
                {
                    mark.StartCap = LineCap.Round;
                    mark.EndCap = LineCap.Round;
                    e.Graphics.DrawLines(mark, new[] {
                        new Point(left + CompanionTheme.Scale(this, 4), top + CompanionTheme.Scale(this, 9)),
                        new Point(left + CompanionTheme.Scale(this, 8), top + CompanionTheme.Scale(this, 13)),
                        new Point(left + CompanionTheme.Scale(this, 15), top + CompanionTheme.Scale(this, 5))
                    });
                }
            }

            Rectangle textBounds = new Rectangle(left + box + CompanionTheme.Scale(this, 9), 0,
                Math.Max(0, Width - left - box - CompanionTheme.Scale(this, 11)), Height);
            Color textColor = Enabled ? palette.Text : palette.DisabledText;
            TextRenderer.DrawText(e.Graphics, Text ?? "", Font, textBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
        }
    }

    internal sealed class ThemedButton : Button
    {
        private ThemeMode themeMode;
        private ThemedButtonKind kind;
        private GlyphKind glyph;
        private bool hover;
        private bool pressed;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; Invalidate(); }
        }

        public ThemedButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        public GlyphKind Glyph
        {
            get { return glyph; }
            set { glyph = value; Invalidate(); }
        }

        public int CornerRadius { get; set; }
        public bool ShowBorder { get; set; }

        public ThemedButton()
        {
            kind = ThemedButtonKind.Secondary;
            glyph = GlyphKind.None;
            CornerRadius = 8;
            ShowBorder = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            Color parent = Parent == null ? palette.Window : Parent.BackColor;
            e.Graphics.Clear(parent);
            Color fill = kind == ThemedButtonKind.Primary ? palette.Accent :
                kind == ThemedButtonKind.Secondary ? palette.Raised : parent;
            Color text = kind == ThemedButtonKind.Primary
                ? (themeMode == ThemeMode.Dark ? palette.Window : Color.White)
                : kind == ThemedButtonKind.Danger ? palette.Danger : palette.Text;
            Color border = kind == ThemedButtonKind.Danger ? CompanionTheme.Blend(parent, palette.Danger, .65F) : palette.BorderStrong;
            if (hover) fill = kind == ThemedButtonKind.Primary
                ? CompanionTheme.Blend(fill, Color.White, themeMode == ThemeMode.Dark ? .08F : .12F)
                : kind == ThemedButtonKind.Danger ? CompanionTheme.Blend(parent, palette.Danger, .12F) : palette.Hover;
            if (pressed) fill = kind == ThemedButtonKind.Primary
                ? CompanionTheme.Blend(fill, Color.Black, .13F) : palette.Pressed;
            if (!Enabled)
            {
                fill = CompanionTheme.Blend(parent, palette.Raised, .45F);
                text = palette.DisabledText;
                border = palette.Border;
            }

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            int radius = CompanionTheme.Scale(this, CornerRadius);
            using (GraphicsPath path = CompanionTheme.RoundedPath(bounds, radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                e.Graphics.FillPath(brush, path);
                if (ShowBorder && kind != ThemedButtonKind.Ghost) e.Graphics.DrawPath(pen, path);
            }

            int iconSize = CompanionTheme.Scale(this, 18);
            int gap = CompanionTheme.Scale(this, 7);
            Size textSize = String.IsNullOrWhiteSpace(Text) ? Size.Empty :
                TextRenderer.MeasureText(Text, Font, new Size(Int32.MaxValue, Height), TextFormatFlags.NoPadding);
            int contentWidth = (glyph == GlyphKind.None ? 0 : iconSize) +
                (glyph != GlyphKind.None && textSize.Width > 0 ? gap : 0) + textSize.Width;
            int contentLeft = Math.Max(0, (Width - contentWidth) / 2);
            if (glyph != GlyphKind.None)
            {
                Rectangle iconBounds = new Rectangle(contentLeft, (Height - iconSize) / 2, iconSize, iconSize);
                CompanionTheme.DrawGlyph(e.Graphics, glyph, iconBounds, text, fill,
                    Math.Max(1.2F, CompanionTheme.Scale(this, 1)));
                contentLeft += iconSize + (textSize.Width > 0 ? gap : 0);
            }
            if (textSize.Width > 0)
            {
                Rectangle textBounds = new Rectangle(contentLeft, 0,
                    Math.Max(0, Width - contentLeft - CompanionTheme.Scale(this, 4)), Height);
                TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
            if (Focused && ShowFocusCues && TabStop)
            {
                Rectangle focus = Rectangle.Inflate(bounds, -CompanionTheme.Scale(this, 4), -CompanionTheme.Scale(this, 4));
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, text, fill);
            }
        }
    }

    internal sealed class ThemedTextBox : TextBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private ThemeMode themeMode;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); UpdateRegion(); }
        }

        public int CornerRadius { get; set; }

        public ThemedTextBox()
        {
            BorderStyle = BorderStyle.None;
            CornerRadius = 8;
            AutoSize = false;
            Padding = new Padding(10, 4, 10, 2);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyColors();
            UpdateRegion();
            CompanionTheme.Apply(this, themeMode);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
        }

        private void UpdateRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            try
            {
                using (GraphicsPath path = CompanionTheme.RoundedPath(
                    new Rectangle(0, 0, Width, Height), CompanionTheme.Scale(this, CornerRadius)))
                {
                    Region previous = Region;
                    Region = new Region(path);
                    if (previous != null) previous.Dispose();
                }
            }
            catch
            {
                // The native edit remains usable if a window region is unavailable.
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawChrome();
        }

        private void DrawChrome()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            using (Graphics graphics = Graphics.FromHwnd(Handle))
            using (var pen = new Pen(Focused ? palette.Accent : palette.BorderStrong,
                Math.Max(1F, CompanionTheme.Scale(this, 1))))
            using (GraphicsPath path = CompanionTheme.RoundedPath(
                new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
                CompanionTheme.Scale(this, CornerRadius)))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class ThemedComboBox : ComboBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private ThemeMode themeMode;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); }
        }

        public ThemedComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            IntegralHeight = false;
            ItemHeight = 26;
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyColors();
            CompanionTheme.Apply(this, themeMode);
            ItemHeight = CompanionTheme.Scale(this, 26);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight = CompanionTheme.Scale(this, 26);
        }

        protected override void OnDropDown(EventArgs e)
        {
            Form owner = FindForm();
            if (owner != null) owner.Activate();
            base.OnDropDown(e);
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawPickerChrome();
        }

        private void DrawPickerChrome()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            using (Graphics graphics = Graphics.FromHwnd(Handle))
            {
                int buttonWidth = Math.Max(CompanionTheme.Scale(this, 28), SystemInformation.VerticalScrollBarWidth + 4);
                Rectangle button = new Rectangle(Math.Max(0, Width - buttonWidth - 1), 1,
                    Math.Max(1, buttonWidth), Math.Max(1, Height - 2));
                using (var brush = new SolidBrush(palette.Input)) graphics.FillRectangle(brush, button);
                int icon = CompanionTheme.Scale(this, 14);
                Rectangle iconBounds = new Rectangle(button.Left + (button.Width - icon) / 2,
                    button.Top + (button.Height - icon) / 2, icon, icon);
                CompanionTheme.DrawGlyph(graphics, GlyphKind.ChevronDown, iconBounds,
                    Enabled ? palette.SecondaryText : palette.DisabledText, palette.Input, 1.35F);
                using (var pen = new Pen(Focused ? palette.Accent : palette.BorderStrong))
                    graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            }
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? palette.Selection : palette.Input))
                e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle textBounds = Rectangle.Inflate(e.Bounds, -CompanionTheme.Scale(this, 8), 0);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds,
                Enabled ? palette.Text : palette.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class ThemedListBox : ListBox
    {
        private ThemeMode themeMode;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); }
        }

        public ThemedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            ItemHeight = 34;
            BorderStyle = BorderStyle.FixedSingle;
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? palette.Selection : palette.Input))
                e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle textBounds = new Rectangle(e.Bounds.Left + CompanionTheme.Scale(this, 10), e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - CompanionTheme.Scale(this, 20)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, textBounds,
                Enabled ? palette.Text : palette.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2));
        }
    }

    internal sealed class ThemedCheckedListBox : CheckedListBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private ThemeMode themeMode;
        private int hoverIndex = -1;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); Invalidate(); }
        }

        public ThemedCheckedListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            CheckOnClick = true;
            IntegralHeight = false;
            ItemHeight = 44;
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyColors();
            CompanionTheme.Apply(this, themeMode);
            CompanionTheme.SetListItemHeight(this, 44);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            CompanionTheme.SetListItemHeight(this, 44);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = IndexFromPoint(e.Location);
            if (next != hoverIndex)
            {
                hoverIndex = next;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool hovered = e.Index == hoverIndex;
            Color rowColor = selected
                ? CompanionTheme.Blend(palette.Selection, palette.Input, .22F)
                : hovered ? palette.Hover : palette.Input;
            Rectangle row = Rectangle.Inflate(e.Bounds, -4, -2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CompanionTheme.RoundedPath(row, CompanionTheme.Scale(this, 7)))
            using (var brush = new SolidBrush(rowColor)) e.Graphics.FillPath(brush, path);

            int box = CompanionTheme.Scale(this, 20);
            int left = CompanionTheme.Scale(this, 12);
            int top = e.Bounds.Top + (e.Bounds.Height - box) / 2;
            bool checkedItem = GetItemChecked(e.Index);
            Color checkFill = checkedItem ? palette.Accent : palette.Input;
            Color checkBorder = checkedItem ? palette.Accent : palette.BorderStrong;
            using (GraphicsPath checkPath = CompanionTheme.RoundedPath(
                new Rectangle(left, top, box, box), CompanionTheme.Scale(this, 6)))
            using (var fill = new SolidBrush(checkFill))
            using (var border = new Pen(checkBorder, Math.Max(1F, CompanionTheme.Scale(this, 1))))
            {
                e.Graphics.FillPath(fill, checkPath);
                e.Graphics.DrawPath(border, checkPath);
            }
            if (checkedItem)
            {
                Color markColor = themeMode == ThemeMode.Dark ? palette.Window : Color.White;
                using (var mark = new Pen(markColor, Math.Max(1.5F, CompanionTheme.Scale(this, 1))))
                {
                    mark.StartCap = LineCap.Round;
                    mark.EndCap = LineCap.Round;
                    Point a = new Point(left + CompanionTheme.Scale(this, 5), top + CompanionTheme.Scale(this, 10));
                    Point b = new Point(left + CompanionTheme.Scale(this, 9), top + CompanionTheme.Scale(this, 14));
                    Point c = new Point(left + CompanionTheme.Scale(this, 16), top + CompanionTheme.Scale(this, 6));
                    e.Graphics.DrawLines(mark, new[] { a, b, c });
                }
            }

            string text = GetItemText(Items[e.Index]) ?? "";
            int textLeft = left + box + CompanionTheme.Scale(this, 12);
            int textWidth = Math.Max(0, e.Bounds.Right - textLeft - CompanionTheme.Scale(this, 12));
            int separator = text.IndexOf(" · ", StringComparison.Ordinal);
            string primary = separator > 0 ? text.Substring(0, separator) : text;
            string secondary = separator > 0 ? text.Substring(separator + 3) : "";
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                TextRenderer.DrawText(e.Graphics, primary, bold,
                    new Rectangle(textLeft, e.Bounds.Top + CompanionTheme.Scale(this, 5), textWidth,
                        CompanionTheme.Scale(this, 20)),
                    Enabled ? palette.Text : palette.DisabledText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
            if (secondary.Length > 0)
                TextRenderer.DrawText(e.Graphics, secondary, Font,
                    new Rectangle(textLeft, e.Bounds.Top + CompanionTheme.Scale(this, 23), textWidth,
                        CompanionTheme.Scale(this, 16)),
                    palette.SecondaryText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            using (var divider = new Pen(CompanionTheme.Blend(palette.Border, palette.Input, .35F)))
                e.Graphics.DrawLine(divider, row.Left + CompanionTheme.Scale(this, 8), row.Bottom,
                    row.Right - CompanionTheme.Scale(this, 8), row.Bottom);
            if ((e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(row, -2, -2));
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawChrome();
        }

        private void DrawChrome()
        {
            if (IsDisposed || !IsHandleCreated || Handle == IntPtr.Zero) return;
            try
            {
                ThemePalette palette = CompanionTheme.Palette(themeMode);
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                using (var pen = new Pen(palette.BorderStrong, Math.Max(1F, CompanionTheme.Scale(this, 1))))
                using (GraphicsPath path = CompanionTheme.RoundedPath(
                    new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
                    CompanionTheme.Scale(this, 8)))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawPath(pen, path);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (ExternalException)
            {
            }
        }
    }

    internal sealed class ThemedOrderListBox : ListBox
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private ThemeMode themeMode;
        private int hoverIndex = -1;
        private int dropIndex = -1;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); Invalidate(); }
        }

        public int DropIndex
        {
            get { return dropIndex; }
            set
            {
                if (dropIndex == value) return;
                dropIndex = value;
                Invalidate();
            }
        }

        public ThemedOrderListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            IntegralHeight = false;
            ItemHeight = 44;
            BorderStyle = BorderStyle.None;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Input;
            ForeColor = palette.Text;
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyColors();
            CompanionTheme.Apply(this, themeMode);
            CompanionTheme.SetListItemHeight(this, 44);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            CompanionTheme.SetListItemHeight(this, 44);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int next = IndexFromPoint(e.Location);
            if (next != hoverIndex)
            {
                hoverIndex = next;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverIndex = -1;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color rowColor = selected ? CompanionTheme.Blend(palette.Selection, palette.Input, .22F)
                : e.Index == hoverIndex ? palette.Hover : palette.Input;
            Rectangle row = Rectangle.Inflate(e.Bounds, -4, -2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = CompanionTheme.RoundedPath(row, CompanionTheme.Scale(this, 7)))
            using (var brush = new SolidBrush(rowColor)) e.Graphics.FillPath(brush, path);

            DrawHandle(e.Graphics, e.Bounds, palette);
            string text = GetItemText(Items[e.Index]) ?? "";
            int textLeft = e.Bounds.Left + CompanionTheme.Scale(this, 38);
            Rectangle textBounds = new Rectangle(textLeft, e.Bounds.Top,
                Math.Max(0, e.Bounds.Right - textLeft - CompanionTheme.Scale(this, 12)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, Font, textBounds,
                Enabled ? palette.Text : palette.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
            if (dropIndex == e.Index || (dropIndex == Items.Count && e.Index == Items.Count - 1))
                DrawDropLine(e.Graphics, e.Bounds, palette, dropIndex == Items.Count);
            if ((e.State & DrawItemState.Focus) != 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(row, -2, -2));
        }

        private void DrawHandle(Graphics graphics, Rectangle bounds, ThemePalette palette)
        {
            int x = bounds.Left + CompanionTheme.Scale(this, 17);
            int y = bounds.Top + CompanionTheme.Scale(this, 17);
            int gap = CompanionTheme.Scale(this, 7);
            int radius = Math.Max(1, CompanionTheme.Scale(this, 1));
            Color color = CompanionTheme.Blend(palette.SecondaryText, palette.Input, .12F);
            using (var brush = new SolidBrush(color))
                for (int row = -1; row <= 1; row++)
                {
                    int cy = y + row * gap;
                    graphics.FillEllipse(brush, x - radius, cy - radius, radius * 2, radius * 2);
                    graphics.FillEllipse(brush, x + CompanionTheme.Scale(this, 7) - radius,
                        cy - radius, radius * 2, radius * 2);
                }
        }

        private void DrawDropLine(Graphics graphics, Rectangle bounds, ThemePalette palette, bool atEnd)
        {
            int y = atEnd ? bounds.Bottom - CompanionTheme.Scale(this, 3) : bounds.Top + CompanionTheme.Scale(this, 1);
            using (var pen = new Pen(palette.Accent, Math.Max(2F, CompanionTheme.Scale(this, 2))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawLine(pen, bounds.Left + CompanionTheme.Scale(this, 8), y,
                    bounds.Right - CompanionTheme.Scale(this, 8), y);
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawChrome();
        }

        private void DrawChrome()
        {
            if (IsDisposed || !IsHandleCreated || Handle == IntPtr.Zero) return;
            try
            {
                ThemePalette palette = CompanionTheme.Palette(themeMode);
                using (Graphics graphics = Graphics.FromHwnd(Handle))
                using (var pen = new Pen(palette.BorderStrong, Math.Max(1F, CompanionTheme.Scale(this, 1))))
                using (GraphicsPath path = CompanionTheme.RoundedPath(
                    new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)),
                    CompanionTheme.Scale(this, 8)))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawPath(pen, path);
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (ExternalException)
            {
            }
        }
    }

    internal sealed class ThemedTabControl : TabControl
    {
        private const int WmPaint = 0x000F;
        private const int WmNcPaint = 0x0085;
        private ThemeMode themeMode;
        private bool fillTabs;
        private bool updatingTabMetrics;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; ApplyColors(); RefreshTabMetrics(); Invalidate(); }
        }

        public bool FillTabs
        {
            get { return fillTabs; }
            set
            {
                fillTabs = value;
                SizeMode = TabSizeMode.Fixed;
                Padding = value ? new Point(0, 0) : new Point(14, 5);
                RefreshTabMetrics();
                Invalidate();
            }
        }

        public ThemedTabControl()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(118, 34);
            Padding = new Point(14, 5);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        private void ApplyColors()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            BackColor = palette.Window;
            ForeColor = palette.Text;
            foreach (TabPage page in TabPages)
            {
                page.BackColor = palette.Window;
                page.ForeColor = palette.Text;
            }
        }

        internal void RefreshTabMetrics()
        {
            if (!fillTabs || updatingTabMetrics || TabPages.Count == 0 || ClientSize.Width <= 0) return;
            int width = Math.Max(1, (ClientSize.Width - CompanionTheme.Scale(this, 8)) / TabPages.Count);
            int height = Math.Max(1, ItemSize.Height);
            if (ItemSize.Width == width && ItemSize.Height == height) return;
            try
            {
                updatingTabMetrics = true;
                ItemSize = new Size(width, height);
            }
            finally
            {
                updatingTabMetrics = false;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyColors();
            RefreshTabMetrics();
            CompanionTheme.Apply(this, themeMode);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            RefreshTabMetrics();
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            ApplyColors();
            RefreshTabMetrics();
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if ((message.Msg == WmPaint || message.Msg == WmNcPaint) && IsHandleCreated)
                DrawChrome();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= TabPages.Count) return;
            DrawTab(e.Graphics, e.Index);
        }

        private void DrawChrome()
        {
            Rectangle display = DisplayRectangle;
            if (display.Width <= 0 || display.Height <= 0) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            using (Graphics graphics = Graphics.FromHwnd(Handle))
            using (var header = new SolidBrush(palette.Surface))
            using (var page = new SolidBrush(palette.Window))
            using (var divider = new Pen(palette.Border))
            {
                int headerHeight = Math.Max(0, Math.Min(Height, display.Top));
                graphics.FillRectangle(header, 0, 0, Width, headerHeight);
                for (int index = 0; index < TabPages.Count; index++) DrawTab(graphics, index);

                if (display.Left > 0)
                    graphics.FillRectangle(page, 0, headerHeight, display.Left, Math.Max(0, Height - headerHeight));
                if (display.Right < Width)
                    graphics.FillRectangle(page, display.Right, headerHeight,
                        Math.Max(0, Width - display.Right), Math.Max(0, Height - headerHeight));
                if (display.Bottom < Height)
                    graphics.FillRectangle(page, 0, display.Bottom, Width, Math.Max(0, Height - display.Bottom));
                if (headerHeight > 0)
                    graphics.DrawLine(divider, 0, headerHeight - 1, Math.Max(0, Width - 1), headerHeight - 1);
            }
        }

        private void DrawTab(Graphics graphics, int index)
        {
            if (index < 0 || index >= TabPages.Count) return;
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            bool selected = index == SelectedIndex;
            Rectangle bounds = GetTabRect(index);
            using (var brush = new SolidBrush(selected ? palette.Raised : palette.Surface))
                graphics.FillRectangle(brush, bounds);
            if (selected)
            {
                using (var accent = new SolidBrush(palette.Accent))
                    graphics.FillRectangle(accent, bounds.Left + CompanionTheme.Scale(this, 10),
                        bounds.Bottom - CompanionTheme.Scale(this, 2),
                        Math.Max(1, bounds.Width - CompanionTheme.Scale(this, 20)), CompanionTheme.Scale(this, 2));
            }
            TextRenderer.DrawText(graphics, TabPages[index].Text, Font, bounds,
                selected ? palette.Text : palette.SecondaryText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class InstructionToggle : CheckBox
    {
        private ThemeMode themeMode;
        private string titleText;
        private string summaryText;
        private bool hover;
        private bool allowReorder;
        private bool dragHandlePressed;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; Invalidate(); }
        }

        public bool AllowReorder
        {
            get { return allowReorder; }
            set
            {
                if (allowReorder == value) return;
                allowReorder = value;
                UpdatePointer();
                UpdateAccessibilityDescription();
                Invalidate();
            }
        }

        private bool ReorderAvailable
        {
            get { return allowReorder && Enabled && Checked; }
        }

        public bool HitTestDragHandle(Point point)
        {
            return ReorderAvailable && point.X >= 0 && point.X < CompanionTheme.Scale(this, 26);
        }

        public string TitleText
        {
            get { return titleText; }
            set
            {
                titleText = value ?? "";
                Text = titleText;
                AccessibleName = titleText;
                Invalidate();
            }
        }

        public string SummaryText
        {
            get { return summaryText; }
            set
            {
                summaryText = value ?? "";
                AccessibleDescription = summaryText;
                Invalidate();
            }
        }

        public InstructionToggle()
        {
            titleText = "";
            summaryText = "";
            AutoSize = false;
            Appearance = Appearance.Normal;
            AccessibleRole = AccessibleRole.CheckButton;
            Cursor = Cursors.Hand;
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            base.OnMouseLeave(e);
            UpdatePointer();
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = HitTestDragHandle(e.Location)
                ? Cursors.SizeAll
                : (Enabled ? Cursors.Hand : Cursors.Default);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            dragHandlePressed = e.Button == MouseButtons.Left && HitTestDragHandle(e.Location);
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragHandlePressed = false;
            UpdatePointer();
        }
        protected override void OnClick(EventArgs e)
        {
            if (dragHandlePressed) return;
            base.OnClick(e);
        }
        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            UpdatePointer();
            UpdateAccessibilityDescription();
            Invalidate();
        }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            UpdatePointer();
            UpdateAccessibilityDescription();
            Invalidate();
        }

        private void UpdatePointer()
        {
            Cursor = ReorderAvailable ? Cursors.SizeAll : (Enabled ? Cursors.Hand : Cursors.Default);
        }

        private void UpdateAccessibilityDescription()
        {
            string value = summaryText;
            if (!Enabled)
                value = String.IsNullOrWhiteSpace(value) ? "当前为只读预览。" : value + "；当前为只读预览。";
            else if (ReorderAvailable)
                value = String.IsNullOrWhiteSpace(value) ? "可拖动调整启用顺序。" : value + "；可拖动调整启用顺序。";
            AccessibleDescription = value;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            Color background = Parent == null ? palette.Window : Parent.BackColor;
            e.Graphics.Clear(hover && Enabled ? palette.Hover : background);

            int left = CompanionTheme.Scale(this, 28);
            int right = CompanionTheme.Scale(this, 8);
            int switchWidth = CompanionTheme.Scale(this, 38);
            int switchHeight = CompanionTheme.Scale(this, 22);
            int switchX = Width - right - switchWidth;
            int switchY = (Height - switchHeight) / 2;
            int textRight = switchX - CompanionTheme.Scale(this, 14);
            Color primary = Enabled ? palette.Text : palette.SecondaryText;
            Color secondary = Enabled ? palette.SecondaryText :
                CompanionTheme.Blend(palette.SecondaryText, palette.Window, .28F);

            Rectangle titleBounds = new Rectangle(left, CompanionTheme.Scale(this, 9),
                Math.Max(0, textRight - left), CompanionTheme.Scale(this, 22));
            Rectangle summaryBounds = new Rectangle(left, titleBounds.Bottom,
                Math.Max(0, textRight - left), CompanionTheme.Scale(this, 20));
            using (var titleFont = new Font(Font, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, titleText, titleFont, titleBounds, primary,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, summaryText, Font, summaryBounds, secondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            if (ReorderAvailable) DrawDragHandle(e.Graphics, palette);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = Checked ? palette.Accent : palette.BorderStrong;
            Color knob = themeMode == ThemeMode.Dark
                ? (Checked ? palette.SecondaryText : palette.DisabledText)
                : Color.White;
            if (!Enabled)
            {
                track = Checked ? CompanionTheme.Blend(palette.Window, palette.Accent, .58F) : palette.BorderStrong;
                knob = themeMode == ThemeMode.Dark ? palette.DisabledText :
                    (Checked ? CompanionTheme.Blend(palette.Window, palette.Text, .72F) : palette.SecondaryText);
            }
            Rectangle trackBounds = new Rectangle(switchX, switchY, switchWidth - 1, switchHeight - 1);
            using (GraphicsPath trackPath = CompanionTheme.RoundedPath(trackBounds, switchHeight / 2))
            using (var trackBrush = new SolidBrush(track))
                e.Graphics.FillPath(trackBrush, trackPath);
            int knobSize = switchHeight - CompanionTheme.Scale(this, 6);
            int knobX = Checked ? switchX + switchWidth - knobSize - CompanionTheme.Scale(this, 3) :
                switchX + CompanionTheme.Scale(this, 3);
            using (var knobBrush = new SolidBrush(knob))
                e.Graphics.FillEllipse(knobBrush, knobX, switchY + CompanionTheme.Scale(this, 3), knobSize, knobSize);
            if (!Enabled)
                DrawReadOnlyGlyph(e.Graphics, trackBounds, knobX, knobSize,
                    CompanionTheme.Blend(palette.Text, palette.Window, .32F));

            using (var divider = new Pen(palette.Border))
                e.Graphics.DrawLine(divider, left, Height - 1, Width - right, Height - 1);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics,
                    new Rectangle(left, CompanionTheme.Scale(this, 5), Width - left - right,
                        Math.Max(1, Height - CompanionTheme.Scale(this, 10))), primary, background);
        }

        private void DrawDragHandle(Graphics graphics, ThemePalette palette)
        {
            int x = CompanionTheme.Scale(this, 12);
            int firstY = CompanionTheme.Scale(this, 24);
            int gap = CompanionTheme.Scale(this, 7);
            int radius = Math.Max(1, CompanionTheme.Scale(this, 1));
            Color color = CompanionTheme.Blend(palette.SecondaryText, palette.Window, .18F);
            using (var brush = new SolidBrush(color))
            {
                for (int row = -1; row <= 1; row++)
                {
                    int y = firstY + row * gap;
                    graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                    graphics.FillEllipse(brush, x + CompanionTheme.Scale(this, 7) - radius,
                        y - radius, radius * 2, radius * 2);
                }
            }
        }

        private void DrawReadOnlyGlyph(Graphics graphics, Rectangle trackBounds, int knobX,
            int knobSize, Color color)
        {
            int knobRight = knobX + knobSize;
            int centerX = Checked
                ? trackBounds.Left + (knobX - trackBounds.Left) / 2
                : knobRight + (trackBounds.Right - knobRight) / 2;
            int bodyWidth = CompanionTheme.Scale(this, 8);
            int bodyHeight = CompanionTheme.Scale(this, 6);
            int shackleWidth = CompanionTheme.Scale(this, 6);
            int shackleHeight = CompanionTheme.Scale(this, 4);
            int bodyTop = trackBounds.Top + (trackBounds.Height - bodyHeight + shackleHeight) / 2;
            int bodyLeft = centerX - bodyWidth / 2;
            int shackleLeft = centerX - shackleWidth / 2;
            int shackleTop = bodyTop - shackleHeight;

            using (var pen = new Pen(color, Math.Max(1F, CompanionTheme.Scale(this, 1))))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                graphics.DrawArc(pen, shackleLeft, shackleTop, shackleWidth,
                    Math.Max(2, shackleHeight * 2), 180, 180);
                graphics.DrawRectangle(pen, bodyLeft, bodyTop, bodyWidth, bodyHeight);
            }
        }
    }

    internal sealed class BubbleControl : Control
    {
        private ThemeMode themeMode;
        private StatusTone statusTone;

        public ThemeMode ThemeMode
        {
            get { return themeMode; }
            set { themeMode = value; Invalidate(); }
        }

        public StatusTone StatusTone
        {
            get { return statusTone; }
            set { statusTone = value; Invalidate(); }
        }

        public BubbleControl()
        {
            statusTone = StatusTone.Accent;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "展开指令面板";
            TabStop = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(palette.Window);
            int icon = CompanionTheme.Scale(this, 22);
            Rectangle iconBounds = new Rectangle((Width - icon) / 2, (Height - icon) / 2, icon, icon);
            CompanionTheme.DrawGlyph(e.Graphics, GlyphKind.Sliders, iconBounds, palette.Text,
                palette.Window, Math.Max(1.3F, CompanionTheme.Scale(this, 1)));

            Color status = statusTone == StatusTone.Danger ? palette.Danger :
                statusTone == StatusTone.Warning ? palette.Warning : palette.Accent;
            int dot = CompanionTheme.Scale(this, 12);
            int inset = CompanionTheme.Scale(this, 3);
            Rectangle dotBounds = new Rectangle(Width - dot - inset, Height - dot - inset, dot, dot);
            using (var border = new SolidBrush(palette.Window))
                e.Graphics.FillEllipse(border, Rectangle.Inflate(dotBounds, CompanionTheme.Scale(this, 2), CompanionTheme.Scale(this, 2)));
            using (var brush = new SolidBrush(status)) e.Graphics.FillEllipse(brush, dotBounds);
            using (var pen = new Pen(palette.BorderStrong))
                e.Graphics.DrawEllipse(pen, new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1)));
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -6, -6));
        }
    }

    internal sealed class ThemeTransitionLayer : Control
    {
        private Bitmap frame;
        private Bitmap targetFrame;
        private Color fallbackColor;
        private Color targetFallbackColor;
        private int frameOpacity;

        public int FrameOpacity
        {
            get { return frameOpacity; }
            set
            {
                int next = Math.Max(0, Math.Min(255, value));
                if (frameOpacity == next) return;
                frameOpacity = next;
                if (next == 0) Visible = false;
                Invalidate();
            }
        }

        public ThemeTransitionLayer()
        {
            frameOpacity = 255;
            fallbackColor = Color.Black;
            targetFallbackColor = Color.Black;
            Visible = false;
            TabStop = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Opaque, true);
        }

        public void SetFrame(Bitmap value, Color fallback)
        {
            if (frame != null) frame.Dispose();
            if (targetFrame != null)
            {
                targetFrame.Dispose();
                targetFrame = null;
            }
            frame = value;
            fallbackColor = fallback;
            targetFallbackColor = fallback;
            frameOpacity = 255;
            Invalidate();
        }

        public void SetTargetFrame(Bitmap value, Color fallback)
        {
            if (targetFrame != null) targetFrame.Dispose();
            targetFrame = value;
            targetFallbackColor = fallback;
            Invalidate();
        }

        public void ClearFrame()
        {
            Visible = false;
            if (frame != null)
            {
                frame.Dispose();
                frame = null;
            }
            if (targetFrame != null)
            {
                targetFrame.Dispose();
                targetFrame = null;
            }
            frameOpacity = 255;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;
            if (targetFrame == null)
            {
                using (var brush = new SolidBrush(targetFallbackColor))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            }
            else
            {
                e.Graphics.DrawImage(targetFrame, ClientRectangle, 0, 0, targetFrame.Width,
                    targetFrame.Height, GraphicsUnit.Pixel);
            }
            if (frameOpacity <= 0) return;
            if (frame == null)
            {
                using (var brush = new SolidBrush(Color.FromArgb(frameOpacity, fallbackColor)))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                return;
            }
            using (var attributes = new ImageAttributes())
            {
                float alpha = frameOpacity / 255F;
                var matrix = new ColorMatrix();
                matrix.Matrix00 = 1F;
                matrix.Matrix11 = 1F;
                matrix.Matrix22 = 1F;
                matrix.Matrix33 = alpha;
                matrix.Matrix44 = 1F;
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                e.Graphics.DrawImage(frame, ClientRectangle, 0, 0, frame.Width, frame.Height,
                    GraphicsUnit.Pixel, attributes);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (frame != null)
                {
                    frame.Dispose();
                    frame = null;
                }
                if (targetFrame != null)
                {
                    targetFrame.Dispose();
                    targetFrame = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
