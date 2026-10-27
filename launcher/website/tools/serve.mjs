import http from "node:http";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
const website = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const root = path.join(website, "dist");
const port = Number(process.argv[2] || 4183);
const types = { ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".png": "image/png", ".svg": "image/svg+xml", ".txt": "text/plain; charset=utf-8" };
const server = http.createServer(async (request, response) => {
  try {
    let pathname = decodeURIComponent(new URL(request.url, "http://127.0.0.1").pathname);
    if (pathname.endsWith("/")) pathname += "index.html";
    const file = path.resolve(root, "." + pathname);
    if (!file.startsWith(root + path.sep) || pathname.includes("\\") || path.basename(file).startsWith(".")) {
      response.writeHead(403); response.end("Forbidden"); return;
    }
    const data = await fs.readFile(file);
    response.writeHead(200, {
      "Content-Type": types[path.extname(file)] || "application/octet-stream",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "Content-Security-Policy": "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'none'"
    });
    response.end(data);
  } catch (error) {
    response.writeHead(error.code === "ENOENT" ? 404 : 400, { "Content-Type": "text/plain; charset=utf-8" });
    response.end(error.code === "ENOENT" ? "Page not found" : "Invalid request");
  }
});
server.listen(port, "127.0.0.1", () => console.log("Local: http://127.0.0.1:" + port + "/"));
