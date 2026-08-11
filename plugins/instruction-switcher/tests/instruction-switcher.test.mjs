import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { access, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

const TEST_ROOT = path.dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = path.resolve(TEST_ROOT, "..");
const { FOCUS_DOM_EXPRESSION, resolveCodexFocus } = await import(
  pathToFileURL(path.join(PLUGIN_ROOT, "lib", "codex-focus.mjs")).href
);

function keyFor(sessionId) {
  return createHash("sha256").update(sessionId).digest("hex");
}

async function temporaryRoot(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), "instruction-switcher-test-"));
  t.after(async () => {
    await rm(root, { recursive: true, force: true });
  });
  return root;
}

async function writeConfig(root, profiles, contents = {}) {
  const instructions = profiles.map((profile) => ({
    id: profile.id,
    name: profile.name || profile.label,
    file: `instructions/${profile.file || `${profile.id}.md`}`,
    createdAt: "2026-08-10T00:00:00.000Z",
    updatedAt: "2026-08-10T00:00:00.000Z",
  }));
  await mkdir(path.join(root, "instructions"), { recursive: true });
  await writeFile(
    path.join(root, "config.json"),
    `${JSON.stringify({
      version: 3,
      command: "/choose",
      defaultPresetId: null,
      instructions,
      presets: [],
    }, null, 2)}\n`,
    "utf8",
  );
  await Promise.all(Object.entries(contents).map(([file, content]) =>
    writeFile(path.join(root, "instructions", file), content, "utf8"),
  ));
}

async function writeSettings(root, settings, bodies = {}) {
  await mkdir(path.join(root, "instructions"), { recursive: true });
  await writeFile(
    path.join(root, "config.json"),
    `${JSON.stringify(settings, null, 2)}\n`,
    "utf8",
  );
  await Promise.all(Object.entries(bodies).map(async ([file, content]) => {
    const target = path.join(root, file.replaceAll("/", path.sep));
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, content);
  }));
}

async function writeLegacyConfig(root, profiles, contents = {}) {
  await mkdir(path.join(root, "presets"), { recursive: true });
  await writeFile(
    path.join(root, "config.json"),
    `${JSON.stringify({ command: "/choose", profiles }, null, 2)}\n`,
    "utf8",
  );
  await Promise.all(Object.entries(contents).map(([file, content]) =>
    writeFile(path.join(root, "presets", file), content, "utf8"),
  ));
}

function runScript(relativeScript, input, root, pluginData = "") {
  const result = spawnSync(process.execPath, [path.join(PLUGIN_ROOT, relativeScript)], {
    input: `${JSON.stringify(input)}\n`,
    encoding: "utf8",
    env: {
      ...process.env,
      CODEX_HOME: path.join(root, "codex-home"),
      INSTRUCTION_SWITCHER_HOME: root,
      PLUGIN_DATA: pluginData,
      INSTRUCTION_SWITCHER_NO_COMPANION: "1",
    },
    maxBuffer: 2 * 1024 * 1024,
  });
  assert.equal(result.status, 0, result.stderr);
  const lines = result.stdout.trim().split(/\r?\n/u).filter(Boolean);
  assert.ok(lines.length > 0, `missing hook output: ${result.stderr}`);
  return { output: JSON.parse(lines.at(-1)), stderr: result.stderr };
}

function runModule(code, root, extraEnv = {}) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ["--input-type=module", "-e", code], {
      encoding: "utf8",
      env: {
        ...process.env,
        INSTRUCTION_SWITCHER_HOME: root,
        PLUGIN_DATA: "",
        ...extraEnv,
      },
    });
    let stdout = "";
    let stderr = "";
    child.stdout.setEncoding("utf8");
    child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk) => { stdout += chunk; });
    child.stderr.on("data", (chunk) => { stderr += chunk; });
    child.once("error", reject);
    child.once("close", (status, signal) => resolve({ status, signal, stdout, stderr }));
  });
}

async function readJson(file) {
  return JSON.parse(await readFile(file, "utf8"));
}

