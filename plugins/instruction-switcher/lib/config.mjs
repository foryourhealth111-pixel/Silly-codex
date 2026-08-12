import { constants } from "node:fs";
import {
  access,
  copyFile,
  mkdir,
  open,
  readFile,
  readdir,
  rename,
  rm,
  stat,
  writeFile,
} from "node:fs/promises";
import { randomUUID } from "node:crypto";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { readUiLanguage } from "./ui-language.mjs";

export const CONFIG_VERSION = 3;
export const PLUGIN_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const DEFAULT_ROOT = path.join(PLUGIN_ROOT, "defaults");
export const CODEX_HOME = path.resolve(
  process.env.CODEX_HOME || path.join(os.homedir(), ".codex"),
);
export const CONFIG_ROOT = path.resolve(
  process.env.INSTRUCTION_SWITCHER_HOME || path.join(CODEX_HOME, "instruction-switcher"),
);
export const CONFIG_FILE = path.join(CONFIG_ROOT, "config.json");
export const INSTRUCTION_ROOT = path.join(CONFIG_ROOT, "instructions");
export const STATE_ROOT = path.join(CONFIG_ROOT, "sessions");
const pluginDataRoot = process.env.PLUGIN_DATA ? path.resolve(process.env.PLUGIN_DATA) : null;
const sameRoot =
  pluginDataRoot &&
  (process.platform === "win32"
    ? pluginDataRoot.toLowerCase() === CONFIG_ROOT.toLowerCase()
    : pluginDataRoot === CONFIG_ROOT);
export const LEGACY_STATE_ROOT = pluginDataRoot && !sameRoot
  ? path.join(pluginDataRoot, "sessions")
  : null;

const ID_PATTERN = /^[a-z0-9][a-z0-9_-]{0,63}$/u;
const CONTENT_HASH_PATTERN = /^[a-f0-9]{64}$/u;
const PACKAGE_ORIGINS = new Set(["instruction-package", "preset-package"]);
const MAX_INSTRUCTIONS = 512;
const MAX_PRESETS = 256;
const MAX_CONTENT_BYTES = 64_000;
const LOCK_WAIT_MS = 10_000;
const LOCK_STALE_MS = 60_000;
const LOCK_RETRY_MS = 25;

async function exists(file) {
  try {
    await access(file);
    return true;
  } catch (error) {
    if (error?.code === "ENOENT") return false;
    throw error;
  }
}

async function copyOnce(source, target) {
  await mkdir(path.dirname(target), { recursive: true });
  try {
    await copyFile(source, target, constants.COPYFILE_EXCL);
  } catch (error) {
    if (error?.code !== "EEXIST") throw error;
  }
}

