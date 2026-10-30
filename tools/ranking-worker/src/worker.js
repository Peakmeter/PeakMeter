// PeakMeter ranking: the best DPS, healing and stagger per dungeon boss, from players who turned it on in the meter.
// POST /api/submit       one kill (sent by the meter); answers with the player's places
// GET  /api/bosses       the bosses with entries, per region (period=all|week)
// GET  /api/top          one boss's list for one metric (period=all|week, cls=<class>)
// GET  /                 the download page
// GET  /ranking          the ranking page (the meter before 1.36 opened /?region=..., which lands there too)
// GET  /player[/<region>/<server>/<name>]   player search and profile
// GET  /compare?r=<region>&p1=<server>/<name>&p2=...   two players side by side
// POST /api/bosses/report  world boss timers of one server, from a meter (no player data)
// GET  /api/bosses/live  servers with live timers, or one server's timers
// GET  /api/updates      every version with its note (from the meter's own changelog) and release date
// GET  /api/char/search  NCSOFT's official character search, trimmed
// GET  /api/char         one character: official profile, gear, skills (NCSOFT) + PeakMeter bests
// POST /api/report       a fight shared from the meter ("share as link"); GET /api/report?id= reads it back
// GET  /r/<id>           the fight report page
// POST /api/alerts       world boss alerts into a Discord channel webhook (cron posts them every minute)
// POST /api/gather       gathered items (item + count, no player data); GET /api/gather = totals per item
// POST /api/census       players seen in fights (no names); GET /api/census, /census = the census page
// POST /discord          Discord slash commands (/boss /player /rank /peakmeter); GET /discord = add the bot
// GET  /api/char/board   one Daevanion board of a character: every node with its place, grade, effect and state
import page from "./page.html";
import landing from "./landing.html";
import player from "./player.html";
import compare from "./compare.html";
import timers from "./timers.html";
import report from "./report.html";
import census from "./census.html";
import ITEMS from "./items.json";
import siteCss from "./client/site.css";
import siteJs from "./client/site.js";
import ogPng from "./og.png";
import { discordBot } from "./discord.mjs";
import { alerts } from "./alerts.mjs";

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
  async fetch(req, env, ctx) {
    const url = new URL(req.url);
    if (req.method === "OPTIONS") return new Response(null, { headers: cors });
    try {
      if (url.pathname === "/" || url.pathname === "/index.html") {
        if (url.searchParams.has("region")) return Response.redirect(url.origin + "/ranking" + url.search, 302);
        return html(url.searchParams.get("lang") === "de" ? landingDe() : landing);
      }
      if (url.pathname === "/robots.txt") return text(`User-agent: *\nAllow: /\nDisallow: /api/\n\nSitemap: ${url.origin}/sitemap.xml\n`);
      if (url.pathname === "/sitemap.xml") return await sitemap(url, env);
      if (url.pathname === `/${INDEXNOW_KEY}.txt`) return text(INDEXNOW_KEY);
      // Google Search Console ownership file
      if (url.pathname === "/google4f18a70ffc7e17f1.html") return html("google-site-verification: google4f18a70ffc7e17f1.html");
      if (url.pathname === "/og.png") return new Response(ogPng, { headers: { "content-type": "image/png", "cache-control": "public, max-age=86400" } });
      if (url.pathname === "/site.css") return asset(siteCss, "text/css; charset=utf-8");
      if (url.pathname === "/site.js") return asset(siteJs, "text/javascript; charset=utf-8");
      if (url.pathname === "/ranking" || url.pathname === "/ranking/") return html(isBot(req) ? await rankingCard(url, env, page) : page);
      if (url.pathname === "/player" || url.pathname.startsWith("/player/")) return html(isBot(req) ? await playerCard(url, env, ctx, player) : player);
      if (url.pathname === "/timers" || url.pathname === "/timers/") return html(timers);
      if (url.pathname === "/api/alerts" && req.method === "POST") return await alertsMod().save(req, env);
      if (url.pathname === "/census" || url.pathname === "/census/") return html(census);
      if (url.pathname === "/api/gather" && req.method === "POST") return await gatherSave(req, env);
      if (url.pathname === "/api/gather") return await gatherGet(url, env);
      if (url.pathname === "/api/census" && req.method === "POST") return await censusSave(req, env);
      if (url.pathname === "/api/census") return await censusGet(url, env);
      if (url.pathname.startsWith("/r/")) return html(isBot(req) ? await reportCard(url, env, report) : report);
      if (url.pathname === "/api/report" && req.method === "POST") return await reportSave(req, env, url);
      if (url.pathname === "/api/report") return await reportGet(url, env);
      if (url.pathname === "/compare" || url.pathname === "/compare/") return html(isBot(req) ? await duelCard(url, env, ctx, compare) : compare);
      if (url.pathname === "/discord") return await bot()(req, env, ctx);
      if (url.pathname === "/api/char/search") return await charSearch(url);
      if (url.pathname === "/api/char") return await charProfile(url, env, ctx);
      if (url.pathname === "/api/char/board") return await charBoard(url, ctx);
      if (url.pathname === "/api/updates") return await updates(url, ctx);
      if (url.pathname === "/api/bosses/report" && req.method === "POST") return await bossReport(req, env);
      if (url.pathname === "/api/bosses/live") return await bossLive(url, env);
      if (url.pathname === "/api/submit" && req.method === "POST") return await submit(req, env);
      if (url.pathname === "/api/bosses") return await bosses(url, env);
      if (url.pathname === "/api/top") return await top(url, env);
      return json({ error: "not found" }, 404);
    } catch (e) {
      return json({ error: "server error" }, 500);
    }
  },
  // Every minute (wrangler.toml [triggers]): world boss alerts.
  async scheduled(event, env, ctx) {
    ctx.waitUntil(alertsMod().tick(env));
  },
};

const text = (body) => new Response(body, { headers: { "content-type": "text/plain; charset=utf-8", "cache-control": "public, max-age=3600" } });
const html = (body) =>
  new Response(body, { headers: { "content-type": "text/html; charset=utf-8", "cache-control": "public, max-age=300" } });