test("controls skip disabled preset content and normal prompts acknowledge canonical state", async (t) => {
  const root = await temporaryRoot(t);
  const pluginData = path.join(root, "legacy-plugin-data");
  const profiles = [
    { id: "review", label: "Review", file: "review.md" },
    { id: "missing", label: "Missing", file: "missing.md" },
  ];
  await writeConfig(root, profiles, { "review.md": "REVIEW CONTENT" });
  const sessionId = "session-control";
  const key = keyFor(sessionId);
  const input = { session_id: sessionId, cwd: path.join(root, "project"), turn_id: "turn-1" };

  const control = runScript("hooks/prompt-submit.mjs", { ...input, prompt: "/choose set review" }, root, pluginData);
  assert.equal(control.output.decision, "block");

  const stateFile = path.join(root, "sessions", `${key}.json`);
  const state = await readJson(stateFile);
  assert.equal(state.version, 3);
  assert.deepEqual(state.enabled, ["review"]);
  assert.ok(state.revision);
  await assert.rejects(access(path.join(pluginData, "sessions", `${key}.json`)));

  const descriptor = await readJson(path.join(root, "runtime", "sessions", `${key}.json`));
  assert.equal(descriptor.version, 3);
  assert.equal(descriptor.key, key);
  assert.equal(Object.hasOwn(descriptor, "stateFile"), false);
  await assert.rejects(access(path.join(root, "runtime", "active.json")));

  const normal = runScript("hooks/prompt-submit.mjs", { ...input, prompt: "continue" }, root, pluginData);
  assert.match(normal.output.hookSpecificOutput.additionalContext, /REVIEW CONTENT/u);
  const acknowledgement = await readJson(path.join(root, "runtime", "acks", `${key}.json`));
  assert.equal(acknowledgement.revision, state.revision);
});

test("legacy config migration copies bodies byte-for-byte and keeps legacy files", async (t) => {
  const root = await temporaryRoot(t);
  const legacyBody = Buffer.from([0xef, 0xbb, 0xbf, 0x41, 0x0d, 0x0a, 0xe4, 0xb8, 0xad, 0x0a]);
  await mkdir(path.join(root, "presets"), { recursive: true });
  await writeFile(path.join(root, "config.json"), `${JSON.stringify({
    command: "/choose",
    profiles: [{ id: "review", label: "Review", file: "review.md" }],
  }, null, 2)}\n`, "utf8");
  const legacyFile = path.join(root, "presets", "review.md");
  await writeFile(legacyFile, legacyBody);

  runScript("hooks/session-start.mjs", { session_id: "legacy-config", cwd: root }, root);

  const config = await readJson(path.join(root, "config.json"));
  assert.equal(config.version, 3);
  assert.deepEqual(config.instructions.map((item) => item.id), ["review"]);
  assert.deepEqual(config.presets.map((item) => item.id), ["preset-code-review"]);
  assert.deepEqual(
    await readFile(path.join(root, "instructions", "review.md")),
    legacyBody,
  );
  assert.deepEqual(await readFile(legacyFile), legacyBody);
});

test("SessionStart initializes the selected default preset before the first prompt", async (t) => {
  const root = await temporaryRoot(t);
  const settings = {
    version: 3,
    command: "/choose",
    defaultPresetId: "preset-default",
    instructions: [{
      id: "review",
      name: "Review",
      file: "instructions/review.md",
      createdAt: "2026-08-10T00:00:00.000Z",
      updatedAt: "2026-08-10T00:00:00.000Z",
    }],
    presets: [{
      id: "preset-default",
      name: "Default",
      instructionIds: ["review"],
      createdAt: "2026-08-10T00:00:00.000Z",
      updatedAt: "2026-08-10T00:00:00.000Z",
    }],
  };
  await writeSettings(root, settings, { "instructions/review.md": "DEFAULT REVIEW" });

  runScript("hooks/session-start.mjs", { session_id: "new-task-before-prompt", cwd: root }, root);

  const state = await readJson(path.join(root, "sessions", `${keyFor("new-task-before-prompt")}.json`));
  assert.equal(state.version, 3);
  assert.deepEqual(state.enabled, ["review"]);
  assert.equal(state.activePresetId, "preset-default");
});

