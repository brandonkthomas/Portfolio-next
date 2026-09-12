import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";

// Keep host-specific deployment material in the private deployment workspace.
const allowedDeploymentFile = "deploy/Dockerfile";
const thisFile = "tools/check-public-tree.mjs";
const privateAddress = /(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])/g;
const privatePath = /\/(?:Users|home|srv|volume\d+)\//g;
const deviceName = /\b(?:turul|karura|synology|tailscale|unifi|cloudnet|mmcblk\d*|raspberry)\b/gi;
const tracked = execFileSync("git", ["ls-files", "-z"], { encoding: "utf8" })
  .split("\0")
  .filter(Boolean);
const findings = [];

for (const file of tracked) {
  if (file.startsWith("deploy/") && file !== allowedDeploymentFile) {
    findings.push(`${file}: deployment file is tracked`);
  }
  if (file === thisFile) continue;

  const content = readFileSync(file);
  if (content.includes(0)) continue;
  const lines = content.toString("utf8").split(/\r?\n/);

  for (const [index, line] of lines.entries()) {
    for (const match of line.matchAll(privateAddress)) {
      if (match[0] !== "127.0.0.1" && match[0] !== "0.0.0.0") {
        findings.push(`${file}:${index + 1}: non-loopback IP address`);
      }
    }
    if (privatePath.test(line)) findings.push(`${file}:${index + 1}: host filesystem path`);
    if (deviceName.test(line)) findings.push(`${file}:${index + 1}: device or private network name`);
    privatePath.lastIndex = 0;
    deviceName.lastIndex = 0;
  }
}

if (findings.length > 0) {
  process.stderr.write(`${findings.join("\n")}\n`);
  process.exitCode = 1;
} else {
  process.stdout.write("Tracked tree has no known private deployment details.\n");
}
