using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace InstructionSwitcherCompanion
{
    internal sealed class NamePromptForm : Form
    {
        private readonly TextBox input;

        private NamePromptForm(string title, string initial, ThemeMode themeMode)
        {
            Text = title;
            ClientSize = new Size(380, 124);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = CompanionTheme.UiFont(9F);
            Controls.Add(new Label { Text = UiText.T("名称"), Location = new Point(16, 16), Size = new Size(348, 20) });
            input = new TextBox { Text = initial ?? "", Location = new Point(16, 39), Size = new Size(348, 26), BorderStyle = BorderStyle.FixedSingle };
            Controls.Add(input);
            var cancel = new ThemedButton { Text = UiText.T("取消"), Kind = ThemedButtonKind.Secondary, DialogResult = DialogResult.Cancel, Location = new Point(278, 81), Size = new Size(86, 28) };
            var save = new ThemedButton { Text = UiText.T("确定"), Kind = ThemedButtonKind.Primary, DialogResult = DialogResult.OK, Location = new Point(186, 81), Size = new Size(86, 28) };
            Controls.Add(save); Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
            CompanionTheme.Apply(this, themeMode);
            CompanionTheme.ApplyWindow(this, themeMode);
        }

        public static bool Ask(IWin32Window owner, string title, string initial, out string value)
        {
            return Ask(owner, title, initial, CompanionTheme.DetectSystemTheme(), out value);
        }

        public static bool Ask(IWin32Window owner, string title, string initial, ThemeMode themeMode, out string value)
        {
            using (var dialog = new NamePromptForm(title, initial, themeMode))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK)
                {
                    value = null;
                    return false;
                }
                value = dialog.input.Text.Trim();
                if (value.Length == 0 || value.Length > 200)
                {
                    MessageBox.Show(owner, UiText.IsEnglish ? "Name must contain 1 to 200 characters." : "名称长度需要在 1 到 200 个字符之间。", title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                return true;
            }
        }
    }

    internal sealed class PresetSelectionForm : Form
    {
        private readonly ComboBox picker;

        private PresetSelectionForm(PresetDto[] presets, ThemeMode themeMode)
        {
            Text = UiText.T("选择配置预设");
            ClientSize = new Size(400, 124);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = CompanionTheme.UiFont(9F);
            Controls.Add(new Label { Text = UiText.T("更新目标"), Location = new Point(16, 16), Size = new Size(368, 20) });
            picker = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(16, 39), Size = new Size(368, 28), ThemeMode = themeMode };
            foreach (PresetDto preset in presets ?? new PresetDto[0]) picker.Items.Add(new PresetItem(preset));
            if (picker.Items.Count > 0) picker.SelectedIndex = 0;
            Controls.Add(picker);
            var cancel = new ThemedButton { Text = UiText.T("取消"), Kind = ThemedButtonKind.Secondary, DialogResult = DialogResult.Cancel, Location = new Point(298, 81), Size = new Size(86, 28) };
            var save = new ThemedButton { Text = UiText.T("确定"), Kind = ThemedButtonKind.Primary, DialogResult = DialogResult.OK, Location = new Point(206, 81), Size = new Size(86, 28) };
            Controls.Add(save); Controls.Add(cancel); AcceptButton = save; CancelButton = cancel;
            CompanionTheme.Apply(this, themeMode);
            CompanionTheme.ApplyWindow(this, themeMode);
        }

        public static bool SelectPreset(IWin32Window owner, PresetDto[] presets, out PresetDto selected)
        {
            return SelectPreset(owner, presets, CompanionTheme.DetectSystemTheme(), out selected);
        }

        public static bool SelectPreset(IWin32Window owner, PresetDto[] presets, ThemeMode themeMode, out PresetDto selected)
        {
            using (var dialog = new PresetSelectionForm(presets, themeMode))
            {
                if (dialog.ShowDialog(owner) == DialogResult.OK)
                {
                    PresetItem item = dialog.picker.SelectedItem as PresetItem;
                    selected = item == null ? null : item.Preset;
                    return selected != null;
                }
                selected = null;
                return false;
            }
        }
    }

    internal sealed class InstructionListItem
    {
        public InstructionDto Instruction { get; private set; }
        public InstructionListItem(InstructionDto instruction) { Instruction = instruction; }
        public override string ToString()
        {
            if (Instruction == null) return "";
            string suffix = String.Equals(Instruction.origin, "preset-package", StringComparison.Ordinal)
                ? " · " + UiText.T("随预设") : "";
            if (Instruction.showInCustomPicker == false) suffix += " · " + UiText.T("已隐藏");
            return Instruction.name + suffix;
        }
    }

    internal sealed class LibraryManagerForm : Form
    {
        private readonly string configFile;
        private readonly string root;
        private readonly string stateRoot;
        private readonly ThemeMode themeMode;
        private readonly JavaScriptSerializer json = CreateSerializer();
        private readonly ToolTip tips = new ToolTip();
        private SettingsDto settings;
        private string configSignature;
        private bool libraryReady;

        private ThemedTextBox instructionSearch;
        private ComboBox instructionScope;
        private ListBox instructionList;
        private ThemedTextBox instructionName;
        private CheckBox instructionVisible;
        private RichTextBox instructionBody;
        private RichTextBox instructionPreview;
        private TabControl editorTabs;
        private Label instructionMeta;
        private Label instructionStatus;
        private InstructionDto selectedInstruction;
        private bool newInstruction;
        private bool instructionDirty;
        private bool instructionBodyDirty;
        private bool instructionBodyReadFailed;
        private bool suppressInstructionEvents;

        private ThemedTextBox presetSearch;
        private ListBox presetList;
        private ThemedTextBox presetName;
        private CheckBox presetDefault;
        private ThemedTextBox availableSearch;
        private ThemedCheckedListBox availableInstructions;
        private ThemedOrderListBox orderedInstructions;
        private Label presetMeta;
        private Label presetStatus;
        private PresetDto selectedPreset;
        private readonly List<string> orderedIds = new List<string>();
        private bool newPreset;
        private bool presetDirty;
        private bool suppressPresetEvents;
        private int dragIndex = -1;
        private Point dragStart;
        private bool dragArmed;

        public string ImportedPresetIdToApply { get; private set; }

        private static JavaScriptSerializer CreateSerializer()
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            return serializer;
        }

        public LibraryManagerForm(string configFile, string root, string stateRoot)
            : this(configFile, root, stateRoot, CompanionTheme.DetectSystemTheme())
        {
        }

        public LibraryManagerForm(string configFile, string root, string stateRoot, ThemeMode themeMode)
        {
            this.configFile = configFile;
            this.root = root;
            this.stateRoot = stateRoot;
            this.themeMode = themeMode;
            Text = UiText.T("管理指令库与配置预设");
            ClientSize = new Size(960, 640);
            MinimumSize = new Size(860, 580);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            Font = CompanionTheme.UiFont(9.5F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            BuildWindow();
            ApplyTheme();
            Reload(null, null);
            FormClosing += HandleClosing;
        }

        private void BuildWindow()
        {
            var tabs = new ThemedTabControl { Dock = DockStyle.Fill, ThemeMode = themeMode, FillTabs = true };
            var instructions = new TabPage(UiText.T("指令库"));
            var presets = new TabPage(UiText.T("配置预设"));
            tabs.TabPages.Add(instructions); tabs.TabPages.Add(presets);
            BuildInstructionPage(instructions);
            BuildPresetPage(presets);

            var commandBar = new FlowLayoutPanel {
                Dock = DockStyle.Bottom,
                Height = 48,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(10, 8, 10, 6)
            };
            var moreMenu = new ContextMenuStrip();
            moreMenu.Items.Add(UiText.T("备份整个指令库…"), null, ExportBackup);
            moreMenu.Items.Add(UiText.T("恢复备份…"), null, RestoreBackup);
            var more = new ThemedButton {
                Text = "",
                Glyph = GlyphKind.More,
                ThemeMode = themeMode,
                Kind = ThemedButtonKind.Ghost,
                Size = new Size(38, 32),
                Margin = new Padding(4, 0, 0, 0)
            };
            tips.SetToolTip(more, UiText.T("更多库操作"));
            more.Click += delegate { moreMenu.Show(more, new Point(0, more.Height)); };
            var import = new ThemedButton {
                Text = UiText.T("导入包…"),
                ThemeMode = themeMode,
                Kind = ThemedButtonKind.Primary,
                Size = new Size(104, 32),
                Margin = new Padding(0)
            };
            import.Click += ImportPackage;
            commandBar.Controls.Add(import);
            commandBar.Controls.Add(more);
            Controls.Add(tabs);
            Controls.Add(commandBar);
            tabs.RefreshTabMetrics();
        }

        private void ApplyTheme()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            CompanionTheme.Apply(this, themeMode);
            BackColor = palette.Window;
            if (instructionMeta != null)
            {
                ((ThemedLabel)instructionMeta).ThemeMode = themeMode;
                ((ThemedLabel)instructionMeta).Role = ThemedLabelRole.Secondary;
            }
            if (presetMeta != null)
            {
                ((ThemedLabel)presetMeta).ThemeMode = themeMode;
                ((ThemedLabel)presetMeta).Role = ThemedLabelRole.Secondary;
            }
            if (instructionStatus is ThemedStatusLabel)
                ((ThemedStatusLabel)instructionStatus).ThemeMode = themeMode;
            if (presetStatus is ThemedStatusLabel)
                ((ThemedStatusLabel)presetStatus).ThemeMode = themeMode;
            if (instructionPreview != null)
            {
                instructionPreview.BackColor = palette.Window;
                instructionPreview.ForeColor = palette.Text;
            }
            if (instructionBody != null)
            {
                instructionBody.BackColor = palette.Input;
                instructionBody.ForeColor = palette.Text;
            }
            if (availableInstructions != null)
            {
                availableInstructions.BackColor = palette.Input;
                availableInstructions.ForeColor = palette.Text;
            }
            if (orderedInstructions != null)
            {
                orderedInstructions.BackColor = palette.Input;
                orderedInstructions.ForeColor = palette.Text;
            }
            CompanionTheme.ApplyWindow(this, themeMode);
            Invalidate(true);
        }

        private Button ToolbarButton(string text, string tooltip)
        {
            ThemedButtonKind kind = text.StartsWith(UiText.T("新增"), StringComparison.Ordinal)
                ? ThemedButtonKind.Primary
                : text.StartsWith(UiText.T("删除指令"), StringComparison.Ordinal) || text.StartsWith(UiText.T("删除预设"), StringComparison.Ordinal)
                    ? ThemedButtonKind.Danger
                    : ThemedButtonKind.Secondary;
            var button = new ThemedButton
            {
                Text = text,
                Dock = DockStyle.Fill,
                Kind = kind,
                Margin = new Padding(2),
                AutoEllipsis = false
            };
            tips.SetToolTip(button, tooltip);
            return button;
        }

        private Button CommandButton(string text, int width)
        {
            ThemedButtonKind kind = text == UiText.T("保存") ? ThemedButtonKind.Primary :
                text.IndexOf(UiText.T("删除指令"), StringComparison.Ordinal) >= 0
                    ? ThemedButtonKind.Danger : ThemedButtonKind.Ghost;
            var button = new ThemedButton {
                Text = text,
                Size = new Size(width, 30),
                Kind = kind,
                Margin = new Padding(4)
            };
            return button;
        }

        private SplitContainer LibrarySplitContainer()
        {
            var split = new SplitContainer();
            split.Size = new Size(820, 520);
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.Panel1MinSize = 250;
            split.Panel2MinSize = 460;
            split.SplitterDistance = 280;
            return split;
        }

        private void BuildInstructionPage(TabPage page)
        {
            var split = LibrarySplitContainer();
            split.Panel1.Padding = new Padding(12); split.Panel2.Padding = new Padding(14, 12, 12, 12);
            instructionSearch = new ThemedTextBox { Dock = DockStyle.Top, Height = 32, ThemeMode = themeMode };
            CompanionTheme.SetCueBanner(instructionSearch, UiText.T("搜索指令"));
            instructionSearch.TextChanged += delegate { RefreshInstructionList(true); };
            instructionScope = new ThemedComboBox {
                Dock = DockStyle.Top,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList,
                ThemeMode = themeMode
            };
            instructionScope.Items.AddRange(new object[] { UiText.T("常用指令"), UiText.T("随预设导入"), UiText.T("已隐藏"), UiText.T("全部指令") });
            instructionScope.SelectedIndex = 0;
            instructionScope.SelectedIndexChanged += delegate { RefreshInstructionList(true); };
            instructionList = new ThemedListBox { Dock = DockStyle.Fill, IntegralHeight = false, ThemeMode = themeMode };
            instructionList.SelectedIndexChanged += InstructionSelectionChanged;
            instructionList.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode != Keys.Delete || e.Modifiers != Keys.None) return;
                DeleteInstruction(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            instructionList.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Right) return;
                int index = instructionList.IndexFromPoint(e.Location);
                if (index >= 0) instructionList.SelectedIndex = index;
            };
            var instructionMenu = new ContextMenuStrip();
            ToolStripItem exportInstruction = instructionMenu.Items.Add(UiText.T("导出当前指令…"), null, ExportSelectedInstruction);
            ToolStripItem deleteInstruction = instructionMenu.Items.Add(UiText.T("删除指令项"), null, DeleteInstruction);
            instructionMenu.Opening += delegate {
                bool selected = instructionList.SelectedItem != null;
                exportInstruction.Enabled = selected;
                deleteInstruction.Enabled = selected;
            };
            instructionList.ContextMenuStrip = instructionMenu;
            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                ColumnCount = 2,
                RowCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            Button add = ToolbarButton(UiText.T("新增指令"), UiText.T("新增指令项")); add.Click += delegate { BeginInstruction(null); };
            Button copy = ToolbarButton(UiText.T("复制指令"), UiText.T("复制指令项")); copy.Click += CopyInstruction;
            Button remove = ToolbarButton(UiText.T("删除指令"), UiText.T("删除指令项")); remove.Click += DeleteInstruction;
            Button export = ToolbarButton(UiText.T("导出指令"), UiText.T("导出当前指令项")); export.Click += ExportSelectedInstruction;
            toolbar.Controls.Add(add, 0, 0);
            toolbar.Controls.Add(copy, 1, 0);
            toolbar.Controls.Add(export, 0, 1);
            toolbar.Controls.Add(remove, 1, 1);
            split.Panel1.Controls.Add(instructionList); split.Panel1.Controls.Add(toolbar);
            split.Panel1.Controls.Add(instructionScope); split.Panel1.Controls.Add(instructionSearch);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button save = CommandButton(UiText.T("保存"), 82); save.Click += delegate { SaveInstruction(); };
            Button cancel = CommandButton(UiText.T("取消"), 82); cancel.Click += delegate { CancelInstructionEdit(); };
            Button removeInstruction = CommandButton(UiText.T("删除指令"), 92); removeInstruction.Click += DeleteInstruction;
            footer.Controls.Add(save); footer.Controls.Add(cancel); footer.Controls.Add(removeInstruction);
            instructionStatus = new ThemedStatusLabel { Dock = DockStyle.Bottom, Height = 28, Text = UiText.T("就绪"), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ThemeMode = themeMode };
            editorTabs = new ThemedTabControl { Dock = DockStyle.Fill, ThemeMode = themeMode, ItemSize = new Size(86, 32) };
            var edit = new TabPage(UiText.T("编辑")); var preview = new TabPage(UiText.T("预览"));
            instructionBody = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AcceptsTab = true, Font = new Font("Consolas", 10F) };
            instructionBody.TextChanged += InstructionEdited;
            instructionPreview = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, ReadOnly = true, BackColor = Color.White, Font = CompanionTheme.UiFont(10F) };
            edit.Controls.Add(instructionBody); preview.Controls.Add(instructionPreview); editorTabs.TabPages.Add(edit); editorTabs.TabPages.Add(preview);
            editorTabs.SelectedIndexChanged += delegate { if (editorTabs.SelectedIndex == 1) RefreshPreview(); };
            instructionMeta = new ThemedLabel { Dock = DockStyle.Top, Height = 24, Role = ThemedLabelRole.Secondary, ThemeMode = themeMode, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            instructionVisible = new ThemedCheckBox {
                Text = UiText.T("显示在自定义列表"),
                Dock = DockStyle.Top,
                Height = 32,
                ThemeMode = themeMode
            };
            instructionVisible.CheckedChanged += InstructionEdited;
            instructionName = new ThemedTextBox { Dock = DockStyle.Top, Height = 34, ThemeMode = themeMode };
            instructionName.TextChanged += InstructionEdited;
            var nameLabel = new Label { Text = UiText.T("名称"), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft };
            split.Panel2.Controls.Add(editorTabs); split.Panel2.Controls.Add(instructionMeta); split.Panel2.Controls.Add(instructionVisible); split.Panel2.Controls.Add(instructionName);
            split.Panel2.Controls.Add(nameLabel); split.Panel2.Controls.Add(instructionStatus); split.Panel2.Controls.Add(footer);
            page.Controls.Add(split);
        }

        private void BuildPresetPage(TabPage page)
        {
            var split = LibrarySplitContainer();
            split.Panel1.Padding = new Padding(12); split.Panel2.Padding = new Padding(14, 12, 12, 12);
            presetSearch = new ThemedTextBox { Dock = DockStyle.Top, Height = 32, ThemeMode = themeMode };
            CompanionTheme.SetCueBanner(presetSearch, UiText.T("搜索预设"));
            presetSearch.TextChanged += delegate { RefreshPresetList(true); };
            presetList = new ThemedListBox { Dock = DockStyle.Fill, IntegralHeight = false, ThemeMode = themeMode };
            presetList.SelectedIndexChanged += PresetSelectionChanged;
            presetList.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode != Keys.Delete || e.Modifiers != Keys.None) return;
                DeletePreset(sender, e);
                e.Handled = true;
                e.SuppressKeyPress = true;
            };
            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 66,
                ColumnCount = 2,
                RowCount = 2,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            Button add = ToolbarButton(UiText.T("新增预设"), UiText.T("新增配置预设")); add.Click += delegate { BeginPreset(null); };
            Button remove = ToolbarButton(UiText.T("删除预设"), UiText.T("删除配置预设")); remove.Click += DeletePreset;
            Button export = ToolbarButton(UiText.T("导出预设"), UiText.T("导出当前配置预设及依赖指令")); export.Click += ExportSelectedPreset;
            toolbar.Controls.Add(add, 0, 0);
            toolbar.Controls.Add(remove, 1, 0);
            toolbar.Controls.Add(export, 0, 1);
            toolbar.SetColumnSpan(export, 2);
            split.Panel1.Controls.Add(presetList); split.Panel1.Controls.Add(toolbar); split.Panel1.Controls.Add(presetSearch);

            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button save = CommandButton(UiText.T("保存"), 82); save.Click += delegate { SavePreset(); };
            Button cancel = CommandButton(UiText.T("取消"), 82); cancel.Click += delegate { CancelPresetEdit(); };
            footer.Controls.Add(save); footer.Controls.Add(cancel);
            presetStatus = new ThemedStatusLabel { Dock = DockStyle.Bottom, Height = 28, Text = UiText.T("就绪"), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ThemeMode = themeMode };

            var selection = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 10, 0, 0),
                Margin = new Padding(0)
            };
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            selection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            selection.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var availablePanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 10, 0) };
            var availableHeading = new ThemedLabel { Text = UiText.T("可用指令"), Dock = DockStyle.Top, Height = 22, ThemeMode = themeMode };
            var availableHint = new ThemedLabel {
                Text = UiText.T("勾选后加入预设"),
                Dock = DockStyle.Top,
                Height = 20,
                ThemeMode = themeMode,
                Role = ThemedLabelRole.Secondary,
                Font = CompanionTheme.UiFont(8.5F)
            };
            availableSearch = new ThemedTextBox { Dock = DockStyle.Top, Height = 32, ThemeMode = themeMode };
            availableSearch.TextChanged += delegate { RebuildAvailableInstructions(); };
            CompanionTheme.SetCueBanner(availableSearch, UiText.T("搜索指令"));
            availableInstructions = new ThemedCheckedListBox {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                ThemeMode = themeMode
            };
            availableInstructions.ItemCheck += AvailableInstructionChecked;
            availablePanel.Controls.Add(availableInstructions);
            availablePanel.Controls.Add(availableSearch);
            availablePanel.Controls.Add(availableHint);
            availablePanel.Controls.Add(availableHeading);

            var orderedPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 0, 0) };
            var orderedHeading = new ThemedLabel { Text = UiText.T("启用顺序"), Dock = DockStyle.Top, Height = 22, ThemeMode = themeMode };
            var orderedHint = new ThemedLabel {
                Text = UiText.T("拖动条目调整顺序 · 取消勾选可移除"),
                Dock = DockStyle.Top,
                Height = 20,
                ThemeMode = themeMode,
                Role = ThemedLabelRole.Secondary,
                Font = CompanionTheme.UiFont(8.5F)
            };
            orderedInstructions = new ThemedOrderListBox {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                AllowDrop = true,
                ThemeMode = themeMode
            };
            orderedInstructions.MouseDown += OrderedMouseDown;
            orderedInstructions.MouseMove += OrderedMouseMove;
            orderedInstructions.MouseUp += OrderedMouseUp;
            orderedInstructions.DragEnter += OrderedDragEnter;
            orderedInstructions.DragOver += OrderedDragOver;
            orderedInstructions.DragLeave += OrderedDragLeave;
            orderedInstructions.DragDrop += OrderedDragDrop;
            orderedInstructions.KeyDown += OrderedKeyDown;
            orderedPanel.Controls.Add(orderedInstructions);
            orderedPanel.Controls.Add(orderedHint);
            orderedPanel.Controls.Add(orderedHeading);

            selection.Controls.Add(availablePanel, 0, 0);
            selection.Controls.Add(orderedPanel, 1, 0);

            presetMeta = new ThemedLabel { Dock = DockStyle.Top, Height = 24, Role = ThemedLabelRole.Secondary, ThemeMode = themeMode, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            presetDefault = new ThemedCheckBox {
                Text = UiText.T("设为新任务的默认配置"),
                Dock = DockStyle.Top,
                Height = 32,
                ThemeMode = themeMode
            };
            presetDefault.CheckedChanged += PresetEdited;
            presetName = new ThemedTextBox { Dock = DockStyle.Top, Height = 34, ThemeMode = themeMode };
            presetName.TextChanged += PresetEdited;
            var nameLabel = new Label { Text = UiText.T("名称"), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.BottomLeft };
            split.Panel2.Controls.Add(selection); split.Panel2.Controls.Add(presetMeta); split.Panel2.Controls.Add(presetDefault);
            split.Panel2.Controls.Add(presetName); split.Panel2.Controls.Add(nameLabel); split.Panel2.Controls.Add(presetStatus); split.Panel2.Controls.Add(footer);
            page.Controls.Add(split);
        }

        private SettingsDto CloneSettings()
        {
            return json.Deserialize<SettingsDto>(json.Serialize(settings));
        }

        private void Reload(string instructionId, string presetId)
        {
            try
            {
                settings = LibraryStore.Load(configFile, root);
                configSignature = LibraryStore.Signature(configFile);
                libraryReady = true;
                RefreshInstructionList(!instructionDirty); RefreshPresetList(!presetDirty);
                if (!instructionDirty)
                {
                    if (!String.IsNullOrWhiteSpace(instructionId)) SelectInstruction(instructionId);
                    if (instructionList.SelectedItem == null)
                    {
                        if (instructionList.Items.Count > 0) instructionList.SelectedIndex = 0;
                        else BeginInstruction(null);
                    }
                }
                if (!presetDirty)
                {
                    if (!String.IsNullOrWhiteSpace(presetId)) SelectPreset(presetId);
                    else if (presetList.Items.Count > 0) presetList.SelectedIndex = 0;
                    else BeginPreset(null);
                }
            }
            catch (Exception error)
            {
                libraryReady = false;
                configSignature = "unavailable";
                if (settings == null) settings = new SettingsDto { version = 3, command = "/choose", instructions = new InstructionDto[0], presets = new PresetDto[0] };
                instructionStatus.Text = UiText.IsEnglish ? "Read failed: " + UiText.Error(error.Message) : "读取失败：" + error.Message;
                presetStatus.Text = instructionStatus.Text;
            }
        }

        private void RefreshInstructionList(bool loadSelection)
        {
            if (settings == null) return;
            string selectedId = selectedInstruction == null ? null : selectedInstruction.id;
            string query = (instructionSearch.Text ?? "").Trim();
            suppressInstructionEvents = true;
            instructionList.Items.Clear();
            foreach (InstructionDto item in settings.instructions ?? new InstructionDto[0])
                if (InstructionMatchesScope(item) &&
                    (query.Length == 0 || item.name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (!String.IsNullOrWhiteSpace(item.sourcePackageId) &&
                    item.sourcePackageId.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)))
                    instructionList.Items.Add(new InstructionListItem(item));
            suppressInstructionEvents = false;
            if (String.IsNullOrWhiteSpace(selectedId)) return;
            if (loadSelection && !instructionDirty) SelectInstruction(selectedId);
            else SelectInstructionListItem(selectedId);
        }

        private bool InstructionMatchesScope(InstructionDto item)
        {
            int scope = instructionScope == null ? 3 : instructionScope.SelectedIndex;
            if (scope == 0) return item.showInCustomPicker != false;
            if (scope == 1) return String.Equals(item.origin, "preset-package", StringComparison.Ordinal);
            if (scope == 2) return item.showInCustomPicker == false;
            return true;
        }

        private void SelectInstructionListItem(string id)
        {
            suppressInstructionEvents = true;
            int selectedIndex = -1;
            for (int i = 0; i < instructionList.Items.Count; i++)
            {
                InstructionListItem item = instructionList.Items[i] as InstructionListItem;
                if (item != null && String.Equals(item.Instruction.id, id, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }
            instructionList.SelectedIndex = selectedIndex;
            suppressInstructionEvents = false;
        }

        private void SelectInstruction(string id)
        {
            SelectInstructionListItem(id);
            InstructionListItem selected = instructionList.SelectedItem as InstructionListItem;
            if (selected != null && String.Equals(selected.Instruction.id, id, StringComparison.Ordinal))
                LoadInstruction(selected.Instruction);
        }

        private void InstructionSelectionChanged(object sender, EventArgs e)
        {
            if (suppressInstructionEvents) return;
            InstructionListItem item = instructionList.SelectedItem as InstructionListItem;
            if (item == null) return;
            if (!ConfirmInstructionEdit())
            {
                RestoreInstructionSelection();
                return;
            }
            suppressInstructionEvents = true;
            instructionList.SelectedItem = item;
            suppressInstructionEvents = false;
            LoadInstruction(item.Instruction);
        }

        private void RestoreInstructionSelection()
        {
            SelectInstructionListItem(selectedInstruction == null ? null : selectedInstruction.id);
        }

        private void LoadInstruction(InstructionDto instruction)
        {
            suppressInstructionEvents = true;
            selectedInstruction = instruction;
            newInstruction = false;
            instructionBodyDirty = false;
            instructionBodyReadFailed = false;
            instructionName.Text = instruction == null ? "" : instruction.name;
            instructionVisible.Checked = instruction == null || instruction.showInCustomPicker != false;
            try { instructionBody.Text = instruction == null ? "" : LibraryStore.ReadBody(root, instruction); }
            catch (Exception error)
            {
                instructionBody.Text = "";
                instructionBodyReadFailed = instruction != null;
                instructionStatus.Text = UiText.IsEnglish ? "Content read failed: " + UiText.Error(error.Message) : "正文读取失败：" + error.Message;
            }
            int refs = instruction == null ? 0 : LibraryStore.CountPresetReferences(settings, instruction.id);
            string source = instruction == null || String.Equals(instruction.origin, "local", StringComparison.Ordinal)
                ? (UiText.IsEnglish ? "Created locally" : "本地创建")
                : String.Equals(instruction.origin, "preset-package", StringComparison.Ordinal)
                    ? UiText.T("随预设导入") : (UiText.IsEnglish ? "Imported from an instruction package" : "通过指令包导入");
            if (instruction != null && !String.IsNullOrWhiteSpace(instruction.sourcePackageId))
                source += " · " + instruction.sourcePackageId;
            instructionMeta.Text = instruction == null ? (UiText.IsEnglish ? "New instruction" : "新指令项") :
                (UiText.IsEnglish ? "Internal ID: " + instruction.id + " · Referenced by " + refs + " presets · " + source :
                    "内部 ID：" + instruction.id + " · 被 " + refs + " 个配置预设引用 · " + source);
            instructionDirty = false;
            suppressInstructionEvents = false;
            if (editorTabs.SelectedIndex == 1) RefreshPreview();
        }

        private void BeginInstruction(InstructionDto source)
        {
            if (!ConfirmInstructionEdit()) return;
            suppressInstructionEvents = true;
            instructionList.ClearSelected();
            selectedInstruction = null;
            newInstruction = true;
            instructionBodyDirty = source != null;
            instructionBodyReadFailed = false;
            instructionName.Text = source == null ? "" : source.name + (UiText.IsEnglish ? " copy" : " 副本");
            instructionVisible.Checked = true;
            try { instructionBody.Text = source == null ? "" : LibraryStore.ReadBody(root, source); }
            catch (Exception error)
            {
                instructionBody.Text = "";
                instructionBodyReadFailed = source != null;
                instructionStatus.Text = UiText.IsEnglish ? "Content read failed: " + UiText.Error(error.Message) : "正文读取失败：" + error.Message;
            }
            instructionMeta.Text = UiText.IsEnglish ? "New instruction · A stable ID is assigned when saved" : "新指令项 · 保存后分配稳定 ID";
            instructionStatus.Text = UiText.IsEnglish ? "Unsaved" : "待保存";
            instructionDirty = source != null;
            suppressInstructionEvents = false;
            instructionName.Focus(); instructionName.SelectAll();
            if (editorTabs.SelectedIndex == 1) RefreshPreview();
        }

        private void InstructionEdited(object sender, EventArgs e)
        {
            if (suppressInstructionEvents) return;
            if (sender == instructionBody)
            {
                instructionBodyDirty = true;
                instructionBodyReadFailed = false;
            }
            instructionDirty = true;
            instructionStatus.Text = UiText.IsEnglish ? "Unsaved changes" : "有未保存修改";
        }

        private bool SaveInstruction()
        {
            if (!libraryReady || String.IsNullOrWhiteSpace(configSignature) || configSignature == "unavailable")
            {
                instructionStatus.Text = UiText.IsEnglish ? "The library is not ready; reopen this window" : "配置库未就绪，请重新打开管理面板";
                return false;
            }
            string name = (instructionName.Text ?? "").Trim();
            if (name.Length == 0 || name.Length > 200)
            {
                MessageBox.Show(this, UiText.IsEnglish ? "Name must contain 1 to 200 characters." : "名称长度需要在 1 到 200 个字符之间。", UiText.IsEnglish ? "Save instruction" : "保存指令项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (instructionBodyReadFailed)
            {
                instructionStatus.Text = UiText.IsEnglish ? "Content could not be read. Reopen this window to retry, or edit the content before saving." : "正文读取失败，请重新打开管理面板后重试；编辑正文后可以继续保存。";
                return false;
            }
            if (Encoding.UTF8.GetByteCount(instructionBody.Text ?? "") > 64000)
            {
                MessageBox.Show(this, UiText.IsEnglish ? "Instruction content exceeds 64,000 bytes." : "指令正文超过 64000 字节。", UiText.IsEnglish ? "Save instruction" : "保存指令项", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                if (LibraryStore.Signature(configFile) != configSignature)
                    throw new InvalidOperationException(UiText.IsEnglish ? "The library changed; reopen this window" : "配置库已更新，请重新打开管理面板");
                SettingsDto next = CloneSettings();
                string now = DateTime.UtcNow.ToString("o");
                InstructionDto target;
                if (newInstruction || selectedInstruction == null)
                {
                    string id = LibraryStore.NewId("instruction");
                    target = new InstructionDto {
                        id = id, name = name, label = name, file = "instructions/" + id + ".md",
                        origin = "local", showInCustomPicker = instructionVisible.Checked,
                        createdAt = now, updatedAt = now
                    };
                    var list = (next.instructions ?? new InstructionDto[0]).ToList(); list.Add(target); next.instructions = list.ToArray();
                }
                else
                {
                    target = next.instructions.First(item => String.Equals(item.id, selectedInstruction.id, StringComparison.Ordinal));
                    target.name = name; target.label = name; target.showInCustomPicker = instructionVisible.Checked; target.updatedAt = now;
                }
                if (newInstruction || instructionBodyDirty)
                {
                    LibraryStore.SaveWithBody(configFile, root, next, configSignature,
                        delegate { LibraryStore.WriteBody(root, target, instructionBody.Text); },
                        new[] { LibraryStore.BodyPath(root, target) });
                }
                else
                {
                    LibraryStore.Save(configFile, root, next, configSignature);
                }
                string idToSelect = target.id;
                instructionDirty = false;
                instructionBodyDirty = false;
                Reload(idToSelect, selectedPreset == null ? null : selectedPreset.id);
                instructionStatus.Text = UiText.IsEnglish ? "Saved" : "已保存";
                return true;
            }
            catch (Exception error)
            {
                instructionStatus.Text = UiText.IsEnglish ? "Save failed: " + UiText.Error(error.Message) : "保存失败：" + error.Message;
                return false;
            }
        }

        private bool ConfirmInstructionEdit()
        {
            if (!instructionDirty) return true;
            DialogResult choice = MessageBox.Show(this,
                UiText.IsEnglish ? "The current instruction has unsaved changes. Select Yes to save or No to discard them." : "当前指令项有未保存修改。选择“是”保存，选择“否”放弃修改。",
                UiText.IsEnglish ? "Unsaved changes" : "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes) return SaveInstruction();
            if (choice == DialogResult.No) { instructionDirty = false; return true; }
            return false;
        }

        private void CancelInstructionEdit()
        {
            instructionDirty = false;
            if (selectedInstruction != null) LoadInstruction(selectedInstruction);
            else if (instructionList.Items.Count > 0) instructionList.SelectedIndex = 0;
            else BeginInstruction(null);
        }

        private void CopyInstruction(object sender, EventArgs e)
        {
            InstructionListItem item = instructionList.SelectedItem as InstructionListItem;
            if (item != null) BeginInstruction(item.Instruction);
        }

        private void DeleteInstruction(object sender, EventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
            InstructionDto target = selectedInstruction;
            if (target == null || !libraryReady) return;
            int presetRefs = LibraryStore.CountPresetReferences(settings, target.id);
            int taskRefs = LibraryStore.CountSessionReferences(stateRoot, target.id);
            string message = UiText.IsEnglish
                ? "Delete " + UiText.Quote(target.name) + "?\r\n\r\nIt is referenced by " + presetRefs + " presets and " + taskRefs + " tasks. Those references will be removed."
                : "删除“" + target.name + "”？\r\n\r\n它当前被 " + presetRefs + " 个配置预设和 " + taskRefs + " 个任务引用。删除后会自动清理这些引用。";
            if (MessageBox.Show(this, message, UiText.T("删除指令项"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                SettingsDto next = CloneSettings();
                next.instructions = next.instructions.Where(item => !String.Equals(item.id, target.id, StringComparison.Ordinal)).ToArray();
                foreach (PresetDto preset in next.presets)
                    preset.instructionIds = (preset.instructionIds ?? new string[0]).Where(id => !String.Equals(id, target.id, StringComparison.Ordinal)).ToArray();
                string body = LibraryStore.BodyPath(root, target);
                bool sharedBody = next.instructions.Any(item =>
                {
                    try
                    {
                        return String.Equals(LibraryStore.BodyPath(root, item), body, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
                if (sharedBody)
                {
                    LibraryStore.Save(configFile, root, next, configSignature);
                }
                else
                {
                    LibraryStore.SaveWithBody(configFile, root, next, configSignature,
                        delegate { if (File.Exists(body)) File.Delete(body); }, new[] { body });
                }
                int cleaned = LibraryStore.CleanSessionReferences(stateRoot, next, target.id);
                selectedInstruction = null; instructionDirty = false;
                Reload(null, selectedPreset == null ? null : selectedPreset.id);
                instructionStatus.Text = UiText.IsEnglish
                    ? "Deleted; cleaned " + cleaned + " task states" + (sharedBody ? "; shared content was kept" : "")
                    : "已删除并清理 " + cleaned + " 个任务状态" + (sharedBody ? "，共用正文已保留" : "");
            }
            catch (Exception error)
            {
                instructionStatus.Text = UiText.IsEnglish ? "Delete failed: " + UiText.Error(error.Message) : "删除失败：" + error.Message;
            }
        }

        private void RefreshPreview()
        {
            instructionPreview.Text = instructionBody.Text ?? "";
            using (var normalFont = CompanionTheme.UiFont(10F))
            using (var headingFont = CompanionTheme.UiFont(12F, FontStyle.Bold))
            {
                instructionPreview.SelectAll();
                instructionPreview.SelectionFont = normalFont;
                int offset = 0;
                foreach (string line in instructionPreview.Lines)
                {
                    if (line.StartsWith("#", StringComparison.Ordinal))
                    {
                        instructionPreview.Select(offset, line.Length);
                        instructionPreview.SelectionFont = headingFont;
                    }
                    offset += line.Length + 1;
                }
            }
            instructionPreview.Select(0, 0);
        }

        private void RefreshPresetList(bool loadSelection)
        {
            if (settings == null) return;
            string selectedId = selectedPreset == null ? null : selectedPreset.id;
            string query = (presetSearch.Text ?? "").Trim();
            suppressPresetEvents = true;
            presetList.Items.Clear();
            foreach (PresetDto item in settings.presets ?? new PresetDto[0])
                if (query.Length == 0 || item.name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    presetList.Items.Add(new PresetItem(item));
            suppressPresetEvents = false;
            if (String.IsNullOrWhiteSpace(selectedId)) return;
            if (loadSelection && !presetDirty) SelectPreset(selectedId);
            else SelectPresetListItem(selectedId);
        }

        private void SelectPresetListItem(string id)
        {
            suppressPresetEvents = true;
            int selectedIndex = -1;
            for (int i = 0; i < presetList.Items.Count; i++)
            {
                PresetItem item = presetList.Items[i] as PresetItem;
                if (item != null && item.Preset != null && String.Equals(item.Preset.id, id, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }
            presetList.SelectedIndex = selectedIndex;
            suppressPresetEvents = false;
        }

        private void SelectPreset(string id)
        {
            SelectPresetListItem(id);
            PresetItem selected = presetList.SelectedItem as PresetItem;
            if (selected != null && selected.Preset != null && String.Equals(selected.Preset.id, id, StringComparison.Ordinal))
                LoadPreset(selected.Preset);
        }

        private void PresetSelectionChanged(object sender, EventArgs e)
        {
            if (suppressPresetEvents) return;
            PresetItem item = presetList.SelectedItem as PresetItem;
            if (item == null || item.Preset == null) return;
            if (!ConfirmPresetEdit())
            {
                RestorePresetSelection();
                return;
            }
            suppressPresetEvents = true;
            presetList.SelectedItem = item;
            suppressPresetEvents = false;
            LoadPreset(item.Preset);
        }

        private void RestorePresetSelection()
        {
            SelectPresetListItem(selectedPreset == null ? null : selectedPreset.id);
        }

        private void LoadPreset(PresetDto preset)
        {
            suppressPresetEvents = true;
            selectedPreset = preset;
            newPreset = false;
            presetName.Text = preset == null ? "" : preset.name;
            presetDefault.Checked = preset != null && String.Equals(settings.defaultPresetId, preset.id, StringComparison.Ordinal);
            orderedIds.Clear();
            if (preset != null) orderedIds.AddRange(preset.instructionIds ?? new string[0]);
            presetMeta.Text = preset == null ? (UiText.IsEnglish ? "New preset" : "新配置预设") :
                (UiText.IsEnglish ? "Internal ID: " + preset.id + " · " + orderedIds.Count + " instructions" : "内部 ID：" + preset.id + " · " + orderedIds.Count + " 条指令");
            presetDirty = false;
            suppressPresetEvents = false;
            RebuildAvailableInstructions(); RebuildOrderedInstructions();
        }

        private void BeginPreset(PresetDto source)
        {
            if (!ConfirmPresetEdit()) return;
            suppressPresetEvents = true;
            presetList.ClearSelected(); selectedPreset = null; newPreset = true;
            presetName.Text = source == null ? "" : source.name + (UiText.IsEnglish ? " copy" : " 副本");
            presetDefault.Checked = false; orderedIds.Clear();
            if (source != null) orderedIds.AddRange(source.instructionIds ?? new string[0]);
            presetMeta.Text = UiText.IsEnglish ? "New preset · A stable ID is assigned when saved" : "新配置预设 · 保存后分配稳定 ID";
            presetStatus.Text = UiText.IsEnglish ? "Unsaved" : "待保存"; presetDirty = source != null;
            suppressPresetEvents = false;
            RebuildAvailableInstructions(); RebuildOrderedInstructions();
            presetName.Focus(); presetName.SelectAll();
        }

        private void PresetEdited(object sender, EventArgs e)
        {
            if (suppressPresetEvents) return;
            presetDirty = true; presetStatus.Text = UiText.IsEnglish ? "Unsaved changes" : "有未保存修改";
        }

        private void RebuildAvailableInstructions()
        {
            if (settings == null || availableInstructions == null) return;
            string query = (availableSearch.Text ?? "").Trim();
            suppressPresetEvents = true;
            availableInstructions.Items.Clear();
            foreach (InstructionDto item in settings.instructions ?? new InstructionDto[0])
            {
                if (query.Length > 0 && item.name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                int index = availableInstructions.Items.Add(new InstructionListItem(item));
                availableInstructions.SetItemChecked(index, orderedIds.Contains(item.id));
            }
            suppressPresetEvents = false;
        }

        private void RebuildOrderedInstructions()
        {
            orderedInstructions.DropIndex = -1;
            orderedInstructions.Items.Clear();
            var map = (settings.instructions ?? new InstructionDto[0]).ToDictionary(item => item.id, StringComparer.Ordinal);
            foreach (string id in orderedIds.ToArray())
            {
                InstructionDto item;
                if (map.TryGetValue(id, out item)) orderedInstructions.Items.Add(new InstructionListItem(item));
                else orderedIds.Remove(id);
            }
            presetMeta.Text = (selectedPreset == null ? (UiText.IsEnglish ? "New preset" : "新配置预设") : (UiText.IsEnglish ? "Internal ID: " : "内部 ID：") + selectedPreset.id) + " · " +
                (UiText.IsEnglish ? orderedIds.Count + " instructions" : orderedIds.Count + " 条指令");
        }

        private void AvailableInstructionChecked(object sender, ItemCheckEventArgs e)
        {
            if (suppressPresetEvents) return;
            InstructionListItem item = availableInstructions.Items[e.Index] as InstructionListItem;
            if (item == null) return;
            if (e.NewValue == CheckState.Checked && !orderedIds.Contains(item.Instruction.id)) orderedIds.Add(item.Instruction.id);
            if (e.NewValue != CheckState.Checked) orderedIds.Remove(item.Instruction.id);
            presetDirty = true; presetStatus.Text = UiText.IsEnglish ? "Unsaved changes" : "有未保存修改";
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new MethodInvoker(RebuildOrderedInstructions));
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void RemoveOrdered()
        {
            int index = orderedInstructions.SelectedIndex;
            if (index < 0 || index >= orderedIds.Count) return;
            orderedIds.RemoveAt(index); presetDirty = true;
            presetStatus.Text = UiText.IsEnglish ? "Unsaved changes" : "有未保存修改";
            RebuildAvailableInstructions(); RebuildOrderedInstructions();
            if (orderedInstructions.Items.Count > 0)
                orderedInstructions.SelectedIndex = Math.Min(index, orderedInstructions.Items.Count - 1);
        }

        private void OrderedMouseDown(object sender, MouseEventArgs e)
        {
            dragArmed = false;
            dragIndex = -1;
            if (e.Button != MouseButtons.Left) return;
            int index = orderedInstructions.IndexFromPoint(e.Location);
            if (index < 0 || index >= orderedIds.Count) return;
            orderedInstructions.SelectedIndex = index;
            dragIndex = index;
            dragStart = e.Location;
            dragArmed = true;
        }

        private void OrderedMouseMove(object sender, MouseEventArgs e)
        {
            if (!dragArmed || e.Button != MouseButtons.Left || dragIndex < 0 || dragIndex >= orderedIds.Count)
                return;
            Size threshold = SystemInformation.DragSize;
            if (Math.Abs(e.X - dragStart.X) <= threshold.Width / 2 &&
                Math.Abs(e.Y - dragStart.Y) <= threshold.Height / 2) return;
            string id = orderedIds[dragIndex];
            dragArmed = false;
            try
            {
                orderedInstructions.DoDragDrop(id, DragDropEffects.Move);
            }
            finally
            {
                dragIndex = -1;
                orderedInstructions.DropIndex = -1;
            }
        }

        private void OrderedMouseUp(object sender, MouseEventArgs e)
        {
            bool wasArmed = dragArmed;
            dragArmed = false;
            if (e.Button == MouseButtons.Left && wasArmed)
                dragIndex = -1;
        }

        private void OrderedKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete || e.Modifiers != Keys.None) return;
            RemoveOrdered();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void OrderedDragEnter(object sender, DragEventArgs e)
        {
            OrderedDragOver(sender, e);
        }

        private void OrderedDragOver(object sender, DragEventArgs e)
        {
            string id = e.Data.GetDataPresent(typeof(string)) ? e.Data.GetData(typeof(string)) as string : null;
            if (String.IsNullOrWhiteSpace(id) || !orderedIds.Contains(id))
            {
                e.Effect = DragDropEffects.None;
                orderedInstructions.DropIndex = -1;
                return;
            }
            e.Effect = DragDropEffects.Move;
            Point point = orderedInstructions.PointToClient(new Point(e.X, e.Y));
            orderedInstructions.DropIndex = OrderedInsertIndex(point);
        }

        private void OrderedDragLeave(object sender, EventArgs e)
        {
            orderedInstructions.DropIndex = -1;
        }

        private int OrderedInsertIndex(Point point)
        {
            if (orderedInstructions.Items.Count == 0) return 0;
            for (int index = 0; index < orderedInstructions.Items.Count; index++)
            {
                Rectangle bounds = orderedInstructions.GetItemRectangle(index);
                if (point.Y < bounds.Top + bounds.Height / 2) return index;
            }
            return orderedInstructions.Items.Count;
        }

        private void OrderedDragDrop(object sender, DragEventArgs e)
        {
            string id = e.Data.GetData(typeof(string)) as string;
            try
            {
                if (String.IsNullOrWhiteSpace(id)) return;
                int source = orderedIds.IndexOf(id);
                if (source < 0) return;
                Point point = orderedInstructions.PointToClient(new Point(e.X, e.Y));
                int target = OrderedInsertIndex(point);
                if (target > source) target--;
                if (target == source) return;
                orderedIds.RemoveAt(source);
                target = Math.Max(0, Math.Min(target, orderedIds.Count));
                orderedIds.Insert(target, id);
                presetDirty = true;
                presetStatus.Text = UiText.IsEnglish ? "Unsaved changes" : "有未保存修改";
                RebuildOrderedInstructions();
                orderedInstructions.SelectedIndex = target;
            }
            finally
            {
                dragIndex = -1;
                dragArmed = false;
                orderedInstructions.DropIndex = -1;
            }
        }

        private bool SavePreset()
        {
            if (!libraryReady || String.IsNullOrWhiteSpace(configSignature) || configSignature == "unavailable")
            {
                presetStatus.Text = UiText.IsEnglish ? "The library is not ready; reopen this window" : "配置库未就绪，请重新打开管理面板";
                return false;
            }
            string name = (presetName.Text ?? "").Trim();
            if (name.Length == 0 || name.Length > 200)
            {
                MessageBox.Show(this, UiText.IsEnglish ? "Name must contain 1 to 200 characters." : "名称长度需要在 1 到 200 个字符之间。", UiText.IsEnglish ? "Save preset" : "保存配置预设", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                SettingsDto next = CloneSettings(); string now = DateTime.UtcNow.ToString("o"); PresetDto target;
                if (newPreset || selectedPreset == null)
                {
                    target = new PresetDto { id = LibraryStore.NewId("preset"), name = name, instructionIds = orderedIds.ToArray(), createdAt = now, updatedAt = now };
                    var list = (next.presets ?? new PresetDto[0]).ToList(); list.Add(target); next.presets = list.ToArray();
                }
                else
                {
                    target = next.presets.First(item => String.Equals(item.id, selectedPreset.id, StringComparison.Ordinal));
                    target.name = name; target.instructionIds = orderedIds.ToArray(); target.updatedAt = now;
                }
                if (presetDefault.Checked) next.defaultPresetId = target.id;
                else if (String.Equals(next.defaultPresetId, target.id, StringComparison.Ordinal)) next.defaultPresetId = null;
                LibraryStore.Save(configFile, root, next, configSignature);
                presetDirty = false;
                string id = target.id; Reload(selectedInstruction == null ? null : selectedInstruction.id, id);
                presetStatus.Text = UiText.IsEnglish ? "Saved" : "已保存"; return true;
            }
            catch (Exception error)
            {
                presetStatus.Text = UiText.IsEnglish ? "Save failed: " + UiText.Error(error.Message) : "保存失败：" + error.Message; return false;
            }
        }

        private bool ConfirmPresetEdit()
        {
            if (!presetDirty) return true;
            DialogResult choice = MessageBox.Show(this,
                UiText.IsEnglish ? "The current preset has unsaved changes. Select Yes to save or No to discard them." : "当前配置预设有未保存修改。选择“是”保存，选择“否”放弃修改。",
                UiText.IsEnglish ? "Unsaved changes" : "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes) return SavePreset();
            if (choice == DialogResult.No) { presetDirty = false; return true; }
            return false;
        }

        private void CancelPresetEdit()
        {
            presetDirty = false;
            if (selectedPreset != null) LoadPreset(selectedPreset);
            else if (presetList.Items.Count > 0) presetList.SelectedIndex = 0;
            else BeginPreset(null);
        }

        private void DeletePreset(object sender, EventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
            PresetDto target = selectedPreset;
            if (target == null || !libraryReady) return;
            string deletePresetMessage = UiText.IsEnglish
                ? "Delete preset " + UiText.Quote(target.name) + "? Instructions already enabled in tasks will remain enabled."
                : "删除配置预设“" + target.name + "”？任务中已启用的指令项会继续保留。";
            if (MessageBox.Show(this, deletePresetMessage,
                UiText.T("删除配置预设"), MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            try
            {
                SettingsDto next = CloneSettings();
                next.presets = next.presets.Where(item => !String.Equals(item.id, target.id, StringComparison.Ordinal)).ToArray();
                if (String.Equals(next.defaultPresetId, target.id, StringComparison.Ordinal)) next.defaultPresetId = null;
                LibraryStore.Save(configFile, root, next, configSignature);
                int cleaned = LibraryStore.CleanPresetReferences(stateRoot, next, target.id);
                selectedPreset = null; presetDirty = false; Reload(selectedInstruction == null ? null : selectedInstruction.id, null);
                presetStatus.Text = UiText.IsEnglish ? "Preset deleted; cleaned " + cleaned + " task states" : "已删除配置预设，清理 " + cleaned + " 个任务状态";
            }
            catch (Exception error) { presetStatus.Text = UiText.IsEnglish ? "Delete failed: " + UiText.Error(error.Message) : "删除失败：" + error.Message; }
        }

        private void ExportSelectedInstruction(object sender, EventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
            InstructionListItem selected = instructionList.SelectedItem as InstructionListItem;
            InstructionDto instruction = selected == null ? selectedInstruction : selected.Instruction;
            if (instruction == null)
            {
                instructionStatus.Text = UiText.IsEnglish ? "Select an instruction to export" : "请选择要导出的指令项";
                return;
            }
            try
            {
                PackageDocumentDto document = PackageExchange.CreateInstructionPackage(root, settings,
                    new[] { instruction.id }, instruction.name);
                ExportDocument(document, UiText.IsEnglish ? "Export instruction package" : "导出指令包", SafeFileName(instruction.name) + ".ispkg.json");
            }
            catch (Exception error) { SetLibraryStatus(UiText.IsEnglish ? "Export failed: " + UiText.Error(error.Message) : "导出失败：" + error.Message); }
        }

        private void ExportSelectedPreset(object sender, EventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
            PresetItem selected = presetList.SelectedItem as PresetItem;
            PresetDto preset = selected == null ? selectedPreset : selected.Preset;
            if (preset == null)
            {
                presetStatus.Text = UiText.IsEnglish ? "Select a preset to export" : "请选择要导出的配置预设";
                return;
            }
            try
            {
                PackageDocumentDto document = PackageExchange.CreatePresetPackage(root, settings, preset.id);
                ExportDocument(document, UiText.IsEnglish ? "Export preset package" : "导出配置预设包", SafeFileName(preset.name) + ".ispkg.json");
            }
            catch (Exception error) { SetLibraryStatus(UiText.IsEnglish ? "Export failed: " + UiText.Error(error.Message) : "导出失败：" + error.Message); }
        }

        private void ExportBackup(object sender, EventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
            try
            {
                PackageDocumentDto document = PackageExchange.CreateBackup(root, settings);
                ExportDocument(document, UiText.IsEnglish ? "Back up instruction library" : "备份整个指令库",
                    "instruction-switcher-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ispkg.json");
            }
            catch (Exception error) { SetLibraryStatus(UiText.IsEnglish ? "Backup failed: " + UiText.Error(error.Message) : "备份失败：" + error.Message); }
        }

        private void ExportDocument(PackageDocumentDto document, string title, string fileName)
        {
            using (var dialog = new SaveFileDialog {
                Title = title,
                Filter = UiText.IsEnglish ? "Instruction Switcher packages (*.ispkg.json)|*.ispkg.json|JSON files (*.json)|*.json" : "Instruction Switcher 包 (*.ispkg.json)|*.ispkg.json|JSON 文件 (*.json)|*.json",
                FileName = fileName,
                AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                PackageExchange.WritePackage(dialog.FileName, document);
                SetLibraryStatus(UiText.IsEnglish ? "Exported to " + dialog.FileName : "已导出到 " + dialog.FileName);
            }
        }

        private void ImportPackage(object sender, EventArgs e)
        {
            ImportPackageFromFile(false);
        }

        private void RestoreBackup(object sender, EventArgs e)
        {
            ImportPackageFromFile(true);
        }

        private void ImportPackageFromFile(bool backupOnly)
        {
            using (var dialog = new OpenFileDialog {
                Title = backupOnly ? UiText.T("恢复指令库备份") : (UiText.IsEnglish ? "Import instruction or preset package" : "导入指令或配置预设包"),
                Filter = UiText.IsEnglish ? "Instruction Switcher packages (*.ispkg.json;*.json)|*.ispkg.json;*.json|JSON files (*.json)|*.json" : "Instruction Switcher 包 (*.ispkg.json;*.json)|*.ispkg.json;*.json|JSON 文件 (*.json)|*.json"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) return;
                    if (!libraryReady || String.IsNullOrWhiteSpace(configSignature) || configSignature == "unavailable")
                        throw new InvalidOperationException(UiText.IsEnglish ? "The library is not ready" : "配置库尚未就绪");
                    PackageDocumentDto document = PackageExchange.ReadPackage(dialog.FileName);
                    if (backupOnly && document.kind != PackageKinds.Backup)
                        throw new InvalidDataException(UiText.IsEnglish ? "The selected file is not a full library backup" : "所选文件不是整库备份");
                    ImportPlan plan = PackageExchange.PreviewImport(document, settings, root, configSignature);
                    bool applyToTask;
                    string presetKeyToApply;
                    using (var preview = new ImportPreviewForm(plan, themeMode))
                    {
                        if (preview.ShowDialog(this) != DialogResult.OK) return;
                        applyToTask = preview.ApplyToCurrentTask;
                        presetKeyToApply = preview.PresetKeyToApply;
                    }
                    ImportResult result = PackageExchange.ApplyImport(plan, configFile, root);
                    ImportedPresetIdToApply = applyToTask
                        ? ResolveImportedPresetId(result, presetKeyToApply) : null;
                    instructionDirty = false;
                    presetDirty = false;
                    Reload(null, null);
                    int instructionCount = result.createdInstructions + result.updatedInstructions + result.reusedInstructions;
                    int presetCount = result.createdPresets + result.updatedPresets + result.reusedPresets;
                    string summary = UiText.IsEnglish
                        ? (plan.replaceLibrary ? "Restore complete: " : "Import complete: ") + instructionCount + " instructions, " + presetCount + " presets"
                        : (plan.replaceLibrary ? "恢复完成" : "导入完成") + "：" + instructionCount + " 条指令，" + presetCount + " 个配置预设";
                    SetLibraryStatus(summary);
                }
                catch (Exception error) { SetLibraryStatus(UiText.IsEnglish
                    ? (backupOnly ? "Restore failed: " : "Import failed: ") + UiText.Error(error.Message)
                    : (backupOnly ? "恢复失败：" : "导入失败：") + error.Message); }
            }
        }

        private static string ResolveImportedPresetId(ImportResult result, string packageKey)
        {
            if (result == null || String.IsNullOrWhiteSpace(packageKey) ||
                result.presetKeys == null || result.presetIds == null) return null;
            int count = Math.Min(result.presetKeys.Length, result.presetIds.Length);
            for (int index = 0; index < count; index++)
                if (String.Equals(result.presetKeys[index], packageKey, StringComparison.Ordinal))
                    return result.presetIds[index];
            return null;
        }

        private void SetLibraryStatus(string message)
        {
            if (instructionStatus != null) instructionStatus.Text = message;
            if (presetStatus != null) presetStatus.Text = message;
        }

        private static string SafeFileName(string value)
        {
            string name = String.IsNullOrWhiteSpace(value) ? "instruction-switcher-package" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }

        private void HandleClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmInstructionEdit() || !ConfirmPresetEdit()) e.Cancel = true;
        }
    }
}
