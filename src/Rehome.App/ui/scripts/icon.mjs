// Builds Rehome.ico from assets/logo.svg and assets/logo-small.svg.
// npm run icon   (from src/Rehome.App/ui)
//
// Headless Edge (scripts/edge.mjs) draws each size on a canvas, so small sizes are rendered at that size instead
// of scaled down from 256. The .ico holds PNG frames, which Windows has read since Vista.
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { openEdge } from "./edge.mjs";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "../../../..");
const big = readFileSync(resolve(root, "assets/logo.svg"), "utf8");
const small = readFileSync(resolve(root, "assets/logo-small.svg"), "utf8");
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const targets = ["src/Rehome.App/Rehome.ico", "src/Rehome/Rehome.ico"];

const { js, close } = await openEdge({ port: 9334 });
const frames = [];
try {
  for (const size of sizes) {
    const svg = size <= 24 ? small : big;
    const url = "data:image/svg+xml;base64," + Buffer.from(svg).toString("base64");
    const png = await js(`new Promise((done, fail) => {
      const img = new Image(${size}, ${size});
      img.onload = () => {
        const c = document.createElement("canvas");
        c.width = c.height = ${size};
        c.getContext("2d").drawImage(img, 0, 0, ${size}, ${size});
        done(c.toDataURL("image/png").split(",")[1]);
      };
      img.onerror = () => fail(new Error("couldn't draw the logo"));
      img.src = ${JSON.stringify(url)};
    })`);
    frames.push({ size, data: Buffer.from(png, "base64") });
  }
} finally {
  await close();
}

// ICONDIR, then one ICONDIRENTRY per frame, then the PNGs.
const head = Buffer.alloc(6 + 16 * frames.length);
head.writeUInt16LE(0, 0);
head.writeUInt16LE(1, 2);
head.writeUInt16LE(frames.length, 4);
let offset = head.length;
frames.forEach(({ size, data }, i) => {
  const at = 6 + 16 * i;
  head.writeUInt8(size >= 256 ? 0 : size, at);
  head.writeUInt8(size >= 256 ? 0 : size, at + 1);
  head.writeUInt16LE(1, at + 4); // planes
  head.writeUInt16LE(32, at + 6); // bits per pixel
  head.writeUInt32LE(data.length, at + 8);
  head.writeUInt32LE(offset, at + 12);
  offset += data.length;
});
const ico = Buffer.concat([head, ...frames.map((f) => f.data)]);
for (const t of targets) {
  writeFileSync(resolve(root, t), ico);
  console.log(`  ${t} (${ico.length} bytes, ${sizes.join(", ")} px)`);
}
