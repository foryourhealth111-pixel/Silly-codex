using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace InstructionSwitcherCompanion
{
    internal static class PackageKinds
    {
        public const string Instruction = "instruction";
        public const string Preset = "preset";
        public const string Backup = "backup";
        public const string Legacy = "legacy";
    }

    internal static class ImportActions
    {
        public const string Create = "create";
        public const string Reuse = "reuse";
        public const string Update = "update";
        public const string Copy = "copy";
        public const string Skip = "skip";
        public const string Replace = "replace";
    }

    internal sealed class PackageProbeDto
    {
        public string format { get; set; }
        public int schemaVersion { get; set; }
        public int version { get; set; }
    }

    internal sealed class PackageInstructionDto
    {
        public string packageKey { get; set; }
        public string stableId { get; set; }
        public string name { get; set; }
        public string content { get; set; }
        public string origin { get; set; }
        public string sourcePackageId { get; set; }
        public string sourcePackageKey { get; set; }
        public string sourceContentHash { get; set; }
        public bool? showInCustomPicker { get; set; }
        public string createdAt { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class PackagePresetDto
    {
        public string packageKey { get; set; }
        public string stableId { get; set; }
        public string name { get; set; }
        public string[] instructionKeys { get; set; }
        public string origin { get; set; }
        public string sourcePackageId { get; set; }
        public string sourcePackageKey { get; set; }
        public string sourceContentHash { get; set; }
        public string createdAt { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class PackageDocumentDto
    {
        public string format { get; set; }
        public int schemaVersion { get; set; }
        public string kind { get; set; }
        public string packageId { get; set; }
        public string name { get; set; }
        public string exportedAt { get; set; }
        public string command { get; set; }
        public string defaultPresetKey { get; set; }
        public PackageInstructionDto[] instructions { get; set; }
        public PackagePresetDto[] presets { get; set; }
    }

    internal sealed class ImportInstructionPlanItem
    {
        public string packageKey { get; set; }
        public string name { get; set; }
        public string status { get; set; }
        public string detail { get; set; }
        public string selectedAction { get; set; }
        public string[] allowedActions { get; set; }
        public string existingId { get; set; }
        public string newId { get; set; }
        public bool conflict { get; set; }
        internal PackageInstructionDto incoming { get; set; }
    }

    internal sealed class ImportPresetPlanItem
    {
        public string packageKey { get; set; }
        public string name { get; set; }
        public string status { get; set; }
        public string detail { get; set; }
        public string selectedAction { get; set; }
        public string[] allowedActions { get; set; }
        public string existingId { get; set; }
        public string newId { get; set; }
        public bool conflict { get; set; }
        internal PackagePresetDto incoming { get; set; }
    }

    internal sealed class ImportPlan
    {
        public PackageDocumentDto document { get; set; }
        public string expectedSignature { get; set; }
        public bool replaceLibrary { get; set; }
        public bool showPresetInstructions { get; set; }
        public ImportInstructionPlanItem[] instructions { get; set; }
        public ImportPresetPlanItem[] presets { get; set; }
        internal SettingsDto snapshot { get; set; }
    }

    internal sealed class ImportResult
    {
        public int createdInstructions { get; set; }
        public int updatedInstructions { get; set; }
        public int reusedInstructions { get; set; }
        public int skippedInstructions { get; set; }
        public int createdPresets { get; set; }
        public int updatedPresets { get; set; }
        public int reusedPresets { get; set; }
        public int skippedPresets { get; set; }
        public string[] presetKeys { get; set; }
        public string[] presetIds { get; set; }
    }

    internal static class PackageExchange
    {
        private const string PackageFormat = "instruction-switcher-package";
        private const int SchemaVersion = 2;
        private const int MaxPackageBytes = 256 * 1024 * 1024;
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaxPackageBytes;
            return serializer;
        }

        public static PackageDocumentDto ReadPackage(string file)
        {
            if (String.IsNullOrWhiteSpace(file)) throw new InvalidDataException("导入文件路径为空");
            var info = new FileInfo(file);
            if (!info.Exists) throw new FileNotFoundException("导入文件不存在", file);
            if (info.Length > MaxPackageBytes) throw new InvalidDataException("导入包过大");
            return ReadPackageJson(File.ReadAllText(file, Encoding.UTF8));
        }

        public static PackageDocumentDto ReadPackageJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new InvalidDataException("导入文件为空");
            if (Encoding.UTF8.GetByteCount(json) > MaxPackageBytes) throw new InvalidDataException("导入包过大");
            PackageProbeDto probe;
            try { probe = Serializer.Deserialize<PackageProbeDto>(json); }
            catch (Exception error) { throw new InvalidDataException("导入文件不是有效 JSON", error); }
            if (probe != null && String.Equals(probe.format, PackageFormat, StringComparison.Ordinal))
            {
                PackageDocumentDto document = Serializer.Deserialize<PackageDocumentDto>(json);
                return NormalizePackage(document);
            }
            if (probe != null && probe.version == 1)
            {
                ExportBundleDto legacy = Serializer.Deserialize<ExportBundleDto>(json);
                return ConvertLegacyBundle(legacy, json);
            }
            throw new InvalidDataException("无法识别导入包格式");
        }

        public static string SerializePackage(PackageDocumentDto document)
        {
            return Serializer.Serialize(NormalizePackage(document));
        }

        public static void WritePackage(string file, PackageDocumentDto document)
        {
            File.WriteAllText(file, SerializePackage(document), new UTF8Encoding(false));
        }

        public static PackageDocumentDto CreateInstructionPackage(string root, SettingsDto settings,
            string[] instructionIds, string packageName)
        {
            settings = LibraryStore.Normalize(settings, root);
            var selected = new HashSet<string>(instructionIds ?? new string[0], StringComparer.Ordinal);
            InstructionDto[] instructions = (settings.instructions ?? new InstructionDto[0])
                .Where(item => selected.Contains(item.id)).ToArray();
            if (instructions.Length == 0) throw new InvalidOperationException("请至少选择一条指令项");
            string name = String.IsNullOrWhiteSpace(packageName)
                ? (instructions.Length == 1 ? instructions[0].name : "指令包")
                : packageName.Trim();
            return NormalizePackage(new PackageDocumentDto {
                format = PackageFormat,
                schemaVersion = SchemaVersion,
                kind = PackageKinds.Instruction,
                packageId = StablePackageId("instructions", instructions.Select(item => item.id)),
                name = name,
                exportedAt = DateTime.UtcNow.ToString("o"),
                instructions = instructions.Select(item => ExportInstruction(root, item, false)).ToArray(),
                presets = new PackagePresetDto[0]
            });
        }

        public static PackageDocumentDto CreatePresetPackage(string root, SettingsDto settings, string presetId)
        {
            settings = LibraryStore.Normalize(settings, root);
            PresetDto preset = (settings.presets ?? new PresetDto[0]).FirstOrDefault(item =>
                String.Equals(item.id, presetId, StringComparison.Ordinal));
            if (preset == null) throw new InvalidOperationException("请选择要导出的配置预设");
            var byId = (settings.instructions ?? new InstructionDto[0]).ToDictionary(item => item.id, StringComparer.Ordinal);
            InstructionDto[] dependencies = (preset.instructionIds ?? new string[0]).Where(byId.ContainsKey)
                .Select(id => byId[id]).ToArray();
            return NormalizePackage(new PackageDocumentDto {
                format = PackageFormat,
                schemaVersion = SchemaVersion,
                kind = PackageKinds.Preset,
                packageId = StablePackageId("preset", new[] { preset.id }),
                name = preset.name,
                exportedAt = DateTime.UtcNow.ToString("o"),
                instructions = dependencies.Select(item => ExportInstruction(root, item, false)).ToArray(),
                presets = new[] { ExportPreset(preset, false) }
            });
        }

        public static PackageDocumentDto CreateBackup(string root, SettingsDto settings)
        {
            settings = LibraryStore.Normalize(settings, root);
            return NormalizePackage(new PackageDocumentDto {
                format = PackageFormat,
                schemaVersion = SchemaVersion,
                kind = PackageKinds.Backup,
                packageId = "backup-" + Guid.NewGuid().ToString("N"),
                name = "Instruction Switcher 指令库备份",
                exportedAt = DateTime.UtcNow.ToString("o"),
                command = settings.command,
                defaultPresetKey = settings.defaultPresetId,
                instructions = (settings.instructions ?? new InstructionDto[0])
                    .Select(item => ExportInstruction(root, item, true)).ToArray(),
                presets = (settings.presets ?? new PresetDto[0]).Select(item => ExportPreset(item, true)).ToArray()
            });
        }

        public static ImportPlan PreviewImport(PackageDocumentDto document, SettingsDto settings,
            string root, string expectedSignature)
        {
            document = NormalizePackage(document);
            settings = LibraryStore.Normalize(settings, root);
            var plan = new ImportPlan {
                document = document,
                expectedSignature = expectedSignature,
                replaceLibrary = document.kind == PackageKinds.Backup,
                showPresetInstructions = document.kind != PackageKinds.Preset,
                snapshot = Clone(settings)
            };
            if (plan.replaceLibrary)
            {
                plan.instructions = document.instructions.Select(item => new ImportInstructionPlanItem {
                    packageKey = item.packageKey, name = item.name, status = "从备份恢复", detail = "替换本地指令库内容",
                    selectedAction = ImportActions.Replace, allowedActions = new[] { ImportActions.Replace },
                    newId = item.stableId, incoming = item
                }).ToArray();
                plan.presets = document.presets.Select(item => new ImportPresetPlanItem {
                    packageKey = item.packageKey, name = item.name, status = "从备份恢复", detail = "替换本地配置预设",
                    selectedAction = ImportActions.Replace, allowedActions = new[] { ImportActions.Replace },
                    newId = item.stableId, incoming = item
                }).ToArray();
                return plan;
            }

            var usedInstructionIds = new HashSet<string>((settings.instructions ?? new InstructionDto[0]).Select(item => item.id), StringComparer.Ordinal);
            var bodyById = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (InstructionDto item in settings.instructions ?? new InstructionDto[0])
                bodyById[item.id] = LibraryStore.ReadBody(root, item);
            var instructionPlans = new List<ImportInstructionPlanItem>();
            foreach (PackageInstructionDto incoming in document.instructions)
                instructionPlans.Add(PreviewInstruction(document, incoming, settings.instructions, bodyById, usedInstructionIds));
            plan.instructions = instructionPlans.ToArray();

            var usedPresetIds = new HashSet<string>((settings.presets ?? new PresetDto[0]).Select(item => item.id), StringComparer.Ordinal);
            var presetPlans = new List<ImportPresetPlanItem>();
            foreach (PackagePresetDto incoming in document.presets)
                presetPlans.Add(PreviewPreset(document, incoming, settings.presets, plan.instructions, usedPresetIds));
            plan.presets = presetPlans.ToArray();
            return plan;
        }

        public static string ValidatePlan(ImportPlan plan)
        {
            if (plan == null || plan.document == null) return "导入计划为空";
            foreach (ImportInstructionPlanItem item in plan.instructions ?? new ImportInstructionPlanItem[0])
                if (!Allowed(item.selectedAction, item.allowedActions)) return "指令项“" + item.name + "”的处理方式无效";
            foreach (ImportPresetPlanItem item in plan.presets ?? new ImportPresetPlanItem[0])
                if (!Allowed(item.selectedAction, item.allowedActions)) return "配置预设“" + item.name + "”的处理方式无效";
            if (plan.replaceLibrary) return null;

            var mapped = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ImportInstructionPlanItem item in plan.instructions ?? new ImportInstructionPlanItem[0])
            {
                string id = TargetInstructionId(item);
                if (id != null) mapped[item.packageKey] = id;
            }
            foreach (ImportPresetPlanItem item in plan.presets ?? new ImportPresetPlanItem[0])
            {
                if (item.selectedAction == ImportActions.Skip) continue;
                string[] refs = (item.incoming.instructionKeys ?? new string[0])
                    .Where(mapped.ContainsKey).Select(key => mapped[key]).Distinct(StringComparer.Ordinal).ToArray();
                string missing = (item.incoming.instructionKeys ?? new string[0]).FirstOrDefault(key => !mapped.ContainsKey(key));
                if (missing != null) return "配置预设“" + item.name + "”缺少必要指令，请调整跳过项";
                if (item.selectedAction == ImportActions.Reuse)
                {
                    PresetDto existing = (plan.snapshot.presets ?? new PresetDto[0]).FirstOrDefault(local =>
                        String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                    if (existing == null || !Same(existing.instructionIds, refs))
                        return "配置预设“" + item.name + "”仍引用旧指令，请选择更新本地或创建副本";
                }
            }
            int instructionCount = (plan.snapshot.instructions ?? new InstructionDto[0]).Length +
                (plan.instructions ?? new ImportInstructionPlanItem[0]).Count(item =>
                    item.selectedAction == ImportActions.Create || item.selectedAction == ImportActions.Copy);
            int presetCount = (plan.snapshot.presets ?? new PresetDto[0]).Length +
                (plan.presets ?? new ImportPresetPlanItem[0]).Count(item =>
                    item.selectedAction == ImportActions.Create || item.selectedAction == ImportActions.Copy);
            if (instructionCount > LibraryStore.MaxInstructions) return "导入后指令项数量超过上限";
            if (presetCount > LibraryStore.MaxPresets) return "导入后配置预设数量超过上限";
            return null;
        }

        public static ImportResult ApplyImport(ImportPlan plan, string configFile, string root)
        {
            string validation = ValidatePlan(plan);
            if (validation != null) throw new InvalidOperationException(validation);
            if (String.IsNullOrWhiteSpace(plan.expectedSignature) ||
                !String.Equals(LibraryStore.Signature(configFile), plan.expectedSignature, StringComparison.Ordinal))
                throw new InvalidOperationException("配置库已更新，请重新预览导入内容");
            return plan.replaceLibrary
                ? ApplyBackup(plan, configFile, root)
                : ApplyMerge(plan, configFile, root);
        }

        public static string ActionLabel(string action)
        {
            if (action == ImportActions.Create) return "新增";
            if (action == ImportActions.Reuse) return "复用本地";
            if (action == ImportActions.Update) return "更新本地";
            if (action == ImportActions.Copy) return "创建副本";
            if (action == ImportActions.Skip) return "跳过";
            if (action == ImportActions.Replace) return "恢复";
            return action ?? "";
        }

        private static ImportInstructionPlanItem PreviewInstruction(PackageDocumentDto document,
            PackageInstructionDto incoming, InstructionDto[] locals, Dictionary<string, string> bodyById,
            HashSet<string> usedIds)
        {
            locals = locals ?? new InstructionDto[0];
            string incomingHash = BodyHash(incoming.content);
            InstructionDto sourceMatch = locals.FirstOrDefault(item =>
                String.Equals(item.sourcePackageId, document.packageId, StringComparison.Ordinal) &&
                String.Equals(item.sourcePackageKey, incoming.packageKey, StringComparison.Ordinal));
            InstructionDto exact = locals.FirstOrDefault(item =>
                String.Equals(item.name, incoming.name, StringComparison.CurrentCultureIgnoreCase) &&
                String.Equals(BodyHash(bodyById[item.id]), incomingHash, StringComparison.Ordinal));
            InstructionDto stable = document.kind == PackageKinds.Legacy && LibraryStore.ValidId(incoming.stableId)
                ? locals.FirstOrDefault(item => String.Equals(item.id, incoming.stableId, StringComparison.Ordinal)) : null;
            InstructionDto sameName = locals.FirstOrDefault(item =>
                String.Equals(item.name, incoming.name, StringComparison.CurrentCultureIgnoreCase));
            InstructionDto existing = sourceMatch ?? exact ?? stable ?? sameName;
            string newId = AllocateId(incoming.stableId, "instruction", usedIds);
            if (sourceMatch != null)
            {
                string currentHash = BodyHash(bodyById[sourceMatch.id]);
                if (String.Equals(currentHash, incomingHash, StringComparison.Ordinal))
                    return InstructionPlan(incoming, sourceMatch.id, newId, ImportActions.Reuse, false,
                        "已导入", "来源和正文一致", new[] { ImportActions.Reuse, ImportActions.Copy, ImportActions.Skip });
                if (String.Equals(sourceMatch.sourceContentHash, currentHash, StringComparison.Ordinal))
                    return InstructionPlan(incoming, sourceMatch.id, newId, ImportActions.Update, false,
                        "包更新", "本地正文保持上次导入版本", new[] { ImportActions.Update, ImportActions.Copy, ImportActions.Skip });
                return InstructionPlan(incoming, sourceMatch.id, newId, ImportActions.Copy, true,
                    "本地已修改", "默认保留本地版本并创建副本", new[] { ImportActions.Copy, ImportActions.Update, ImportActions.Skip });
            }
            if (exact != null)
                return InstructionPlan(incoming, exact.id, newId, ImportActions.Reuse, false,
                    "内容相同", "复用本地同名指令", new[] { ImportActions.Reuse, ImportActions.Copy, ImportActions.Skip });
            if (existing != null)
                return InstructionPlan(incoming, existing.id, newId, ImportActions.Copy, true,
                    "名称冲突", "正文不同，默认创建副本", new[] { ImportActions.Copy, ImportActions.Update, ImportActions.Skip });
            return InstructionPlan(incoming, null, newId, ImportActions.Create, false,
                "新增", "创建新的指令项", new[] { ImportActions.Create, ImportActions.Skip });
        }

        private static ImportInstructionPlanItem InstructionPlan(PackageInstructionDto incoming,
            string existingId, string newId, string action, bool conflict, string status, string detail,
            string[] allowed)
        {
            return new ImportInstructionPlanItem {
                packageKey = incoming.packageKey,
                name = incoming.name,
                existingId = existingId,
                newId = newId,
                selectedAction = action,
                conflict = conflict,
                status = status,
                detail = detail,
                allowedActions = allowed,
                incoming = incoming
            };
        }

        private static ImportPresetPlanItem PreviewPreset(PackageDocumentDto document,
            PackagePresetDto incoming, PresetDto[] locals, ImportInstructionPlanItem[] instructionPlans,
            HashSet<string> usedIds)
        {
            locals = locals ?? new PresetDto[0];
            string[] mappedIds = (incoming.instructionKeys ?? new string[0])
                .Select(key => TargetInstructionId(instructionPlans.First(item => item.packageKey == key)))
                .Where(id => id != null).ToArray();
            PresetDto sourceMatch = locals.FirstOrDefault(item =>
                String.Equals(item.sourcePackageId, document.packageId, StringComparison.Ordinal) &&
                String.Equals(item.sourcePackageKey, incoming.packageKey, StringComparison.Ordinal));
            PresetDto exact = locals.FirstOrDefault(item =>
                String.Equals(item.name, incoming.name, StringComparison.CurrentCultureIgnoreCase) &&
                Same(item.instructionIds, mappedIds));
            PresetDto stable = document.kind == PackageKinds.Legacy && LibraryStore.ValidId(incoming.stableId)
                ? locals.FirstOrDefault(item => String.Equals(item.id, incoming.stableId, StringComparison.Ordinal)) : null;
            PresetDto sameName = locals.FirstOrDefault(item =>
                String.Equals(item.name, incoming.name, StringComparison.CurrentCultureIgnoreCase));
            PresetDto existing = sourceMatch ?? exact ?? stable ?? sameName;
            string newId = AllocateId(incoming.stableId, "preset", usedIds);
            if (sourceMatch != null)
            {
                string currentHash = PresetHash(sourceMatch.instructionIds);
                string incomingHash = PresetHash(mappedIds);
                if (String.Equals(currentHash, incomingHash, StringComparison.Ordinal))
                    return PresetPlan(incoming, sourceMatch.id, newId, ImportActions.Reuse, false,
                        "已导入", "来源和内容一致", new[] { ImportActions.Reuse, ImportActions.Copy, ImportActions.Skip });
                if (String.Equals(sourceMatch.sourceContentHash, currentHash, StringComparison.Ordinal))
                    return PresetPlan(incoming, sourceMatch.id, newId, ImportActions.Update, false,
                        "包更新", "本地预设保持上次导入版本", new[] { ImportActions.Update, ImportActions.Copy, ImportActions.Skip });
                return PresetPlan(incoming, sourceMatch.id, newId, ImportActions.Copy, true,
                    "本地已修改", "默认保留本地预设并创建副本", new[] { ImportActions.Copy, ImportActions.Update, ImportActions.Skip });
            }
            if (exact != null)
                return PresetPlan(incoming, exact.id, newId, ImportActions.Reuse, false,
                    "内容相同", "复用本地同名配置预设", new[] { ImportActions.Reuse, ImportActions.Copy, ImportActions.Skip });
            if (existing != null)
                return PresetPlan(incoming, existing.id, newId, ImportActions.Copy, true,
                    "名称冲突", "引用内容不同，默认创建副本", new[] { ImportActions.Copy, ImportActions.Update, ImportActions.Skip });
            return PresetPlan(incoming, null, newId, ImportActions.Create, false,
                "新增", "创建新的配置预设", new[] { ImportActions.Create, ImportActions.Skip });
        }

        private static ImportPresetPlanItem PresetPlan(PackagePresetDto incoming, string existingId,
            string newId, string action, bool conflict, string status, string detail, string[] allowed)
        {
            return new ImportPresetPlanItem {
                packageKey = incoming.packageKey,
                name = incoming.name,
                existingId = existingId,
                newId = newId,
                selectedAction = action,
                conflict = conflict,
                status = status,
                detail = detail,
                allowedActions = allowed,
                incoming = incoming
            };
        }

        private static ImportResult ApplyBackup(ImportPlan plan, string configFile, string root)
        {
            var instructionMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var instructions = new List<InstructionDto>();
            var bodyWrites = new List<Action>();
            var bodyFiles = new List<string>();
            var oldBodyFiles = (plan.snapshot.instructions ?? new InstructionDto[0])
                .Select(item => LibraryStore.BodyPath(root, item)).ToArray();
            string now = DateTime.UtcNow.ToString("o");
            foreach (PackageInstructionDto incoming in plan.document.instructions)
            {
                string id = incoming.stableId;
                var target = new InstructionDto {
                    id = id,
                    name = incoming.name,
                    label = incoming.name,
                    file = NewBodyReference(id),
                    origin = incoming.origin,
                    sourcePackageId = incoming.sourcePackageId,
                    sourcePackageKey = incoming.sourcePackageKey,
                    sourceContentHash = incoming.sourceContentHash,
                    showInCustomPicker = incoming.showInCustomPicker ?? true,
                    createdAt = String.IsNullOrWhiteSpace(incoming.createdAt) ? now : incoming.createdAt,
                    updatedAt = String.IsNullOrWhiteSpace(incoming.updatedAt) ? now : incoming.updatedAt
                };
                instructions.Add(target);
                instructionMap[incoming.packageKey] = id;
                bodyWrites.Add(delegate { LibraryStore.WriteBody(root, target, incoming.content); });
                bodyFiles.Add(LibraryStore.BodyPath(root, target));
            }
            var presetMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var presets = new List<PresetDto>();
            foreach (PackagePresetDto incoming in plan.document.presets)
            {
                var target = new PresetDto {
                    id = incoming.stableId,
                    name = incoming.name,
                    instructionIds = incoming.instructionKeys.Select(key => instructionMap[key]).ToArray(),
                    origin = incoming.origin,
                    sourcePackageId = incoming.sourcePackageId,
                    sourcePackageKey = incoming.sourcePackageKey,
                    sourceContentHash = incoming.sourceContentHash,
                    createdAt = String.IsNullOrWhiteSpace(incoming.createdAt) ? now : incoming.createdAt,
                    updatedAt = String.IsNullOrWhiteSpace(incoming.updatedAt) ? now : incoming.updatedAt
                };
                presets.Add(target);
                presetMap[incoming.packageKey] = target.id;
            }
            string defaultId = null;
            if (!String.IsNullOrWhiteSpace(plan.document.defaultPresetKey))
                presetMap.TryGetValue(plan.document.defaultPresetKey, out defaultId);
            var next = new SettingsDto {
                version = 3,
                command = plan.document.command,
                defaultPresetId = defaultId,
                instructions = instructions.ToArray(),
                presets = presets.ToArray()
            };
            LibraryStore.SaveWithBody(configFile, root, next, plan.expectedSignature, delegate {
                foreach (Action write in bodyWrites) write();
            }, bodyFiles);
            CleanupUnreferencedBodies(root, next, oldBodyFiles);
            return new ImportResult {
                createdInstructions = instructions.Count,
                createdPresets = presets.Count,
                presetKeys = plan.document.presets.Select(item => item.packageKey).ToArray(),
                presetIds = presets.Select(item => item.id).ToArray()
            };
        }

        private static ImportResult ApplyMerge(ImportPlan plan, string configFile, string root)
        {
            SettingsDto next = Clone(plan.snapshot);
            var instructions = (next.instructions ?? new InstructionDto[0]).ToList();
            var presets = (next.presets ?? new PresetDto[0]).ToList();
            var instructionMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var bodyWrites = new List<Action>();
            var bodyFiles = new List<string>();
            var oldBodyFiles = new List<string>();
            var result = new ImportResult();
            string origin = plan.document.kind == PackageKinds.Preset ? "preset-package" : "instruction-package";
            string now = DateTime.UtcNow.ToString("o");

            foreach (ImportInstructionPlanItem item in plan.instructions ?? new ImportInstructionPlanItem[0])
            {
                PackageInstructionDto incoming = item.incoming;
                if (item.selectedAction == ImportActions.Skip)
                {
                    result.skippedInstructions++;
                    continue;
                }
                InstructionDto target;
                if (item.selectedAction == ImportActions.Reuse)
                {
                    target = instructions.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                    if (plan.showPresetInstructions) target.showInCustomPicker = true;
                    result.reusedInstructions++;
                }
                else if (item.selectedAction == ImportActions.Update)
                {
                    target = instructions.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                    target.name = incoming.name;
                    target.label = incoming.name;
                    target.origin = origin;
                    target.sourcePackageId = plan.document.packageId;
                    target.sourcePackageKey = incoming.packageKey;
                    target.sourceContentHash = BodyHash(incoming.content);
                    if (plan.showPresetInstructions) target.showInCustomPicker = true;
                    target.updatedAt = now;
                    oldBodyFiles.Add(LibraryStore.BodyPath(root, target));
                    target.file = NewBodyReference(target.id);
                    AddBodyWrite(root, target, incoming.content, bodyWrites, bodyFiles);
                    result.updatedInstructions++;
                }
                else
                {
                    if (item.selectedAction == ImportActions.Copy && item.existingId != null)
                    {
                        InstructionDto previous = instructions.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                        if (SameSource(previous.sourcePackageId, previous.sourcePackageKey, plan.document.packageId, incoming.packageKey))
                            DetachInstructionSource(previous);
                    }
                    target = new InstructionDto {
                        id = item.newId,
                        name = UniqueInstructionName(instructions, incoming.name),
                        label = incoming.name,
                        file = "instructions/" + item.newId + ".md",
                        origin = origin,
                        sourcePackageId = plan.document.packageId,
                        sourcePackageKey = incoming.packageKey,
                        sourceContentHash = BodyHash(incoming.content),
                        showInCustomPicker = plan.document.kind == PackageKinds.Preset
                            ? plan.showPresetInstructions : true,
                        createdAt = String.IsNullOrWhiteSpace(incoming.createdAt) ? now : incoming.createdAt,
                        updatedAt = now
                    };
                    target.label = target.name;
                    instructions.Add(target);
                    AddBodyWrite(root, target, incoming.content, bodyWrites, bodyFiles);
                    result.createdInstructions++;
                }
                instructionMap[item.packageKey] = target.id;
            }

            var importedPresetIds = new List<string>();
            foreach (ImportPresetPlanItem item in plan.presets ?? new ImportPresetPlanItem[0])
            {
                PackagePresetDto incoming = item.incoming;
                if (item.selectedAction == ImportActions.Skip)
                {
                    result.skippedPresets++;
                    continue;
                }
                string[] refs = incoming.instructionKeys.Select(key => instructionMap[key]).Distinct(StringComparer.Ordinal).ToArray();
                PresetDto target;
                if (item.selectedAction == ImportActions.Reuse)
                {
                    target = presets.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                    result.reusedPresets++;
                }
                else if (item.selectedAction == ImportActions.Update)
                {
                    target = presets.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                    target.name = incoming.name;
                    target.instructionIds = refs;
                    target.origin = "preset-package";
                    target.sourcePackageId = plan.document.packageId;
                    target.sourcePackageKey = incoming.packageKey;
                    target.sourceContentHash = PresetHash(refs);
                    target.updatedAt = now;
                    result.updatedPresets++;
                }
                else
                {
                    if (item.selectedAction == ImportActions.Copy && item.existingId != null)
                    {
                        PresetDto previous = presets.First(local => String.Equals(local.id, item.existingId, StringComparison.Ordinal));
                        if (SameSource(previous.sourcePackageId, previous.sourcePackageKey, plan.document.packageId, incoming.packageKey))
                            DetachPresetSource(previous);
                    }
                    target = new PresetDto {
                        id = item.newId,
                        name = UniquePresetName(presets, incoming.name),
                        instructionIds = refs,
                        origin = "preset-package",
                        sourcePackageId = plan.document.packageId,
                        sourcePackageKey = incoming.packageKey,
                        createdAt = String.IsNullOrWhiteSpace(incoming.createdAt) ? now : incoming.createdAt,
                        updatedAt = now
                    };
                    target.sourceContentHash = PresetHash(refs);
                    presets.Add(target);
                    result.createdPresets++;
                }
                importedPresetIds.Add(target.id);
            }
            next.instructions = instructions.ToArray();
            next.presets = presets.ToArray();
            LibraryStore.SaveWithBody(configFile, root, next, plan.expectedSignature, delegate {
                foreach (Action write in bodyWrites) write();
            }, bodyFiles);
            CleanupUnreferencedBodies(root, next, oldBodyFiles);
            result.presetKeys = (plan.presets ?? new ImportPresetPlanItem[0])
                .Where(item => item.selectedAction != ImportActions.Skip).Select(item => item.packageKey).ToArray();
            result.presetIds = importedPresetIds.ToArray();
            return result;
        }

        private static void AddBodyWrite(string root, InstructionDto target, string content,
            List<Action> writes, List<string> files)
        {
            writes.Add(delegate { LibraryStore.WriteBody(root, target, content); });
            files.Add(LibraryStore.BodyPath(root, target));
        }

        private static string NewBodyReference(string id)
        {
            return "instructions/" + id + "-" + Guid.NewGuid().ToString("N") + ".md";
        }

        private static void CleanupUnreferencedBodies(string root, SettingsDto settings, IEnumerable<string> candidates)
        {
            var referenced = new HashSet<string>((settings.instructions ?? new InstructionDto[0])
                .Select(item => LibraryStore.BodyPath(root, item)), StringComparer.OrdinalIgnoreCase);
            foreach (string file in (candidates ?? new string[0]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (String.IsNullOrWhiteSpace(file) || referenced.Contains(file)) continue;
                try { if (File.Exists(file)) File.Delete(file); }
                catch { }
            }
        }

        private static PackageInstructionDto ExportInstruction(string root, InstructionDto item, bool backup)
        {
            return new PackageInstructionDto {
                packageKey = item.id,
                stableId = item.id,
                name = item.name,
                content = LibraryStore.ReadBody(root, item),
                origin = backup ? item.origin : null,
                sourcePackageId = backup ? item.sourcePackageId : null,
                sourcePackageKey = backup ? item.sourcePackageKey : null,
                sourceContentHash = backup ? item.sourceContentHash : null,
                showInCustomPicker = backup ? item.showInCustomPicker : null,
                createdAt = item.createdAt,
                updatedAt = item.updatedAt
            };
        }

        private static PackagePresetDto ExportPreset(PresetDto item, bool backup)
        {
            return new PackagePresetDto {
                packageKey = item.id,
                stableId = item.id,
                name = item.name,
                instructionKeys = item.instructionIds ?? new string[0],
                origin = backup ? item.origin : null,
                sourcePackageId = backup ? item.sourcePackageId : null,
                sourcePackageKey = backup ? item.sourcePackageKey : null,
                sourceContentHash = backup ? item.sourceContentHash : null,
                createdAt = item.createdAt,
                updatedAt = item.updatedAt
            };
        }

        private static PackageDocumentDto ConvertLegacyBundle(ExportBundleDto legacy, string json)
        {
            if (legacy == null || legacy.instructions == null || legacy.presets == null)
                throw new InvalidDataException("旧版导入包格式无效");
            return NormalizePackage(new PackageDocumentDto {
                format = PackageFormat,
                schemaVersion = SchemaVersion,
                kind = PackageKinds.Legacy,
                packageId = "legacy-" + BodyHash(json).Substring(0, 20),
                name = "旧版指令库包",
                exportedAt = String.IsNullOrWhiteSpace(legacy.exportedAt) ? DateTime.UtcNow.ToString("o") : legacy.exportedAt,
                defaultPresetKey = legacy.defaultPresetId,
                instructions = legacy.instructions.Select(item => new PackageInstructionDto {
                    packageKey = item.id,
                    stableId = item.id,
                    name = item.name,
                    content = item.content,
                    createdAt = item.createdAt,
                    updatedAt = item.updatedAt
                }).ToArray(),
                presets = legacy.presets.Select(item => new PackagePresetDto {
                    packageKey = item.id,
                    stableId = item.id,
                    name = item.name,
                    instructionKeys = item.instructionIds,
                    createdAt = item.createdAt,
                    updatedAt = item.updatedAt
                }).ToArray()
            });
        }

        private static PackageDocumentDto NormalizePackage(PackageDocumentDto raw)
        {
            if (raw == null || !String.Equals(raw.format, PackageFormat, StringComparison.Ordinal) || raw.schemaVersion != SchemaVersion)
                throw new InvalidDataException("导入包版本不受支持");
            string kind = (raw.kind ?? "").Trim().ToLowerInvariant();
            if (kind != PackageKinds.Instruction && kind != PackageKinds.Preset &&
                kind != PackageKinds.Backup && kind != PackageKinds.Legacy)
                throw new InvalidDataException("导入包类型无效");
            string packageId = RequiredText(raw.packageId, 128, "包 ID");
            string packageName = RequiredText(raw.name, 200, "包名称");
            PackageInstructionDto[] sourceInstructions = raw.instructions ?? new PackageInstructionDto[0];
            PackagePresetDto[] sourcePresets = raw.presets ?? new PackagePresetDto[0];
            if (sourceInstructions.Length > LibraryStore.MaxInstructions) throw new InvalidDataException("导入包指令项过多");
            if (sourcePresets.Length > LibraryStore.MaxPresets) throw new InvalidDataException("导入包配置预设过多");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var stableIds = new HashSet<string>(StringComparer.Ordinal);
            var instructions = new List<PackageInstructionDto>();
            foreach (PackageInstructionDto item in sourceInstructions)
            {
                if (item == null) throw new InvalidDataException("导入包包含空指令项");
                string key = RequiredPackageKey(item.packageKey, "指令包内键");
                if (!keys.Add(key)) throw new InvalidDataException("导入包包含重复指令键");
                string stableId = OptionalStableId(item.stableId);
                if (kind == PackageKinds.Backup && (stableId == null || !stableIds.Add(stableId)))
                    throw new InvalidDataException("备份包含无效或重复指令 ID");
                string content = item.content ?? "";
                if (Encoding.UTF8.GetByteCount(content) > LibraryStore.MaxBodyBytes)
                    throw new InvalidDataException("导入指令正文过大");
                instructions.Add(new PackageInstructionDto {
                    packageKey = key,
                    stableId = stableId,
                    name = RequiredText(item.name, 200, "指令名称"),
                    content = content,
                    origin = OptionalText(item.origin, 32),
                    sourcePackageId = OptionalText(item.sourcePackageId, 128),
                    sourcePackageKey = OptionalText(item.sourcePackageKey, 64),
                    sourceContentHash = OptionalHash(item.sourceContentHash),
                    showInCustomPicker = item.showInCustomPicker,
                    createdAt = OptionalText(item.createdAt, 80),
                    updatedAt = OptionalText(item.updatedAt, 80)
                });
            }
            var presetKeys = new HashSet<string>(StringComparer.Ordinal);
            var presetStableIds = new HashSet<string>(StringComparer.Ordinal);
            var presets = new List<PackagePresetDto>();
            foreach (PackagePresetDto item in sourcePresets)
            {
                if (item == null) throw new InvalidDataException("导入包包含空配置预设");
                string key = RequiredPackageKey(item.packageKey, "预设包内键");
                if (!presetKeys.Add(key)) throw new InvalidDataException("导入包包含重复预设键");
                string stableId = OptionalStableId(item.stableId);
                if (kind == PackageKinds.Backup && (stableId == null || !presetStableIds.Add(stableId)))
                    throw new InvalidDataException("备份包含无效或重复预设 ID");
                var seenRefs = new HashSet<string>(StringComparer.Ordinal);
                var refs = new List<string>();
                foreach (string candidate in item.instructionKeys ?? new string[0])
                {
                    string reference = RequiredPackageKey(candidate, "预设指令引用");
                    if (!keys.Contains(reference)) throw new InvalidDataException("配置预设引用了包内不存在的指令");
                    if (!seenRefs.Add(reference)) throw new InvalidDataException("配置预设包含重复指令引用");
                    refs.Add(reference);
                }
                presets.Add(new PackagePresetDto {
                    packageKey = key,
                    stableId = stableId,
                    name = RequiredText(item.name, 200, "配置预设名称"),
                    instructionKeys = refs.ToArray(),
                    origin = OptionalText(item.origin, 32),
                    sourcePackageId = OptionalText(item.sourcePackageId, 128),
                    sourcePackageKey = OptionalText(item.sourcePackageKey, 64),
                    sourceContentHash = OptionalHash(item.sourceContentHash),
                    createdAt = OptionalText(item.createdAt, 80),
                    updatedAt = OptionalText(item.updatedAt, 80)
                });
            }
            if (kind == PackageKinds.Instruction && presets.Count > 0)
                throw new InvalidDataException("指令包不能包含配置预设");
            if (kind == PackageKinds.Preset && presets.Count == 0)
                throw new InvalidDataException("配置预设包中没有配置预设");
            string command = kind == PackageKinds.Backup ? RequiredText(raw.command, 64, "控制命令") : null;
            if (command != null && (!command.StartsWith("/", StringComparison.Ordinal) || command.Any(Char.IsWhiteSpace)))
                throw new InvalidDataException("备份中的控制命令无效");
            string defaultKey = OptionalText(raw.defaultPresetKey, 64);
            if (kind == PackageKinds.Backup && defaultKey != null && !presetKeys.Contains(defaultKey))
                throw new InvalidDataException("备份中的默认配置预设无效");
            return new PackageDocumentDto {
                format = PackageFormat,
                schemaVersion = SchemaVersion,
                kind = kind,
                packageId = packageId,
                name = packageName,
                exportedAt = String.IsNullOrWhiteSpace(raw.exportedAt) ? DateTime.UtcNow.ToString("o") : raw.exportedAt.Trim(),
                command = command,
                defaultPresetKey = defaultKey,
                instructions = instructions.ToArray(),
                presets = presets.ToArray()
            };
        }

        private static string TargetInstructionId(ImportInstructionPlanItem item)
        {
            if (item == null || item.selectedAction == ImportActions.Skip) return null;
            if (item.selectedAction == ImportActions.Create || item.selectedAction == ImportActions.Copy ||
                item.selectedAction == ImportActions.Replace) return item.newId;
            return item.existingId;
        }

        private static bool Allowed(string selected, string[] allowed)
        {
            return !String.IsNullOrWhiteSpace(selected) && (allowed ?? new string[0]).Contains(selected);
        }

        private static string AllocateId(string preferred, string prefix, HashSet<string> used)
        {
            string id = LibraryStore.ValidId(preferred) && !used.Contains(preferred)
                ? preferred : LibraryStore.NewId(prefix);
            while (used.Contains(id)) id = LibraryStore.NewId(prefix);
            used.Add(id);
            return id;
        }

        private static string UniqueInstructionName(IEnumerable<InstructionDto> items, string requested)
        {
            return UniqueName((items ?? new InstructionDto[0]).Select(item => item.name), requested);
        }

        private static string UniquePresetName(IEnumerable<PresetDto> items, string requested)
        {
            return UniqueName((items ?? new PresetDto[0]).Select(item => item.name), requested);
        }

        private static string UniqueName(IEnumerable<string> names, string requested)
        {
            var used = new HashSet<string>(names ?? new string[0], StringComparer.CurrentCultureIgnoreCase);
            if (!used.Contains(requested)) return requested;
            string candidate = requested + "（导入）";
            int suffix = 2;
            while (used.Contains(candidate)) candidate = requested + "（导入 " + suffix++ + "）";
            return candidate;
        }

        private static void DetachInstructionSource(InstructionDto item)
        {
            item.origin = "local";
            item.sourcePackageId = null;
            item.sourcePackageKey = null;
            item.sourceContentHash = null;
        }

        private static void DetachPresetSource(PresetDto item)
        {
            item.origin = "local";
            item.sourcePackageId = null;
            item.sourcePackageKey = null;
            item.sourceContentHash = null;
        }

        private static bool SameSource(string localPackageId, string localKey, string packageId, string packageKey)
        {
            return String.Equals(localPackageId, packageId, StringComparison.Ordinal) &&
                String.Equals(localKey, packageKey, StringComparison.Ordinal);
        }

        private static bool Same(string[] left, string[] right)
        {
            left = left ?? new string[0];
            right = right ?? new string[0];
            return left.Length == right.Length && left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private static string StablePackageId(string prefix, IEnumerable<string> ids)
        {
            string payload = String.Join("\n", (ids ?? new string[0]).OrderBy(id => id, StringComparer.Ordinal));
            return prefix + "-" + BodyHash(payload).Substring(0, 20);
        }

        private static string BodyHash(string content)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(content ?? ""));
                return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            }
        }

        private static string PresetHash(string[] ids)
        {
            return BodyHash(String.Join("\n", ids ?? new string[0]));
        }

        private static string RequiredPackageKey(string value, string label)
        {
            string key = RequiredText(value, 64, label);
            if (!LibraryStore.ValidId(key)) throw new InvalidDataException(label + "无效");
            return key;
        }

        private static string OptionalStableId(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string id = value.Trim();
            return LibraryStore.ValidId(id) ? id : null;
        }

        private static string RequiredText(string value, int maxLength, string label)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.Length == 0 || normalized.Length > maxLength)
                throw new InvalidDataException(label + "无效");
            return normalized;
        }

        private static string OptionalText(string value, int maxLength)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim();
            if (normalized.Length > maxLength) throw new InvalidDataException("导入包元数据过长");
            return normalized;
        }

        private static string OptionalHash(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Length != 64 || normalized.Any(c => !((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))))
                throw new InvalidDataException("导入包内容指纹无效");
            return normalized;
        }

        private static T Clone<T>(T value)
        {
            return Serializer.Deserialize<T>(Serializer.Serialize(value));
        }
    }
}