const asset = (body, type) => new Response(body, { headers: { "content-type": type, "cache-control": "public, max-age=300" } });
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

// ---------------------------------------------------------------- characters (NCSOFT's official character pages)

const NC_SEARCH = "https://api-search.plaync.com/";
const NC_CHAR = "https://aion2.plaync.com/api/character/";
const NC_IMG = "https://profileimg.plaync.com";
const strip = (s) => String(s ?? "").replace(/<[^>]+>/g, "");
// The official pcId: four in a row per class (gender and race), starting at 5.
const PC_CLASSES = ["gladiator", "templar", "ranger", "assassin", "elementalist", "sorcerer", "cleric", "chanter"];
const classOf = (pcId) => (Number.isInteger(pcId) && pcId >= 5 ? PC_CLASSES[Math.floor((pcId - 5) / 4)] || null : null);

// Taiwan has no public character search.
function ncSearchUrl(region, q, serverId) {
  const p = `keyword=${encodeURIComponent(q)}&page=1&size=30` + (serverId ? `&serverId=${serverId}` : "");
  if (region === "kr") return `${NC_SEARCH}aion2/search/v2/character?${p}`;
  if (region === "eu" || region === "nae" || region === "naw")
    return `${NC_SEARCH}aion2global/search/v2/character?${p}&region=${region}&localeInfo=en-US`;
  return null;
}

async function ncJson(u) {
  const r = await fetch(u, {
    headers: { "user-agent": "PeakMeter (+https://peakmeter-ranking.peakmeter.workers.dev)", accept: "application/json" },
    cf: { cacheTtl: 300, cacheEverything: true },
  });
  if (!r.ok) throw new Error("nc " + r.status);
  return r.json();
}

async function charSearch(url) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  const q = (url.searchParams.get("q") || "").trim();
  if (!REGIONS.has(region) || q.length < 2 || q.length > 24 || /[\u0000-\u001f<>]/.test(q)) return json({ error: "invalid" }, 400);
  const u = ncSearchUrl(region, q);
  if (!u) return json({ error: "region" }, 400);
  let d;
  try { d = await ncJson(u); } catch { return json({ error: "official search not reachable" }, 502); }
  const list = (d.list || []).map((c) => ({
    name: strip(c.name), server: c.serverName || "", serverId: c.serverId, level: c.level, race: c.race, cls: classOf(c.pcId),
    img: c.profileImageUrl ? NC_IMG + c.profileImageUrl : null,
  }));
  return json(list, 200, { "cache-control": "public, max-age=300" });
}

// The official character for a name on a server (by name or id); null when the search has none or is down.
async function findChar(region, server, name) {
  const sid = /^\d+$/.test(server) ? Number(server) : null;
  const su = ncSearchUrl(region, name, sid);
  if (!su) return null;
  try {
    const d = await ncJson(su);
    return (d.list || []).find((c) => strip(c.name).toLowerCase() === name.toLowerCase() &&
      (sid ? c.serverId === sid : String(c.serverName || "").toLowerCase() === server.toLowerCase())) || null;
  } catch {
    return null;
  }
}

const charQuery = (region, lang, found) =>
  (region === "kr" ? `?lang=ko-KR` : `?region=${region}&lang=${lang}`) +
  `&characterId=${encodeURIComponent(decodeURIComponent(found.characterId))}&serverId=${found.serverId}`;

async function charBoard(url, ctx) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  const name = cleanText(url.searchParams.get("name"), 24);
  const server = cleanText(url.searchParams.get("server"), 30);
  const board = Number(url.searchParams.get("board"));
  const lang = url.searchParams.get("lang") === "de" ? "de-DE" : "en-US";
  if (!REGIONS.has(region) || !name || !server || !Number.isInteger(board) || board <= 0 || board > 999) return json({ error: "invalid" }, 400);
  const key = new Request(`https://cache.peakmeter/board/v1/${region}/${encodeURIComponent(server.toLowerCase())}/${encodeURIComponent(name.toLowerCase())}/${board}/${lang}`);
  const cached = await caches.default.match(key);
  if (cached) return cached;
  const found = await findChar(region, server, name);
  if (!found) return json({ error: "not found" }, 404, { "cache-control": "public, max-age=60" });
  let d;
  try { d = await ncJson(NC_CHAR + "daevanion/detail" + charQuery(region, lang, found) + `&boardId=${board}`); }
  catch { return json({ error: "official page not reachable" }, 502); }
  const body = {
    board,
    nodes: (d.nodeList || []).filter((n) => n.type !== "None").map((n) => ({
      r: n.row, c: n.col, t: n.type, g: n.grade, o: n.open ? 1 : 0, n: n.name, e: (n.effectList || []).map((x) => x.desc),
    })),
    stats: (d.openStatEffectList || []).map((x) => x.desc),
    skills: (d.openSkillEffectList || []).map((x) => x.desc),
  };
  const res = json(body, 200, { "cache-control": "public, max-age=600" });
  ctx.waitUntil(caches.default.put(key, res.clone()));
  return res;
}

// ---------------------------------------------------------------- gear grade
// Every profile looked up here adds its item level to a table; a character's grade is the share of others of the
// same class and level (or within three levels while few are known) whose item level is lower.
const GRADE_MIN = 10;
const GRADE_TIERS = [[99, "M"], [95, "S"], [85, "A"], [65, "B"], [40, "C"], [0, "D"]];

