#!/usr/bin/env node

import { spawnSync } from "node:child_process";

const MARKETPLACE = "silly-codex";
const MARKETPLACE_SOURCE = "foryourhealth111-pixel/Silly-codex";
const MARKETPLACE_REF = "main";
const PLUGIN = "instruction-switcher@silly-codex";
const PERSONAL_PLUGIN = "instruction-switcher@personal";

function usage() {
  process.stdout.write(`Silly-codex installer

Usage:
  silly-codex [install] [--replace-personal] [--dry-run]
  silly-codex --help

Options:
  --replace-personal  Remove a conflicting personal installation first.
  --dry-run           Print the Codex commands without changing anything.
  --help              Show this help.
`);
}

function parseArguments(arguments_) {
  const options = { dryRun: false, replacePersonal: false };
  for (const argument of arguments_) {
    if (argument === "install" || argument === "--") continue;
    if (argument === "--dry-run") options.dryRun = true;
    else if (argument === "--replace-personal") options.replacePersonal = true;
    else if (argument === "--help" || argument === "-h") options.help = true;
    else throw new Error(`Unknown argument: ${argument}`);
  }
  return options;
}

function displayCommand(arguments_) {
  return ["codex", ...arguments_].join(" ");
}

function codexInvocation(arguments_) {
  if (process.platform !== "win32") {
    return { command: "codex", arguments: arguments_ };
  }
  const commandLine = displayCommand(arguments_);
  return {
    command: process.env.ComSpec || "cmd.exe",
    arguments: ["/d", "/s", "/c", commandLine],
  };
}

function runCodex(arguments_, { capture = false } = {}) {
  const invocation = codexInvocation(arguments_);
  const result = spawnSync(invocation.command, invocation.arguments, {
    encoding: "utf8",
    stdio: capture ? ["ignore", "pipe", "pipe"] : "inherit",
    windowsHide: true,
  });
  if (result.error) {
    if (result.error.code === "ENOENT") {
      throw new Error("Codex CLI was not found in PATH. Install or update Codex first.");
    }
    throw result.error;
  }
  if (result.status !== 0) {
    const detail = capture ? String(result.stderr || result.stdout || "").trim() : "";
    throw new Error(
      `Command failed (${result.status}): ${displayCommand(arguments_)}` +
      (detail ? `\n${detail}` : ""),
    );
  }
  return capture ? String(result.stdout || "") : "";
}

function readJson(arguments_) {
  const output = runCodex(arguments_, { capture: true }).trim();
  try {
    return JSON.parse(output);
  } catch {
    throw new Error(`Codex returned invalid JSON for: ${displayCommand(arguments_)}`);
  }
}

function plannedCommands(replacePersonal) {
  const commands = [];
  if (replacePersonal) commands.push(["plugin", "remove", PERSONAL_PLUGIN]);
  commands.push([
    "plugin",
    "marketplace",
    "add",
    MARKETPLACE_SOURCE,
    "--ref",
    MARKETPLACE_REF,
  ]);
  commands.push(["plugin", "add", PLUGIN]);
  return commands;
}

function install(options) {
  if (options.dryRun) {
    for (const command of plannedCommands(options.replacePersonal)) {
      process.stdout.write(`${displayCommand(command)}\n`);
    }
    return;
  }

  const pluginList = readJson(["plugin", "list", "--json"]);
  const installed = Array.isArray(pluginList.installed) ? pluginList.installed : [];
  const personalInstalled = installed.some((plugin) => plugin?.pluginId === PERSONAL_PLUGIN);
  if (personalInstalled && !options.replacePersonal) {
    throw new Error(
      `A conflicting ${PERSONAL_PLUGIN} installation is enabled. ` +
      "Fully exit Codex, then run this installer with --replace-personal.",
    );
  }
  if (personalInstalled) runCodex(["plugin", "remove", PERSONAL_PLUGIN]);

  const marketplaceList = readJson(["plugin", "marketplace", "list", "--json"]);
  const marketplaces = Array.isArray(marketplaceList.marketplaces)
    ? marketplaceList.marketplaces
    : [];
  if (marketplaces.some((marketplace) => marketplace?.name === MARKETPLACE)) {
    runCodex(["plugin", "marketplace", "upgrade", MARKETPLACE]);
  } else {
    runCodex([
      "plugin",
      "marketplace",
      "add",
      MARKETPLACE_SOURCE,
      "--ref",
      MARKETPLACE_REF,
    ]);
  }
  runCodex(["plugin", "add", PLUGIN]);
  process.stdout.write(
    "Silly-codex installed. Restart Codex or start a new task, then review and trust the plugin hooks.\n",
  );
}

try {
  const options = parseArguments(process.argv.slice(2));
  if (options.help) usage();
  else install(options);
} catch (error) {
  process.stderr.write(`silly-codex: ${error.message || error}\n`);
  process.exitCode = 1;
}
