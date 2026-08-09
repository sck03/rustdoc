#!/bin/sh
set -eu

browser_pid=""
proxy_pid=""

stop_runtime() {
    trap - INT TERM HUP
    if [ -n "$proxy_pid" ] && kill -0 "$proxy_pid" 2>/dev/null; then
        kill -TERM "$proxy_pid" 2>/dev/null || true
    fi
    if [ -n "$browser_pid" ] && kill -0 "$browser_pid" 2>/dev/null; then
        kill -TERM "$browser_pid" 2>/dev/null || true
    fi
    if [ -n "$proxy_pid" ]; then
        wait "$proxy_pid" 2>/dev/null || true
    fi
    if [ -n "$browser_pid" ]; then
        wait "$browser_pid" 2>/dev/null || true
    fi
}

trap 'stop_runtime; exit 143' INT TERM HUP

/usr/bin/chromium "$@" &
browser_pid=$!

# Current Chromium releases bind the DevTools listener to the container loopback interface.
# Keep that listener private and expose only a byte-for-byte CDP bridge to the
# dedicated Compose browser network. The API resolves the service name to its
# private container address before discovery, avoiding Chromium's Host-header
# protection and rewriting the discovered WebSocket authority safely.
/usr/bin/socat \
    TCP-LISTEN:9222,bind=0.0.0.0,reuseaddr,fork \
    TCP:127.0.0.1:9223 &
proxy_pid=$!

while kill -0 "$browser_pid" 2>/dev/null && kill -0 "$proxy_pid" 2>/dev/null; do
    sleep 1 &
    wait $! || true
done

exit_code=1
if ! kill -0 "$browser_pid" 2>/dev/null; then
    wait "$browser_pid" || exit_code=$?
elif ! kill -0 "$proxy_pid" 2>/dev/null; then
    wait "$proxy_pid" || exit_code=$?
fi

stop_runtime
exit "$exit_code"