async function gearGrade(env, region, serverId, name, p, stats) {
  const il = (stats || []).find((s) => s.type === "ItemLevel")?.value;
  if (!p || !p.cls || !p.level || !(il > 0) || !serverId) return null;
  try {
    await env.DB.prepare(
      `INSERT INTO gear (region, server, name, cls, level, il, cp, at) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
       ON CONFLICT(region, server, name) DO UPDATE SET cls = excluded.cls, level = excluded.level, il = excluded.il, cp = excluded.cp, at = excluded.at`
    ).bind(region, serverId, name, p.cls, p.level, il, p.cp || 0, Date.now()).run();
    const cohort = (lo, hi) => env.DB.prepare(
      `SELECT COUNT(*) AS n, SUM(CASE WHEN il < ?4 THEN 1 ELSE 0 END) AS below, SUM(CASE WHEN il = ?4 THEN 1 ELSE 0 END) AS same
       FROM gear WHERE cls = ?1 AND level BETWEEN ?2 AND ?3`
    ).bind(p.cls, lo, hi, il).first();
    let lo = p.level, hi = p.level, r = await cohort(lo, hi);
    if ((r?.n || 0) < GRADE_MIN) { lo = p.level - 3; hi = p.level + 3; r = await cohort(lo, hi); }
    const n = r?.n || 0;
    if (n < GRADE_MIN) return { n, need: GRADE_MIN, il };
    // Among the others (this character counts once in n and once in same): ties count half.
    const pct = ((r.below || 0) + Math.max(0, (r.same || 1) - 1) / 2) / Math.max(1, n - 1) * 100;
    const tier = GRADE_TIERS.find(([min]) => pct >= min)[1];
    return { tier, pct: Math.round(pct), n, il, lo, hi };
  } catch {
    return null;
  }
}

async function charProfile(url, env, ctx) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  const name = cleanText(url.searchParams.get("name"), 24);
  const server = cleanText(url.searchParams.get("server"), 30);
  const lang = url.searchParams.get("lang") === "de" ? "de-DE" : "en-US";
  if (!REGIONS.has(region) || !name || !server) return json({ error: "invalid" }, 400);

  const key = new Request(`https://cache.peakmeter/char/v4/${region}/${encodeURIComponent(server.toLowerCase())}/${encodeURIComponent(name.toLowerCase())}/${lang}`);
  const cached = await caches.default.match(key);
  if (cached) return cached;

  const sid = /^\d+$/.test(server) ? Number(server) : null;
  const found = await findChar(region, server, name);

  let info = null, equip = null;
  if (found) {
    const q = charQuery(region, lang, found);
    [info, equip] = await Promise.all([ncJson(NC_CHAR + "info" + q).catch(() => null), ncJson(NC_CHAR + "equipment" + q).catch(() => null)]);
  }

  // PeakMeter's own: this player's best kills with their places.
  const serverId = found ? found.serverId : sid;
  const where = serverId ? "b.server = ?2" : "lower(b.server_name) = lower(?2)";
  const { results: bests } = await env.DB.prepare(
    `SELECT b.boss AS boss, o.name AS bossName, o.zone AS zone, b.metric AS metric, b.value AS value, b.dur_ms AS durMs,
            b.party AS party, b.at AS at, b.cls AS cls, b.cp AS cp,
            (SELECT COUNT(*) + 1 FROM best x WHERE x.region = b.region AND x.boss = b.boss AND x.metric = b.metric AND x.value > b.value) AS place,
            (SELECT COUNT(*) FROM best x WHERE x.region = b.region AND x.boss = b.boss AND x.metric = b.metric) AS total
     FROM best b JOIN bosses o ON o.boss = b.boss
     WHERE b.region = ?1 AND ${where} AND lower(b.name) = lower(?3) ORDER BY o.zone, o.name`
  ).bind(region, serverId ?? server, name).all();

  if (!found && !bests.length) return json({ error: "not found" }, 404, { "cache-control": "public, max-age=60" });

  const pr = info?.profile;
  const body = {
    region,
    name: found ? strip(found.name) : name,
    server: found?.serverName || pr?.serverName || server,
    serverId,
    official: !!pr,
    officialUrl: found && region !== "kr"
      ? `https://aion2.plaync.com/${lang === "de-DE" ? "de-de" : "en-us"}/characters/${found.serverId}/${found.characterId}?region=${region}`
      : null,
    profile: pr ? {
      level: pr.characterLevel, className: pr.className, cls: classOf(pr.pcId ?? found?.pcId), race: pr.raceName, gender: pr.genderName, cp: pr.combatPower,
      img: pr.profileImage || (found?.profileImageUrl ? NC_IMG + found.profileImageUrl : null),
      title: pr.titleName ? { name: pr.titleName, grade: pr.titleGrade } : null,
    } : found ? { level: found.level, cls: classOf(found.pcId), img: found.profileImageUrl ? NC_IMG + found.profileImageUrl : null } : null,
    stats: (info?.stat?.statList || []).map((x) => ({ type: x.type, name: x.name, value: x.value, more: x.statSecondList || [] })),
    titles: info?.title ? { owned: info.title.ownedCount, total: info.title.totalCount } : null,
    titleList: (info?.title?.titleList || []).map((x) => ({
      cat: x.equipCategory, name: x.name, grade: x.grade, owned: x.ownedCount, total: x.totalCount,
      equip: (x.equipStatList || []).map((e) => e.desc), collect: (x.statList || []).map((e) => e.desc),
    })),
    daevanion: (info?.daevanion?.boardList || []).map((x) => ({ id: x.id, name: x.name, icon: x.icon, open: x.openNodeCount, total: x.totalNodeCount, pct: x.openPercent })),
    gear: (equip?.equipment?.equipmentList || []).map((x) => ({ slot: x.slotPosName, name: x.name, icon: x.icon, grade: x.grade, plus: x.enchantLevel, exceed: x.exceedLevel })),
    pet: equip?.petwing?.pet ? { name: equip.petwing.pet.name, icon: equip.petwing.pet.icon, level: equip.petwing.pet.level } : null,
    wing: equip?.petwing?.wing ? { name: equip.petwing.wing.name, icon: equip.petwing.wing.icon, grade: equip.petwing.wing.grade, plus: equip.petwing.wing.enchantLevel } : null,
    skills: (equip?.skill?.skillList || []).filter((x) => x.acquired).map((x) => ({ name: x.name, icon: x.icon, level: x.skillLevel, kind: x.category, on: !!x.equip })),
    bests,
    at: Date.now(),
  };
  body.grade = await gearGrade(env, region, serverId, body.name, body.profile, body.stats);
  const res = json(body, 200, { "cache-control": "public, max-age=600" });
  ctx.waitUntil(caches.default.put(key, res.clone()));
  return res;
}

