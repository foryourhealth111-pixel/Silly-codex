import { randomUUID } from "node:crypto";
import { mkdir, readFile, rename, rm, stat, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { FOCUS_DOM_EXPRESSION, resolveCodexFocus } from "../lib/codex-focus.mjs";

const POLL_MS = 250;
const REQUEST_TIMEOUT_MS = 1_500;
const HEARTBEAT_MS = 1_500;
const UNAVAILABLE_AFTER_MS = 2_000;

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function parentIsRunning(parentPid) {
  if (!Number.isInteger(parentPid) || parentPid <= 0) return false;
  try {
    process.kill(parentPid, 0);
    return true;
  } catch {
    return false;
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

function isLoopbackWebSocket(value, port) {
  try {
    const url = new URL(value);
    const loopback = url.hostname === "127.0.0.1" || url.hostname === "localhost" ||
      url.hostname === "[::1]";
    return url.protocol === "ws:" && loopback && Number(url.port) === port;
  } catch {
    return false;
  }
}

async function fetchTargets(port) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    const response = await fetch(`http://127.0.0.1:${port}/json/list`, {
      signal: controller.signal,
    });
    if (!response.ok) throw new Error(`DevTools target request failed (${response.status})`);
    const targets = await response.json();
    if (!Array.isArray(targets)) throw new Error("DevTools target list is invalid");
    const target = targets.find((item) =>
      item?.type === "page" && item?.url === "app://-/index.html" &&
      isLoopbackWebSocket(item?.webSocketDebuggerUrl, port)
    );
    if (!target) throw new Error("Codex page target is unavailable");
    return target.webSocketDebuggerUrl;
  } finally {
    clearTimeout(timeout);
  }
}

class CdpClient {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    socket.addEventListener("message", (event) => this.onMessage(event));
    socket.addEventListener("close", () => this.rejectPending(new Error("DevTools socket closed")));
    socket.addEventListener("error", () => this.rejectPending(new Error("DevTools socket failed")));
  }

  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        socket.close();
        reject(new Error("DevTools connection timed out"));
      }, REQUEST_TIMEOUT_MS);
      socket.addEventListener("open", () => {
        clearTimeout(timeout);
        resolve();
      }, { once: true });
      socket.addEventListener("error", () => {
        clearTimeout(timeout);
        reject(new Error("DevTools connection failed"));
      }, { once: true });
    });
    return new CdpClient(socket);
  }

  onMessage(event) {
    let message;
    try {
      message = JSON.parse(String(event.data));
    } catch {
      return;
    }
    if (!Number.isInteger(message?.id) || !this.pending.has(message.id)) return;
    const pending = this.pending.get(message.id);
    this.pending.delete(message.id);
    clearTimeout(pending.timeout);
    if (message.error) pending.reject(new Error(message.error.message || "DevTools request failed"));
    else pending.resolve(message.result);
  }

  rejectPending(error) {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timeout);
      pending.reject(error);
    }
    this.pending.clear();
  }

  request(method, params) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error("DevTools request timed out"));
      }, REQUEST_TIMEOUT_MS);
      this.pending.set(id, { resolve, reject, timeout });
      try {
        this.socket.send(JSON.stringify({ id, method, params }));
      } catch (error) {
        clearTimeout(timeout);
        this.pending.delete(id);
        reject(error);
      }
    });
  }

  close() {
    this.rejectPending(new Error("DevTools client closed"));
    try { this.socket.close(); } catch { }
  }
}

class GlobalStateCache {
  constructor(file) {
    this.file = file;
    this.signature = "";
    this.value = null;
  }

  async read() {
    try {
      const info = await stat(this.file);
      const signature = `${info.mtimeMs}:${info.size}`;
      if (signature === this.signature) return this.value;
      const text = (await readFile(this.file, "utf8")).replace(/^\uFEFF/u, "");
      const value = JSON.parse(text);
      this.signature = signature;
      this.value = value && typeof value === "object" && !Array.isArray(value) ? value : null;
      return this.value;
    } catch {
      this.signature = "";
      this.value = null;
      return null;
    }
  }
}

async function queryFocus(client, globalStateCache) {
  const result = await client.request("Runtime.evaluate", {
    expression: FOCUS_DOM_EXPRESSION,
    returnByValue: true,
  });
  const raw = result?.result?.value;
  const needsAlias = typeof raw?.rowId === "string" &&
    raw.rowId.startsWith("local:client-new-thread:");
  const globalState = needsAlias ? await globalStateCache.read() : null;
  return resolveCodexFocus(raw, globalState);
}

async function run(runtimeRoot, port, parentPid) {
  const focusFile = path.join(runtimeRoot, "focus.json");
  const codexHome = path.resolve(process.env.CODEX_HOME || path.join(os.homedir(), ".codex"));
  const globalState = new GlobalStateCache(path.join(codexHome, ".codex-global-state.json"));
  let client = null;
  let candidateKey = "";
  let candidateCount = 0;
  let publishedKey = "";
  let lastPublishedAt = 0;
  let lastResolvedAt = Date.now();
  let unavailablePublished = false;

  while (parentIsRunning(parentPid)) {
    try {
      if (!client) client = await CdpClient.connect(await fetchTargets(port));
      const focus = await queryFocus(client, globalState);
      if (!focus) throw new Error("Codex task selection is unresolved");

      lastResolvedAt = Date.now();
      unavailablePublished = false;
      if (focus.key === candidateKey) candidateCount++;
      else {
        candidateKey = focus.key;
        candidateCount = 1;
      }

      const now = Date.now();
      if (candidateCount >= 2 &&
          (publishedKey !== focus.key || now - lastPublishedAt >= HEARTBEAT_MS)) {
        await writeJson(focusFile, { ...focus, observedAt: new Date(now).toISOString() });
        publishedKey = focus.key;
        lastPublishedAt = now;
      }
    } catch (error) {
      if (client) client.close();
      client = null;
      candidateKey = "";
      candidateCount = 0;
      const now = Date.now();
      if (!unavailablePublished && now - lastResolvedAt >= UNAVAILABLE_AFTER_MS) {
        await writeJson(focusFile, {
          version: 1,
          available: false,
          reason: error?.message || "Codex task selection is unavailable",
          observedAt: new Date(now).toISOString(),
        });
        unavailablePublished = true;
        publishedKey = "";
        lastPublishedAt = now;
      }
    }
    await delay(POLL_MS);
  }
  if (client) client.close();
}

const runtimeRoot = process.argv[2] ? path.resolve(process.argv[2]) : "";
const port = Number(process.argv[3]);
const parentPid = Number(process.argv[4]);
if (!runtimeRoot || !Number.isInteger(port) || port < 1 || port > 65535 ||
    !Number.isInteger(parentPid) || parentPid <= 0 || typeof WebSocket !== "function") {
  process.exitCode = 2;
} else {
  await run(runtimeRoot, port, parentPid).catch(() => {
    process.exitCode = 1;
  });
}
