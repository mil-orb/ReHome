// Headless Microsoft Edge over the DevTools protocol, for the screenshot and icon scripts.
// Uses the Edge that ships with Windows and Node's built-in WebSocket, so it adds no packages.
import { spawn } from "node:child_process";
import { existsSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const edgePath = [
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
].find(existsSync);

/** Starts a fresh headless Edge with its own throwaway profile. Call close() when done. */
export async function openEdge({ width = 800, height = 600, port = 9333 } = {}) {
  if (!edgePath) throw new Error("Microsoft Edge not found. It ships with Windows; reinstall it from microsoft.com/edge.");
  const profile = join(tmpdir(), `rehome-edge-${process.pid}-${port}`);
  const browser = spawn(edgePath, [
    "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--hide-scrollbars",
    `--window-size=${width},${height}`, "about:blank",
  ], { stdio: "ignore" });

  const close = async () => {
    browser.kill();
    await sleep(500);
    rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  };

  try {
    let target;
    for (let i = 0; i < 50 && !target; i++) {
      await sleep(200);
      try { target = (await (await fetch(`http://127.0.0.1:${port}/json/list`)).json()).find((t) => t.type === "page"); } catch {}
    }
    if (!target) throw new Error("Edge didn't open its debugging port.");

    const ws = new WebSocket(target.webSocketDebuggerUrl);
    await new Promise((r) => ws.addEventListener("open", r, { once: true }));
    let id = 0;
    const pending = new Map();
    ws.addEventListener("message", (m) => {
      const msg = JSON.parse(m.data);
      pending.get(msg.id)?.(msg);
      pending.delete(msg.id);
    });
    const send = (method, params = {}) => new Promise((r) => { pending.set(++id, r); ws.send(JSON.stringify({ id, method, params })); });
    const js = async (expression) => {
      const r = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
      if (r.result?.exceptionDetails) throw new Error(r.result.exceptionDetails.exception?.description ?? "script failed");
      return r.result?.result?.value;
    };
    return { send, js, close: async () => { ws.close(); await close(); } };
  } catch (e) {
    await close();
    throw e;
  }
}