test("preset order is preserved and a missing body does not suppress other instructions", async (t) => {
  const root = await temporaryRoot(t);
  const settings = {
    version: 3,
    command: "/choose",
    defaultPresetId: null,
    instructions: [
      { id: "first", name: "First", file: "instructions/first.md" },
      { id: "second", name: "Second", file: "instructions/second.md" },
      { id: "missing", name: "Missing", file: "instructions/missing.md" },
    ],
    presets: [{
      id: "ordered",
      name: "Ordered",
      instructionIds: ["second", "first", "missing"],
    }],
  };
  await writeSettings(root, settings, {
    "instructions/first.md": "FIRST BODY",
    "instructions/second.md": "SECOND BODY",
  });
  const sessionId = "ordered-session";
  const key = keyFor(sessionId);
  await mkdir(path.join(root, "sessions"), { recursive: true });
  await writeFile(path.join(root, "sessions", `${key}.json`), JSON.stringify({
    version: 3,
    revision: randomUUID(),
    enabled: ["second", "first"],
    activePresetId: "ordered",
  }), "utf8");

  const result = runScript("hooks/prompt-submit.mjs", {
    session_id: sessionId,
    cwd: root,
    prompt: "continue",
  }, root);
  const context = result.output.hookSpecificOutput.additionalContext;
  assert.match(context, /FIRST BODY/u);
  assert.match(context, /SECOND BODY/u);
  assert.ok(context.indexOf("Second (second)") < context.indexOf("First (first)"));
  assert.doesNotMatch(context, /Missing \(missing\)/u);
});

test("deleting an instruction reference adapts presets and task state", async (t) => {
  const root = await temporaryRoot(t);
  const settings = {
    version: 3,
    command: "/choose",
    defaultPresetId: "preset-all",
    instructions: [
      { id: "keep", name: "Keep", file: "instructions/keep.md" },
      { id: "remove", name: "Remove", file: "instructions/remove.md" },
    ],
    presets: [{
      id: "preset-all",
      name: "All",
      instructionIds: ["keep", "remove", "keep", "missing"],
    }],
  };
  await writeSettings(root, settings, { "instructions/keep.md": "KEEP", "instructions/remove.md": "REMOVE" });
  const sessionId = "deleted-instruction";
  const key = keyFor(sessionId);
  await mkdir(path.join(root, "sessions"), { recursive: true });
  await writeFile(path.join(root, "sessions", `${key}.json`), JSON.stringify({
    version: 3,
    revision: randomUUID(),
    enabled: ["keep", "remove"],
    activePresetId: "preset-all",
  }), "utf8");
  settings.instructions = [settings.instructions[0]];
  await writeFile(path.join(root, "config.json"), `${JSON.stringify(settings, null, 2)}\n`, "utf8");

  const result = runScript("hooks/prompt-submit.mjs", { session_id: sessionId, cwd: root, prompt: "continue" }, root);
  assert.match(result.output.hookSpecificOutput.additionalContext, /KEEP/u);
  assert.doesNotMatch(result.output.hookSpecificOutput.additionalContext, /REMOVE/u);
  const normalizedConfig = await readJson(path.join(root, "config.json"));
  assert.deepEqual(normalizedConfig.presets[0].instructionIds, ["keep"]);
  const normalizedState = await readJson(path.join(root, "sessions", `${key}.json`));
  assert.deepEqual(normalizedState.enabled, ["keep"]);
  assert.equal(normalizedState.activePresetId, "preset-all");
});