// ---------------------------------------------------------------- search engines
const INDEXNOW_KEY = "4f3905cb2dd980f3ed3ec5815bf7828d";

// The German home page: the same page, told it is German (the page then shows its German texts).
function landingDe() {
  return landing
    .replace('<html lang="en">', '<html lang="de">')
    .replace(/<title>[^<]*<\/title>/, "<title>PeakMeter – Kostenloser AION 2 DPS-Meter</title>")
    .replace(/<meta name="description" content="[^"]*">/, '<meta name="description" content="PeakMeter ist ein kostenloser DPS-Meter (Damage Meter) für AION 2: Live-DPS, Heilung und Stagger, Boss-HP und Kill-Zeit, Leerlauf und Todes-Analyse, Kampf-Logs, eine Rangliste pro Dungeon-Boss und Spieler-Profile. Für Windows · EU, NA, KR, TW.">')
    .replace(/<link rel="canonical" href="([^"]*)">/, '<link rel="canonical" href="$1?lang=de">')
    .replace(/<meta property="og:title" content="[^"]*">/, '<meta property="og:title" content="PeakMeter – Kostenloser AION 2 DPS-Meter">');
}

const xml = (s) => String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

// Every page worth finding: home (both languages), ranking, search, duel, each boss list and each ranked player.
async function sitemap(url, env) {
  const o = url.origin;
  const day = (ms) => new Date(ms).toISOString().slice(0, 10);
  const rows = [
    `<url><loc>${o}/</loc><xhtml:link rel="alternate" hreflang="en" href="${o}/"/><xhtml:link rel="alternate" hreflang="de" href="${o}/?lang=de"/><changefreq>weekly</changefreq><priority>1.0</priority></url>`,
    `<url><loc>${o}/?lang=de</loc><xhtml:link rel="alternate" hreflang="en" href="${o}/"/><xhtml:link rel="alternate" hreflang="de" href="${o}/?lang=de"/><changefreq>weekly</changefreq><priority>0.9</priority></url>`,
    `<url><loc>${o}/ranking</loc><changefreq>daily</changefreq><priority>0.8</priority></url>`,
    `<url><loc>${o}/player</loc><changefreq>weekly</changefreq><priority>0.7</priority></url>`,
    `<url><loc>${o}/timers</loc><changefreq>weekly</changefreq><priority>0.7</priority></url>`,
    `<url><loc>${o}/compare</loc><changefreq>monthly</changefreq><priority>0.5</priority></url>`,
  ];
  try {
    const { results: bosses } = await env.DB.prepare(
      "SELECT region, boss, MAX(at) AS at FROM best GROUP BY region, boss ORDER BY region, boss LIMIT 1000").all();
    for (const b of bosses)
      rows.push(`<url><loc>${xml(`${o}/ranking?region=${b.region}&boss=${b.boss}&metric=dps`)}</loc><lastmod>${day(b.at)}</lastmod><changefreq>daily</changefreq><priority>0.6</priority></url>`);
    const { results: players } = await env.DB.prepare(
      `SELECT region, server_name AS server, name, MAX(at) AS at FROM best WHERE server_name IS NOT NULL AND server_name != ''
       GROUP BY region, server_name, name ORDER BY at DESC LIMIT 20000`).all();
    for (const p of players)
      rows.push(`<url><loc>${xml(`${o}/player/${p.region}/${encodeURIComponent(p.server)}/${encodeURIComponent(p.name)}`)}</loc><lastmod>${day(p.at)}</lastmod><changefreq>weekly</changefreq><priority>0.5</priority></url>`);
  } catch { /* the fixed pages still go out */ }
  const body = `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9" xmlns:xhtml="http://www.w3.org/1999/xhtml">\n${rows.join("\n")}\n</urlset>\n`;
  return new Response(body, { headers: { "content-type": "application/xml; charset=utf-8", "cache-control": "public, max-age=3600" } });
}

// ---------------------------------------------------------------- link previews
// Discord, WhatsApp, Reddit, X … read og: tags when a link is posted. Only their fetchers get the filled-in page,
// so ordinary visits never cost an extra call to NCSOFT.
const BOTS = /discordbot|twitterbot|facebookexternalhit|facebot|slackbot|telegrambot|whatsapp|linkedinbot|redditbot|embedly|skypeuripreview|pinterest|vkshare|mastodon|bluesky|iframely|applebot|googlebot|bingbot/i;
const isBot = (req) => BOTS.test(req.headers.get("user-agent") || "");
const attr = (s) => String(s ?? "").replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
const short = (v) => (v >= 1e6 ? (v / 1e6).toFixed(2) + "M" : v >= 1e4 ? (v / 1e3).toFixed(1) + "K" : String(Math.round(v)));
const group = (v) => Number(v || 0).toLocaleString("en-US");
const CLASS_EN = { gladiator: "Gladiator", templar: "Templar", ranger: "Ranger", assassin: "Assassin", sorcerer: "Sorcerer", elementalist: "Spiritmaster", cleric: "Cleric", chanter: "Chanter" };
// Link preview card (1200×630); bump ?v= when the picture changes so Discord fetches it again.
const OG_IMAGE = "https://peakmeter-ranking.peakmeter.workers.dev/og.png?v=1";

function withCard(page, url, m) {
  const tags = [
    `<meta property="og:site_name" content="PeakMeter">`,
    `<meta property="og:type" content="website">`,
    `<meta property="og:url" content="${attr(url.href)}">`,
    `<meta property="og:title" content="${attr(m.title)}">`,
    `<meta property="og:description" content="${attr(m.desc)}">`,
    `<meta property="og:image" content="${attr(m.image || OG_IMAGE)}">`,
    `<meta name="twitter:card" content="${m.image && !m.large ? "summary" : "summary_large_image"}">`,
    `<meta name="twitter:title" content="${attr(m.title)}">`,
    `<meta name="twitter:description" content="${attr(m.desc)}">`,
    `<meta name="twitter:image" content="${attr(m.image || OG_IMAGE)}">`,
    `<meta name="description" content="${attr(m.desc)}">`,
    `<link rel="canonical" href="${attr(url.origin + url.pathname + (url.pathname.startsWith("/player/") ? "" : url.search))}">`,
  ].join("\n");
  return page.replace(/<title>[\s\S]*?<\/title>/, `<title>${attr(m.title)}</title>`).replace(/<meta name="description"[^>]*>/, "").replace("</head>", tags + "\n</head>");
}

