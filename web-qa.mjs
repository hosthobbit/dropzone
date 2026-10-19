// GPU-free smoke test of the WebGL build: headless Chromium with SwiftShader (CPU WebGL),
// driven over the DevTools protocol with Node's built-in WebSocket. No npm packages needed.
// Usage: [MODE=autoplay|mobile] node web-qa.mjs "http://localhost:PORT/" <screenshot-dir>
import { spawn } from "node:child_process";
import { mkdtempSync, writeFileSync, mkdirSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const [url, outDir] = process.argv.slice(2);
if (!url || !outDir) { console.error("usage: node web-qa.mjs <url> <dir>"); process.exit(1); }
mkdirSync(outDir, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const port = 9300 + Math.floor(Math.random() * 500);
const profile = mkdtempSync(join(tmpdir(), "dz-chrome-"));

const env = { ...process.env };
delete env.WAYLAND_DISPLAY; delete env.DISPLAY;
// keep every graphics library away from the NVIDIA driver
env.__EGL_VENDOR_LIBRARY_FILENAMES = "/usr/share/glvnd/egl_vendor.d/50_mesa.json";
env.__GLX_VENDOR_LIBRARY_NAME = "mesa";
if (process.env.LVP_ICD) env.VK_DRIVER_FILES = env.VK_ICD_FILENAMES = process.env.LVP_ICD;

const chrome = spawn(process.env.CHROME || "chromium", [
  "--headless=new", `--remote-debugging-port=${port}`, `--user-data-dir=${profile}`,
  "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--ignore-gpu-blocklist",
  "--disable-vulkan", "--mute-audio", "--autoplay-policy=no-user-gesture-required",
  "--window-size=1280,720", "--no-first-run", "--no-default-browser-check", "about:blank",
], { env, stdio: ["ignore", "ignore", "pipe"] });
let chromeErr = "";
chrome.stderr.on("data", (d) => { chromeErr += d; });

let ws, nextId = 1;
const pending = new Map();
const logs = [];
function send(method, params = {}, sessionId) {
  const id = nextId++;
  ws.send(JSON.stringify({ id, method, params, sessionId }));
  return new Promise((res, rej) => pending.set(id, { res, rej }));
}

async function main() {
  let ver;
  for (let i = 0; i < 50 && !ver; i++) {
    try { ver = await (await fetch(`http://127.0.0.1:${port}/json/version`)).json(); } catch { await sleep(200); }
  }
  if (!ver) throw new Error("chromium did not start: " + chromeErr.slice(0, 400));
  ws = new WebSocket(ver.webSocketDebuggerUrl);
  await new Promise((r) => ws.addEventListener("open", r));
  ws.addEventListener("message", (ev) => {
    const m = JSON.parse(ev.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      m.error ? p.rej(new Error(m.error.message)) : p.res(m.result);
    } else if (m.method === "Runtime.consoleAPICalled") {
      logs.push(`[${m.params.type}] ` + m.params.args.map((a) => a.value ?? a.description ?? "").join(" "));
    } else if (m.method === "Runtime.exceptionThrown") {
      logs.push("[exception] " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    }
  });
  const { targetId } = await send("Target.createTarget", { url: "about:blank" });
  const { sessionId } = await send("Target.attachToTarget", { targetId, flatten: true });
  const s = (m, p) => send(m, p, sessionId);
  await s("Runtime.enable"); await s("Page.enable");
  await s("Emulation.setDeviceMetricsOverride", { width: 1280, height: 720, deviceScaleFactor: 1, mobile: false });
  const evalJs = async (expr) => (await s("Runtime.evaluate", { expression: expr, returnByValue: true, awaitPromise: true })).result.value;
  const shot = async (name) => {
    const { data } = await s("Page.captureScreenshot", { format: "png" });
    writeFileSync(join(outDir, name + ".png"), Buffer.from(data, "base64"));
    console.log("shot", name);
  };
  const key = async (type, code, keyName, vk, text) =>
    s("Input.dispatchKeyEvent", { type, code, key: keyName, windowsVirtualKeyCode: vk, nativeVirtualKeyCode: vk, text });

  const mode = process.env.MODE || "autoplay";
  const t0 = Date.now();
  const sendMessage = (cmd) => evalJs(`window.unityInstance.SendMessage("QA", "WebCommand", "${cmd}")`);
  const lastLog = (re) => { const m = logs.filter((l) => re.test(l)); return m.length ? m[m.length - 1] : null; };
  const touch = async (x, y) => {
    await s("Input.dispatchTouchEvent", { type: "touchStart", touchPoints: [{ x, y }] });
    await sleep(90);
    await s("Input.dispatchTouchEvent", { type: "touchEnd", touchPoints: [] });
    await sleep(400);
  };
  const waitUnity = async () => {
    let ready = false;
    for (let i = 0; i < 240 && !ready; i++) { ready = await evalJs("!!window.unityInstance"); if (!ready) await sleep(1000); }
    console.log("unity ready:", ready, "after", Math.round((Date.now() - t0) / 1000), "s");
    if (!ready) { await shot("99_stuck"); throw new Error("Unity never started"); }
  };

  if (mode === "mobile") {
    // a phone in portrait: touch only, no keyboard
    await s("Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
    await s("Emulation.setTouchEmulationEnabled", { enabled: true, maxTouchPoints: 5 });
    await s("Emulation.setUserAgentOverride", { userAgent: "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Mobile Safari/537.36" });
    await s("Page.navigate", { url });
    await sleep(1500);
    await shot("m00_loading");
    await waitUnity();
    await sleep(8000);
    await shot("m01_title_portrait");
    await sendMessage("solo");
    await sleep(9000);
    await sendMessage("cells");
    await sleep(1500);
    const cols = lastLog(/DZ_COLS/);
    console.log(cols);
    const [, dims, list] = cols.match(/DZ_COLS (\d+x\d+) (.*)/);
    const [cw, ch] = dims.split("x").map(Number);
    const cssW = await evalJs("document.querySelector('#unity-canvas').clientWidth");
    const cssH = await evalJs("document.querySelector('#unity-canvas').clientHeight");
    const pts = list.split(";").filter(Boolean).map((p) => p.split(",").map(Number)).map(([x, y]) => [x * cssW / cw, (ch - y) * cssH / ch]);
    await shot("m02_match_portrait");
    // tap column 2 once (preview), then again (drop)
    const [x, y] = pts[2];
    await touch(x, y);
    await sendMessage("state"); await sleep(800);
    const after1 = lastLog(/DZ_STATE/);
    console.log("after first tap:", after1);
    await touch(x, y);
    await sleep(500);
    await sendMessage("state"); await sleep(800);
    const after2 = lastLog(/DZ_STATE/);
    console.log("after second tap:", after2);
    const pass1 = /armed=2/.test(after1) && /board=(\.{7}\/){5}\.{7}/.test(after1);
    console.log(`DZ_WEB touch_first_tap_previews ${pass1 ? "PASS" : "FAIL"}`);
    console.log(`DZ_WEB touch_second_tap_drops ${/commits=[1-9]/.test(after2) ? "PASS" : "FAIL"}`);
    await sleep(2500);
    await shot("m03_after_drop_portrait");
    // battle HUD in portrait, then a landscape phone
    await sendMessage("battle");
    await sleep(9000);
    await shot("m04_battle_portrait");
    await s("Emulation.setDeviceMetricsOverride", { width: 844, height: 390, deviceScaleFactor: 2, mobile: true });
    await sleep(4000);
    await shot("m05_battle_landscape_phone");
    return;
  }

  await s("Page.navigate", { url: url + (url.includes("?") ? "&" : "?") + "autoplay" });
  await sleep(1500);
  await shot("00_loading");
  await waitUnity();
  const limit = Number(process.env.LIMIT_S || 900);
  for (let i = 0; ; i++) {
    await sleep(20000);
    await shot(`01_run_${String(i).padStart(2, "0")}`);
    const done = logs.filter((l) => /DZ_MATCH/.test(l));
    if (done.length) console.log(done.map((l) => l.slice(0, 400)).join("\n"));
    if (done.length >= Number(process.env.MATCHES || 1) || (Date.now() - t0) / 1000 > limit) break;
  }
  const fps = await evalJs(`new Promise(r => { let n = 0; const t = performance.now(); (function f() { n++; if (performance.now() - t < 3000) requestAnimationFrame(f); else r(Math.round(n / 3)); })(); })`).catch(() => null);
  console.log("page rAF fps (software rendering):", fps);
  logs.filter((l) => /DZ_/.test(l)).forEach((l) => console.log(l.slice(0, 600)));
}

main()
  .then(() => { console.log("RESULT ok"); })
  .catch((e) => { console.log("RESULT fail:", e.message); process.exitCode = 1; })
  .finally(() => {
    const bad = logs.filter((l) => /exception|error/i.test(l));
    console.log(`console lines: ${logs.length}, errors: ${bad.length}`);
    bad.slice(0, 15).forEach((l) => console.log("  ", l.slice(0, 300)));
    writeFileSync(join(outDir, "console.log"), logs.join("\n"));
    chrome.kill("SIGTERM");
  });
