// PeakMeter ranking: the best DPS, healing and stagger per dungeon boss, from players who turned it on in the meter.
// POST /api/submit       one kill (sent by the meter); answers with the player's places
// GET  /api/bosses       the bosses with entries, per region (period=all|week)
// GET  /api/top          one boss's list for one metric (period=all|week, cls=<class>)
// GET  /                 the download page
// GET  /ranking          the ranking page (the meter before 1.36 opened /?region=..., which lands there too)
import page from "./page.html";
import landing from "./landing.html";

const REGIONS = new Set(["eu", "na", "nae", "naw", "kr", "tw"]);
const METRICS = ["dps", "hps", "stagger"];
const CLASSES = new Set(["Gladiator", "Templar", "Ranger", "Assassin", "Sorcerer", "Elementalist", "Cleric", "Chanter", "Brawler"]);
// Far above anything real: only keeps typos and nonsense out (the HP check below does the real work).
const CAP = { dps: 50_000_000, hps: 50_000_000, stagger: 10_000_000 };
const PER_HOUR = 60;
const DAY = 86_400_000;
// Weeks start Monday 00:00 UTC (1970-01-01 was a Thursday).
const weekOf = (ms) => Math.floor((Math.floor(ms / DAY) + 3) / 7);

const cors = {
  "access-control-allow-origin": "*",
  "access-control-allow-methods": "GET, POST, OPTIONS",
  "access-control-allow-headers": "content-type",
};
const json = (body, status = 200, extra = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json; charset=utf-8", ...cors, ...extra } });

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    if (req.method === "OPTIONS") return new Response(null, { headers: cors });
    try {
      if (url.pathname === "/" || url.pathname === "/index.html") {
        if (url.searchParams.has("region")) return Response.redirect(url.origin + "/ranking" + url.search, 302);
        return html(landing);
      }
      if (url.pathname === "/ranking" || url.pathname === "/ranking/") return html(page);
      if (url.pathname === "/api/submit" && req.method === "POST") return await submit(req, env);
      if (url.pathname === "/api/bosses") return await bosses(url, env);
      if (url.pathname === "/api/top") return await top(url, env);
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: "server error" }, 500);
    }
  },
};

const html = (body) =>
  new Response(body, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "public, max-age=300" } });
const int = (v) => (Number.isInteger(v) ? v : NaN);
const cleanText = (s, max) =>
  typeof s === "string" && s.length > 0 && s.length <= max && !/[\u0000-\u001f\u007f<>]/.test(s) ? s.trim() : null;

