import { access, readdir, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const REPOSITORY_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const pluginRoot = path.resolve(process.argv[2] || path.join(REPOSITORY_ROOT, "plugins", "instruction-switcher"));
const errors = [];

async function exists(relativePath) {
  try {
    await access(path.join(pluginRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

async function readJson(relativePath) {
  try {
    return JSON.parse(await readFile(path.join(pluginRoot, relativePath), "utf8"));
  } catch (error) {
    errors.push(`${relativePath}: ${error.message}`);
    return null;
  }
}

function requireString(value, field) {
  if (typeof value !== "string" || value.trim() === "") {
    errors.push(`${field} must be a non-empty string`);
  }
}

function requireHttps(value, field) {
  requireString(value, field);
  if (typeof value === "string" && value.trim() !== "") {
    try {
      if (new URL(value).protocol !== "https:") errors.push(`${field} must use https`);
    } catch {
      errors.push(`${field} must be an absolute URL`);
    }
  }
}

const manifest = await readJson(path.join(".codex-plugin", "plugin.json"));
if (manifest) {
  requireString(manifest.name, "name");
  requireString(manifest.version, "version");
  requireString(manifest.description, "description");
  if (typeof manifest.version === "string" && !/^\d+\.\d+\.\d+$/.test(manifest.version)) {
    errors.push(`version must be strict SemVer (x.y.z): ${manifest.version}`);
  }
  if (manifest.license !== "Apache-2.0") errors.push("license must be Apache-2.0");
  if (!manifest.author || typeof manifest.author !== "object") {
    errors.push("author must be an object");
  } else {
    requireString(manifest.author.name, "author.name");
    if (manifest.author.url !== undefined) requireHttps(manifest.author.url, "author.url");
  }
  requireHttps(manifest.homepage, "homepage");
  requireHttps(manifest.repository, "repository");
  if (!Array.isArray(manifest.keywords) || manifest.keywords.length === 0) {
    errors.push("keywords must be a non-empty array");
  }
  const ui = manifest.interface;
  if (!ui || typeof ui !== "object") {
    errors.push("interface must be an object");
  } else {
    for (const field of ["displayName", "shortDescription", "longDescription", "developerName", "category"]) {
      requireString(ui[field], `interface.${field}`);
    }
    if (!Array.isArray(ui.capabilities) || ui.capabilities.length === 0) {
      errors.push("interface.capabilities must be a non-empty array");
    }
    requireString(ui.defaultPrompt, "interface.defaultPrompt");
    requireHttps(ui.websiteURL, "interface.websiteURL");
    requireHttps(ui.privacyPolicyURL, "interface.privacyPolicyURL");
  }
  if (Object.hasOwn(manifest, "mcpServers")) errors.push("mcpServers must be absent from the release manifest");
}

for (const relativePath of [
  "LICENSE",
  "NOTICE",
  "README.md",
  ".codex-plugin/plugin.json",
  "hooks/hooks.json",
  "hooks/session-start.mjs",
  "hooks/prompt-submit.mjs",
  "lib/config.mjs",
  "lib/ui-language.mjs",
  "lib/session-state.mjs",
  "lib/companion.mjs",
  "companion/InstructionSwitcherCompanion.exe",
  "companion/AssemblyInfo.cs",
  "companion/focus-tracker.mjs",
  "defaults/config.json",
  "defaults/en/config.json",
  "defaults/instructions",
  "README.en.md",
]) {
  if (!(await exists(relativePath))) errors.push(`required file is missing: ${relativePath}`);
}

for (const configPath of ["defaults/config.json", "defaults/en/config.json"]) {
  const defaults = await readJson(configPath);
  if (!defaults) continue;
  if (!Array.isArray(defaults.instructions) || defaults.instructions.length === 0) {
    errors.push(`${configPath}: instructions must be a non-empty array`);
    continue;
  }
  for (const instruction of defaults.instructions) {
    const relativeFile = typeof instruction?.file === "string" ? instruction.file : "";
    if (!relativeFile.startsWith("instructions/") || !(await exists(path.join("defaults", relativeFile)))) {
      errors.push(`${configPath}: instruction body is missing: ${relativeFile || "<empty>"}`);
    }
  }
}

if (await exists(".mcp.json")) errors.push("MCP release component must be removed: .mcp.json");
try {
  const mcpEntries = await readdir(path.join(pluginRoot, "mcp"));
  if (mcpEntries.length > 0) errors.push("MCP release component must be removed: mcp");
} catch {
  // The directory may be absent in a clean checkout.
}

const hooks = await readJson("hooks/hooks.json");
if (hooks) {
  for (const name of ["SessionStart", "UserPromptSubmit"]) {
    if (!Array.isArray(hooks.hooks?.[name]) || hooks.hooks[name].length === 0) {
      errors.push(`hooks/hooks.json must define ${name}`);
    }
  }
}

if (errors.length > 0) {
  console.error("Plugin validation failed:");
  for (const error of errors) console.error(`- ${error}`);
  process.exitCode = 1;
} else {
  console.log(`Plugin validation passed: ${pluginRoot}`);
}