test("hidden preset-package metadata survives cleanup and does not affect injection", async (t) => {
  const root = await temporaryRoot(t);
  const settings = {
    version: 3,
    command: "/choose",
    defaultPresetId: null,
    instructions: [{
      id: "imported-review",
      name: "Imported review",
      file: "instructions/imported-review.md",
      origin: "preset-package",
      sourcePackageId: "paper-review-package",
      sourcePackageKey: "review",
      sourceContentHash: "a".repeat(64),
      showInCustomPicker: false,
      createdAt: "2026-08-11T00:00:00.000Z",
      updatedAt: "2026-08-11T00:00:00.000Z",
    }],
    presets: [{
      id: "preset-imported",
      name: "Imported preset",
      instructionIds: ["imported-review", "missing"],
    }],
  };
  await writeSettings(root, settings, {
    "instructions/imported-review.md": "HIDDEN IN PICKER, ACTIVE IN TASK",
  });
  const sessionId = "hidden-imported-instruction";
  const key = keyFor(sessionId);
  await mkdir(path.join(root, "sessions"), { recursive: true });
  await writeFile(path.join(root, "sessions", `${key}.json`), JSON.stringify({
    version: 3,
    revision: randomUUID(),
    enabled: ["imported-review"],
    activePresetId: "preset-imported",
  }), "utf8");

  const result = runScript("hooks/prompt-submit.mjs", {
    session_id: sessionId,
    cwd: root,
    prompt: "continue",
  }, root);
  assert.match(result.output.hookSpecificOutput.additionalContext, /HIDDEN IN PICKER, ACTIVE IN TASK/u);
  const normalized = await readJson(path.join(root, "config.json"));
  assert.equal(normalized.instructions[0].origin, "preset-package");
  assert.equal(normalized.instructions[0].sourcePackageId, "paper-review-package");
  assert.equal(normalized.instructions[0].sourcePackageKey, "review");
  assert.equal(normalized.instructions[0].sourceContentHash, "a".repeat(64));
  assert.equal(normalized.instructions[0].showInCustomPicker, false);
  assert.deepEqual(normalized.presets[0].instructionIds, ["imported-review"]);
});

test("legacy PLUGIN_DATA state migrates lazily to the canonical root", async (t) => {
  const root = await temporaryRoot(t);
  const pluginData = path.join(root, "legacy-plugin-data");
  await writeConfig(
    root,
    [{ id: "review", label: "Review", file: "review.md" }],
    { "review.md": "LEGACY REVIEW" },
  );
  const sessionId = "session-legacy";
  const key = keyFor(sessionId);
  const legacyFile = path.join(pluginData, "sessions", `${key}.json`);
  await mkdir(path.dirname(legacyFile), { recursive: true });
  await writeFile(legacyFile, `\uFEFF${JSON.stringify({ version: 1, enabled: ["review"] })}`, "utf8");

  const result = runScript(
    "hooks/prompt-submit.mjs",
    { session_id: sessionId, cwd: root, prompt: "continue", turn_id: "turn-legacy" },
    root,
    pluginData,
  );
  assert.match(result.output.hookSpecificOutput.additionalContext, /LEGACY REVIEW/u);
  const canonical = await readJson(path.join(root, "sessions", `${key}.json`));
  assert.equal(canonical.version, 3);
  assert.deepEqual(canonical.enabled, ["review"]);
  assert.ok(canonical.revision);
  await access(legacyFile);
});

test("SessionStart migrates a legacy state before the companion reads it", async (t) => {
  const root = await temporaryRoot(t);
  const pluginData = path.join(root, "legacy-plugin-data");
  await writeConfig(root, [{ id: "review", label: "Review", file: "review.md" }]);
  const sessionId = "session-start-legacy";
  const key = keyFor(sessionId);
  const legacyFile = path.join(pluginData, "sessions", `${key}.json`);
  await mkdir(path.dirname(legacyFile), { recursive: true });
  await writeFile(legacyFile, JSON.stringify({ version: 1, enabled: ["review"] }), "utf8");

  runScript("hooks/session-start.mjs", {
    session_id: sessionId,
    cwd: root,
    source: "session-start",
  }, root, pluginData);

  const canonical = await readJson(path.join(root, "sessions", `${key}.json`));
  assert.equal(canonical.version, 3);
  assert.deepEqual(canonical.enabled, ["review"]);
  assert.ok(canonical.revision);
});

