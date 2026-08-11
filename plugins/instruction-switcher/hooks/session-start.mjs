import { loadSettings } from "../lib/config.mjs";
import { launchCompanion } from "../lib/companion.mjs";
import {
  isSubagentEvent,
  readSessionState,
  registerSession,
} from "../lib/session-state.mjs";

function emit(value) {
  process.stdout.write(`${JSON.stringify(value)}\n`);
}

async function inputJson() {
  let text = "";
  for await (const chunk of process.stdin) {
    text += chunk;
    if (text.length > 1_000_000) throw new Error("hook input is too large");
  }
  return JSON.parse(text);
}

let input;
let parseFailed = false;
try {
  input = await inputJson();
} catch (error) {
  parseFailed = true;
  process.stderr.write(`instruction-switcher: invalid SessionStart input (${error.stack || error})\n`);
  emit({});
}

if (!parseFailed && input && typeof input === "object" && !Array.isArray(input)) {
  try {
    const settings = await loadSettings();
    const descriptor = await registerSession(input, settings, input?.source || "session-start");
    if (descriptor && input.session_id) await readSessionState(input.session_id, settings);
  } catch (error) {
    process.stderr.write(`instruction-switcher: session registration skipped (${error.stack || error})\n`);
  }
  if (!isSubagentEvent(input)) {
    try {
      await launchCompanion();
    } catch (error) {
      process.stderr.write(`instruction-switcher: companion launch skipped (${error.stack || error})\n`);
    }
  }
  emit({});
} else if (!parseFailed) {
  emit({});
}
