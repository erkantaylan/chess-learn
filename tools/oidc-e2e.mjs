// End-to-end check of the central login against a local authentik (central-login poc).
//
//   node tools/oidc-e2e.mjs oidc       app started with Auth__Oidc__* and Auth__Oidc__RecheckSeconds=5
//   node tools/oidc-e2e.mjs password   app started with Auth__Username=erkan Auth__Password=spike-pass
//
// Surviving an app restart (Data Protection keys in Postgres) — run a "save" mode, restart the
// app process, then the matching "reuse" mode. STATE is the file the browser state goes in.
//   node tools/oidc-e2e.mjs save-password | save-oidc   sign in, keep the cookie
//   node tools/oidc-e2e.mjs reuse                       the kept cookie still authenticates
//   node tools/oidc-e2e.mjs midflight-start             stop on the IdP's login page (correlation cookie set)
//   node tools/oidc-e2e.mjs midflight-finish            finish that sign-in on the restarted app
//
// Env: PLAYWRIGHT_CORE (path to playwright-core/index.mjs), CHROME (Chromium binary),
// APP, IDP, IDP_ENV (the .env holding AUTHENTIK_BOOTSTRAP_TOKEN), SHOTS (screenshot directory).
// The oidc run deactivates friend1 at the IdP and always re-activates it.
import os from "node:os";
import fs from "node:fs";

const home = os.homedir();
const central = `${home}/Desktop/projects/central-login`;
const { chromium } = await import(process.env.PLAYWRIGHT_CORE
  ?? `${central}/poc/clients/node/node_modules/playwright-core/index.mjs`);
const CHROME = process.env.CHROME ?? `${home}/.cache/ms-playwright/chromium-1248/chrome-linux64/chrome`;
const APP = process.env.APP ?? "http://repertoire.localhost:39115";
const IDP = process.env.IDP ?? "http://id.localhost:39100";
const SHOTS = process.env.SHOTS ?? os.tmpdir();
const RECHECK_MS = 5000;
const mode = process.argv[2] ?? "oidc";
const STATE = process.env.STATE ?? `${SHOTS}/repertoire-e2e-state.json`;

