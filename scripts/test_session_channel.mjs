import assert from "node:assert/strict";
import { isCurrentSession, openSessionChannel, shouldAcceptSessionUpdate } from "../apps/export-doc-web/src/app/sessionChannel.ts";

const session = (accessToken, userId = 7, expiresAt = "2026-09-07T01:00:00Z") => ({
  accessToken, user: { id: userId, capabilities: {} }, apiBaseUrl: "https://app.example.test", expiresAt,
});
const current = session("current");
const updated = session("renewed", 7, "2026-09-07T02:00:00Z");
const renewal = { type: "session-updated", session: updated, previousAccessToken: "current" };
assert(shouldAcceptSessionUpdate(current, renewal), "a matching renewal should preserve the active user's workspace");
assert(!shouldAcceptSessionUpdate(null, renewal), "a delayed renewal must not log a signed-out tab back in");
assert(!shouldAcceptSessionUpdate(updated, renewal), "an already superseded renewal must be ignored");
assert(!shouldAcceptSessionUpdate(session("current", 8), renewal), "a different user's draft must not change ownership");
assert(!shouldAcceptSessionUpdate({ ...current, apiBaseUrl: "https://other.example.test" }, renewal));
assert(shouldAcceptSessionUpdate(null, { ...renewal, previousAccessToken: null }), "login can reach an idle login tab");
assert(shouldAcceptSessionUpdate(current, { ...renewal, previousAccessToken: null }), "same-user login can unlock an expired draft");
assert(!shouldAcceptSessionUpdate(updated, { ...renewal, session: current, previousAccessToken: null }), "a late login must not roll back a newer session");
assert(isCurrentSession(null, null));
assert(isCurrentSession(current, { ...current, user: { ...current.user } }));
assert(!isCurrentSession(null, current));
assert(!isCurrentSession(updated, current));
assert(!isCurrentSession({ ...current, apiBaseUrl: "https://other.example.test" }, current));

const OriginalChannel = globalThis.BroadcastChannel;
let receive;
let closed = false;
let posted;
globalThis.BroadcastChannel = class {
  addEventListener(_, handler) { receive = handler; }
  removeEventListener(_, handler) { assert.equal(handler, receive); }
  postMessage(message) { posted = message; }
  close() { closed = true; }
};
try {
  const received = [];
  const channel = openSessionChannel((event) => received.push(event));
  for (const invalid of [null, {}, { ...renewal, previousAccessToken: undefined },
    { ...renewal, session: { ...updated, user: {} } },
    { ...renewal, session: { ...updated, expiresAt: "invalid" } },
    { type: "session-cleared", apiBaseUrl: current.apiBaseUrl },
  ]) receive({ data: invalid });
  assert.equal(received.length, 0, "malformed or unscoped session events must be ignored");
  receive({ data: renewal });
  receive({ data: { type: "session-cleared", apiBaseUrl: current.apiBaseUrl, accessToken: current.accessToken } });
  assert.equal(received.length, 2);
  channel.post(renewal);
  assert.equal(posted, renewal);
  channel.close();
  assert(closed);
} finally {
  globalThis.BroadcastChannel = OriginalChannel;
}
process.stdout.write("session channel contracts passed\n");
