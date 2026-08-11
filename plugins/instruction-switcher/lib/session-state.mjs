import { createHash, randomUUID } from "node:crypto";
import { mkdir, open, readFile, rename, rm, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { CONFIG_ROOT, LEGACY_STATE_ROOT, STATE_ROOT } from "./config.mjs";

export const STATE_VERSION = 3;
export const RUNTIME_ROOT = path.join(CONFIG_ROOT, "runtime");
const descriptorDir = path.join(RUNTIME_ROOT, "sessions");
const acknowledgementDir = path.join(RUNTIME_ROOT, "acks");
const stateDir = STATE_ROOT;
const LOCK_WAIT_MS = 10_000;
const LOCK_STALE_MS = 60_000;
const LOCK_RETRY_MS = 25;

export function sessionKey(sessionId) {
  return createHash("sha256").update(sessionId).digest("hex");
}

export function sessionFile(sessionId) {
  return path.join(stateDir, `${sessionKey(sessionId)}.json`);
}

export function acknowledgementFile(sessionId) {
  return path.join(acknowledgementDir, `${sessionKey(sessionId)}.json`);
}

async function writeJson(target, value) {
  await mkdir(path.dirname(target), { recursive: true });
  const temp = `${target}.${process.pid}.${randomUUID()}.tmp`;
  try {
    await writeFile(temp, `${JSON.stringify(value, null, 2)}\n`, "utf8");
    await rename(temp, target);
  } finally {
    await rm(temp, { force: true }).catch(() => {});
  }
}

async function readJson(target) {
  try {
    const text = (await readFile(target, "utf8")).replace(/^\uFEFF/u, "");
    return { exists: true, value: JSON.parse(text) };
  } catch (error) {
    if (error?.code === "ENOENT") return { exists: false, value: null };
    throw error;
  }
}

function lockFile(target) {
  return `${target}.lock`;
}

async function acquireFileLock(target) {
  const lockPath = lockFile(target);
  await mkdir(path.dirname(target), { recursive: true });
  const token = `${process.pid}:${randomUUID()}`;
  const deadline = Date.now() + LOCK_WAIT_MS;

  while (true) {
    let handle = null;
    let created = false;
    try {
      handle = await open(lockPath, "wx");
      created = true;
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
              process.stderr.write(`instruction-switcher: lock cleanup skipped (${error.message})\n`);
            }
          }
        }
      };
    } catch (error) {
      if (handle) await handle.close().catch(() => {});
      if (created) {
        await rm(lockPath, { force: true }).catch(() => {});
        throw error;
      }
      if (error?.code !== "EEXIST") throw error;
      if (Date.now() >= deadline) {
        const timeout = new Error("状态锁等待超时，请稍后重试");
        timeout.code = "ELOCKTIMEOUT";
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

async function withFileLock(target, operation) {
  const release = await acquireFileLock(target);
  try {
    return await operation();
  } finally {
    await release();
  }
}

function contextFor(value) {
  if (value && typeof value === "object" && !Array.isArray(value) &&
      value.validIds instanceof Set && Array.isArray(value.presets)) {
    return value;
  }
  if (value && typeof value === "object" && !Array.isArray(value) &&
      !(value instanceof Set) && Array.isArray(value.instructions)) {
    const validIds = new Set(value.instructions
      .map((item) => item?.id)
      .filter((id) => typeof id === "string"));
    const presets = Array.isArray(value.presets)
      ? value.presets.map((preset) => {
        const seen = new Set();
        const instructionIds = (Array.isArray(preset?.instructionIds) ? preset.instructionIds : [])
          .filter((id) => validIds.has(id) && !seen.has(id) && (seen.add(id), true));
        return {
          id: typeof preset?.id === "string" ? preset.id : "",
          instructionIds,
        };
      }).filter((preset) => preset.id)
      : [];
    return {
      validIds,
      presets,
      defaultPresetId: typeof value.defaultPresetId === "string" ? value.defaultPresetId : null,
    };
  }
  const validIds = value instanceof Set
    ? new Set(value)
    : Array.isArray(value) ? new Set(value) : null;
  return { validIds, presets: [], defaultPresetId: null };
}

function sameList(left, right) {
  return left.length === right.length && left.every((id, index) => id === right[index]);
}

function normalizedEnabled(raw, validIds) {
  const seen = new Set();
  return raw.filter((id) => typeof id === "string" &&
    (validIds === null || validIds.has(id)) && !seen.has(id) && (seen.add(id), true));
}

function presetMatch(enabled, presets, preferred = null) {
  if (preferred) {
    const selected = presets.find((preset) => preset.id === preferred);
    if (selected && sameList(enabled, selected.instructionIds)) return selected.id;
  }
  return presets.find((preset) => sameList(enabled, preset.instructionIds))?.id || null;
}

function stateFor(raw, contextValue) {
  if (!raw || typeof raw !== "object" || Array.isArray(raw) || !Array.isArray(raw.enabled)) {
    throw new Error("state must contain an enabled array");
  }
  const context = contextFor(contextValue);
  const version = raw.version === undefined ? 1 : raw.version;
  if (version !== 1 && version !== 2 && version !== STATE_VERSION) {
    throw new Error("unsupported state version");
  }
  const enabled = normalizedEnabled(raw.enabled, context.validIds);
  const rawActive = typeof raw.activePresetId === "string" ? raw.activePresetId : null;
  const activePresetId = presetMatch(enabled, context.presets, rawActive);
  const revision = typeof raw.revision === "string" && raw.revision ? raw.revision : null;
  const updatedAt = typeof raw.updatedAt === "string" && raw.updatedAt
    ? raw.updatedAt
    : new Date().toISOString();
  const rawEnabled = raw.enabled.filter((id) => typeof id === "string");
  return {
    state: {
      version: STATE_VERSION,
      enabled,
      activePresetId,
      revision,
      updatedAt,
    },
    needsUpgrade: version !== STATE_VERSION || !revision ||
      !sameList(rawEnabled, enabled) || rawActive !== activePresetId,
  };
}

function stateError(key, file, error) {
  process.stderr.write(`instruction-switcher: state read failed (${file}: ${error.message})\n`);
  return {
    status: "error",
    key,
    file,
    enabled: [],
    activePresetId: null,
    revision: null,
    updatedAt: "",
    error,
  };
}

function legacySessionFile(key) {
  return LEGACY_STATE_ROOT ? path.join(LEGACY_STATE_ROOT, `${key}.json`) : null;
}

function defaultState(context) {
  const preset = context.presets.find((item) => item.id === context.defaultPresetId);
  const enabled = preset ? [...preset.instructionIds] : [];
  return {
    version: STATE_VERSION,
    enabled,
    activePresetId: preset ? preset.id : null,
    revision: randomUUID(),
    updatedAt: new Date().toISOString(),
  };
}

export async function readSessionState(sessionId, validIdsOrSettings = null) {
  const key = sessionKey(sessionId);
  const currentFile = sessionFile(sessionId);
  const context = contextFor(validIdsOrSettings);

  try {
    let sourceFile = currentFile;
    let result = await readJson(currentFile);
    if (!result.exists) {
      const legacyFile = legacySessionFile(key);
      if (legacyFile) {
        const legacy = await readJson(legacyFile);
        if (legacy.exists) {
          sourceFile = legacyFile;
          result = legacy;
        }
      }
    }

    if (!result.exists) {
      if (!context.defaultPresetId || !context.presets.some((preset) => preset.id === context.defaultPresetId)) {
        return {
          status: "missing",
          key,
          file: currentFile,
          enabled: [],
          activePresetId: null,
          revision: null,
          updatedAt: "",
        };
      }
      const created = await withFileLock(currentFile, async () => {
        const canonical = await readJson(currentFile);
        if (canonical.exists) return stateFor(canonical.value, context).state;
        const state = defaultState(context);
        await writeJson(currentFile, state);
        return state;
      });
      return { status: "ok", key, file: currentFile, sourceFile: currentFile, migrated: true, ...created };
    }

    const normalized = stateFor(result.value, context);
    const needsMigration = sourceFile !== currentFile || normalized.needsUpgrade;
    let state = {
      ...normalized.state,
      revision: normalized.state.revision || randomUUID(),
    };
    if (needsMigration) {
      try {
        state = await withFileLock(currentFile, async () => {
          const canonical = await readJson(currentFile);
          if (canonical.exists) {
            const latest = stateFor(canonical.value, context);
            const latestState = {
              ...latest.state,
              revision: latest.state.revision || randomUUID(),
            };
            if (latest.needsUpgrade || latestState.revision !== latest.state.revision) {
              await writeJson(currentFile, latestState);
            }
            return latestState;
          }
          await writeJson(currentFile, state);
          return state;
        });
      } catch (error) {
        process.stderr.write(`instruction-switcher: state migration skipped (${error.message})\n`);
      }
    }

    return {
      status: "ok",
      key,
      file: currentFile,
      sourceFile,
      migrated: needsMigration,
      ...state,
    };
  } catch (error) {
    return stateError(key, currentFile, error);
  }
}

export async function readEnabled(sessionId, validIdsOrSettings = null) {
  const state = await readSessionState(sessionId, validIdsOrSettings);
  return state.enabled;
}

async function currentRevision(target) {
  const result = await readJson(target);
  if (!result.exists) return null;
  if (!result.value || typeof result.value !== "object") throw new Error("state must be an object");
  return typeof result.value.revision === "string" && result.value.revision
    ? result.value.revision
    : null;
}

export async function writeEnabled(
  sessionId,
  enabled,
  expectedRevision,
  settingsOrIds = null,
  preferredPresetId = undefined,
) {
  const target = sessionFile(sessionId);
  const context = contextFor(settingsOrIds);
  return withFileLock(target, async () => {
    if (expectedRevision !== undefined) {
      const actualRevision = await currentRevision(target);
      if (actualRevision !== expectedRevision) {
        const error = new Error("状态已变化，请重新读取后再保存");
        error.code = "ESTATECONFLICT";
        throw error;
      }
    }
    const normalized = normalizedEnabled(
      Array.isArray(enabled) ? enabled : [],
      context.validIds,
    );
    const state = {
      version: STATE_VERSION,
      revision: randomUUID(),
      enabled: normalized,
      activePresetId: presetMatch(normalized, context.presets,
        preferredPresetId === undefined ? null : preferredPresetId),
      updatedAt: new Date().toISOString(),
    };
    await writeJson(target, state);
    return state;
  });
}

export async function acknowledgeState(sessionId, state, input) {
  if (!state || !state.revision) return null;
  const target = sessionFile(sessionId);
  return withFileLock(target, async () => {
    const actualRevision = await currentRevision(target);
    if (actualRevision !== state.revision) return null;
    const acknowledgement = {
      version: 1,
      key: sessionKey(sessionId),
      revision: state.revision,
      observedAt: new Date().toISOString(),
      turnId: typeof input?.turn_id === "string" ? input.turn_id : "",
    };
    await writeJson(acknowledgementFile(sessionId), acknowledgement);
    return acknowledgement;
  });
}

export function isSubagentEvent(input) {
  if (input?.agent_id || input?.agent_type) return true;
  const transcript = typeof input?.transcript_path === "string" ? input.transcript_path : "";
  return /[\\/]subagents[\\/]/iu.test(transcript);
}

export async function registerSession(input, settings, source = "prompt") {
  const sessionId = typeof input?.session_id === "string" ? input.session_id : "";
  if (!sessionId || isSubagentEvent(input)) return null;

  const cwd = typeof input?.cwd === "string" && input.cwd ? path.resolve(input.cwd) : "";
  const key = sessionKey(sessionId);
  const now = new Date().toISOString();
  const instructions = Array.isArray(settings?.instructions) ? settings.instructions : [];
  const presets = Array.isArray(settings?.presets) ? settings.presets : [];
  const descriptor = {
    version: STATE_VERSION,
    key,
    project: cwd ? path.basename(cwd) : "Codex task",
    cwd,
    instructions: instructions.map(({ id, name, label }) => ({ id, name: name || label || id })),
    presets: presets.map(({ id, name, instructionIds }) => ({
      id,
      name,
      instructionIds: Array.isArray(instructionIds) ? instructionIds : [],
    })),
    defaultPresetId: typeof settings?.defaultPresetId === "string" ? settings.defaultPresetId : null,
    // Keep the old view for one release so an older companion can still show useful labels.
    profiles: instructions.map(({ id, name, label }) => ({ id, label: name || label || id })),
    source,
    updatedAt: now,
  };

  await writeJson(path.join(descriptorDir, `${key}.json`), descriptor);
  return descriptor;
}

export { contextFor, presetMatch, stateFor };