const browser = await chromium.launch({ executablePath: CHROME, headless: true });
let failures = 0;
function check(name, ok, detail = "") {
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? `  (${detail})` : ""}`);
  if (!ok) failures++;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function idpLogin(page, user, pass) {
  const uid = page.locator('input[name="uidField"]');
  await uid.fill(user);
  await uid.press("Enter");
  await uid.waitFor({ state: "detached" });
  const pw = page.locator('input[name="password"]');
  await pw.waitFor({ state: "visible" });
  await pw.fill(pass);
  await pw.press("Enter");
}

async function signIn(ctx, user, pass) {
  const page = await ctx.newPage();
  await page.goto(APP + "/login?next=/");
  await idpLogin(page, user, pass);
  await page.waitForURL(APP + "/", { timeout: 15000 });
  return page;
}

const status = async (ctx, path, opts) => (await ctx.request.fetch(APP + path, opts)).status();
const me = async (ctx) => (await ctx.request.get(APP + "/api/me")).json();
const authCookies = async (ctx) => (await ctx.cookies(APP)).filter((c) => c.name.startsWith("repertoire.auth"));
const cookieSummary = (cs) => `${cs.length} chunk(s), ${cs.reduce((n, c) => n + c.name.length + c.value.length + 1, 0)} B total`;
const newStudy = { name: "oidc spike", start_fen: "", tree: null, pgn: "" };

function idpAdmin() {
  const env = fs.readFileSync(process.env.IDP_ENV ?? `${central}/poc/idp/.env`, "utf8");
  const token = env.match(/^AUTHENTIK_BOOTSTRAP_TOKEN=(.*)$/m)[1].trim();
  const call = async (method, path, body) => {
    const r = await fetch(IDP + "/api/v3" + path, {
      method, body: body && JSON.stringify(body),
      headers: { Authorization: `Bearer ${token}`, "Content-Type": "application/json" },
    });
    if (!r.ok) throw new Error(`${method} ${path}: ${r.status}`);
    return r.json();
  };
  return {
    pk: async (username) => (await call("GET", `/core/users/?username=${username}`)).results[0].pk,
    setActive: (pk, active) => call("PATCH", `/core/users/${pk}/`, { is_active: active }),
  };
}

if (mode === "save-password" || mode === "save-oidc") {
  const ctx = await browser.newContext();
  if (mode === "save-oidc") {
    await signIn(ctx, "akadmin", "local-owner-pass");
  } else {
    const r = await ctx.request.post(APP + "/api/login", { data: { username: "erkan", password: "spike-pass" } });
    check("save: POST /api/login accepted", r.ok(), String(r.status()));
  }
  const m = await me(ctx);
  check("save: signed in, can save", m.authed && m.can_save === true, JSON.stringify(m));
  check("save: GET /api/studies is 200", (await status(ctx, "/api/studies")) === 200);
  await ctx.storageState({ path: STATE });
  console.log(`      cookie kept: ${cookieSummary(await authCookies(ctx))} -> ${STATE}`);
  await ctx.close();
} else if (mode === "reuse") {
  const ctx = await browser.newContext({ storageState: STATE });
  console.log(`      reusing ${cookieSummary(await authCookies(ctx))} from ${STATE}`);
  const m = await me(ctx);
  check("reuse: /api/me still authed after the restart", m.authed === true && m.can_save === true, JSON.stringify(m));
  check("reuse: GET /api/studies is 200", (await status(ctx, "/api/studies")) === 200);
  await ctx.close();
} else if (mode === "midflight-start") {
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  await page.goto(APP + "/login?next=/");
  await page.locator('input[name="uidField"]').waitFor();
  const corr = (await ctx.cookies(APP + "/signin-oidc")).filter((c) => c.name.startsWith(".AspNetCore.Correlation") || c.name.startsWith(".AspNetCore.OpenIdConnect.Nonce"));
  check("midflight: on the IdP login page with correlation + nonce cookies", corr.length >= 2, corr.map((c) => c.name.split(".").slice(0, 3).join(".")).join(", "));
  fs.writeFileSync(STATE + ".url", page.url());
  await ctx.storageState({ path: STATE });
  await ctx.close();
} else if (mode === "midflight-finish") {
  const ctx = await browser.newContext({ storageState: STATE });
  const page = await ctx.newPage();
  await page.goto(fs.readFileSync(STATE + ".url", "utf8"));
  await idpLogin(page, "akadmin", "local-owner-pass");
  const back = await page.waitForURL(APP + "/", { timeout: 15000 }).then(() => true, () => false);
  check("midflight: callback on the restarted app accepted the correlation cookie", back, page.url());
  const m = await me(ctx);
  check("midflight: signed in as akadmin", m.authed && m.user === "akadmin", JSON.stringify(m));
  await ctx.close();
} else if (mode === "oidc") {
  // (d) anonymous
  {
    const ctx = await browser.newContext();
    check("anonymous: GET /api/studies is 401", (await status(ctx, "/api/studies")) === 401);
    check("anonymous: /api/me not authed", (await me(ctx)).authed === false);
    await ctx.close();
  }

  // (b) akadmin: user + guest, full access; survives re-checks; (h) logout ends the IdP session
  {
    const ctx = await browser.newContext();
    const page = await signIn(ctx, "akadmin", "local-owner-pass");
    const m = await me(ctx);
    check("akadmin: /api/me shows user + roles, can_save",
      m.authed && m.user === "akadmin" && m.roles.includes("user") && m.roles.includes("guest") && m.can_save === true,
      JSON.stringify(m));
    check("akadmin: GET /api/studies is 200", (await status(ctx, "/api/studies")) === 200);
    check("akadmin: POST /api/studies is 201", (await status(ctx, "/api/studies", { method: "POST", data: newStudy })) === 201);
    const before = await authCookies(ctx);
    check("akadmin: cookie HttpOnly + Lax", before.every((c) => c.httpOnly && c.sameSite === "Lax"), cookieSummary(before));
    await page.waitForFunction(() => document.getElementById("libState").textContent === "signed in as akadmin");
    check("akadmin: board says signed in as akadmin", true);

    // Past the re-check interval: a page reload fires a burst of requests with the same cookie.
    await sleep(RECHECK_MS + 1500);
    await page.reload();
    await page.waitForLoadState("networkidle");
    const after = await authCookies(ctx);
    check("akadmin: re-check renewed the cookie (refresh token rotated)",
      after.map((c) => c.value).join() !== before.map((c) => c.value).join(), cookieSummary(after));
    check("akadmin: still 200 after the re-check burst", (await status(ctx, "/api/studies")) === 200);
    await sleep(RECHECK_MS + 1500);
    check("akadmin: and after a second re-check (rotated token works)", (await status(ctx, "/api/studies")) === 200);
    check("akadmin: roles intact after re-checks", (await me(ctx)).can_save === true);

    await page.goto(APP + "/logout");
    await page.waitForURL(APP + "/", { timeout: 15000 }).catch(() => {});
    check("logout: back on the board, signed out", page.url() === APP + "/" && (await me(ctx)).authed === false, page.url());
    check("logout: GET /api/studies is 401", (await status(ctx, "/api/studies")) === 401);
    await page.goto(APP + "/login");
    const asksAgain = await page.locator('input[name="uidField"]').waitFor({ timeout: 10000 }).then(() => true, () => false);
    check("logout: IdP session ended too (next /login asks for credentials)", asksAgain, page.url());
    await ctx.close();
  }

  // (c) friend1: guest only; (e) deactivation at the IdP ends the session at the next re-check
  {
    const admin = idpAdmin();
    const pk = await admin.pk("friend1");
    const ctx = await browser.newContext();
    try {
      const page = await signIn(ctx, "friend1", "local-test-pass");
      const m = await me(ctx);
      check("friend1: /api/me shows friend1 as guest, cannot save",
        m.authed && m.user === "friend1" && m.roles.includes("guest") && !m.roles.includes("user") && m.can_save === false,
        JSON.stringify(m));
      check("friend1: GET /api/studies is 403", (await status(ctx, "/api/studies")) === 403);
      check("friend1: POST /api/studies is 403", (await status(ctx, "/api/studies", { method: "POST", data: newStudy })) === 403);
      await page.waitForFunction(() => document.getElementById("libState").textContent.includes("(guest)"));
      const state = await page.locator("#libState").innerText();
      const note = await page.locator("#libNote").innerText();
      check("friend1: UI says signed in as guest", state === "signed in as friend1 (guest)", state);
      check("friend1: UI says ask the owner for access, Save disabled",
        /ask the owner for access/.test(note) && await page.locator("#saveStudy").isDisabled()
          && !(await page.locator("#studyList").isVisible()), note);
      const shot = `${SHOTS}/friend1-guest.png`;
      await page.waitForLoadState("networkidle");
      await page.locator("#libNote").scrollIntoViewIfNeeded();
      await page.screenshot({ path: shot, fullPage: false });
      console.log(`      screenshot: ${shot}`);

      await sleep(RECHECK_MS + 1500);
      check("friend1: a re-check with guest only keeps the session", (await me(ctx)).authed === true);

      await admin.setActive(pk, false);
      console.log("      friend1 deactivated at the IdP");
      check("friend1: still signed in within the interval", (await me(ctx)).authed === true);
      await sleep(RECHECK_MS + 1500);
      const gone = await me(ctx);
      check("friend1: deactivation ended the session at the next request after 5 s", gone.authed === false, JSON.stringify(gone));
      check("friend1: GET /api/studies is 401 now", (await status(ctx, "/api/studies")) === 401);
      check("friend1: cookie cleared", (await authCookies(ctx)).length === 0);
    } finally {
      await admin.setActive(pk, true);
      console.log("      friend1 re-activated");
      await ctx.close();
    }
  }
} else {
  // (g) password mode: the old login page and endpoint unchanged
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  check("password: GET /api/studies anonymous is 401", (await status(ctx, "/api/studies")) === 401);
  await page.goto(APP + "/login");
  check("password: /login is the local page", page.url() === APP + "/login" && (await page.locator("#password").count()) === 1, page.url());
  await page.fill("#username", "erkan");
  await page.fill("#password", "wrong");
  await page.click("#submit");
  await page.locator("#err").waitFor({ state: "visible" });
  check("password: wrong password refused", (await page.locator("#err").innerText()).includes("Wrong username or password"));
  await page.fill("#password", "spike-pass");
  await page.click("#submit");
  await page.waitForURL(APP + "/");
  const m = await me(ctx);
  check("password: signed in as erkan, can save", m.authed && m.user === "erkan" && m.can_save === true, JSON.stringify(m));
  check("password: GET /api/studies is 200", (await status(ctx, "/api/studies")) === 200);
  check("password: POST /api/studies is 201", (await status(ctx, "/api/studies", { method: "POST", data: newStudy })) === 201);
  await page.waitForFunction(() => document.getElementById("libState").textContent === "signed in as erkan");
  check("password: board says signed in as erkan", true);
  await page.goto(APP + "/logout");
  await page.waitForURL(APP + "/");
  check("password: /logout signs out locally", (await me(ctx)).authed === false && (await status(ctx, "/api/studies")) === 401);
  await ctx.close();
}

await browser.close();
console.log(failures ? `${failures} FAILED` : "all passed");
process.exit(failures);