test("a corrupt canonical state never falls back to an older legacy copy", async (t) => {
  const root = await temporaryRoot(t);
  const pluginData = path.join(root, "legacy-plugin-data");
  await writeConfig(
    root,
    [{ id: "review", label: "Review", file: "review.md" }],
    { "review.md": "REVIEW" },
  );
  const sessionId = "session-corrupt";
  const key = keyFor(sessionId);
  const canonical = path.join(root, "sessions", `${key}.json`);
  const legacy = path.join(pluginData, "sessions", `${key}.json`);
  await mkdir(path.dirname(canonical), { recursive: true });
  await mkdir(path.dirname(legacy), { recursive: true });
  await writeFile(canonical, "{broken", "utf8");
  await writeFile(legacy, JSON.stringify({ version: 1, enabled: ["review"] }), "utf8");

  const result = runScript(
    "hooks/prompt-submit.mjs",
    { session_id: sessionId, cwd: root, prompt: "continue" },
    root,
    pluginData,
  );
  assert.deepEqual(result.output, {});
  assert.match(result.stderr, /state read failed/u);
  assert.equal(await readFile(canonical, "utf8"), "{broken");
});

test("descriptor and acknowledgement failures do not suppress core context", async (t) => {
  const root = await temporaryRoot(t);
  await writeConfig(
    root,
    [{ id: "review", label: "Review", file: "review.md" }],
    { "review.md": "CORE CONTEXT" },
  );
  const sessionId = "session-runtime-failure";
  const key = keyFor(sessionId);
  const stateFile = path.join(root, "sessions", `${key}.json`);
  await mkdir(path.dirname(stateFile), { recursive: true });
  await writeFile(stateFile, JSON.stringify({
    version: 3,
    revision: randomUUID(),
    enabled: ["review"],
    updatedAt: new Date().toISOString(),
  }), "utf8");
  await writeFile(path.join(root, "runtime"), "directory collision", "utf8");

  const result = runScript(
    "hooks/prompt-submit.mjs",
    { session_id: sessionId, cwd: root, prompt: "continue" },
    root,
  );
  assert.match(result.output.hookSpecificOutput.additionalContext, /CORE CONTEXT/u);
  assert.match(result.stderr, /session registration skipped/u);
  assert.match(result.stderr, /acknowledgement skipped/u);
});

test("two sessions keep independent selections", async (t) => {
  const root = await temporaryRoot(t);
  await writeConfig(
    root,
    [
      { id: "review", label: "Review", file: "review.md" },
      { id: "tdd", label: "TDD", file: "tdd.md" },
    ],
    { "review.md": "REVIEW ONLY", "tdd.md": "TDD ONLY" },
  );
  const a = { session_id: "session-a", cwd: root };
  const b = { session_id: "session-b", cwd: root };
  runScript("hooks/prompt-submit.mjs", { ...a, prompt: "/choose set review" }, root);
  runScript("hooks/prompt-submit.mjs", { ...b, prompt: "/choose set tdd" }, root);
  const outputA = runScript("hooks/prompt-submit.mjs", { ...a, prompt: "continue" }, root).output;
  const outputB = runScript("hooks/prompt-submit.mjs", { ...b, prompt: "continue" }, root).output;
  assert.match(outputA.hookSpecificOutput.additionalContext, /REVIEW ONLY/u);
  assert.doesNotMatch(outputA.hookSpecificOutput.additionalContext, /TDD ONLY/u);
  assert.match(outputB.hookSpecificOutput.additionalContext, /TDD ONLY/u);
  assert.doesNotMatch(outputB.hookSpecificOutput.additionalContext, /REVIEW ONLY/u);
});

test("Codex focus maps direct and provisional sidebar identifiers safely", () => {
  const directId = "019fe53e-ce33-7583-af77-e811f2240bfc";
  const direct = resolveCodexFocus({
    rowId: `local:${directId}`,
    title: "Existing task",
    conversationId: directId,
  });
  assert.equal(direct.sessionId, directId);
  assert.equal(direct.key, keyFor(directId));
  assert.equal(direct.mapping, "direct");

  const canonicalId = "019fe926-3b4c-7613-9de5-c00e018c59d6";
  const clientAlias = "client-new-thread:bf66cc79-2362-4e3e-b657-949e88b653de";
  const globalState = {
    "electron-persisted-atom-state": {
      [`thread-client-id-v1:${encodeURIComponent(`local:${canonicalId}`)}`]: clientAlias,
    },
  };
  const provisional = resolveCodexFocus({
    rowId: `local:${clientAlias}`,
    title: "New task",
    conversationId: canonicalId,
  }, globalState);
  assert.equal(provisional.sessionId, canonicalId);
  assert.equal(provisional.key, keyFor(canonicalId));
  assert.equal(provisional.mapping, "persisted-alias");

  const domOnly = resolveCodexFocus({
    rowId: `local:${clientAlias}`,
    title: "New task",
    conversationId: canonicalId,
  });
  assert.equal(domOnly.mapping, "active-dom");
  assert.equal(domOnly.key, keyFor(canonicalId));

  assert.equal(resolveCodexFocus({
    rowId: `local:${clientAlias}`,
    conversationId: directId,
  }, globalState), null);
  assert.equal(resolveCodexFocus({
    rowId: `local:${directId}`,
    conversationId: canonicalId,
  }), null);
  assert.match(FOCUS_DOM_EXPRESSION, /data-app-action-sidebar-thread-selected/u);
  assert.match(FOCUS_DOM_EXPRESSION, /data-above-composer-conversation-id/u);
});

