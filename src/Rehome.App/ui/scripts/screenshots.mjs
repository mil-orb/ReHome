// Regenerates the README screenshots in docs/images from the dev UI and its fixture scan.
// npm run screenshots   (from src/Rehome.App/ui)
//
// Starts Vite on a spare port, drives headless Microsoft Edge (scripts/edge.mjs), and saves light and dark PNGs at 2x.
// Everything shown is fixture data and the simulated install run. Nothing touches winget.
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createServer } from "vite";
import { openEdge, sleep } from "./edge.mjs";

const ui = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const out = resolve(ui, "../../../docs/images");
const W = 760, H = 820, SCALE = 2, DEBUG_PORT = 9333; // the app window's default client size
mkdirSync(out, { recursive: true });
const server = await createServer({ root: ui, logLevel: "error", server: { port: 5299, strictPort: false } });
await server.listen();
const url = server.resolvedUrls.local[0];

async function capture(scheme) {
  const { send, js, close } = await openEdge({ width: W, height: H, port: DEBUG_PORT });
  try {
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
    // Just the content, measured, with a margin: the window around it is mostly empty space.
    const box = await js(`(() => {
      const a = document.querySelector(".intro").getBoundingClientRect(), b = document.querySelector(".start .fine").getBoundingClientRect();
      return { y: Math.max(0, a.top - 40), height: b.bottom - a.top + 80 };
    })()`);
    await shot("start", { x: 0, y: box.y, width: W, height: box.height });
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

  } finally {
    await close();
  }
}

try {
  console.log(`Writing to ${out}`);
  for (const scheme of ["light", "dark"]) await capture(scheme);
} finally {
  await server.close();
}