async function charData(url, env, ctx, region, server, name) {
  const u = new URL(url.origin + "/api/char");
  u.search = new URLSearchParams({ region, server, name, lang: "en" }).toString();
  try {
    const r = await charProfile(u, env, ctx);
    return r.ok ? await r.json() : null;
  } catch {
    return null;
  }
}

function charLine(d) {
  const p = d.profile || {};
  const cls = CLASS_EN[p.cls] || p.className || "";
  return [p.level ? "Lv " + p.level : "", cls].filter(Boolean).join(" ");
}

async function playerCard(url, env, ctx, page) {
  const [, , region, server, name] = url.pathname.split("/").map(decodeURIComponent);
  if (!region || !server || !name) return withCard(page, url, { title: "PeakMeter · Find any AION 2 player", desc: "Gear, stats, Daevanion and skills of any AION 2 player, plus their best kills measured with PeakMeter." });
  const d = await charData(url, env, ctx, region.toLowerCase(), server, name);
  if (!d) return withCard(page, url, { title: `${name} · ${server} · PeakMeter`, desc: "AION 2 player profile on PeakMeter." });
  const p = d.profile || {};
  const il = (d.stats || []).find((s) => s.type === "ItemLevel");
  const best = (d.bests || []).filter((b) => b.metric === "dps").sort((a, b) => b.value - a.value)[0];
  const desc = [
    p.cp ? group(p.cp) + " combat power" : "",
    il ? "item level " + group(il.value) : "",
    best ? `best: ${best.bossName} ${short(best.value)} DPS (#${best.place} of ${best.total})` : "",
  ].filter(Boolean).join(" · ") + " — gear, stats, Daevanion and skills on PeakMeter.";
  return withCard(page, url, { title: `${d.name} · ${charLine(d)} · ${d.server} (${region.toUpperCase()})`, desc, image: p.img });
}

async function duelCard(url, env, ctx, page) {
  const region = (url.searchParams.get("r") || "eu").toLowerCase();
  const pick = (v) => { const i = (v || "").indexOf("/"); return i > 0 ? [v.slice(0, i), v.slice(i + 1)] : null; };
  const a = pick(url.searchParams.get("p1")), b = pick(url.searchParams.get("p2"));
  if (!a || !b) return withCard(page, url, { title: "PeakMeter · The duel", desc: "Compare two AION 2 players side by side: combat power, gear, stats, Daevanion and best kills." });
  const [da, db] = await Promise.all([charData(url, env, ctx, region, a[0], a[1]), charData(url, env, ctx, region, b[0], b[1])]);
  const side = (d, w) => d ? `${d.name} (${charLine(d)}, ${group(d.profile?.cp)} CP)` : w[1];
  return withCard(page, url, {
    title: `${da?.name || a[1]} vs ${db?.name || b[1]} · PeakMeter duel`,
    desc: `${side(da, a)} vs ${side(db, b)} — gear, stats, Daevanion and best kills side by side.`,
    image: da?.profile?.img || db?.profile?.img,
  });
}

async function rankingCard(url, env, page) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  const boss = Number(url.searchParams.get("boss"));
  const metric = METRICS.includes(url.searchParams.get("metric")) ? url.searchParams.get("metric") : "dps";
  const label = { dps: "DPS", hps: "healing", stagger: "stagger" }[metric];
  const plain = { title: "PeakMeter Hall of Fame · AION 2", desc: "The best DPS, healing and stagger per AION 2 dungeon boss – overall, per class and per week." };
  if (!REGIONS.has(region) || !Number.isInteger(boss) || boss <= 0) return withCard(page, url, plain);
  try {
    const o = await env.DB.prepare("SELECT name FROM bosses WHERE boss = ?1").bind(boss).first();
    const { results } = await env.DB.prepare(
      "SELECT name, value FROM best WHERE region = ?1 AND boss = ?2 AND metric = ?3 ORDER BY value DESC LIMIT 3"
    ).bind(region, boss, metric).all();
    if (!o) return withCard(page, url, plain);
    const top = results.map((r, i) => `${i + 1}. ${r.name} ${short(r.value)}`).join(" · ");
    return withCard(page, url, {
      title: `Hall of Fame · ${o.name} · ${label} (${region.toUpperCase()})`,
      desc: (top || "No entries yet – be the first.") + " — PeakMeter ranking for AION 2.",
    });
  } catch {
    return withCard(page, url, plain);
  }
}

// ---------------------------------------------------------------- live world boss timers
// A meter shares its server's timers: boss, alive or down, when it comes back. The game's own list ("game") is the
// best word; a kill seen nearby ("kill") or a boss seen alive ("seen") does not replace a game report younger than
// ten minutes.
const BOSS_PER_HOUR = 120;

