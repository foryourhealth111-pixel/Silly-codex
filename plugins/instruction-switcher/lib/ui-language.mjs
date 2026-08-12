import { readFile } from "node:fs/promises";
import path from "node:path";

export function languageFromLocale(locale) {
  return String(locale || "").toLowerCase().startsWith("zh") ? "zh" : "en";
}

export async function readUiLanguage(configRoot) {
  try {
    const preferenceFile = path.join(configRoot, "runtime", "window-position.json");
    const raw = JSON.parse(await readFile(preferenceFile, "utf8"));
    const language = String(raw?.language || "").toLowerCase();
    if (language === "zh" || language === "en") return language;
  } catch {
    // Missing or invalid window preferences fall back to the operating system.
  }
  return languageFromLocale(Intl.DateTimeFormat().resolvedOptions().locale);
}
