import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";
const root = path.dirname(fileURLToPath(import.meta.url));
const output = path.join(root, "dist");
const files = ["index.html", "privacy.html", "styles.css", "app.js", "site.config.js", "links.js", "_headers", "robots.txt"];
await fs.mkdir(output, { recursive: true });
for (const file of files) await fs.copyFile(path.join(root, file), path.join(output, file));
// Publish only approved assets. The owner supplies application screenshots later.
const assetOutput = path.join(output, "assets");
if (path.dirname(assetOutput) !== output || path.basename(assetOutput) !== "assets") throw new Error("Invalid asset output directory");
await fs.rm(assetOutput, { recursive: true, force: true });
await fs.mkdir(assetOutput, { recursive: true });
await fs.copyFile(path.join(root, "assets/favicon.svg"), path.join(assetOutput, "favicon.svg"));
const sandbox = { window: {} };
vm.runInNewContext(await fs.readFile(path.join(root, "site.config.js"), "utf8"), sandbox);
for (const source of new Set(Object.values(sandbox.window.ISLAND_SITE.screenshots || {}))) {
  if (!source) continue;
  if (!/^\.\/assets\/[a-zA-Z0-9_./-]+\.(png|webp|jpe?g)$/i.test(source) || source.includes("..")) throw new Error("Invalid screenshot path");
  const destination = path.resolve(output, source);
  await fs.mkdir(path.dirname(destination), { recursive: true });
  await fs.copyFile(path.resolve(root, source), destination);
}
await fs.copyFile(path.join(root, "..", "LICENSE"), path.join(output, "LICENSE.txt"));
await fs.copyFile(path.join(root, "..", "LICENSE"), path.join(root, "LICENSE.txt"));
console.log("Static website ready: " + output);
console.log("No network connection, dependency install or cloud deployment was performed.");