async function bossReport(req, env) {
  let b;
  try { b = await req.json(); } catch { return json({ error: "bad json" }, 400); }
  const region = typeof b.region === "string" ? b.region.toLowerCase() : "";
  const server = int(b.server);
  const reports = Array.isArray(b.reports) ? b.reports.slice(0, 80) : [];
  if (!REGIONS.has(region) || !(server > 0 && server < 100000) || !reports.length) return json({ error: "invalid" }, 400);
  const ip = await hash("b:" + (req.headers.get("cf-connecting-ip") || "?"));
  const hour = Math.floor(Date.now() / 3600_000);
  const row = await env.DB.prepare(
    "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
  ).bind(ip, hour).first();
  if (row && row.n > BOSS_PER_HOUR) return json({ error: "slow down" }, 429);
  const now = Date.now();
  const name = cleanText(b.serverName, 30);
  const stmts = [];
  for (const r of reports) {
    const boss = int(r?.boss);
    if (!(boss >= 2000000 && boss < 3000000)) continue;
    const src = ["game", "kill", "seen"].includes(r.src) ? r.src : "kill";
    let state, next = null, since = null;
    if (r.state === "up") {
      state = "up";
      if (int(r.since) > now - 86400_000 && r.since < now + 300_000) since = r.since;
    } else if (r.state === "down" && int(r.next) > now - 3 * 3600_000 && r.next < now + 48 * 3600_000) {
      state = "down"; next = r.next;
    } else continue;
    stmts.push(env.DB.prepare(
      `INSERT INTO boss_live (region, server, boss, server_name, state, next_at, since, src, reported)
       VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9)
       ON CONFLICT(region, server, boss) DO UPDATE SET server_name = COALESCE(excluded.server_name, boss_live.server_name),
         state = excluded.state, next_at = excluded.next_at, since = excluded.since, src = excluded.src, reported = excluded.reported
       WHERE excluded.src = 'game' OR boss_live.src != 'game' OR excluded.reported - boss_live.reported > 600000`
    ).bind(region, server, boss, name, state, next, since, src, now));
  }
  if (stmts.length) await env.DB.batch(stmts);
  return json({ ok: true, taken: stmts.length });
}

async function bossLive(url, env) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  if (!REGIONS.has(region)) return json({ error: "invalid" }, 400);
  const day = Date.now() - 2 * 86400_000;
  const server = Number(url.searchParams.get("server"));
  if (!server) {
    const { results } = await env.DB.prepare(
      `SELECT server, MAX(server_name) AS name, COUNT(*) AS bosses, MAX(reported) AS last FROM boss_live
       WHERE region = ?1 AND reported > ?2 GROUP BY server ORDER BY last DESC`
    ).bind(region, day).all();
    return json(results, 200, { "cache-control": "public, max-age=30" });
  }
  if (!Number.isInteger(server)) return json({ error: "invalid" }, 400);
  const { results } = await env.DB.prepare(
    `SELECT boss, state, next_at AS next, since, src, reported FROM boss_live WHERE region = ?1 AND server = ?2 AND reported > ?3`
  ).bind(region, server, day).all();
  return json(results, 200, { "cache-control": "public, max-age=30" });
}

// ---------------------------------------------------------------- updates (patch notes)

// The meter's changelog (Settings → What's new) is the one list of every version, in German and English.
const CHANGELOG_SRC = "https://raw.githubusercontent.com/Peakmeter/PeakMeter/main/src/PeakMeter/SettingsWindow.xaml.cs";
const RELEASES = "https://api.github.com/repos/Peakmeter/PeakMeter/releases?per_page=100";

