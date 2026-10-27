import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import assert from "node:assert/strict";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const read = (file) => fs.readFileSync(path.join(root, file), "utf8");
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; console.log("PASS " + label); }
const sandbox = { window: {}, URL };
vm.runInNewContext(read("site.config.js"), sandbox);
vm.runInNewContext(read("links.js"), sandbox);
const resolve = sandbox.window.ISLAND_RESOLVE_LINKS;
const config = sandbox.window.ISLAND_SITE;
check(config.version === "0.10.22" && resolve(config).repository === "https://github.com/yosumi-maya/ISLAND-Launcher", "Published project and current version point to the owner's new launcher repository");
check(resolve({}).download === null && resolve({}).repository === null, "Empty configuration creates no active GitHub or download destination");
for (const bad of ["javascript:alert(1)", "http://github.com/owner/project", "https://github.com.evil.example/owner/project", "https://github.com/owner", "https://user:secret@github.com/owner/project", "https://example.com/owner/project"]) {
  check(resolve({ githubRepoUrl: bad, githubReleaseUrl: bad }).download === null, "Invalid / non-GitHub destinations cannot activate download: " + bad.replace("user:secret@", ""));
}
const sample = resolve({ githubRepoUrl: "https://github.com/example-owner/example-launcher.git" });
check(sample.repository === "https://github.com/example-owner/example-launcher" && sample.download === "https://github.com/example-owner/example-launcher/releases/latest", "A future GitHub repository enables its own latest-release page");
const explicit = resolve({ githubReleaseUrl: "https://github.com/example-owner/example-launcher/releases/tag/v1.0" });
check(explicit.repository === "https://github.com/example-owner/example-launcher" && explicit.download.endsWith("/releases/tag/v1.0"), "An explicit future release URL retains its target and supplies the correct repository");
check(resolve({ githubRepoUrl: "https://github.com/example-owner/example-launcher", contactUrl: "javascript:alert(1)" }).contact === "https://github.com/example-owner/example-launcher", "Invalid contact URLs safely fall back to the project page");
const app = read("app.js");
const englishMatch = app.match(/var en = ([\s\S]*?);\s*Object\.assign\(en,/);
assert.ok(englishMatch);
const english = vm.runInNewContext("(" + englishMatch[1] + ")");
const overrideMatch = app.match(/Object\.assign\(en, ([\s\S]*?)\);\s*var dynamic =/);
assert.ok(overrideMatch);
Object.assign(english, JSON.parse(overrideMatch[1]));
for (const name of ["index.html", "privacy.html"]) {
  const html = read(name);
  const keys = [...html.matchAll(/data-i18n(?:-aria|-alt)?="([^"]+)"/g)].map(match => match[1]);
  check(keys.every(key => typeof english[key] === "string" && english[key].length), name + " has complete English text, accessible names and image descriptions");
  check([...html.matchAll(/<[^>]+data-i18n-(aria|alt)="[^"]+"[^>]*>/g)].every(match => new RegExp('\\b' + (match[1] === 'aria' ? 'aria-label' : 'alt') + '="[^\"]+"').test(match[0])), name + " translated accessible names retain a nonempty default-language label");
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  check(new Set(ids).size === ids.length, name + " contains no duplicate element IDs");
  for (const match of html.matchAll(/\b(?:href|src)="([^"]+)"/g)) {
    const value = match[1];
    if (value.startsWith("#")) { assert.ok(ids.includes(value.slice(1)), "Missing anchor " + value); continue; }
    if (/^(https?:|data:)/.test(value)) continue;
    assert.ok(fs.existsSync(path.join(root, value.split(/[?#]/)[0])), "Missing local resource: " + value);
  }
  check(true, name + " internal links and local resources all resolve");
  check(!/<(?:script|link|img)\b[^>]*(?:src|href)="https?:/i.test(html), name + " loads no third-party scripts, styles, fonts or images");
  check(!/<input|<form|<iframe/i.test(html), name + " has no account, password, upload or embedded third-party form");
}
check(!/localStorage|sessionStorage|document\.cookie|\bfetch\s*\(|XMLHttpRequest/.test(app), "The website performs no tracking storage or API requests");
check(/prefers-reduced-motion:reduce/.test(read("styles.css")) && app.includes("prefers-reduced-motion: reduce"), "Pointer effects honor the visitor's reduced-motion preference");
check(Object.values(config.screenshots).every(value => value === ""), "Screenshot slots remain empty pending owner-supplied images");
check(read("index.html").includes('id="main-placeholder"') && !/id="main-screenshot"[^>]*\bsrc=/.test(read("index.html")), "Missing screenshots show an explicit placeholder without broken-image requests");
check(app.includes('mainImage.onerror') && app.includes('screenshotRevision'), "Failed and stale image loads preserve the placeholder");
check(read("_headers").includes("script-src 'self'") && read("_headers").includes("frame-ancestors 'none'"), "Cloudflare static headers restrict scripts and framing");
console.log("ALL " + checks + " STATIC WEBSITE CHECKS PASSED");
