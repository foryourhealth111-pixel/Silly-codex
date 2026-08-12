import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";

const TEST_ROOT = path.dirname(fileURLToPath(import.meta.url));
const PLUGIN_ROOT = path.resolve(TEST_ROOT, "..");
const CONFIG_MODULE = pathToFileURL(path.join(PLUGIN_ROOT, "lib", "config.mjs")).href;
const LANGUAGE_MODULE = pathToFileURL(path.join(PLUGIN_ROOT, "lib", "ui-language.mjs")).href;

async function temporaryRoot(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), "instruction-switcher-locale-test-"));
  t.after(async () => rm(root, { recursive: true, force: true }));
  return root;
}

async function writeLanguage(root, language) {
  const runtime = path.join(root, "runtime");
  await mkdir(runtime, { recursive: true });
  await writeFile(
    path.join(runtime, "window-position.json"),
    `${JSON.stringify({ version: 2, language })}\n`,
    "utf8",
  );
}

test("saved UI language overrides locale detection", async (t) => {
  const root = await temporaryRoot(t);
  await writeLanguage(root, "en");
  const { languageFromLocale, readUiLanguage } = await import(LANGUAGE_MODULE);
  assert.equal(languageFromLocale("zh-CN"), "zh");
  assert.equal(languageFromLocale("en-US"), "en");
  assert.equal(await readUiLanguage(root), "en");
  await writeLanguage(root, "zh");
  assert.equal(await readUiLanguage(root), "zh");
});

test("a new English installation receives English seed content", async (t) => {
  const root = await temporaryRoot(t);
  await writeLanguage(root, "en");
  const code = `
    const { loadSettings } = await import(${JSON.stringify(CONFIG_MODULE)});
    const settings = await loadSettings({ contentIds: ["review"] });
    process.stdout.write(JSON.stringify(settings));
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
  const settings = JSON.parse(result.stdout);
  assert.deepEqual(settings.instructions.map((item) => item.name), ["Strict review"]);
  assert.match(settings.instructions[0].content, /strict code-review standard/u);
  const installed = JSON.parse(await readFile(path.join(root, "config.json"), "utf8"));
  assert.deepEqual(
    installed.presets.map((item) => item.name),
    ["Default", "Code review", "Test mode"],
  );
});

test("legacy migration uses the saved language for generated presets", async (t) => {
  const root = await temporaryRoot(t);
  await writeLanguage(root, "en");
  await mkdir(path.join(root, "presets"), { recursive: true });
  await writeFile(path.join(root, "presets", "review.md"), "LEGACY REVIEW", "utf8");
  await writeFile(
    path.join(root, "config.json"),
    `${JSON.stringify({
      command: "/choose",
      profiles: [{ id: "review", label: "Review", file: "review.md" }],
    })}\n`,
    "utf8",
  );
  const code = `
    const { loadSettings } = await import(${JSON.stringify(CONFIG_MODULE)});
    process.stdout.write(JSON.stringify(await loadSettings()));
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
  const settings = JSON.parse(result.stdout);
  assert.deepEqual(settings.presets.map((item) => item.name), ["Code review"]);
});

test("English preference localizes choose command feedback", async (t) => {
  const root = await temporaryRoot(t);
  await writeLanguage(root, "en");
  const input = JSON.stringify({
    session_id: "english-feedback",
    prompt: "/choose status",
    cwd: root,
  });
  const result = spawnSync(process.execPath, [path.join(PLUGIN_ROOT, "hooks", "prompt-submit.mjs")], {
    input: `${input}\n`,
    encoding: "utf8",
    env: {
      ...process.env,
      INSTRUCTION_SWITCHER_HOME: root,
      PLUGIN_DATA: "",
      INSTRUCTION_SWITCHER_NO_COMPANION: "1",
    },
  });
  assert.equal(result.status, 0, result.stderr);
  const output = JSON.parse(result.stdout.trim().split(/\r?\n/u).at(-1));
  assert.equal(output.decision, "block");
  assert.match(output.reason, /^Enabled: None · Custom selection$/u);
});