// Reads `Changelog => L.S("de…" + …, "en…" + …);` without running any C#: string literals only.
export function parseChangelog(src, english) {
  const start = src.indexOf("Changelog => L.S(");
  if (start < 0) return [];
  const tail = src.slice(start + "Changelog => L.S(".length);
  const end = /"\s*\);/.exec(tail);
  if (!end) return [];
  const args = tail.slice(0, end.index + 1).split(/"\s*,\s*\r?\n\s*"/);
  const literal = (s) => s.trim().replace(/^"/, "").replace(/"$/, "").replace(/"\s*\+\s*\r?\n\s*"/g, "")
    .replace(/\\n/g, "\n").replace(/\\"/g, '"').replace(/\\\\/g, "\\");
  const text = literal(args[english && args.length > 1 ? 1 : 0]);
  return text.split(/\n\s*\n+/).map((e) => {
    const m = /^v(\d+(?:\.\d+)*)\s*[–-]\s*([\s\S]+)$/.exec(e.trim());
    return m ? { v: m[1], text: m[2].replace(/\s+/g, " ").trim() } : null;
  }).filter(Boolean);
}

async function updates(url, ctx) {
  const english = url.searchParams.get("lang") !== "de";
  const key = new Request("https://cache.peakmeter/updates/v1/" + (english ? "en" : "de"));
  const cached = await caches.default.match(key);
  if (cached) return cached;
  let list = [];
  try {
    const r = await fetch(CHANGELOG_SRC, { headers: { "user-agent": "PeakMeter" }, cf: { cacheTtl: 600, cacheEverything: true } });
    if (r.ok) list = parseChangelog(await r.text(), english);
  } catch { /* GitHub down: an empty list, the page says so */ }
  const dates = {};
  try {
    const r = await fetch(RELEASES, { headers: { "user-agent": "PeakMeter", accept: "application/vnd.github+json" }, cf: { cacheTtl: 3600, cacheEverything: true } });
    if (r.ok) for (const x of await r.json()) if (!x.draft) dates[String(x.tag_name).replace(/^v/, "")] = x.published_at;
  } catch { /* no dates */ }
  const body = list.map((e) => ({ ...e, date: dates[e.v] || dates[e.v + ".0"] || null }));
  const res = json(body, list.length ? 200 : 502, { "cache-control": "public, max-age=600" });
  if (list.length) ctx.waitUntil(caches.default.put(key, res.clone()));
  return res;
}

// Built on first use: the bot shares the worker's own lookups.
let botHandler = null, alertsHandler = null;
const alertsMod = () => (alertsHandler ??= alerts({ json, REGIONS, hash, timersHtml: timers }));
const bot = () => (botHandler ??= discordBot({
  json, REGIONS, METRICS, source, short, group, CLASS_EN, OG_IMAGE, timersHtml: timers,
  ncSearchUrl, ncJson, strip, charData,
}));

// ---------------------------------------------------------------- shared fight reports
// The meter sends one finished fight when the player clicks "share as link". Only the fields below are kept, each
// checked and trimmed; the page renders them as text. Reports stay 180 days.
const REPORT_PER_HOUR = 30;
const REPORT_DAYS = 180;
const REPORT_CLASSES = new Set(["gladiator", "templar", "ranger", "assassin", "sorcerer", "elementalist", "cleric", "chanter", "brawler"]);
const num = (v, max = 1e15) => (typeof v === "number" && Number.isFinite(v) && v >= 0 ? Math.min(v, max) : 0);
const str = (v, max) => (typeof v === "string" ? v.replace(/[\u0000-\u001f\u007f]/g, "").slice(0, max) : "");

function cleanReport(b) {
  if (!b || typeof b !== "object" || !Array.isArray(b.players) || !b.players.length) return null;
  const region = typeof b.region === "string" && REGIONS.has(b.region) ? b.region : "eu";
  const players = b.players.slice(0, 24).map((p) => ({
    n: str(p?.n, 32) || "?",
    c: REPORT_CLASSES.has(p?.c) ? p.c : null,
    s: str(p?.s, 30) || null,
    self: p?.self === true,
    dmg: num(p?.dmg), dps: num(p?.dps), adps: num(p?.adps), share: num(p?.share, 1), hits: num(p?.hits, 1e7),
    crit: num(p?.crit, 1), max: num(p?.max), heal: num(p?.heal), hps: num(p?.hps), stag: num(p?.stag), taken: num(p?.taken),
    deaths: num(p?.deaths, 999), cp: num(p?.cp, 1e9), gs: num(p?.gs, 1e7),
    skills: Array.isArray(p?.skills) ? p.skills.slice(0, 16).map((k) => ({
      n: str(k?.n, 48) || "?", i: /^[A-Za-z0-9_]{1,64}$/.test(k?.i || "") ? k.i : null, h: num(k?.h, 1e7), d: num(k?.d),
      share: num(k?.share, 1), crit: k?.crit == null ? null : num(k.crit, 1), max: num(k?.max),
    })) : [],
    step: Math.max(1, Math.min(600, Math.round(num(p?.step, 600)) || 1)),
    ps: Array.isArray(p?.ps) ? p.ps.slice(0, 300).map((v) => num(v)) : [],
    deathList: Array.isArray(p?.deathList) ? p.deathList.slice(0, 5).map((d) => ({
      t: num(d?.t, 1e8),
      hits: Array.isArray(d?.hits) ? d.hits.slice(0, 8).map((h) => ({ t: num(h?.t, 1e8), src: str(h?.src, 40), sk: str(h?.sk, 48), d: num(h?.d) })) : [],
    })) : [],
  }));
  const boss = b.boss && typeof b.boss === "object" ? {
    code: Math.round(num(b.boss.code, 1e9)), name: str(b.boss.name, 60), maxHp: num(b.boss.maxHp), hp: num(b.boss.hp), known: b.boss.known === true,
  } : null;
  return {
    v: 1, region, lang: b.lang === "de" ? "de" : "en",
    title: str(b.title, 80) || boss?.name || "Fight", zone: str(b.zone, 60) || null,
    reason: ["kill", "wipe"].includes(b.reason) ? b.reason : "other",
    at: num(b.at, 4e12), ms: num(b.ms, 6 * 3600_000), total: num(b.total), partyDps: num(b.partyDps), partial: b.partial === true,
    boss, players,
  };
}

async function reportSave(req, env, url) {
  const raw = await req.text();
  if (raw.length > 400_000) return json({ error: "too big" }, 413);
  let r;
  try { r = cleanReport(JSON.parse(raw)); } catch { return json({ error: "bad json" }, 400); }
  if (!r || !(r.ms >= 1000)) return json({ error: "invalid" }, 400);
  const ip = await hash("r:" + (req.headers.get("cf-connecting-ip") || "?"));
  const hour = Math.floor(Date.now() / 3600_000);
  const row = await env.DB.prepare(
    "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
  ).bind(ip, hour).first();
  if (row && row.n > REPORT_PER_HOUR) return json({ error: "slow down" }, 429);
  const abc = "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  const id = [...bytes].map((x) => abc[x % abc.length]).join("");
  const now = Date.now();
  await env.DB.prepare("INSERT INTO reports (id, region, boss, title, at, created, data) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)")
    .bind(id, r.region, r.boss?.code || 0, r.title, r.at, now, JSON.stringify(r)).run();
  if (row && row.n === 1) await env.DB.prepare("DELETE FROM reports WHERE created < ?1").bind(now - REPORT_DAYS * DAY).run();
  return json({ id, url: `${url.origin}/r/${id}` });
}

async function loadReport(env, id) {
  if (!/^[A-Za-z0-9]{6,12}$/.test(id || "")) return null;
  const row = await env.DB.prepare("SELECT data FROM reports WHERE id = ?1").bind(id).first();
  return row ? JSON.parse(row.data) : null;
}

async function reportGet(url, env) {
  const r = await loadReport(env, url.searchParams.get("id"));
  return r ? json(r, 200, { "cache-control": "public, max-age=86400" }) : json({ error: "not found" }, 404);
}

const clock = (ms) => `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")}`;
async function reportCard(url, env, page) {
  const r = await loadReport(env, url.pathname.split("/")[2]);
  if (!r) return withCard(page, url, { title: "PeakMeter · Fight report", desc: "An AION 2 fight measured with PeakMeter." });
  const what = r.reason === "kill" ? "Kill" : r.reason === "wipe" ? "Wipe" : "Fight";
  const top = r.players.slice(0, 3).map((p, i) => `${i + 1}. ${p.n} ${short(p.dps)}`).join(" · ");
  return withCard(page, url, {
    title: `${r.title} · ${what} in ${clock(r.ms)} · ${short(r.partyDps)} DPS`,
    desc: `${top} — ${r.players.length} players${r.zone ? " · " + r.zone : ""}. Fight report from PeakMeter.`,
  });
}

// ---------------------------------------------------------------- census
// Meters send the players they saw in fights: fingerprint, class, server, combat power, gear score. Never a name.
const CENSUS_PER_HOUR = 120;
async function censusSave(req, env) {
  let b;
  try { b = await req.json(); } catch { return json({ error: "bad json" }, 400); }
  const region = typeof b?.region === "string" ? b.region.toLowerCase() : "";
  const list = Array.isArray(b?.players) ? b.players.slice(0, 50) : [];
  if (!REGIONS.has(region) || !list.length) return json({ error: "invalid" }, 400);
  const ip = await hash("c:" + (req.headers.get("cf-connecting-ip") || "?"));
  const hour = Math.floor(Date.now() / 3600_000);
  const row = await env.DB.prepare(
    "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
  ).bind(ip, hour).first();
  if (row && row.n > CENSUS_PER_HOUR) return json({ error: "slow down" }, 429);
  const now = Date.now();
  const stmts = [];
  for (const p of list) {
    const k = typeof p?.k === "string" && /^[0-9a-f]{16}$/.test(p.k) ? p.k : null;
    const server = int(p?.server);
    if (!k || !REPORT_CLASSES.has(p?.cls) || !(server > 0 && server < 100000)) continue;
    const cp = Number.isInteger(p.cp) && p.cp > 0 && p.cp < 10_000_000 ? p.cp : null;
    const gs = Number.isInteger(p.gs) && p.gs > 0 && p.gs < 1_000_000 ? p.gs : null;
    stmts.push(env.DB.prepare(
      `INSERT INTO census (region, k, server, server_name, cls, cp, gs, seen) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
       ON CONFLICT(region, k) DO UPDATE SET server_name = COALESCE(excluded.server_name, census.server_name), cls = excluded.cls,
         cp = COALESCE(excluded.cp, census.cp), gs = COALESCE(excluded.gs, census.gs), seen = excluded.seen`
    ).bind(region, k, server, cleanText(p.serverName, 30), p.cls, cp, gs, now));
  }
  if (stmts.length) await env.DB.batch(stmts);
  return json({ ok: true, taken: stmts.length });
}

async function gatherSave(req, env) {
  let b;
  try { b = await req.json(); } catch { return json({ error: "bad json" }, 400); }
  const region = typeof b?.region === "string" ? b.region.toLowerCase() : "";
  const items = Array.isArray(b?.items) ? b.items.slice(0, 60) : [];
  const crafted = Array.isArray(b?.crafted) ? b.crafted.slice(0, 60) : [];
  if (!REGIONS.has(region) || (!items.length && !crafted.length)) return json({ error: "invalid" }, 400);
  const ip = await hash("g:" + (req.headers.get("cf-connecting-ip") || "?"));
  const hour = Math.floor(Date.now() / 3600_000);
  const row = await env.DB.prepare(
    "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
  ).bind(ip, hour).first();
  if (row && row.n > 30) return json({ error: "slow down" }, 429);
  const day = new Date().toISOString().slice(0, 10);
  const stmts = [];
  for (const [table, list] of [["gather", items], ["craft", crafted]]) for (const it of list) {
    const item = int(it?.item), n = int(it?.n);
    if (!(item >= 100_000_000 && item < 1_000_000_000) || !(n > 0 && n <= 2000)) continue;
    stmts.push(env.DB.prepare(`INSERT INTO ${table} (region, day, item, n) VALUES (?1, ?2, ?3, ?4) ON CONFLICT(region, day, item) DO UPDATE SET n = n + excluded.n`)
      .bind(region, day, item, n));
    const name = cleanText(it?.name, 40);
    if (name) stmts.push(env.DB.prepare("INSERT OR IGNORE INTO gather_names (item, name) VALUES (?1, ?2)").bind(item, name));
  }
  if (stmts.length) await env.DB.batch(stmts);
  return json({ ok: true });
}

async function gatherGet(url, env) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  if (!REGIONS.has(region)) return json({ error: "invalid" }, 400);
  const days = [7, 30, 90].includes(Number(url.searchParams.get("days"))) ? Number(url.searchParams.get("days")) : 30;
  const from = new Date(Date.now() - days * DAY).toISOString().slice(0, 10);
  const { results } = await env.DB.prepare(
    `SELECT g.item AS item, MAX(nm.name) AS name, SUM(g.n) AS n FROM ${url.searchParams.get("kind") === "craft" ? "craft" : "gather"} g LEFT JOIN gather_names nm ON nm.item = g.item
     WHERE g.region = ?1 AND g.day >= ?2 GROUP BY g.item ORDER BY n DESC LIMIT 40`
  ).bind(region, from).all();
  // Names and icons from the item list (German where known), else what the meter sent.
  const de = url.searchParams.get("lang") === "de";
  for (const r of results) {
    const it = ITEMS[r.item];
    if (it) { r.name = (de && it.de) || it.en || r.name; r.icon = it.icon; }
  }
  return json(results, 200, { "cache-control": "public, max-age=300" });
}

