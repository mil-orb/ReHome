// Regenerates the README screenshots in docs/images from the dev UI and its fixture scan.
// npm run screenshots   (from src/Rehome.App/ui)
//
// Starts Vite on a spare port, drives headless Microsoft Edge over the DevTools protocol, and saves light and dark
// PNGs at 2x. Uses Node's built-in WebSocket and the Edge that ships with Windows, so it adds no packages.
// Everything shown is fixture data and the simulated install run. Nothing touches winget.
import { spawn } from "node:child_process";
import { existsSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";

const ui = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const out = resolve(ui, "../../../docs/images");
const W = 760, H = 820, SCALE = 2, DEBUG_PORT = 9333; // the app window's default client size
const edge = [
  "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
  "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe",
].find(existsSync);
if (!edge) throw new Error("Microsoft Edge not found. It ships with Windows; reinstall it from microsoft.com/edge.");

mkdirSync(out, { recursive: true });
const server = await createServer({ root: ui, logLevel: "error", server: { port: 5299, strictPort: false } });
await server.listen();
const url = server.resolvedUrls.local[0];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function capture(scheme) {
  const profile = join(tmpdir(), `rehome-screenshots-${process.pid}`);
  const browser = spawn(edge, [
    "--headless=new", `--remote-debugging-port=${DEBUG_PORT}`, `--user-data-dir=${profile}`,
    "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--hide-scrollbars",
    `--window-size=${W},${H}`, "about:blank",
  ], { stdio: "ignore" });

  try {
    let target;
    for (let i = 0; i < 50 && !target; i++) {
      await sleep(200);
      try { target = (await (await fetch(`http://127.0.0.1:${DEBUG_PORT}/json/list`)).json()).find((t) => t.type === "page"); } catch {}
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
    const click = (label) => js(`(() => {
      const b = [...document.querySelectorAll("button")].find((b) => b.textContent.trim().startsWith(${JSON.stringify(label)}));
      if (!b) throw new Error("No button starting with " + ${JSON.stringify(label)});
      b.click();
    })()`);
    const until = async (expression, ms) => { for (let t = 0; t < ms && !(await js(expression)); t += 250) await sleep(250); };
    const suffix = scheme === "dark" ? "-dark" : "";
    const shot = async (name, clip) => {
      await sleep(450); // dialogs and rows animate in
      const r = await send("Page.captureScreenshot", { format: "png", ...(clip && { clip: { ...clip, scale: 1 } }) });
      writeFileSync(join(out, `${name}${suffix}.png`), Buffer.from(r.result.data, "base64"));
      console.log(`  ${name}${suffix}.png`);
    };

    await send("Page.enable");
    await send("Emulation.setDeviceMetricsOverride", { width: W, height: H, deviceScaleFactor: SCALE, mobile: false });
    await send("Emulation.setEmulatedMedia", { features: [{ name: "prefers-color-scheme", value: scheme }] });

    // Old PC: start, scan, export.
    await send("Page.navigate", { url });
    await until(`!!document.querySelector(".start")`, 10_000);
    await shot("start", { x: 0, y: 180, width: W, height: 470 });
    await click("Scan this PC");
    await until(`/apps?$/.test(document.querySelector("h1")?.textContent ?? "")`, 10_000);
    await shot("scan");
    await click("Export for new PC");
    await until(`!!document.querySelector("[role=dialog]")`, 10_000);
    await shot("export");

    // New PC: restore, the review question for an unsigned installer, the finished run.
    await send("Page.navigate", { url });
    await until(`!!document.querySelector(".start")`, 10_000);
    await click("Restore from a scan");
    await sleep(1_500); // the "already here" check
    await shot("restore");
    await click("Install");
    await until(`!!document.querySelector("[role=dialog]")`, 15_000);
    await shot("review");
    await click("Skip");
    await until(`!/Checking|Installing/.test(document.querySelector("h1")?.textContent ?? "")`, 60_000);
    await shot("done");

    ws.close();
  } finally {
    browser.kill();
    await sleep(500);
    rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
  }
}

try {
  console.log(`Writing to ${out}`);
  for (const scheme of ["light", "dark"]) await capture(scheme);
} finally {
  await server.close();
}
