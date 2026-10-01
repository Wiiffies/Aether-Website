// Behavioural check of every isEmail-style validator in the project.
// Extracts the function source from each file and actually runs it, because
// escaping a broken character class is easy to miss when grepping.
import { readFileSync } from "node:fs";
import vm from "node:vm";

const files = [
  "public/checkout.js", "public/aether.js", "worker/src/index.js",
  "shop.html", "account.html", "admin.html", "website.html", "discord-bot.html",
  "contact.html", "payment-success.html", "payment-cancel.html", "index.html", "about.html",
];
// Emails that MUST pass and emails that MUST fail.
const mustPass = ["a@b.co", "wispz@outlook.de", "test@example.com", "sean.smith@service.co.uk", "x+y@sub.domain.io"];
const mustFail = ["", "nope", "no@domain", "@x.com", "spaces in@mail.com", "two@@at.com"];

let problems = 0;
for (const file of files) {
  let src;
  try { src = readFileSync(file, "utf8"); } catch { continue; }
  const hits = [...src.matchAll(/function\s+isEmail\s*\(([^)]*)\)\s*\{([\s\S]*?)\n?\s*\}/g)];
  if (!hits.length) continue;
  hits.forEach((m, i) => {
    const body = "function isEmail(" + m[1] + "){" + m[2] + "}";
    let fn;
    try { fn = vm.runInNewContext(body + "; isEmail", { String }); } catch (e) {
      console.log(`ERROR ${file} isEmail#${i + 1}: ${e.message}`); problems++; return;
    }
    const badPass = mustPass.filter((e) => fn(e) !== true);
    const badFail = mustFail.filter((e) => fn(e) !== false);
    const shown = body.replace(/\s+/g, " ").slice(0, 120);
    if (badPass.length || badFail.length) {
      problems++;
      console.log(`BROKEN ${file} isEmail#${i + 1}`);
      if (badPass.length) console.log(`   wrongly rejects: ${badPass.join(", ")}`);
      if (badFail.length) console.log(`   wrongly accepts: ${badFail.join(", ")}`);
      console.log(`   ${shown}`);
    } else {
      console.log(`ok     ${file} isEmail#${i + 1}`);
    }
  });
}
console.log(problems ? `\n${problems} broken validator(s).` : "\nAll email validators behave correctly.");
process.exit(problems ? 1 : 0);
