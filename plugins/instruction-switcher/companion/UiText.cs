using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InstructionSwitcherCompanion
{
    internal enum UiLanguage
    {
        Chinese,
        English
    }

    internal static class UiText
    {
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "名称", "Name" },
            { "取消", "Cancel" },
            { "确定", "OK" },
            { "选择配置预设", "Select preset" },
            { "更新目标", "Preset to update" },
            { "随预设", "Bundled with preset" },
            { "已隐藏", "Hidden" },
            { "管理指令库与配置预设", "Manage instructions and presets" },
            { "指令库", "Instruction library" },
            { "配置预设", "Presets" },
            { "备份整个指令库…", "Back up library..." },
            { "恢复备份…", "Restore backup..." },
            { "更多库操作", "More library actions" },
            { "导入包…", "Import package..." },
            { "搜索指令", "Search instructions" },
            { "常用指令", "Common instructions" },
            { "随预设导入", "Imported with presets" },
            { "全部指令", "All instructions" },
            { "导出当前指令…", "Export selected instruction..." },
            { "删除指令项", "Delete instruction" },
            { "新增指令", "New instruction" },
            { "新增指令项", "Create an instruction" },
            { "复制指令", "Duplicate instruction" },
            { "复制指令项", "Duplicate selected instruction" },
            { "删除指令", "Delete instruction" },
            { "导出指令", "Export instruction" },
            { "导出当前指令项", "Export selected instruction" },
            { "保存", "Save" },
            { "就绪", "Ready" },
            { "编辑", "Edit" },
            { "预览", "Preview" },
            { "显示在自定义列表", "Show in custom list" },
            { "搜索预设", "Search presets" },
            { "新增预设", "New preset" },
            { "新增配置预设", "Create a preset" },
            { "删除预设", "Delete preset" },
            { "删除配置预设", "Delete preset" },
            { "导出预设", "Export preset" },
            { "导出当前配置预设及依赖指令", "Export preset and its instructions" },
            { "可用指令", "Available instructions" },
            { "勾选后加入预设", "Select instructions to add" },
            { "启用顺序", "Enabled order" },
            { "拖动条目调整顺序 · 取消勾选可移除", "Drag to reorder · clear a check to remove" },
            { "设为新任务的默认配置", "Use as the default for new tasks" },
            { "展开指令面板", "Open instruction panel" },
            { "折叠为悬浮球", "Collapse to floating button" },
            { "当前任务", "Current task" },
            { "正在识别", "Detecting" },
            { "自动跟随 Codex 当前任务", "Follow the current Codex task" },
            { "等待 Codex 任务", "Waiting for a Codex task" },
            { "保存为新预设", "Save as new preset" },
            { "保存为配置预设", "Save as preset" },
            { "更新配置预设", "Update preset" },
            { "新配置预设", "New preset" },
            { "更新当前预设", "Update current preset" },
            { "撤销最近一次应用", "Undo last preset" },
            { "保存、更新或撤销配置预设", "Save, update, or undo a preset" },
            { "启用指令", "Enabled instructions" },
            { "管理指令库", "Manage library" },
            { "编辑指令项、配置预设、默认配置和导入导出", "Edit instructions, presets, defaults, and packages" },
            { "等待任务状态", "Waiting for task state" },
            { "打开配置目录", "Open data folder" },
            { "隐藏到托盘", "Hide to tray" },
            { "退出", "Exit" },
            { "更多选项", "More options" },
            { "显示面板", "Show panel" },
            { "显示悬浮球", "Show floating button" },
            { "隐藏", "Hide" },
            { "切换到白天模式", "Switch to light theme" },
            { "切换到黑夜模式", "Switch to dark theme" },
            { "语言", "Language" },
            { "中文", "Chinese" },
            { "英文", "English" },
            { "自定义", "Custom" },
            { "自定义配置", "Custom selection" },
            { "手动目标", "Manual target" },
            { "请选择任务", "Select a task" },
            { "已准确跟随", "Following" },
            { "正文暂不可用", "Content unavailable" },
            { "打开或恢复一个 Codex 任务", "Open or resume a Codex task" },
            { "指令库暂不可用", "Instruction library unavailable" },
            { "自定义列表中没有可显示的指令", "No instructions are visible in the custom list" },
            { "当前为只读预览。", "This is a read-only preview." },
            { "可拖动调整启用顺序。", "Drag enabled instructions to reorder them." },
            { "配置预设包", "Preset package" },
            { "指令包", "Instruction package" },
            { "整库备份", "Full library backup" },
            { "旧版指令库包", "Legacy library package" },
            { "恢复指令库备份", "Restore library backup" },
            { "导入包预览", "Import package preview" },
            { "将随包指令显示在自定义列表", "Show packaged instructions in the custom list" },
            { "导入后应用到当前任务", "Apply to the current task after import" },
            { "恢复会替换当前指令库与配置预设", "Restore replaces the current library and presets" },
            { "应用配置预设", "Apply preset" },
            { "项目", "Item" },
            { "判断", "Status" },
            { "处理", "Action" },
            { "内容预览", "Content preview" },
            { "恢复备份", "Restore backup" },
            { "导入", "Import" },
            { "创建副本", "Create copy" },
            { "复用本地", "Reuse local" },
            { "更新本地", "Update local" },
            { "跳过", "Skip" },
            { "恢复", "Restore" },
            { "新增", "Create" },
            { "已导入", "Already imported" },
            { "包更新", "Package update" },
            { "本地已修改", "Locally modified" },
            { "内容相同", "Same content" },
            { "名称冲突", "Name conflict" },
            { "从备份恢复", "Restore from backup" },
            { "来源和正文一致", "Source and content match" },
            { "替换本地指令库内容", "Replace the local instruction library" },
            { "替换本地配置预设", "Replace local presets" },
            { "本地正文保持上次导入版本", "Local content still matches the last imported version" },
            { "默认保留本地版本并创建副本", "Keep the local version and create a copy by default" },
            { "复用本地同名指令", "Reuse the local instruction with the same name" },
            { "正文不同，默认创建副本", "Content differs; create a copy by default" },
            { "创建新的指令项", "Create a new instruction" },
            { "来源和内容一致", "Source and configuration match" },
            { "本地预设保持上次导入版本", "Local preset still matches the last imported version" },
            { "默认保留本地预设并创建副本", "Keep the local preset and create a copy by default" },
            { "复用本地同名配置预设", "Reuse the local preset with the same name" },
            { "引用内容不同，默认创建副本", "References differ; create a copy by default" },
            { "创建新的配置预设", "Create a new preset" },
            { "无", "None" },
            { "时间未知", "Unknown time" },
        };

        private static readonly Dictionary<string, string> ExactEnglish = new Dictionary<string, string>
        {
            { "前台任务探测尚未就绪", "Task detection is not ready" },
            { "前台任务探测数据无效", "Task detection data is invalid" },
            { "暂时无法确认 Codex 前台任务", "The active Codex task cannot be confirmed" },
            { "前台任务探测时间无效", "Task detection timestamp is invalid" },
            { "前台任务探测已断开", "Task detection is disconnected" },
            { "前台任务映射校验失败", "Task mapping validation failed" },
            { "前台任务探测读取失败", "Task detection could not be read" },
            { "前台任务探测器启动失败", "Task detector could not be started" },
            { "配置预设已导入，当前任务暂不可编辑", "The preset was imported; the current task cannot be edited yet" },
            { "任务或状态已更新，请重新确认写入目标", "The task or state changed; confirm the target again" },
            { "已撤销最近一次预设应用", "The last preset application was undone" },
            { "请选择写入目标", "Select a task to edit" },
            { "Hook 回执读取失败", "Hook acknowledgement could not be read" },
            { "尚未启用指令", "No instructions enabled" },
            { "指令库读取失败", "Instruction library could not be read" },
            { "状态文件格式无效", "The task state file is invalid" },
            { "当前任务处于只读状态", "The current task is read-only" },
            { "状态文件路径无效", "The task state path is invalid" },
            { "状态已在其他窗口更新，请重新选择任务", "The state changed in another window; select the task again" },
            { "Hook 回执格式无效", "The Hook acknowledgement is invalid" },
            { "会话标识无效", "The session identifier is invalid" },
            { "运行目录无效", "The runtime directory is invalid" },
            { "配置文件不存在", "The configuration file does not exist" },
            { "配置文件为空", "The configuration file is empty" },
            { "配置文件版本不受支持", "The configuration file version is unsupported" },
            { "配置库尚未成功读取，保存已取消", "The library has not loaded; save was canceled" },
            { "配置库已在其他窗口更新，请重新读取", "The library changed in another window; reload it" },
            { "指令正文路径无效", "The instruction content path is invalid" },
            { "指令正文路径越界", "The instruction content path escapes the data folder" },
            { "指令正文过大", "The instruction content is too large" },
            { "控制命令无效", "The control command is invalid" },
            { "导入文件路径为空", "The import file path is empty" },
            { "导入文件不存在", "The import file does not exist" },
            { "导入包过大", "The import package is too large" },
            { "导入文件为空", "The import file is empty" },
            { "导入文件不是有效 JSON", "The import file is not valid JSON" },
            { "无法识别导入包格式", "The import package format is not recognized" },
            { "请至少选择一条指令项", "Select at least one instruction" },
            { "请选择要导出的配置预设", "Select a preset to export" },
            { "导入计划为空", "The import plan is empty" },
            { "配置库已更新，请重新预览导入内容", "The library changed; preview the import again" },
            { "导入包版本不受支持", "The import package version is unsupported" },
            { "导入包类型无效", "The import package type is invalid" },
            { "指令包不能包含配置预设", "An instruction package cannot contain presets" },
            { "配置预设包中没有配置预设", "The preset package contains no presets" },
            { "文件替换失败", "File replacement failed" },
            { "状态锁等待超时，请稍后重试", "Timed out waiting for the state lock. Try again later." },
            { "旧配置指令 ID 无效", "The legacy instruction ID is invalid" },
            { "旧配置正文路径无效", "The legacy instruction content path is invalid" },
            { "指令库条目数量无效", "The instruction count is invalid" },
            { "配置预设数量无效", "The preset count is invalid" },
            { "指令项元数据无效", "The instruction metadata is invalid" },
            { "配置预设元数据无效", "The preset metadata is invalid" },
            { "正文保存失败，部分正文未恢复", "Content save failed; some content could not be restored" },
            { "正文回滚失败", "Content rollback failed" },
            { "来源内容指纹无效", "The source content fingerprint is invalid" },
            { "旧版导入包格式无效", "The legacy import package format is invalid" },
            { "导入包指令项过多", "The import package contains too many instructions" },
            { "导入包配置预设过多", "The import package contains too many presets" },
            { "导入包包含空指令项", "The import package contains a null instruction" },
            { "导入包包含重复指令键", "The import package contains duplicate instruction keys" },
            { "备份包含无效或重复指令 ID", "The backup contains an invalid or duplicate instruction ID" },
            { "导入指令正文过大", "The imported instruction content is too large" },
            { "导入包包含空配置预设", "The import package contains a null preset" },
            { "导入包包含重复预设键", "The import package contains duplicate preset keys" },
            { "备份包含无效或重复预设 ID", "The backup contains an invalid or duplicate preset ID" },
            { "配置预设引用了包内不存在的指令", "A preset references an instruction missing from the package" },
            { "配置预设包含重复指令引用", "A preset contains duplicate instruction references" },
            { "备份中的控制命令无效", "The backup control command is invalid" },
            { "备份中的默认配置预设无效", "The backup default preset is invalid" },
            { "导入包元数据过长", "The import package metadata is too long" },
            { "导入包内容指纹无效", "The import package content fingerprint is invalid" },
            { "包 ID无效", "The package ID is invalid" },
            { "包名称无效", "The package name is invalid" },
            { "指令包内键无效", "The instruction package key is invalid" },
            { "指令名称无效", "The instruction name is invalid" },
            { "预设包内键无效", "The preset package key is invalid" },
            { "预设指令引用无效", "The preset instruction reference is invalid" },
            { "配置预设名称无效", "The preset name is invalid" },
            { "来源包 ID过长", "The source package ID is too long" },
            { "来源条目键过长", "The source instruction key is too long" },
            { "来源预设键过长", "The source preset key is too long" },
        };

        private static UiLanguage current = DetectSystemLanguage();

        public static UiLanguage Current
        {
            get { return current; }
            set { current = value; }
        }

        public static bool IsEnglish { get { return current == UiLanguage.English; } }

        public static UiLanguage DetectSystemLanguage()
        {
            string name = CultureInfo.CurrentUICulture.Name ?? "";
            return name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
                ? UiLanguage.Chinese
                : UiLanguage.English;
        }

        public static UiLanguage Parse(string value)
        {
            if (String.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "english", StringComparison.OrdinalIgnoreCase))
                return UiLanguage.English;
            if (String.Equals(value, "zh", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "chinese", StringComparison.OrdinalIgnoreCase))
                return UiLanguage.Chinese;
            return DetectSystemLanguage();
        }

        public static string Code(UiLanguage language)
        {
            return language == UiLanguage.English ? "en" : "zh";
        }

        public static string T(string chinese)
        {
            if (!IsEnglish || String.IsNullOrEmpty(chinese)) return chinese;
            string value;
            return English.TryGetValue(chinese, out value) ? value : chinese;
        }

        public static string Error(string chinese)
        {
            if (!IsEnglish || String.IsNullOrEmpty(chinese)) return chinese;
            string value;
            if (English.TryGetValue(chinese, out value)) return value;
            if (ExactEnglish.TryGetValue(chinese, out value)) return value;
            foreach (KeyValuePair<string, string> item in ExactEnglish)
            {
                if (chinese.StartsWith(item.Key, StringComparison.Ordinal))
                {
                    string suffix = chinese.Substring(item.Key.Length);
                    if (suffix.StartsWith("：", StringComparison.Ordinal))
                        suffix = ": " + suffix.Substring(1);
                    return item.Value + suffix;
                }
            }
            return TranslateDynamic(chinese);
        }

        private static string TranslateDynamic(string value)
        {
            var replacements = new[] {
                new[] { "保存失败，已恢复：", "Save failed; restored: " },
                new[] { "保存预设失败：", "Failed to save preset: " },
                new[] { "更新预设失败：", "Failed to update preset: " },
                new[] { "预设保存失败：", "Failed to save preset: " },
                new[] { "任务状态发生变化，预设配置已恢复：", "Task state changed; preset settings were restored: " },
                new[] { "预设已保存，任务状态未更新，配置回滚失败：", "Preset saved; task state was not updated; configuration rollback failed: " },
                new[] { "预设已保存，任务状态未更新；配置已被其他窗口修改，保留当前配置：", "Preset saved; task state was not updated; the configuration changed in another window, so the current configuration was kept: " },
                new[] { "任务状态已更新，指令库重新读取失败", "Task state updated; failed to reload the instruction library" },
                new[] { "任务状态已更新，重新读取失败", "Task state updated; reload failed" },
                new[] { "配置和任务状态已保存，界面刷新失败：", "Settings and task state saved; UI refresh failed: " },
                new[] { "指令项“", "Instruction \"" },
                new[] { "配置预设“", "Preset \"" },
                new[] { "”的处理方式无效", "\" has an invalid action" },
                new[] { "”缺少必要指令，请调整跳过项", "\" is missing required instructions. Adjust skipped items" },
                new[] { "”仍引用旧指令，请选择更新本地或创建副本", "\" still references old instructions. Choose Update local or Create copy" },
                new[] { "导入后指令项数量超过上限", "The import would exceed the instruction limit" },
                new[] { "导入后配置预设数量超过上限", "The import would exceed the preset limit" },
                new[] { "正文读取失败：", "Content read failed: " },
                new[] { "指令库读取失败：", "Library read failed: " },
                new[] { "状态读取失败：", "State read failed: " },
                new[] { "读取失败：", "Read failed: " },
                new[] { "保存失败：", "Save failed: " },
                new[] { "应用失败：", "Apply failed: " },
                new[] { "撤销失败：", "Undo failed: " },
                new[] { "删除失败：", "Delete failed: " },
                new[] { "导入失败：", "Import failed: " },
                new[] { "导出失败：", "Export failed: " },
                new[] { "备份失败：", "Backup failed: " },
                new[] { "恢复失败：", "Restore failed: " },
                new[] { "已保存，等待下一条消息", "Saved; waiting for the next message" },
                new[] { "Hook 已读取", "Hook read the state" },
                new[] { "已跟随当前任务", "Following current task" },
                new[] { "已跟随", "Following" },
                new[] { "已保存", "Saved" },
                new[] { "已确认", "Confirmed" },
                new[] { "正在识别前台任务", "Detecting active task" },
                new[] { "只读预览", "Read-only preview" },
                new[] { "条已启用", "enabled" },
                new[] { "项", "items" },
                new[] { "配置：", "Preset: " },
                new[] { "当前任务：", "Current task: " },
                new[] { "新指令项", "New instruction" },
                new[] { "新配置预设", "New preset" },
                new[] { "待保存", "Unsaved" },
                new[] { "有未保存修改", "Unsaved changes" },
                new[] { "本地创建", "Created locally" },
                new[] { "内部 ID：", "Internal ID: " },
                new[] { "副本", " copy" },
                new[] { "；", "; " },
                new[] { "，", ", " },
                new[] { "。", "." },
            };
            string translated = value;
            foreach (string[] replacement in replacements)
                translated = translated.Replace(replacement[0], replacement[1]);
            return translated;
        }

        public static string F(string chineseFormat, params object[] args)
        {
            return String.Format(CultureInfo.CurrentCulture, T(chineseFormat), args);
        }

        public static string EF(string chineseFormat, params object[] args)
        {
            return String.Format(CultureInfo.CurrentCulture, Error(chineseFormat), args);
        }

        public static string CountItems(int count)
        {
            return IsEnglish ? count + (count == 1 ? " item" : " items") : count + " 项";
        }

        public static string CountEnabled(int count)
        {
            return IsEnglish ? count + " enabled" : count + " 条已启用";
        }

        public static string Quote(string value)
        {
            return IsEnglish ? "\"" + value + "\"" : "“" + value + "”";
        }

        public static string JoinList(IEnumerable<string> values)
        {
            return String.Join(IsEnglish ? ", " : "、", values ?? new string[0]);
        }
    }
}