async function censusGet(url, env) {
  const region = (url.searchParams.get("region") || "eu").toLowerCase();
  if (!REGIONS.has(region)) return json({ error: "invalid" }, 400);
  const days = [7, 30, 90].includes(Number(url.searchParams.get("days"))) ? Number(url.searchParams.get("days")) : 30;
  const server = Number(url.searchParams.get("server")) || 0;
  const since = Date.now() - days * DAY;
  const where = "region = ?1 AND seen > ?2 AND (?3 = 0 OR server = ?3)";
  const [classes, servers, buckets] = await env.DB.batch([
    env.DB.prepare(`SELECT cls, COUNT(*) AS n, AVG(cp) AS cp, MAX(cp) AS cpMax, AVG(gs) AS gs FROM census WHERE ${where} GROUP BY cls ORDER BY n DESC`).bind(region, since, server),
    env.DB.prepare(`SELECT server, MAX(server_name) AS name, COUNT(*) AS n, AVG(cp) AS cp FROM census WHERE region = ?1 AND seen > ?2 GROUP BY server ORDER BY n DESC LIMIT 80`).bind(region, since),
    env.DB.prepare(`SELECT CAST(cp / 10000 AS INTEGER) * 10000 AS b, COUNT(*) AS n FROM census WHERE ${where} AND cp > 0 GROUP BY b ORDER BY b`).bind(region, since, server),
  ]);
  const total = classes.results.reduce((a, r) => a + r.n, 0);
  return json({ region, days, server, total, classes: classes.results, servers: servers.results, cp: buckets.results }, 200, { "cache-control": "public, max-age=300" });
}

async function hash(s) {
  const d = await crypto.subtle.digest("SHA-256", new TextEncoder().encode("pm-rank:" + s));
  return [...new Uint8Array(d).slice(0, 8)].map((x) => x.toString(16).padStart(2, "0")).join("");
}
