import { execFile } from "node:child_process";
import { appendFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import path from "node:path";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";
import { spawnProcessTree, stopProcessTree } from "./lib/child-process-tree.mjs";
import { resolveDotnetCommand } from "./lib/dotnet-command.mjs";
const defaultTests = [
  "ExportDocManager.Infrastructure.Tests.ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplatesToPdf_ShouldUseConfiguredRendererAndRuntimeDataRoot",
  "ExportDocManager.Infrastructure.Tests.ReportHtmlServiceInfrastructureTests.RenderBuiltInProgramTemplatesWithMultiItemBusinessDataToPdf_ShouldPreservePaginationAndDomainIsolation",
];
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const projectPath = path.join(repositoryRoot, "tests", "ExportDocManager.Infrastructure.Tests", "ExportDocManager.Infrastructure.Tests.csproj");
const diagnosticRoot = path.join(repositoryRoot, ".codex-runtime", "report-pdf-test-watchdog");
const watchdogLogPath = path.join(diagnosticRoot, "watchdog.log");
const cleanupTimeoutMs = 10_000;
const execFileAsync = promisify(execFile);
let activeTestProcess;
let options;
let shuttingDown = false;
mkdirSync(diagnosticRoot, { recursive: true });
writeFileSync(watchdogLogPath, "", "utf8");
for (const signal of ["SIGINT", "SIGTERM"]) process.once(signal, () => { void stopForSignal(signal); });
try {
  options = readOptions(process.argv.slice(2));
  await run();
} catch (error) {
  writeWatchdogMessage(`Watchdog failed: ${formatError(error)}`);
  process.exitCode = normalizeExitCode(error?.exitCode);
}
async function run() {
  const dotnetExecutable = resolveDotnetCommand();
  await verifyDotnetSdk(dotnetExecutable);
  for (const test of options.tests) {
    const result = await runTest(dotnetExecutable, test);
    if (result.timedOut) {
      writeWatchdogMessage(`${test} failed because its process tree did not finish in time.`);
      process.exitCode = 124;
      return;
    }
    if (result.exitCode !== 0) {
      const signalSuffix = result.signal ? ` after ${result.signal}` : "";
      writeWatchdogMessage(`${test} failed with exit code ${result.exitCode}${signalSuffix}.`);
      process.exitCode = normalizeExitCode(result.exitCode);
      return;
    }
    writeWatchdogMessage(`${test} completed successfully in ${result.elapsedSeconds.toFixed(1)} seconds.`);
  }
  writeWatchdogMessage("All long-running report PDF tests completed successfully.");
}
async function verifyDotnetSdk(dotnetExecutable) {
  const requiredSdk = JSON.parse(readFileSync(path.join(repositoryRoot, "global.json"), "utf8")).sdk.version;
  const { stdout } = await execFileAsync(dotnetExecutable, ["--version"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    timeout: cleanupTimeoutMs,
    windowsHide: true,
  });
  const actualSdk = stdout.trim();
  if (actualSdk !== requiredSdk) {
    throw new Error(`No native dotnet executable on PATH can load the required SDK ${requiredSdk}; resolved ${dotnetExecutable} reported ${actualSdk || "no version"}.`);
  }
  writeWatchdogMessage(`Using .NET SDK ${actualSdk} from ${dotnetExecutable}.`);
}
function runTest(dotnetExecutable, test) {
  const testSlug = test.split(".").at(-1).replaceAll(/[^A-Za-z0-9_.-]/gu, "_");
  const diagnosticLogPath = path.join(diagnosticRoot, `${testSlug}.diag.log`);
  const trxFileName = `${testSlug}.trx`;
  const trxPath = path.join(diagnosticRoot, trxFileName);
  const args = [
    "test", projectPath,
    "-c", "Release",
    "-m:1",
    "-p:BuildInParallel=false",
    "--no-build",
    "--no-restore",
    "--logger", "console;verbosity=normal",
    "--logger", `trx;LogFileName=${trxFileName}`,
    "--results-directory", diagnosticRoot,
    "--diag", diagnosticLogPath,
    "--filter", `FullyQualifiedName=${test}`,
  ];
  const timeoutMs = options.perTestTimeoutSeconds * 1000;
  const startedAt = Date.now();
  const elapsedSeconds = () => (Date.now() - startedAt) / 1000;
  rmSync(trxPath, { force: true });
  writeWatchdogMessage(`Starting ${test} with a hard timeout of ${options.perTestTimeoutSeconds} seconds.`);
  const child = spawnProcessTree(dotnetExecutable, args, {
    cwd: repositoryRoot,
    env: process.env,
    stdio: "inherit",
    windowsHide: true,
  });
  child.unref(); // Timers, rather than an unreliable native handle, own the watchdog lifetime.
  activeTestProcess = child;

  return new Promise((resolve, reject) => {
    let settled = false;
    const heartbeat = setInterval(() => {
      writeWatchdogMessage(`${test} is still running after ${Math.floor(elapsedSeconds())} seconds (PID ${child.pid}).`);
    }, options.heartbeatSeconds * 1000);
    const timeout = setTimeout(() => { void terminateTimedOutTest(); }, timeoutMs);
    child.once("error", finishWithError);
    child.once("exit", (code, signal) => { void finishExitedTest(code, signal); });

    async function finishExitedTest(code, signal) {
      if (settled) return;
      settled = true;
      clearTimers();
      let exitCode = code ?? 1;
      if (exitCode === 0) {
        try {
          if (readTrxExecutedTestCount(trxPath) < 1) {
            writeWatchdogMessage(`${test} did not execute any tests; treating the stale filter as a failure.`);
            exitCode = 1;
          }
        } catch (error) {
          writeWatchdogMessage(`${test} did not produce a readable TRX result: ${formatError(error)}`);
          exitCode = 1;
        }
      }
      if (!await terminateProcessTree(child, "Post-test process-tree cleanup failed")) exitCode = 1;
      clearActiveProcess(child);
      resolve({ timedOut: false, exitCode, signal, elapsedSeconds: elapsedSeconds() });
    }

    async function terminateTimedOutTest() {
      if (settled) return;
      settled = true;
      clearTimers();
      writeWatchdogMessage(`${test} exceeded its hard timeout; terminating the complete dotnet/testhost/Chrome process tree.`);
      await terminateProcessTree(child, "Timed-out process-tree cleanup failed");
      clearActiveProcess(child);
      resolve({ timedOut: true, exitCode: 124, signal: null, elapsedSeconds: elapsedSeconds() });
    }

    function finishWithError(error) {
      if (settled) return;
      settled = true;
      clearTimers();
      clearActiveProcess(child);
      reject(error);
    }

    function clearTimers() {
      clearInterval(heartbeat);
      clearTimeout(timeout);
    }
  });
}
async function terminateProcessTree(child, failurePrefix) {
  try {
    if (await stopProcessTree(child, cleanupTimeoutMs)) return true;
    writeWatchdogMessage(`${failurePrefix}: PID ${child.pid} did not exit within the cleanup deadline.`);
  } catch (error) {
    writeWatchdogMessage(`${failurePrefix} for PID ${child.pid}: ${formatError(error)}`);
  }
  return false;
}
async function stopForSignal(signal) {
  if (shuttingDown) return;
  shuttingDown = true;
  const exitCode = signal === "SIGINT" ? 130 : 143;
  const forcedExit = setTimeout(() => process.exit(exitCode), cleanupTimeoutMs + 2_000);
  writeWatchdogMessage(`Received ${signal}; terminating the active report test process tree.`);
  try {
    if (activeTestProcess) await terminateProcessTree(activeTestProcess, "Signal cleanup failed");
  } finally {
    clearTimeout(forcedExit);
    process.exit(exitCode);
  }
}
function readOptions(args) {
  let perTestTimeoutSeconds = 180;
  let heartbeatSeconds = 15;
  const tests = [];
  for (const argument of args) {
    if (argument.startsWith("--per-test-timeout-seconds=")) {
      perTestTimeoutSeconds = readIntegerOption(argument, "--per-test-timeout-seconds", 1, 900);
    } else if (argument.startsWith("--heartbeat-seconds=")) {
      heartbeatSeconds = readIntegerOption(argument, "--heartbeat-seconds", 1, 60);
    } else if (argument.startsWith("--test=")) {
      const test = argument.slice("--test=".length).trim();
      if (!test) throw new Error("--test must not be empty");
      tests.push(test);
    } else {
      throw new Error(`Unknown argument: ${argument}`);
    }
  }
  return { perTestTimeoutSeconds, heartbeatSeconds, tests: tests.length ? [...new Set(tests)] : defaultTests };
}
function readIntegerOption(argument, name, minimum, maximum) {
  const value = Number.parseInt(argument.slice(name.length + 1), 10);
  if (!Number.isInteger(value) || value < minimum || value > maximum) {
    throw new Error(`${name} must be an integer from ${minimum} through ${maximum}`);
  }
  return value;
}
function readTrxExecutedTestCount(trxPath) {
  const match = readFileSync(trxPath, "utf8").match(/<Counters\b[^>]*\bexecuted="(?<executed>\d+)"/u);
  if (!match?.groups?.executed) throw new Error(`TRX counters were not found in ${trxPath}`);
  return Number.parseInt(match.groups.executed, 10);
}
function clearActiveProcess(child) {
  if (activeTestProcess === child) activeTestProcess = undefined;
}
function writeWatchdogMessage(message) {
  const line = `${new Date().toISOString()} ${message}`;
  process.stdout.write(`${line}\n`);
  appendFileSync(watchdogLogPath, `${line}\n`, "utf8");
}
function normalizeExitCode(exitCode) {
  return Number.isInteger(exitCode) && exitCode > 0 && exitCode <= 255 ? exitCode : 1;
}
function formatError(error) {
  return error instanceof Error ? error.message : String(error);
}
