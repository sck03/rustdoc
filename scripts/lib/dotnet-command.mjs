import { existsSync, readFileSync } from "node:fs";
import path from "node:path";

export function resolveDotnetCommand(environment = process.env) {
  const architectureVariable = new Map([
    ["x64", "DOTNET_ROOT_X64"],
    ["arm64", "DOTNET_ROOT_ARM64"],
    ["ia32", "DOTNET_ROOT_X86"],
  ]).get(process.arch);
  const executableName = process.platform === "win32" ? "dotnet.exe" : "dotnet";
  const candidates = [
    architectureVariable ? environment[architectureVariable] : "",
    environment.DOTNET_ROOT,
  ];

  for (const root of candidates) {
    if (!root) continue;
    const candidate = path.resolve(root, executableName);
    if (existsSync(candidate)) return candidate;
  }

  if (process.platform === "win32") {
    const pathCommand = resolveWindowsPathCommand(environment, executableName);
    if (pathCommand) return pathCommand;
  }

  return "dotnet";
}

function resolveWindowsPathCommand(environment, executableName) {
  const pathValue = environment.Path || environment.PATH || "";
  for (const rawEntry of pathValue.split(path.delimiter)) {
    const entry = rawEntry.trim().replace(/^"|"$/gu, "");
    if (!entry) continue;

    const executable = path.join(entry, executableName);
    if (existsSync(executable)) return executable;

    for (const extension of [".cmd", ".bat"]) {
      const wrapper = path.join(entry, `dotnet${extension}`);
      const target = resolveWindowsWrapperTarget(wrapper, environment);
      if (target) return target;
    }
  }

  return "";
}

function resolveWindowsWrapperTarget(wrapperPath, environment) {
  if (!existsSync(wrapperPath)) return "";

  let content;
  try {
    content = readFileSync(wrapperPath, "utf8");
  } catch {
    return "";
  }

  const expanded = content
    .replaceAll(/%~dp0/giu, `${path.dirname(wrapperPath)}${path.sep}`)
    .replaceAll(/%([^%]+)%/gu, (_, name) => environment[name] || environment[name.toUpperCase()] || "");
  const match = expanded.match(/(?:"(?<quoted>[^"\r\n]*[\\/]dotnet\.exe)"|(?<bare>(?:[A-Za-z]:[\\/]|\\\\)[^\s\r\n]*dotnet\.exe))/iu);
  const target = match?.groups?.quoted || match?.groups?.bare || "";
  return target && existsSync(target) ? path.resolve(target) : "";
}