test("revision comparison rejects a stale writer", async (t) => {
  const root = await temporaryRoot(t);
  const moduleUrl = pathToFileURL(path.join(PLUGIN_ROOT, "lib", "session-state.mjs")).href;
  const code = `
    const state = await import(${JSON.stringify(moduleUrl)});
    const first = await state.writeEnabled("conflict-session", ["review"], null);
    await state.writeEnabled("conflict-session", ["tdd"], first.revision);
    try {
      await state.writeEnabled("conflict-session", [], first.revision);
      process.exitCode = 2;
    } catch (error) {
      process.stdout.write(error.code || "missing-code");
    }
  `;
  const result = spawnSync(process.execPath, ["--input-type=module", "-e", code], {
    encoding: "utf8",
    env: {
      ...process.env,
      INSTRUCTION_SWITCHER_HOME: root,
      PLUGIN_DATA: "",
    },
  });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout, "ESTATECONFLICT");
});

test("concurrent writers serialize the revision check", async (t) => {
  const root = await temporaryRoot(t);
  const sessionId = "concurrent-session";
  const key = keyFor(sessionId);
  const revision = randomUUID();
  const stateFile = path.join(root, "sessions", `${key}.json`);
  await mkdir(path.dirname(stateFile), { recursive: true });
  await writeFile(stateFile, JSON.stringify({
    version: 3,
    revision,
    enabled: [],
    updatedAt: new Date().toISOString(),
  }), "utf8");

  const moduleUrl = pathToFileURL(path.join(PLUGIN_ROOT, "lib", "session-state.mjs")).href;
  const code = `
    const state = await import(${JSON.stringify(moduleUrl)});
    try {
      const next = await state.writeEnabled(${JSON.stringify(sessionId)}, [process.env.WORKER_ID], ${JSON.stringify(revision)});
      process.stdout.write("ok:" + next.revision);
    } catch (error) {
      process.stdout.write(error.code || "error");
    }
  `;
  const results = await Promise.all(
    Array.from({ length: 8 }, (_, index) => runModule(code, root, { WORKER_ID: `worker-${index}` })),
  );
  const outputs = results.map((result) => result.stdout);
  assert.equal(outputs.filter((value) => value.startsWith("ok:")).length, 1);
  assert.equal(outputs.filter((value) => value === "ESTATECONFLICT").length, 7);
  const finalState = await readJson(stateFile);
  assert.match(finalState.enabled[0], /^worker-\d+$/u);
});

test("future state versions remain untouched", async (t) => {
  const root = await temporaryRoot(t);
  await writeConfig(root, [{ id: "review", label: "Review", file: "review.md" }], { "review.md": "REVIEW" });
  const sessionId = "future-state";
  const key = keyFor(sessionId);
  const stateFile = path.join(root, "sessions", `${key}.json`);
  const future = JSON.stringify({ version: 4, revision: "future", enabled: ["review"], future: true });
  await mkdir(path.dirname(stateFile), { recursive: true });
  await writeFile(stateFile, future, "utf8");
  const result = runScript("hooks/prompt-submit.mjs", { session_id: sessionId, prompt: "continue" }, root);
  assert.deepEqual(result.output, {});
  assert.equal(await readFile(stateFile, "utf8"), future);
});
