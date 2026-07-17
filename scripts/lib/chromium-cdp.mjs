export function waitForDevToolsUrl(child, slug) {
  return new Promise((resolve, reject) => {
    let output = "";
    let settled = false;
    const timer = setTimeout(() => {
      finish(new Error(`${slug}: timed out waiting for Chrome DevTools endpoint.\n${output}`));
    }, 30000);

    function finish(error, value) {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timer);
      if (error) {
        reject(error);
      } else {
        resolve(value);
      }
    }

    function consume(data) {
      output += data.toString();
      const match = output.match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) {
        finish(null, match[1]);
      }
    }

    child.stdout.on("data", consume);
    child.stderr.on("data", consume);
    child.once("error", (error) => finish(error));
    child.once("exit", (code, signal) => {
      finish(new Error(`${slug}: Chrome exited before DevTools endpoint was ready (${code ?? signal}).\n${output}`));
    });
  });
}

export async function getPageWebSocketUrl(browserWebSocketUrl, slug) {
  const browserUrl = new URL(browserWebSocketUrl);
  const listUrl = `http://${browserUrl.host}/json/list`;
  for (let attempt = 0; attempt < 30; attempt += 1) {
    const response = await fetch(listUrl);
    if (response.ok) {
      const targets = await response.json();
      const pageTarget = targets.find((target) => target.type === "page" && target.webSocketDebuggerUrl);
      if (pageTarget) {
        return pageTarget.webSocketDebuggerUrl;
      }
    }

    await delay(100);
  }

  throw new Error(`${slug}: Chrome did not expose a page target.`);
}

export class CdpClient {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    this.eventWaiters = [];
    this.socket.addEventListener("message", (event) => {
      void this.handleMessage(event.data);
    });
    this.socket.addEventListener("close", () => {
      const error = new Error("Chrome DevTools socket closed.");
      for (const waiter of this.pending.values()) {
        waiter.reject(error);
      }
      this.pending.clear();
    });
  }

  static connect(url) {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(url);
      const timer = setTimeout(() => {
        reject(new Error(`Timed out connecting to DevTools socket: ${url}`));
      }, 30000);

      socket.addEventListener("open", () => {
        clearTimeout(timer);
        resolve(new CdpClient(socket));
      });
      socket.addEventListener("error", () => {
        clearTimeout(timer);
        reject(new Error(`Failed to connect to DevTools socket: ${url}`));
      });
    });
  }

  async handleMessage(data) {
    const text = await readWebSocketData(data);
    const message = JSON.parse(text);
    if (message.id && this.pending.has(message.id)) {
      const waiter = this.pending.get(message.id);
      this.pending.delete(message.id);
      if (message.error) {
        waiter.reject(new Error(`${message.error.message || "CDP error"}: ${message.error.data || ""}`));
      } else {
        waiter.resolve(message.result || {});
      }
      return;
    }

    if (message.method) {
      for (const waiter of [...this.eventWaiters]) {
        if (waiter.method === message.method && waiter.predicate(message.params || {})) {
          this.eventWaiters = this.eventWaiters.filter((item) => item !== waiter);
          clearTimeout(waiter.timer);
          waiter.resolve(message.params || {});
        }
      }
    }
  }

  send(method, params = {}, sessionId = undefined) {
    const id = this.nextId;
    this.nextId += 1;
    const message = { id, method, params };
    if (sessionId) {
      message.sessionId = sessionId;
    }

    const payload = JSON.stringify(message);
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.socket.send(payload);
    });
  }

  waitForEvent(method, predicate = () => true, timeoutMs = 30000) {
    return new Promise((resolve, reject) => {
      const waiter = {
        method,
        predicate,
        resolve,
        reject,
        timer: setTimeout(() => {
          this.eventWaiters = this.eventWaiters.filter((item) => item !== waiter);
          reject(new Error(`Timed out waiting for DevTools event: ${method}`));
        }, timeoutMs),
      };
      this.eventWaiters.push(waiter);
    });
  }

  close() {
    try {
      this.socket.close();
    } catch {
      // Socket may already be closed.
    }
  }
}

async function readWebSocketData(data) {
  if (typeof data === "string") {
    return data;
  }

  if (data instanceof ArrayBuffer) {
    return Buffer.from(data).toString("utf8");
  }

  if (ArrayBuffer.isView(data)) {
    return Buffer.from(data.buffer, data.byteOffset, data.byteLength).toString("utf8");
  }

  if (data && typeof data.arrayBuffer === "function") {
    return Buffer.from(await data.arrayBuffer()).toString("utf8");
  }

  throw new Error("Unsupported WebSocket message payload.");
}

export async function closeChrome(browserWebSocketUrl, child) {
  if (browserWebSocketUrl) {
    try {
      const browser = await CdpClient.connect(browserWebSocketUrl);
      try {
        await browser.send("Browser.close");
      } finally {
        browser.close();
      }
    } catch {
      // Fall back to killing the spawned process below.
    }
  }

  const exited = await waitForProcessExit(child, 5000);
  if (!exited && !child.killed) {
    child.kill();
    await waitForProcessExit(child, 3000);
  }
}

function waitForProcessExit(child, timeoutMs) {
  if (child.exitCode !== null || child.signalCode !== null) {
    return Promise.resolve(true);
  }

  return new Promise((resolve) => {
    const timer = setTimeout(() => {
      child.off("exit", onExit);
      resolve(false);
    }, timeoutMs);
    function onExit() {
      clearTimeout(timer);
      resolve(true);
    }
    child.once("exit", onExit);
  });
}

export function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
