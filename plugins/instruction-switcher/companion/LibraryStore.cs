using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace InstructionSwitcherCompanion
{
    internal sealed class ProfileDto
    {
        public string id { get; set; }
        public string label { get; set; }
        public string file { get; set; }
    }

    internal sealed class InstructionDto
    {
        public string id { get; set; }
        public string name { get; set; }
        public string label { get; set; }
        public string file { get; set; }
        public string origin { get; set; }
        public string sourcePackageId { get; set; }
        public string sourcePackageKey { get; set; }
        public string sourceContentHash { get; set; }
        public bool? showInCustomPicker { get; set; }
        public string createdAt { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class PresetDto
    {
        public string id { get; set; }
        public string name { get; set; }
        public string[] instructionIds { get; set; }
        public string origin { get; set; }
        public string sourcePackageId { get; set; }
        public string sourcePackageKey { get; set; }
        public string sourceContentHash { get; set; }
        public string createdAt { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class SettingsDto
    {
        public int version { get; set; }
        public string command { get; set; }
        public string defaultPresetId { get; set; }
        public InstructionDto[] instructions { get; set; }
        public PresetDto[] presets { get; set; }
        public ProfileDto[] profiles { get; set; }
    }

    internal sealed class SessionDescriptor
    {
        public int version { get; set; }
        public string key { get; set; }
        public string project { get; set; }
        public string cwd { get; set; }
        public string stateFile { get; set; }
        public InstructionDto[] instructions { get; set; }
        public PresetDto[] presets { get; set; }
        public string defaultPresetId { get; set; }
        public ProfileDto[] profiles { get; set; }
        public string source { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class SessionState
    {
        public int version { get; set; }
        public string revision { get; set; }
        public string[] enabled { get; set; }
        public string activePresetId { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class ExportInstructionDto
    {
        public string id { get; set; }
        public string name { get; set; }
        public string content { get; set; }
        public string createdAt { get; set; }
        public string updatedAt { get; set; }
    }

    internal sealed class ExportBundleDto
    {
        public int version { get; set; }
        public string exportedAt { get; set; }
        public string defaultPresetId { get; set; }
        public ExportInstructionDto[] instructions { get; set; }
        public PresetDto[] presets { get; set; }
    }

    internal sealed class BodySnapshot
    {
        public string file { get; set; }
        public bool existed { get; set; }
        public byte[] content { get; set; }
    }

    internal sealed class ConfigCommit
    {
        public string file { get; set; }
        public bool previousExisted { get; set; }
        public byte[] previousContent { get; set; }
        public string committedSignature { get; set; }
        public byte[] committedContent { get; set; }
    }

    internal static class LibraryStore
    {
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();
        internal const int MaxInstructions = 512;
        internal const int MaxPresets = 256;
        internal const int MaxBodyBytes = 64000;
        private const int MoveFileReplaceExisting = 0x1;
        private const int MoveFileWriteThrough = 0x8;

        private static JavaScriptSerializer CreateSerializer()
        {
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 32 * 1024 * 1024;
            return serializer;
        }

        public static string Signature(string file)
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

        public static SettingsDto Load(string configFile, string root)
        {
            if (!File.Exists(configFile))
                throw new FileNotFoundException("配置文件不存在", configFile);
            SettingsDto raw = Serializer.Deserialize<SettingsDto>(File.ReadAllText(configFile, Encoding.UTF8));
            if (raw == null) throw new InvalidDataException("配置文件为空");
            if (raw.version == 0 && raw.profiles != null) raw = ConvertLegacy(raw, root);
            if (raw.version != 3) throw new InvalidDataException("配置文件版本不受支持");
            return Normalize(raw, root);
        }

        private static SettingsDto ConvertLegacy(SettingsDto raw, string root)
        {
            var list = new List<InstructionDto>();
            string presetRoot = Path.GetFullPath(Path.Combine(root, "presets")) + Path.DirectorySeparatorChar;
            string instructionRoot = Path.GetFullPath(Path.Combine(root, "instructions")) + Path.DirectorySeparatorChar;
            foreach (ProfileDto profile in raw.profiles ?? new ProfileDto[0])
            {
                string id = profile == null ? "" : (profile.id ?? "").Trim();
                if (!IsValidId(id)) throw new InvalidDataException("旧配置指令 ID 无效");
                string file = LegacyBodyReference(profile);
                string source = Path.GetFullPath(Path.Combine(root, file));
                if (!IsSafeRelative(file) || !source.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("旧配置正文路径无效");
                string target = Path.Combine(root, "instructions", id + ".md");
                if (File.Exists(source) && !File.Exists(target))
                {
                    Directory.CreateDirectory(instructionRoot);
                    File.Copy(source, target);
                }
                string now = DateTime.UtcNow.ToString("o");
                list.Add(new InstructionDto {
                    id = id,
                    name = profile.label,
                    label = profile.label,
                    file = "instructions/" + id + ".md",
                    origin = "local",
                    showInCustomPicker = true,
                    createdAt = now,
                    updatedAt = now
                });
            }
            return new SettingsDto {
                version = 3,
                command = raw.command,
                defaultPresetId = null,
                instructions = list.ToArray(),
                presets = new PresetDto[0]
            };
        }

        private static string LegacyBodyReference(ProfileDto profile)
        {
            string relative = String.IsNullOrWhiteSpace(profile == null ? null : profile.file)
                ? (profile == null ? "" : profile.id + ".md")
                : profile.file.Trim().Replace('\\', '/');
            while (relative.StartsWith("/", StringComparison.Ordinal)) relative = relative.Substring(1);
            if (relative.StartsWith("presets/", StringComparison.OrdinalIgnoreCase))
                relative = relative.Substring("presets/".Length);
            return "presets/" + relative;
        }

        public static SettingsDto Normalize(SettingsDto raw, string root)
        {
            if (raw == null) throw new InvalidDataException("配置文件为空");
            if (raw.instructions == null || raw.instructions.Length > MaxInstructions)
                throw new InvalidDataException("指令库条目数量无效");
            if (raw.presets == null || raw.presets.Length > MaxPresets)
                throw new InvalidDataException("配置预设数量无效");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var instructions = new List<InstructionDto>();
            foreach (InstructionDto item in raw.instructions)
            {
                string id = (item == null ? null : item.id ?? "").Trim();
                string name = (item == null ? null : (String.IsNullOrWhiteSpace(item.name) ? item.label : item.name) ?? "").Trim();
                if (!IsValidId(id) || !ids.Add(id) || String.IsNullOrWhiteSpace(name) || name.Length > 200)
                    throw new InvalidDataException("指令项元数据无效");
                string file = (item.file ?? "").Trim();
                if (!IsSafeRelative(file)) throw new InvalidDataException("指令正文路径无效");
                string full = Path.GetFullPath(Path.Combine(root, file));
                string instructionRoot = Path.GetFullPath(Path.Combine(root, "instructions")) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(instructionRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("指令正文路径越界");
                string created = String.IsNullOrWhiteSpace(item.createdAt) ? DateTime.UtcNow.ToString("o") : item.createdAt;
                string origin = NormalizeOrigin(item.origin);
                string sourcePackageId = NormalizeOptional(item.sourcePackageId, 128, "来源包 ID");
                string sourcePackageKey = NormalizeOptional(item.sourcePackageKey, 64, "来源条目键");
                string sourceContentHash = NormalizeHash(item.sourceContentHash);
                if (origin != "local" && (sourcePackageId == null || sourcePackageKey == null)) origin = "local";
                if (origin == "local")
                {
                    sourcePackageId = null;
                    sourcePackageKey = null;
                    sourceContentHash = null;
                }
                instructions.Add(new InstructionDto {
                    id = id, name = name, label = name, file = file.Replace('\\', '/'),
                    origin = origin, sourcePackageId = sourcePackageId, sourcePackageKey = sourcePackageKey,
                    sourceContentHash = sourceContentHash, showInCustomPicker = item.showInCustomPicker ?? true,
                    createdAt = created, updatedAt = String.IsNullOrWhiteSpace(item.updatedAt) ? created : item.updatedAt
                });
            }
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            var presets = new List<PresetDto>();
            foreach (PresetDto item in raw.presets)
            {
                string id = (item == null ? null : item.id ?? "").Trim();
                string name = (item == null ? null : item.name ?? "").Trim();
                if (!IsValidId(id) || !presetIds.Add(id) || String.IsNullOrWhiteSpace(name) || name.Length > 200)
                    throw new InvalidDataException("配置预设元数据无效");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var refs = new List<string>();
                foreach (string reference in item.instructionIds ?? new string[0])
                    if (ids.Contains(reference) && seen.Add(reference)) refs.Add(reference);
                string created = String.IsNullOrWhiteSpace(item.createdAt) ? DateTime.UtcNow.ToString("o") : item.createdAt;
                string origin = NormalizeOrigin(item.origin);
                string sourcePackageId = NormalizeOptional(item.sourcePackageId, 128, "来源包 ID");
                string sourcePackageKey = NormalizeOptional(item.sourcePackageKey, 64, "来源预设键");
                string sourceContentHash = NormalizeHash(item.sourceContentHash);
                if (origin != "local" && (sourcePackageId == null || sourcePackageKey == null)) origin = "local";
                if (origin == "local")
                {
                    sourcePackageId = null;
                    sourcePackageKey = null;
                    sourceContentHash = null;
                }
                presets.Add(new PresetDto {
                    id = id, name = name, instructionIds = refs.ToArray(), createdAt = created,
                    origin = origin, sourcePackageId = sourcePackageId, sourcePackageKey = sourcePackageKey,
                    sourceContentHash = sourceContentHash,
                    updatedAt = String.IsNullOrWhiteSpace(item.updatedAt) ? created : item.updatedAt
                });
            }
            string command = (raw.command ?? "").Trim();
            if (!command.StartsWith("/") || command.Any(Char.IsWhiteSpace) || command.Length > 64)
                throw new InvalidDataException("控制命令无效");
            string defaultId = raw.defaultPresetId;
            if (String.IsNullOrWhiteSpace(defaultId) || !presetIds.Contains(defaultId)) defaultId = null;
            return new SettingsDto {
                version = 3, command = command, defaultPresetId = defaultId,
                instructions = instructions.ToArray(), presets = presets.ToArray()
            };
        }

        public static ConfigCommit Save(string configFile, string root, SettingsDto settings, string expectedSignature)
        {
            using (StateFileLock.Acquire(configFile))
            {
                if (String.IsNullOrWhiteSpace(expectedSignature))
                    throw new InvalidOperationException("配置库尚未成功读取，保存已取消");
                if (Signature(configFile) != expectedSignature)
                    throw new InvalidOperationException("配置库已在其他窗口更新，请重新读取");
                SettingsDto normalized = Normalize(settings, root);
                bool previousExisted = File.Exists(configFile);
                byte[] previousContent = previousExisted ? File.ReadAllBytes(configFile) : null;
                byte[] committedContent = Encoding.UTF8.GetBytes(Serializer.Serialize(normalized));
                AtomicWriteBytes(configFile, committedContent);
                return new ConfigCommit {
                    file = Path.GetFullPath(configFile),
                    previousExisted = previousExisted,
                    previousContent = previousContent,
                    committedSignature = Signature(configFile),
                    committedContent = committedContent
                };
            }
        }

        public static bool TryRollback(ConfigCommit commit)
        {
            if (commit == null || String.IsNullOrWhiteSpace(commit.file)) return false;
            using (StateFileLock.Acquire(commit.file))
            {
                if (!String.Equals(Signature(commit.file), commit.committedSignature, StringComparison.Ordinal))
                    return false;
                byte[] current = File.Exists(commit.file) ? File.ReadAllBytes(commit.file) : null;
                if (!SameBytes(current, commit.committedContent)) return false;
                if (commit.previousExisted)
                    AtomicWriteBytes(commit.file, commit.previousContent ?? new byte[0]);
                else if (File.Exists(commit.file))
                    File.Delete(commit.file);
                return true;
            }
        }

        public static void SaveWithBody(string configFile, string root, SettingsDto settings,
            string expectedSignature, Action writeBodies, IEnumerable<string> bodyFiles = null)
        {
            using (StateFileLock.Acquire(configFile))
            {
                if (String.IsNullOrWhiteSpace(expectedSignature))
                    throw new InvalidOperationException("配置库尚未成功读取，保存已取消");
                if (Signature(configFile) != expectedSignature)
                    throw new InvalidOperationException("配置库已在其他窗口更新，请重新读取");
                SettingsDto normalized = Normalize(settings, root);
                List<BodySnapshot> snapshots = SnapshotBodies(root, bodyFiles);
                try
                {
                    if (writeBodies != null) writeBodies();
                    AtomicWrite(configFile, Serializer.Serialize(normalized));
                }
                catch (Exception writeError)
                {
                    try
                    {
                        RestoreBodies(snapshots);
                    }
                    catch (Exception rollbackError)
                    {
                        throw new IOException("正文保存失败，部分正文未恢复：" + rollbackError.Message, writeError);
                    }
                    throw;
                }
            }
        }

        public static string BodyPath(string root, InstructionDto instruction)
        {
            if (instruction == null || String.IsNullOrWhiteSpace(instruction.file)) throw new InvalidDataException("指令正文路径无效");
            string full = Path.GetFullPath(Path.Combine(root, instruction.file));
            string instructionRoot = Path.GetFullPath(Path.Combine(root, "instructions")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(instructionRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("指令正文路径越界");
            return full;
        }

        public static string ReadBody(string root, InstructionDto instruction)
        {
            string file = BodyPath(root, instruction);
            if (!File.Exists(file)) return "";
            byte[] bytes = File.ReadAllBytes(file);
            if (bytes.Length > MaxBodyBytes) throw new InvalidDataException("指令正文过大");
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        }

        public static void WriteBody(string root, InstructionDto instruction, string content)
        {
            if (Encoding.UTF8.GetByteCount(content ?? "") > MaxBodyBytes)
                throw new InvalidDataException("指令正文过大");
            string file = BodyPath(root, instruction);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            AtomicWrite(file, content ?? "");
        }

        private static List<BodySnapshot> SnapshotBodies(string root, IEnumerable<string> bodyFiles)
        {
            var snapshots = new List<BodySnapshot>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in bodyFiles ?? new string[0])
            {
                string file = ValidateBodyFile(root, candidate);
                if (!seen.Add(file)) continue;
                var info = new FileInfo(file);
                if (info.Exists && info.Length > MaxBodyBytes)
                    throw new InvalidDataException("指令正文过大");
                snapshots.Add(new BodySnapshot {
                    file = file,
                    existed = info.Exists,
                    content = info.Exists ? File.ReadAllBytes(file) : null,
                });
            }
            return snapshots;
        }

        private static void RestoreBodies(IEnumerable<BodySnapshot> snapshots)
        {
            var failures = new List<string>();
            foreach (BodySnapshot snapshot in snapshots ?? new BodySnapshot[0])
            {
                try
                {
                    if (snapshot.existed) AtomicWriteBytes(snapshot.file, snapshot.content ?? new byte[0]);
                    else if (File.Exists(snapshot.file)) File.Delete(snapshot.file);
                }
                catch (Exception error)
                {
                    failures.Add(Path.GetFileName(snapshot.file) + "：" + error.Message);
                }
            }
            if (failures.Count > 0)
                throw new IOException("正文回滚失败：" + String.Join("；", failures));
        }

        private static string ValidateBodyFile(string root, string file)
        {
            if (String.IsNullOrWhiteSpace(file)) throw new InvalidDataException("指令正文路径无效");
            string full = Path.GetFullPath(file);
            string instructionRoot = Path.GetFullPath(Path.Combine(root, "instructions")) + Path.DirectorySeparatorChar;
            string presetRoot = Path.GetFullPath(Path.Combine(root, "presets")) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(instructionRoot, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(presetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("指令正文路径越界");
            return full;
        }

        public static string NewId(string prefix)
        {
            return prefix + "-" + Guid.NewGuid().ToString("N");
        }

        public static bool ValidId(string value)
        {
            return IsValidId(value);
        }

        public static ExportBundleDto Export(string root, SettingsDto settings)
        {
            return new ExportBundleDto {
                version = 1,
                exportedAt = DateTime.UtcNow.ToString("o"),
                defaultPresetId = settings.defaultPresetId,
                instructions = (settings.instructions ?? new InstructionDto[0]).Select(item => new ExportInstructionDto {
                    id = item.id, name = item.name, content = ReadBody(root, item), createdAt = item.createdAt, updatedAt = item.updatedAt
                }).ToArray(),
                presets = settings.presets ?? new PresetDto[0]
            };
        }

        public static int CountPresetReferences(SettingsDto settings, string id)
        {
            return (settings.presets ?? new PresetDto[0]).Count(preset => (preset.instructionIds ?? new string[0]).Contains(id));
        }

        public static int CountSessionReferences(string stateRoot, string id)
        {
            int count = 0;
            if (!Directory.Exists(stateRoot)) return count;
            foreach (FileInfo file in new DirectoryInfo(stateRoot).GetFiles("*.json"))
            {
                try
                {
                    SessionState state = Serializer.Deserialize<SessionState>(File.ReadAllText(file.FullName, Encoding.UTF8));
                    if (state != null && state.enabled != null && state.enabled.Contains(id)) count++;
                }
                catch { }
            }
            return count;
        }

        public static int CleanSessionReferences(string stateRoot, SettingsDto settings, string deletedId)
        {
            int changed = 0;
            if (!Directory.Exists(stateRoot)) return changed;
            foreach (FileInfo file in new DirectoryInfo(stateRoot).GetFiles("*.json"))
            {
                try
                {
                    using (StateFileLock.Acquire(file.FullName))
                    {
                        SessionState state = Serializer.Deserialize<SessionState>(File.ReadAllText(file.FullName, Encoding.UTF8));
                        if (state == null || state.enabled == null || (state.version != 1 && state.version != 2 && state.version != 3)) continue;
                        string[] next = state.enabled.Where(id => !String.Equals(id, deletedId, StringComparison.Ordinal)).Distinct().ToArray();
                        string active = state.activePresetId;
                        PresetDto matched = (settings.presets ?? new PresetDto[0]).FirstOrDefault(preset =>
                            String.Equals(preset.id, active, StringComparison.Ordinal) && Same(next, preset.instructionIds));
                        if (matched == null) matched = (settings.presets ?? new PresetDto[0]).FirstOrDefault(preset => Same(next, preset.instructionIds));
                        if (state.version != 3 || !Same(state.enabled, next) || !String.Equals(active, matched == null ? null : matched.id, StringComparison.Ordinal))
                        {
                            state.version = 3;
                            state.enabled = next;
                            state.activePresetId = matched == null ? null : matched.id;
                            state.revision = Guid.NewGuid().ToString("D");
                            state.updatedAt = DateTime.UtcNow.ToString("o");
                            AtomicWrite(file.FullName, Serializer.Serialize(state));
                            changed++;
                        }
                    }
                }
                catch
                {
                    // Hook 会在下一次读取时再次清理失效引用。
                }
            }
            return changed;
        }

        public static int CleanPresetReferences(string stateRoot, SettingsDto settings, string deletedPresetId)
        {
            int changed = 0;
            if (!Directory.Exists(stateRoot)) return changed;
            foreach (FileInfo file in new DirectoryInfo(stateRoot).GetFiles("*.json"))
            {
                try
                {
                    using (StateFileLock.Acquire(file.FullName))
                    {
                        SessionState state = Serializer.Deserialize<SessionState>(File.ReadAllText(file.FullName, Encoding.UTF8));
                        if (state == null || state.enabled == null ||
                            (state.version != 1 && state.version != 2 && state.version != 3)) continue;
                        if (!String.Equals(state.activePresetId, deletedPresetId, StringComparison.Ordinal)) continue;
                        PresetDto match = (settings.presets ?? new PresetDto[0]).FirstOrDefault(preset =>
                            Same(state.enabled, preset.instructionIds));
                        state.version = 3;
                        state.activePresetId = match == null ? null : match.id;
                        state.revision = Guid.NewGuid().ToString("D");
                        state.updatedAt = DateTime.UtcNow.ToString("o");
                        AtomicWrite(file.FullName, Serializer.Serialize(state));
                        changed++;
                    }
                }
                catch { }
            }
            return changed;
        }

        private static bool Same(string[] left, string[] right)
        {
            left = left ?? new string[0]; right = right ?? new string[0];
            return left.Length == right.Length && left.SequenceEqual(right, StringComparer.Ordinal);
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (Object.ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static bool IsSafeRelative(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && !Path.IsPathRooted(value) &&
                !value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Contains("..");
        }

        private static bool IsValidId(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool letter = c >= 'a' && c <= 'z'; bool digit = c >= '0' && c <= '9';
                bool extra = c == '-' || c == '_';
                if (!(letter || digit || (i > 0 && extra))) return false;
            }
            return true;
        }

        private static string NormalizeOrigin(string value)
        {
            string origin = (value ?? "").Trim().ToLowerInvariant();
            return origin == "instruction-package" || origin == "preset-package" ? origin : "local";
        }

        private static string NormalizeOptional(string value, int maxLength, string label)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim();
            if (normalized.Length > maxLength) throw new InvalidDataException(label + "过长");
            return normalized;
        }

        private static string NormalizeHash(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string hash = value.Trim().ToLowerInvariant();
            if (hash.Length != 64 || hash.Any(c => !((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))))
                throw new InvalidDataException("来源内容指纹无效");
            return hash;
        }

        private static void AtomicWrite(string target, string content)
        {
            AtomicWriteBytes(target, Encoding.UTF8.GetBytes(content ?? ""));
        }

        private static void AtomicWriteBytes(string target, byte[] content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, content ?? new byte[0]);
                if (!NativeMoveFile.Move(temp, target)) throw new IOException("文件替换失败");
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    internal static class NativeMoveFile
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileEx(string existing, string replacement, int flags);

        public static bool Move(string existing, string replacement)
        {
            return MoveFileEx(existing, replacement, 0x1 | 0x8);
        }
    }
}
