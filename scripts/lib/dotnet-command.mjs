import { existsSync } from "node:fs";
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

  return "dotnet";
}
