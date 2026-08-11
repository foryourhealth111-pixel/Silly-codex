import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import test from "node:test";

const installer = fileURLToPath(
  new URL("../../../scripts/install-silly-codex.mjs", import.meta.url),
);

function run(...arguments_) {
  return spawnSync(process.execPath, [installer, ...arguments_], {
    encoding: "utf8",
  });
}

test("npm installer exposes a GitHub-backed dry run", () => {
  const result = run("--dry-run");
  assert.equal(result.status, 0, result.stderr);
  assert.match(
    result.stdout,
    /codex plugin marketplace add foryourhealth111-pixel\/Silly-codex --ref main/u,
  );
  assert.match(result.stdout, /codex plugin add instruction-switcher@silly-codex/u);
  assert.doesNotMatch(result.stdout, /instruction-switcher@personal/u);
});

test("npm installer makes personal-plugin replacement explicit", () => {
  const result = run("--", "--dry-run", "--replace-personal");
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /codex plugin remove instruction-switcher@personal/u);
});

test("npm installer rejects unknown arguments", () => {
  const result = run("--unknown");
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Unknown argument: --unknown/u);
});
