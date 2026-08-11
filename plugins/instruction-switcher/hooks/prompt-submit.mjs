import { loadSettings } from "../lib/config.mjs";
import { launchCompanion } from "../lib/companion.mjs";
import {
  acknowledgeState,
  isSubagentEvent,
  readSessionState,
  registerSession,
  writeEnabled,
} from "../lib/session-state.mjs";

const EVENT = "UserPromptSubmit";

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

function parseControl(prompt, command) {
  const text = prompt.trim();
  if (text === command) return { verb: "status", ids: [] };
  if (!text.startsWith(command) || !/^\s/u.test(text.slice(command.length))) return null;
  const rest = text.slice(command.length).trim();
  if (!rest) return { verb: "status", ids: [] };
  const [verb, ...tail] = rest.split(/\s+/u);
  const ids = tail.join(" ").split(/[\s,，]+/u).filter(Boolean);
  return { verb: verb.toLowerCase(), ids };
}

function block(reason) {
  return { decision: "block", reason };
}

function instructionsOf(settings) {
  return Array.isArray(settings?.instructions) ? settings.instructions : [];
}

function presetsOf(settings) {
  return Array.isArray(settings?.presets) ? settings.presets : [];
}

function ordered(ids, instructions) {
  const available = new Set(instructions.map((instruction) => instruction.id));
  const seen = new Set();
  return ids.filter((id) => available.has(id) && !seen.has(id) && (seen.add(id), true));
}

function names(ids, instructions) {
  const labels = new Map(instructions.map((instruction) => [instruction.id, instruction.name]));
  const selected = ids.map((id) => labels.get(id)).filter(Boolean);
  return selected.length ? selected.join("、") : "无";
}

function presetLabel(id, settings) {
  return presetsOf(settings).find((preset) => preset.id === id)?.name || "自定义";
}

function usage(command) {
  return `用法：${command} preset <预设ID>；${command} set review,tdd；${command} on review；${command} off review；${command} clear；${command} status`;
}

async function handleControl(control, sessionId, settings, state) {
  const command = settings.command;
  const instructions = instructionsOf(settings);
  const presets = presetsOf(settings);
  const validIds = new Set(instructions.map((instruction) => instruction.id));
  const enabled = state.enabled;

  if (state.status === "error") {
    return block("任务状态读取失败，配置未改变。请检查状态文件后重试。");
  }

  if (control.verb === "status") {
    if (control.ids.length) return block(`用法：${command} status`);
    const presetText = state.activePresetId ? ` · 配置：${presetLabel(state.activePresetId, settings)}` : " · 自定义配置";
    return block(`当前启用：${names(enabled, instructions)}${presetText}`);
  }
  if (control.verb === "list") {
    if (control.ids.length) return block(`用法：${command} list`);
    const selected = new Set(enabled);
    const itemText = instructions.map((instruction) =>
      `${instruction.id}${selected.has(instruction.id) ? "（已启用）" : ""}`).join("、");
    const presetText = presets.map((preset) => `${preset.id}（${preset.name}）`).join("、");
    return block(`可用指令：${itemText || "无"}；配置预设：${presetText || "无"}`);
  }
  if (control.verb === "help") return block(usage(command));

  if (control.verb === "preset" || control.verb === "apply") {
    if (control.ids.length !== 1) return block(usage(command));
    const preset = presets.find((item) => item.id === control.ids[0]);
    if (!preset) return block(`未知配置预设：${control.ids[0]}`);
    const next = ordered(preset.instructionIds, instructions);
    try {
      await writeEnabled(sessionId, next, state.revision, settings, preset.id);
      return block(`已应用配置预设“${preset.name}”：${names(next, instructions)}`);
    } catch (error) {
      const reason = error?.code === "ESTATECONFLICT"
        ? "任务状态已在其他窗口更新，请重试。"
        : `保存失败，配置未改变：${error.message}`;
      return block(reason);
    }
  }

  const bad = control.ids.filter((id) => !validIds.has(id));
  if (bad.length) return block(`未知指令项：${bad.join("、")}`);

  let next;
  if (control.verb === "clear" && control.ids.length === 0) next = [];
  if (control.verb === "set" && control.ids.length > 0) next = ordered(control.ids, instructions);
  if (control.verb === "on" && control.ids.length > 0) next = ordered([...enabled, ...control.ids], instructions);
  if (control.verb === "off" && control.ids.length > 0) {
    const removed = new Set(control.ids);
    next = enabled.filter((id) => !removed.has(id));
  }
  if (!next) return block(usage(command));

  try {
    await writeEnabled(sessionId, next, state.revision, settings);
    return block(`已启用：${names(next, instructions)} · ${presetLabel(
      presets.find((preset) => JSON.stringify(preset.instructionIds) === JSON.stringify(next))?.id,
      settings,
    )}`);
  } catch (error) {
    const reason = error?.code === "ESTATECONFLICT"
      ? "任务状态已在其他窗口更新，请重试。"
      : `保存失败，配置未改变：${error.message}`;
    return block(reason);
  }
}

function contextFor(enabled, instructions) {
  const byId = new Map(instructions.map((instruction) => [instruction.id, instruction]));
  const sections = enabled
    .map((id) => byId.get(id))
    .filter(Boolean)
    .map((instruction) => `## ${instruction.name} (${instruction.id})\n${instruction.content}`);
  if (!sections.length) return "";
  return [
    "[Instruction Switcher]",
    "The user enabled these task-specific instructions for this conversation:",
    ...sections,
  ].join("\n\n");
}

async function refreshCompanion(input, settings) {
  try {
    await registerSession(input, settings);
  } catch (error) {
    process.stderr.write(`instruction-switcher: session registration skipped (${error.message})\n`);
  }
  if (isSubagentEvent(input)) return;
  try {
    await launchCompanion();
  } catch (error) {
    process.stderr.write(`instruction-switcher: companion launch skipped (${error.message})\n`);
  }
}

async function acknowledge(sessionId, state, input) {
  try {
    await acknowledgeState(sessionId, state, input);
  } catch (error) {
    process.stderr.write(`instruction-switcher: acknowledgement skipped (${error.message})\n`);
  }
}

try {
  const input = await inputJson();
  const prompt = typeof input?.prompt === "string" ? input.prompt : "";
  const sessionId = typeof input?.session_id === "string" ? input.session_id : "";
  const settings = await loadSettings();
  const control = parseControl(prompt, settings.command);

  if (!sessionId) {
    emit(control ? block("无法识别当前对话，配置未改变。") : {});
  } else {
    const state = await readSessionState(sessionId, settings);
    await refreshCompanion(input, settings);
    if (control) {
      emit(await handleControl(control, sessionId, settings, state));
    } else if (state.status === "error") {
      emit({});
    } else {
      const contentSettings = state.enabled.length
        ? await loadSettings({ contentIds: state.enabled })
        : { ...settings, instructions: [] };
      const additionalContext = contextFor(state.enabled, contentSettings.instructions);
      await acknowledge(sessionId, state, input);
      emit(
        additionalContext
          ? { hookSpecificOutput: { hookEventName: EVENT, additionalContext } }
          : {},
      );
    }
  }
} catch (error) {
  process.stderr.write(`instruction-switcher: hook failed open (${error.stack || error})\n`);
  emit({});
}
