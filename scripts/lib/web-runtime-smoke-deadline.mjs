export function createSmokeStageRunner(globalTimeoutMs, stageTimeoutMs, log = console.error) {
  const startedAt = Date.now();
  const deadline = startedAt + globalTimeoutMs;

  return async function runStage(name, operation) {
    const remainingMs = deadline - Date.now();
    if (remainingMs <= 0) {
      throw new Error(`Web runtime smoke exceeded its ${globalTimeoutMs} ms global deadline before stage: ${name}.`);
    }

    const boundedTimeoutMs = Math.min(stageTimeoutMs, remainingMs);
    log(`[smoke] ${name} started; ${Math.ceil(remainingMs / 1000)}s remain.`);
    let timer;
    try {
      const result = await Promise.race([
        Promise.resolve().then(() => operation(boundedTimeoutMs)),
        new Promise((_, reject) => {
          timer = setTimeout(() => {
            reject(new Error(`Web runtime smoke exceeded its global deadline during stage: ${name}.`));
          }, remainingMs);
        }),
      ]);
      log(`[smoke] ${name} completed; ${Date.now() - startedAt}ms total elapsed.`);
      return result;
    } finally {
      clearTimeout(timer);
    }
  };
}