async function submit(req, env) {
  let b;
  try {
    b = await req.json();
  } catch {
    return json({ error: "bad json" }, 400);
  }
  const region = typeof b.region === "string" ? b.region.toLowerCase() : "";
  const boss = int(b.boss);
  const name = cleanText(b.name, 24);
  const server = int(b.server);
  const durMs = int(b.durMs);
  const maxHp = Number(b.bossMaxHp);
  const damage = Number(b.damage);
  if (!REGIONS.has(region) || !(boss > 0) || !name || !(server >= 0) || !(durMs >= 15_000 && durMs <= 2 * 3600_000))
    return json({ error: "invalid" }, 400);
  if (!(maxHp > 0) || !(damage >= 0)) return json({ error: "invalid" }, 400);

  // Spam: a few kills an hour is normal, hundreds are not.
  const ip = await hash(req.headers.get("cf-connecting-ip") || "?");
  const hour = Math.floor(Date.now() / 3600_000);
  const row = await env.DB.prepare(
    "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
  ).bind(ip, hour).first();
  if (row && row.n > PER_HOUR) return json({ error: "slow down" }, 429);
  if (row && row.n === 1) {
    await env.DB.prepare("DELETE FROM hits WHERE hour < ?1").bind(hour - 2).run();
    await env.DB.prepare("DELETE FROM week_best WHERE week < ?1").bind(weekOf(Date.now()) - 8).run();
  }

  // The boss's HP: the first report sets it; later ones must agree (within 10 %).
  const known = await env.DB.prepare("SELECT max_hp FROM bosses WHERE boss = ?1").bind(boss).first();
  if (known) {
    if (Math.abs(known.max_hp - maxHp) > known.max_hp * 0.1) return json({ error: "boss hp" }, 400);
  } else {
    await env.DB.prepare("INSERT OR IGNORE INTO bosses (boss, name, zone, max_hp) VALUES (?1, ?2, ?3, ?4)")
      .bind(boss, cleanText(b.bossName, 60) || `Boss ${boss}`, cleanText(b.zone, 60) || "", Math.round(maxHp)).run();
  }
  const hpRef = known ? known.max_hp : maxHp;
  // One player cannot deal more damage than the boss has HP (a little slack for overkill and adds).
  if (damage > hpRef * 1.2) return json({ error: "damage" }, 400);
  const dps = Number(b.dps);
  if (!(dps >= 0) || Math.abs(dps - damage / (durMs / 1000)) > Math.max(1, dps * 0.02)) return json({ error: "dps" }, 400);

  const cls = CLASSES.has(b.cls) ? b.cls : null;
  const cp = int(b.cp) >= 0 && b.cp < 10_000_000 ? b.cp : null;
  const party = int(b.party) >= 1 && b.party <= 48 ? b.party : null;
  const serverName = cleanText(b.serverName, 30);
  const now = Date.now();

  const week = weekOf(now);
  const cols = "region, boss, metric, name, server, server_name, cls, cp, value, dur_ms, party, at";
  const set = `server_name = excluded.server_name, cls = excluded.cls, cp = excluded.cp, value = excluded.value,
           dur_ms = excluded.dur_ms, party = excluded.party, at = excluded.at`;
  const sent = [];
  const stmts = [];
  for (const m of METRICS) {
    const v = Number(b[m]);
    if (!(v > 0) || v > CAP[m]) continue;
    sent.push(m);
    const args = [region, boss, m, name, server, serverName, cls, cp, Math.round(v), durMs, party, now];
    stmts.push(
      env.DB.prepare(
        `INSERT INTO best (${cols}) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)
         ON CONFLICT(region, boss, metric, name, server) DO UPDATE SET ${set}
         WHERE excluded.value > best.value RETURNING value`
      ).bind(...args),
      env.DB.prepare(
        `INSERT INTO week_best (week, ${cols}) VALUES (?13, ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12)
         ON CONFLICT(week, region, boss, metric, name, server) DO UPDATE SET ${set}
         WHERE excluded.value > week_best.value`
      ).bind(...args, week)
    );
  }
  if (!stmts.length) return json({ ok: true, places: {} });
  const done = await env.DB.batch(stmts);

  // Where the player stands now: overall, in the class, and this week.
  const ask = sent.map((m) =>
    env.DB.prepare(
      `WITH me AS (SELECT value, cls FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3 AND name = ?4 AND server = ?5),
            wk AS (SELECT value FROM week_best WHERE week = ?6 AND region = ?1 AND boss = ?2 AND metric = ?3 AND name = ?4 AND server = ?5)
       SELECT (SELECT value FROM me) AS best,
         (SELECT COUNT(*) FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3) AS total,
         (SELECT COUNT(*) + 1 FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3 AND value > (SELECT value FROM me)) AS place,
         (SELECT COUNT(*) FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3 AND cls = (SELECT cls FROM me)) AS clsTotal,
         (SELECT COUNT(*) + 1 FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3 AND cls = (SELECT cls FROM me)
            AND value > (SELECT value FROM me)) AS clsPlace,
         (SELECT COUNT(*) FROM week_best WHERE week = ?6 AND region = ?1 AND boss = ?2 AND metric = ?3) AS weekTotal,
         (SELECT COUNT(*) + 1 FROM week_best WHERE week = ?6 AND region = ?1 AND boss = ?2 AND metric = ?3
            AND value > (SELECT value FROM wk)) AS weekPlace`
    ).bind(region, boss, m, name, server, week)
  );
  const rows = await env.DB.batch(ask);
  const places = {};
  sent.forEach((m, i) => {
    const r = rows[i].results[0] || {};
    places[m] = {
      value: Math.round(Number(b[m])),
      best: r.best,
      improved: (done[i * 2].results || []).length > 0,
      place: r.place, total: r.total,
      clsPlace: cls ? r.clsPlace : null, clsTotal: cls ? r.clsTotal : null,
      weekPlace: r.weekPlace, weekTotal: r.weekTotal,
    };
  });
  return json({ ok: true, places });
}

// period=week: this week's table (since Monday 00:00 UTC); anything else: all time.
function source(url) {
  return url.searchParams.get("period") === "week"
    ? { table: "week_best", where: `week = ${weekOf(Date.now())} AND ` }
    : { table: "best", where: "" };
}

async function bosses(url, env) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  if (!REGIONS.has(region)) return json({ error: "invalid" }, 400);
  const { table, where } = source(url);
  const { results } = await env.DB.prepare(
    `SELECT b.boss AS boss, o.name AS name, o.zone AS zone, COUNT(DISTINCT b.name || '|' || b.server) AS players
     FROM ${table} b JOIN bosses o ON o.boss = b.boss WHERE ${where}b.region = ?1 GROUP BY b.boss ORDER BY o.zone, o.name`
  ).bind(region).all();
  return json(results, 200, { "cache-control": "public, max-age=60" });
}

async function top(url, env) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  const boss = Number(url.searchParams.get("boss"));
  const metric = url.searchParams.get("metric") || "dps";
  const cls = url.searchParams.get("cls") || "";
  const limit = Math.min(100, Math.max(1, Number(url.searchParams.get("limit")) || 50));
  if (!REGIONS.has(region) || !Number.isInteger(boss) || !METRICS.includes(metric) || (cls && !CLASSES.has(cls)))
    return json({ error: "invalid" }, 400);
  const { table, where } = source(url);
  const { results } = await env.DB.prepare(
    `SELECT name, server_name AS server, cls, cp, value, dur_ms AS durMs, party, at FROM ${table}
     WHERE ${where}region = ?1 AND boss = ?2 AND metric = ?3 AND (?5 = '' OR cls = ?5) ORDER BY value DESC LIMIT ?4`
  ).bind(region, boss, metric, limit, cls).all();
  return json(results, 200, { "cache-control": "public, max-age=60" });
}

async function hash(s) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode("pm-rank:" + s));
  return [...new Uint8Array(d).slice(0, 8)].map((x) => x.toString(16).padStart(2, "0")).join("");
}
