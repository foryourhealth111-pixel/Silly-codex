using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InstructionSwitcherCompanion
{
    internal sealed class ImportPreviewForm : Form
    {
        private readonly ImportPlan plan;
        private readonly ThemeMode themeMode;
        private readonly DataGridView grid;
        private readonly RichTextBox detail;
        private readonly Label summary;
        private readonly CheckBox showDependencies;
        private readonly CheckBox applyToCurrentTask;
        private readonly ComboBox applyPresetPicker;

        private sealed class PresetTargetOption
        {
            public string key { get; set; }
            public string name { get; set; }

            public override string ToString()
            {
                return name ?? "";
            }
        }

        public bool ApplyToCurrentTask
        {
            get { return applyToCurrentTask.Checked; }
        }

        public string PresetKeyToApply
        {
            get
            {
                if (!ApplyToCurrentTask) return null;
                PresetTargetOption selected = applyPresetPicker.SelectedItem as PresetTargetOption;
                return selected == null ? null : selected.key;
            }
        }

        public ImportPreviewForm(ImportPlan plan, ThemeMode themeMode)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            this.plan = plan;
            this.themeMode = themeMode;
            Text = plan.replaceLibrary ? "恢复指令库备份" : "导入包预览";
            ClientSize = new Size(780, 610);
            MinimumSize = new Size(700, 540);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9F);
            bool canApplyPreset = !plan.replaceLibrary &&
                (plan.presets ?? new ImportPresetPlanItem[0]).Length > 0;

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(18, 14, 18, 14)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, canApplyPreset ? 68F : 36F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));

            string kind = plan.document.kind == PackageKinds.Preset ? "配置预设包" :
                plan.document.kind == PackageKinds.Instruction ? "指令包" :
                plan.document.kind == PackageKinds.Backup ? "整库备份" : "旧版指令库包";
            var header = new ThemedLabel {
                Dock = DockStyle.Fill,
                Text = plan.document.name + "\r\n" + kind,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
                ThemeMode = themeMode,
                TextAlign = ContentAlignment.MiddleLeft
            };
            summary = new ThemedLabel {
                Dock = DockStyle.Fill,
                ThemeMode = themeMode,
                Role = ThemedLabelRole.Secondary,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var options = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = canApplyPreset ? 2 : 1,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            if (canApplyPreset) options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            var primaryOptions = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = new Padding(0, 4, 0, 0)
            };
            showDependencies = new CheckBox {
                AutoSize = true,
                Text = "将随包指令显示在自定义列表",
                Checked = plan.showPresetInstructions,
                Visible = plan.document.kind == PackageKinds.Preset,
                Margin = new Padding(0, 3, 22, 0)
            };
            showDependencies.CheckedChanged += delegate { plan.showPresetInstructions = showDependencies.Checked; };
            applyToCurrentTask = new CheckBox {
                AutoSize = true,
                Text = "导入后应用到当前任务",
                Checked = false,
                Visible = canApplyPreset,
                Margin = new Padding(0, 3, 0, 0)
            };
            applyToCurrentTask.CheckedChanged += delegate { RefreshApplyTargetState(); };
            primaryOptions.Controls.Add(showDependencies);
            primaryOptions.Controls.Add(applyToCurrentTask);
            if (plan.replaceLibrary)
            {
                primaryOptions.Controls.Add(new ThemedLabel {
                    AutoSize = true,
                    Text = "恢复会替换当前指令库与配置预设",
                    ThemeMode = themeMode,
                    Role = ThemedLabelRole.Warning,
                    Margin = new Padding(0, 5, 0, 0)
                });
            }
            options.Controls.Add(primaryOptions, 0, 0);

            var targetOptions = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Visible = canApplyPreset,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            targetOptions.Controls.Add(new ThemedLabel {
                AutoSize = false,
                Width = 108,
                Height = 28,
                Text = "应用配置预设",
                ThemeMode = themeMode,
                Role = ThemedLabelRole.Secondary,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            });
            applyPresetPicker = new ThemedComboBox {
                Width = 300,
                Height = 28,
                ThemeMode = themeMode,
                Enabled = false,
                Margin = Padding.Empty
            };
            targetOptions.Controls.Add(applyPresetPicker);
            if (canApplyPreset) options.Controls.Add(targetOptions, 0, 1);

            grid = new DataGridView {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                RowTemplate = { Height = 32 },
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.FixedSingle,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "项目",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 55F
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "判断",
                ReadOnly = true,
                Width = 150
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = "处理",
                ReadOnly = false,
                Width = 130
            });
            AddRows();
            grid.SelectionChanged += delegate { RefreshDetail(); };
            grid.CurrentCellDirtyStateChanged += delegate {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += ActionChanged;
            grid.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };

            var detailHeading = new ThemedLabel {
                Dock = DockStyle.Fill,
                Text = "内容预览",
                ThemeMode = themeMode,
                TextAlign = ContentAlignment.BottomLeft
            };
            detail = new RichTextBox {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F),
                DetectUrls = false
            };

            var footer = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0, 8, 0, 0)
            };
            var confirm = new ThemedButton {
                Text = plan.replaceLibrary ? "恢复备份" : "导入",
                Width = 100,
                Height = 32,
                ThemeMode = themeMode,
                Kind = ThemedButtonKind.Primary,
                DialogResult = DialogResult.None
            };
            confirm.Click += ConfirmImport;
            var cancel = new ThemedButton {
                Text = "取消",
                Width = 86,
                Height = 32,
                ThemeMode = themeMode,
                Kind = ThemedButtonKind.Ghost,
                DialogResult = DialogResult.Cancel
            };
            footer.Controls.Add(confirm);
            footer.Controls.Add(cancel);
            CancelButton = cancel;

            layout.Controls.Add(header, 0, 0);
            layout.Controls.Add(summary, 0, 1);
            layout.Controls.Add(options, 0, 2);
            layout.Controls.Add(grid, 0, 3);
            layout.Controls.Add(detailHeading, 0, 4);
            layout.Controls.Add(detail, 0, 5);
            layout.Controls.Add(footer, 0, 6);
            Controls.Add(layout);
            ApplyTheme();
            RefreshApplyTargets();
            RefreshSummary();
            if (grid.Rows.Count > 0) grid.Rows[0].Selected = true;
            RefreshDetail();
        }

        private void AddRows()
        {
            foreach (ImportInstructionPlanItem item in plan.instructions ?? new ImportInstructionPlanItem[0])
                AddRow(item.name, item.status, item.selectedAction, item.allowedActions, item);
            foreach (ImportPresetPlanItem item in plan.presets ?? new ImportPresetPlanItem[0])
                AddRow(item.name + "（配置预设）", item.status, item.selectedAction, item.allowedActions, item);
        }

        private void AddRow(string name, string status, string action, string[] allowed, object tag)
        {
            int index = grid.Rows.Add(name, status, PackageExchange.ActionLabel(action));
            DataGridViewRow row = grid.Rows[index];
            row.Tag = tag;
            if ((allowed ?? new string[0]).Length <= 1)
            {
                row.Cells[2].ReadOnly = true;
                return;
            }
            var cell = new DataGridViewComboBoxCell {
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat
            };
            foreach (string candidate in allowed) cell.Items.Add(PackageExchange.ActionLabel(candidate));
            cell.Value = PackageExchange.ActionLabel(action);
            row.Cells[2] = cell;
        }

        private void ActionChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
            DataGridViewRow row = grid.Rows[e.RowIndex];
            string label = Convert.ToString(row.Cells[2].Value);
            ImportInstructionPlanItem instruction = row.Tag as ImportInstructionPlanItem;
            if (instruction != null) instruction.selectedAction = ActionForLabel(label, instruction.allowedActions);
            ImportPresetPlanItem preset = row.Tag as ImportPresetPlanItem;
            if (preset != null) preset.selectedAction = ActionForLabel(label, preset.allowedActions);
            RefreshApplyTargets();
            RefreshSummary();
            RefreshDetail();
        }

        private void RefreshApplyTargets()
        {
            PresetTargetOption previous = applyPresetPicker.SelectedItem as PresetTargetOption;
            string previousKey = previous == null ? null : previous.key;
            ImportPresetPlanItem[] candidates = (plan.presets ?? new ImportPresetPlanItem[0])
                .Where(item => item.selectedAction != ImportActions.Skip).ToArray();

            applyPresetPicker.BeginUpdate();
            try
            {
                applyPresetPicker.Items.Clear();
                foreach (ImportPresetPlanItem item in candidates)
                    applyPresetPicker.Items.Add(new PresetTargetOption { key = item.packageKey, name = item.name });
                int preservedIndex = -1;
                if (previousKey != null)
                {
                    for (int index = 0; index < applyPresetPicker.Items.Count; index++)
                    {
                        PresetTargetOption option = applyPresetPicker.Items[index] as PresetTargetOption;
                        if (option != null && String.Equals(option.key, previousKey, StringComparison.Ordinal))
                        {
                            preservedIndex = index;
                            break;
                        }
                    }
                }
                applyPresetPicker.SelectedIndex = preservedIndex >= 0 ? preservedIndex :
                    (candidates.Length == 1 ? 0 : -1);
            }
            finally
            {
                applyPresetPicker.EndUpdate();
            }

            applyToCurrentTask.Enabled = candidates.Length > 0;
            if (candidates.Length == 0) applyToCurrentTask.Checked = false;
            RefreshApplyTargetState();
        }

        private void RefreshApplyTargetState()
        {
            applyPresetPicker.Enabled = applyToCurrentTask.Visible && applyToCurrentTask.Enabled &&
                applyToCurrentTask.Checked;
        }

        private static string ActionForLabel(string label, string[] allowed)
        {
            return (allowed ?? new string[0]).FirstOrDefault(action =>
                String.Equals(PackageExchange.ActionLabel(action), label, StringComparison.CurrentCulture)) ?? "";
        }

        private void RefreshSummary()
        {
            int create = (plan.instructions ?? new ImportInstructionPlanItem[0]).Count(item =>
                item.selectedAction == ImportActions.Create || item.selectedAction == ImportActions.Copy) +
                (plan.presets ?? new ImportPresetPlanItem[0]).Count(item =>
                item.selectedAction == ImportActions.Create || item.selectedAction == ImportActions.Copy);
            int reuse = (plan.instructions ?? new ImportInstructionPlanItem[0]).Count(item => item.selectedAction == ImportActions.Reuse) +
                (plan.presets ?? new ImportPresetPlanItem[0]).Count(item => item.selectedAction == ImportActions.Reuse);
            int update = (plan.instructions ?? new ImportInstructionPlanItem[0]).Count(item => item.selectedAction == ImportActions.Update) +
                (plan.presets ?? new ImportPresetPlanItem[0]).Count(item => item.selectedAction == ImportActions.Update);
            int conflict = (plan.instructions ?? new ImportInstructionPlanItem[0]).Count(item => item.conflict) +
                (plan.presets ?? new ImportPresetPlanItem[0]).Count(item => item.conflict);
            if (plan.replaceLibrary)
                summary.Text = "恢复 " + (plan.instructions ?? new ImportInstructionPlanItem[0]).Length + " 条指令 · " +
                    (plan.presets ?? new ImportPresetPlanItem[0]).Length + " 个配置预设";
            else
                summary.Text = "新增 " + create + " · 复用 " + reuse + " · 更新 " + update + " · 冲突 " + conflict;
        }

        private void RefreshDetail()
        {
            if (detail == null || grid.SelectedRows.Count == 0)
            {
                if (detail != null) detail.Text = "";
                return;
            }
            object tag = grid.SelectedRows[0].Tag;
            ImportInstructionPlanItem instruction = tag as ImportInstructionPlanItem;
            if (instruction != null)
            {
                detail.Text = instruction.name + "\r\n" + instruction.detail + "\r\n\r\n" +
                    (instruction.incoming.content ?? "");
                return;
            }
            ImportPresetPlanItem preset = tag as ImportPresetPlanItem;
            if (preset == null) { detail.Text = ""; return; }
            string[] names = (preset.incoming.instructionKeys ?? new string[0]).Select(key => {
                PackageInstructionDto item = (plan.document.instructions ?? new PackageInstructionDto[0])
                    .FirstOrDefault(candidate => String.Equals(candidate.packageKey, key, StringComparison.Ordinal));
                return item == null ? key : item.name;
            }).ToArray();
            detail.Text = preset.name + "\r\n" + preset.detail + "\r\n\r\n" + String.Join("\r\n", names);
        }

        private void ConfirmImport(object sender, EventArgs e)
        {
            plan.showPresetInstructions = showDependencies.Checked;
            if (ApplyToCurrentTask && PresetKeyToApply == null)
            {
                MessageBox.Show(this, "请选择导入后要应用的配置预设。", "请选择配置预设",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string error = PackageExchange.ValidatePlan(plan);
            if (error != null)
            {
                MessageBox.Show(this, error, "无法导入", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyTheme()
        {
            ThemePalette palette = CompanionTheme.Palette(themeMode);
            CompanionTheme.Apply(this, themeMode);
            BackColor = palette.Window;
            grid.BackgroundColor = palette.Window;
            grid.GridColor = palette.Border;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Surface;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Surface;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
            grid.DefaultCellStyle.BackColor = palette.Input;
            grid.DefaultCellStyle.ForeColor = palette.Text;
            grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = palette.Text;
            detail.BackColor = palette.Input;
            detail.ForeColor = palette.Text;
            CompanionTheme.ApplyWindow(this, themeMode);
        }
    }
}
