import { access } from "node:fs/promises";
import path from "node:path";
import { spawn } from "node:child_process";
import { PLUGIN_ROOT } from "./config.mjs";
import { RUNTIME_ROOT } from "./session-state.mjs";

export async function launchCompanion() {
  if (process.platform !== "win32" || process.env.INSTRUCTION_SWITCHER_NO_COMPANION === "1") {
    return;
  }
  const executable = path.join(
    PLUGIN_ROOT,
    "companion",
    "InstructionSwitcherCompanion.exe",
  );
  await access(executable);
  await new Promise((resolve, reject) => {
    const child = spawn(executable, [RUNTIME_ROOT], {
      detached: true,
      stdio: "ignore",
      windowsHide: true,
    });
    child.once("error", reject);
    child.once("spawn", () => {
      child.unref();
      resolve();
    });
  });
}