async function writeJson(target, value) {
  await mkdir(path.dirname(target), { recursive: true });
  const temporary = `${target}.${process.pid}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, `${JSON.stringify(value, null, 2)}\n`, "utf8");
    await rename(temporary, target);
  } finally {
    await rm(temporary, { force: true }).catch(() => {});
  }
}

async function readJson(target) {
  const text = (await readFile(target, "utf8")).replace(/^\uFEFF/u, "");
  return JSON.parse(text);
}

async function acquireConfigLock() {
  const lockPath = `${CONFIG_FILE}.lock`;
  await mkdir(path.dirname(lockPath), { recursive: true });
  const token = `${process.pid}:${randomUUID()}`;
  const deadline = Date.now() + LOCK_WAIT_MS;

  while (true) {
    let handle = null;
    try {
      handle = await open(lockPath, "wx");
      await handle.writeFile(`${token}\n`, "utf8");
      return async () => {
        try {
          await handle.close();
        } finally {
          try {
            const owner = (await readFile(lockPath, "utf8")).trim();
            if (owner === token) await rm(lockPath, { force: true });
          } catch (error) {
            if (error?.code !== "ENOENT") {
              process.stderr.write(`instruction-switcher: config lock cleanup skipped (${error.message})\n`);
            }
          }
        }
      };
    } catch (error) {
      if (handle) await handle.close().catch(() => {});
      if (error?.code !== "EEXIST") throw error;
      if (Date.now() >= deadline) {
        const timeout = new Error("configuration lock timed out");
        timeout.code = "ECONFIGLOCKTIMEOUT";
        throw timeout;
      }
      try {
        const info = await stat(lockPath);
        if (Date.now() - info.mtimeMs > LOCK_STALE_MS) await rm(lockPath, { force: true });
      } catch (lockError) {
        if (lockError?.code !== "ENOENT") throw lockError;
      }
      await new Promise((resolve) => setTimeout(resolve, LOCK_RETRY_MS));
    }
  }
}

async function withConfigLock(operation) {
  const release = await acquireConfigLock();
  try {
    return await operation();
  } finally {
    await release();
  }
}

function isInside(root, target) {
  const relative = path.relative(path.resolve(root), path.resolve(target));
  return relative !== "" && !relative.startsWith(`..${path.sep}`) && relative !== ".." &&
    !path.isAbsolute(relative);
}

function normalizedTimestamp(value, fallback) {
  return typeof value === "string" && value.trim() && value.length <= 80
    ? value.trim()
    : fallback;
}

function normalizedOptionalString(value, maxLength) {
  if (typeof value !== "string" || !value.trim()) return null;
  const normalized = value.trim();
  return normalized.length <= maxLength ? normalized : null;
}

function normalizedSourceMetadata(item) {
  let origin = typeof item?.origin === "string" ? item.origin.trim().toLowerCase() : "local";
  if (!PACKAGE_ORIGINS.has(origin)) origin = "local";
  let sourcePackageId = normalizedOptionalString(item?.sourcePackageId, 128);
  let sourcePackageKey = normalizedOptionalString(item?.sourcePackageKey, 64);
  let sourceContentHash = typeof item?.sourceContentHash === "string"
    ? item.sourceContentHash.trim().toLowerCase()
    : null;
  if (!CONTENT_HASH_PATTERN.test(sourceContentHash || "")) sourceContentHash = null;
  if (origin !== "local" && (!sourcePackageId || !sourcePackageKey)) origin = "local";
  if (origin === "local") {
    sourcePackageId = null;
    sourcePackageKey = null;
    sourceContentHash = null;
  }
  return { origin, sourcePackageId, sourcePackageKey, sourceContentHash };
}

function knownV3(raw) {
  return {
    version: raw?.version,
    command: raw?.command,
    defaultPresetId: raw?.defaultPresetId ?? null,
    instructions: Array.isArray(raw?.instructions)
      ? raw.instructions.map((item) => ({
        id: item?.id,
        name: item?.name,
        file: item?.file,
        origin: item?.origin,
        sourcePackageId: item?.sourcePackageId ?? null,
        sourcePackageKey: item?.sourcePackageKey ?? null,
        sourceContentHash: item?.sourceContentHash ?? null,
        showInCustomPicker: item?.showInCustomPicker,
        createdAt: item?.createdAt,
        updatedAt: item?.updatedAt,
      }))
      : raw?.instructions,
    presets: Array.isArray(raw?.presets)
      ? raw.presets.map((item) => ({
        id: item?.id,
        name: item?.name,
        instructionIds: item?.instructionIds,
        origin: item?.origin,
        sourcePackageId: item?.sourcePackageId ?? null,
        sourcePackageKey: item?.sourcePackageKey ?? null,
        sourceContentHash: item?.sourceContentHash ?? null,
        createdAt: item?.createdAt,
        updatedAt: item?.updatedAt,
      }))
      : raw?.presets,
  };
}

function validateCommand(value) {
  const command = typeof value === "string" ? value.trim() : "";
  if (!command.startsWith("/") || /\s/u.test(command) || command.length > 64) {
    throw new Error("command must start with / and contain no spaces");
  }
  return command;
}

function normalizeV3(raw, root, now = new Date().toISOString()) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) {
    throw new Error("config must be an object");
  }
  if (raw.version !== CONFIG_VERSION) throw new Error(`unsupported config version: ${raw.version}`);
  if (!Array.isArray(raw.instructions) || raw.instructions.length > MAX_INSTRUCTIONS) {
    throw new Error(`instructions must be an array with at most ${MAX_INSTRUCTIONS} entries`);
  }
  if (!Array.isArray(raw.presets) || raw.presets.length > MAX_PRESETS) {
    throw new Error(`presets must be an array with at most ${MAX_PRESETS} entries`);
  }

  const instructionRoot = path.resolve(root, "instructions");
  const instructionIds = new Set();
  const instructions = raw.instructions.map((item) => {
    const id = typeof item?.id === "string" ? item.id.trim() : "";
    const name = typeof item?.name === "string" ? item.name.trim() : "";
    const rawFile = typeof item?.file === "string" ? item.file.trim() : "";
    if (!ID_PATTERN.test(id) || instructionIds.has(id)) {
      throw new Error(`invalid or duplicate instruction id: ${id}`);
    }
    if (!name || name.length > 200 || !rawFile || path.isAbsolute(rawFile)) {
      throw new Error(`invalid instruction metadata: ${id}`);
    }
    const resolved = path.resolve(root, rawFile);
    if (!isInside(instructionRoot, resolved)) {
      throw new Error(`instruction escapes instructions directory: ${id}`);
    }
    instructionIds.add(id);
    const createdAt = normalizedTimestamp(item.createdAt, now);
    const source = normalizedSourceMetadata(item);
    return {
      id,
      name,
      file: path.relative(root, resolved).split(path.sep).join("/"),
      ...source,
      showInCustomPicker: item?.showInCustomPicker !== false,
      createdAt,
      updatedAt: normalizedTimestamp(item.updatedAt, createdAt),
    };
  });

  const presetIds = new Set();
  let removedReferences = 0;
  const presets = raw.presets.map((item) => {
    const id = typeof item?.id === "string" ? item.id.trim() : "";
    const name = typeof item?.name === "string" ? item.name.trim() : "";
    if (!ID_PATTERN.test(id) || presetIds.has(id)) {
      throw new Error(`invalid or duplicate preset id: ${id}`);
    }
    if (!name || name.length > 200 || !Array.isArray(item?.instructionIds)) {
      throw new Error(`invalid preset metadata: ${id}`);
    }
    const seen = new Set();
    const instructionIdsForPreset = [];
    for (const candidate of item.instructionIds) {
      if (typeof candidate !== "string" || !instructionIds.has(candidate) || seen.has(candidate)) {
        removedReferences++;
        continue;
      }
      seen.add(candidate);
      instructionIdsForPreset.push(candidate);
    }
    presetIds.add(id);
    const createdAt = normalizedTimestamp(item.createdAt, now);
    const source = normalizedSourceMetadata(item);
    return {
      id,
      name,
      instructionIds: instructionIdsForPreset,
      ...source,
      createdAt,
      updatedAt: normalizedTimestamp(item.updatedAt, createdAt),
    };
  });

  const requestedDefault = typeof raw.defaultPresetId === "string" ? raw.defaultPresetId : null;
  const defaultPresetId = requestedDefault && presetIds.has(requestedDefault)
    ? requestedDefault
    : null;
  const settings = {
    version: CONFIG_VERSION,
    command: validateCommand(raw.command),
    defaultPresetId,
    instructions,
    presets,
  };
  return {
    settings,
    changed: JSON.stringify(knownV3(raw)) !== JSON.stringify(settings),
    cleanup: {
      removedReferences,
      clearedDefaultPreset: Boolean(requestedDefault && !defaultPresetId),
    },
  };
}

function readLegacy(raw, root) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw) || !Array.isArray(raw.profiles)) {
    throw new Error("legacy config must contain profiles");
  }
  if (raw.profiles.length > MAX_INSTRUCTIONS) throw new Error("legacy config has too many profiles");
  const presetRoot = path.resolve(root, "presets");
  const ids = new Set();
  const profiles = raw.profiles.map((profile) => {
    const id = typeof profile?.id === "string" ? profile.id.trim() : "";
    const name = typeof profile?.label === "string" ? profile.label.trim() : "";
    const rawFile = typeof profile?.file === "string" ? profile.file.trim() : "";
    if (!ID_PATTERN.test(id) || ids.has(id)) throw new Error(`invalid legacy profile id: ${id}`);
    if (!name || name.length > 200 || !rawFile || path.isAbsolute(rawFile)) {
      throw new Error(`invalid legacy profile metadata: ${id}`);
    }
    const sourceFile = path.resolve(presetRoot, rawFile);
    if (!isInside(presetRoot, sourceFile)) throw new Error(`legacy profile escapes presets: ${id}`);
    ids.add(id);
    return { id, name, sourceFile };
  });
  return { command: validateCommand(raw.command), profiles };
}

function seedPresets(validIds, now, language) {
  const candidates = language === "en"
    ? [
      { id: "preset-default", name: "Default", instructionIds: ["concise"] },
      { id: "preset-code-review", name: "Code review", instructionIds: ["review", "concise"] },
      { id: "preset-test-mode", name: "Test mode", instructionIds: ["tdd", "concise"] },
    ]
    : [
      { id: "preset-default", name: "默认", instructionIds: ["concise"] },
      { id: "preset-code-review", name: "代码审查", instructionIds: ["review", "concise"] },
      { id: "preset-test-mode", name: "测试模式", instructionIds: ["tdd", "concise"] },
    ];
  return candidates
    .map((preset) => ({
      ...preset,
      instructionIds: preset.instructionIds.filter((id) => validIds.has(id)),
      createdAt: now,
      updatedAt: now,
    }))
    .filter((preset) => preset.instructionIds.length > 0);
}

async function migrateLegacy(raw, root) {
  const legacy = readLegacy(raw, root);
  const now = new Date().toISOString();
  const language = await readUiLanguage(CONFIG_ROOT);
  const instructions = [];
  for (const profile of legacy.profiles) {
    const target = path.join(root, "instructions", `${profile.id}.md`);
    try {
      await copyOnce(profile.sourceFile, target);
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
      process.stderr.write(`instruction-switcher: legacy body is missing (${profile.id})\n`);
    }
    instructions.push({
      id: profile.id,
      name: profile.name,
      file: `instructions/${profile.id}.md`,
      createdAt: now,
      updatedAt: now,
    });
  }
  const settings = {
    version: CONFIG_VERSION,
    command: legacy.command,
    defaultPresetId: null,
    instructions,
    presets: seedPresets(new Set(instructions.map((item) => item.id)), now, language),
  };
  const normalized = normalizeV3(settings, root).settings;
  await writeJson(path.join(root, "config.json"), normalized);
  return normalized;
}

function legacyRuntimeSettings(raw, root) {
  const legacy = readLegacy(raw, root);
  const now = new Date().toISOString();
  return {
    version: 2,
    command: legacy.command,
    defaultPresetId: null,
    instructions: legacy.profiles.map((profile) => ({
      id: profile.id,
      name: profile.name,
      file: path.relative(root, profile.sourceFile).split(path.sep).join("/"),
      createdAt: now,
      updatedAt: now,
    })),
    presets: [],
  };
}

async function localizedDefaultRoot() {
  return (await readUiLanguage(CONFIG_ROOT)) === "en"
    ? path.join(DEFAULT_ROOT, "en")
    : DEFAULT_ROOT;
}

async function installDefaults() {
  const selectedRoot = await localizedDefaultRoot();
  const sourceRoot = path.join(selectedRoot, "instructions");
  const entries = await readdir(sourceRoot, { withFileTypes: true });
  for (const entry of entries) {
    if (!entry.isFile()) continue;
    await copyOnce(path.join(sourceRoot, entry.name), path.join(INSTRUCTION_ROOT, entry.name));
  }
  const raw = await readJson(path.join(selectedRoot, "config.json"));
  const settings = normalizeV3(raw, CONFIG_ROOT).settings;
  await writeJson(CONFIG_FILE, settings);
  return settings;
}

async function bundledSettings() {
  const selectedRoot = await localizedDefaultRoot();
  const raw = await readJson(path.join(selectedRoot, "config.json"));
  return { root: selectedRoot, settings: normalizeV3(raw, selectedRoot).settings };
}

async function prepareSettings() {
  try {
    return await withConfigLock(async () => {
      if (!(await exists(CONFIG_FILE))) {
        return { root: CONFIG_ROOT, settings: await installDefaults(), legacy: false };
      }
      const raw = await readJson(CONFIG_FILE);
      if (raw?.version === CONFIG_VERSION) {
        const normalized = normalizeV3(raw, CONFIG_ROOT);
        if (normalized.changed) {
          try {
            await writeJson(CONFIG_FILE, normalized.settings);
          } catch (error) {
            process.stderr.write(`instruction-switcher: config cleanup was not persisted (${error.message})\n`);
          }
        }
        return { root: CONFIG_ROOT, settings: normalized.settings, legacy: false };
      }
      if (Array.isArray(raw?.profiles)) {
        try {
          return {
            root: CONFIG_ROOT,
            settings: await migrateLegacy(raw, CONFIG_ROOT),
            legacy: false,
          };
        } catch (error) {
          process.stderr.write(`instruction-switcher: legacy config migration skipped (${error.message})\n`);
          return {
            root: CONFIG_ROOT,
            settings: legacyRuntimeSettings(raw, CONFIG_ROOT),
            legacy: true,
          };
        }
      }
      throw new Error(`unsupported config version: ${raw?.version}`);
    });
  } catch (error) {
    if (await exists(CONFIG_FILE).catch(() => true)) throw error;
    process.stderr.write(`instruction-switcher: using bundled defaults (${error.message})\n`);
    const bundled = await bundledSettings();
    return { root: bundled.root, settings: bundled.settings, legacy: false };
  }
}

async function readInstructionContent(root, settings, instruction) {
  const allowedRoot = path.resolve(root, settings.version === CONFIG_VERSION ? "instructions" : "presets");
  const target = path.resolve(root, instruction.file);
  if (!isInside(allowedRoot, target)) {
    throw new Error(`instruction escapes content directory: ${instruction.id}`);
  }
  const content = (await readFile(target, "utf8")).replace(/^\uFEFF/u, "");
  if (Buffer.byteLength(content, "utf8") > MAX_CONTENT_BYTES) {
    throw new Error(`instruction is too large: ${instruction.id}`);
  }
  return { ...instruction, content };
}

export async function loadSettings({ contentIds = null } = {}) {
  const prepared = await prepareSettings();
  const { root, settings } = prepared;
  if (contentIds === null) return settings;

  const byId = new Map(settings.instructions.map((instruction) => [instruction.id, instruction]));
  const seen = new Set();
  const selected = [];
  const contentErrors = [];
  for (const candidate of contentIds) {
    if (typeof candidate !== "string" || seen.has(candidate)) continue;
    seen.add(candidate);
    const instruction = byId.get(candidate);
    if (!instruction) continue;
    try {
      selected.push(await readInstructionContent(root, settings, instruction));
    } catch (error) {
      contentErrors.push({ id: candidate, message: error.message });
      process.stderr.write(`instruction-switcher: instruction body skipped (${candidate}: ${error.message})\n`);
    }
  }
  return { ...settings, instructions: selected, contentErrors };
}
