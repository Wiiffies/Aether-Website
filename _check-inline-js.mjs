// Syntax-check every inline <script> block in the given HTML files.
// Usage: node _check-inline-js.mjs website.html discord-bot.html ...
import { readFileSync } from "node:fs";
import vm from "node:vm";

let failures = 0;
for (const file of process.argv.slice(2)) {
  const html = readFileSync(file, "utf8");
  const blocks = [...html.matchAll(/<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi)].map((m) => m[1]);
  let bad = 0;
  blocks.forEach((code, i) => {
    try {
      new vm.Script(code, { filename: `${file}#script${i + 1}` });
    } catch (e) {
      bad++;
      failures++;
      console.log(`FAIL ${file} inline script #${i + 1}: ${e.message}`);
    }
  });
  console.log(`${bad === 0 ? "ok  " : "BAD "} ${file.padEnd(22)} ${blocks.length} inline script block(s)`);
}
console.log(failures === 0 ? "\nAll inline scripts parse." : `\n${failures} block(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
