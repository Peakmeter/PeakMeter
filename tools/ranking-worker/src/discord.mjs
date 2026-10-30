// PeakMeter's Discord bot: slash commands answered right here in the worker (Discord's "interactions endpoint").
// No gateway, no bot token at runtime: Discord POSTs each command to /discord, signed with the app's key, and the
// answer goes back in the response (or, for slow lookups, through the interaction's own webhook token).
//   /boss     live world boss timers of one server (from the meters that play there)
//   /player   a player's card: class, level, combat power, gear grade, best kills
//   /rank     top 10 of a dungeon boss (DPS, healing or stagger; all time or this week)
//   /peakmeter  what PeakMeter is, with the download
// Setup (once): wrangler.toml [vars] DISCORD_APP_ID + DISCORD_PUBLIC_KEY, then `node discord/register.mjs`.

const API = "https://discord.com/api/v10";
const COLOR = 0x7fd8ff;
const SITE = "https://peakmeter-ranking.peakmeter.workers.dev";
const BOT_REGIONS = [["EU", "eu"], ["NA East", "nae"], ["NA West", "naw"], ["Korea", "kr"], ["Taiwan", "tw"]];
const SEARCH_REGIONS = new Set(["eu", "nae", "naw", "kr"]);

export function discordBot(h) {
  let bossTable = null;
  const bossNames = () => {
    if (!bossTable) {
      bossTable = new Map();
      try {
        const m = h.timersHtml.match(/const BOSSES = (\[.*\]);/);
        for (const [code, en, de, min, area] of JSON.parse(m[1]))
          bossTable.set(code, { en, de, min, area: area || (code >= 2400000 ? "Altgard" : "Verteron") });
      } catch {}
    }
    return bossTable;
  };

  return async function handle(req, env, ctx) {
    if (req.method === "GET") {
      if (!env.DISCORD_APP_ID) return h.json({ error: "bot not set up" }, 503);
      return Response.redirect(`https://discord.com/oauth2/authorize?client_id=${env.DISCORD_APP_ID}&scope=applications.commands`, 302);
    }
    if (req.method !== "POST") return h.json({ error: "method" }, 405);
    if (!env.DISCORD_PUBLIC_KEY) return h.json({ error: "bot not set up" }, 503);

    const body = await req.text();
    if (!(await verify(env.DISCORD_PUBLIC_KEY, req.headers.get("x-signature-ed25519"), req.headers.get("x-signature-timestamp"), body)))
      return new Response("bad signature", { status: 401 });
    const it = JSON.parse(body);

    if (it.type === 1) return reply({ type: 1 }); // PING
    const de = String(it.locale || it.guild_locale || "").startsWith("de");
    const t = (a, b) => (de ? a : b);
    const name = it.data?.name;
    const opts = Object.fromEntries((it.data?.options || []).map((o) => [o.name, o]));
    const val = (k, d) => (opts[k]?.value ?? d);

    if (it.type === 4) return reply({ type: 8, data: { choices: await autocomplete(env, name, opts, val, de) } });
    if (it.type !== 2) return reply({ type: 4, data: { content: "?", flags: 64 } });

    try {
      if (name === "peakmeter") return reply({ type: 4, data: aboutMessage(t) });
      if (name === "boss") return reply({ type: 4, data: await bossMessage(env, val("region", "eu"), val("server", ""), t, de) });
      if (name === "rank")
        return reply({ type: 4, data: await rankMessage(env, val("region", "eu"), Number(val("boss", 0)), val("metric", "dps"), val("period", "all"), t) });
      if (name === "player") {
        // NCSOFT can take a few seconds: answer "thinking…" now, fill it in afterwards.
        ctx.waitUntil(followUp(env.DISCORD_API || API, it, () => playerMessage(req, env, ctx, val("region", "eu"), String(val("name", "")).trim(), String(val("server", "")).trim(), t)));
        return reply({ type: 5 });
      }
    } catch {
      return reply({ type: 4, data: { content: t("Da ist etwas schiefgelaufen. Versuch es gleich noch mal.", "Something went wrong. Try again in a moment."), flags: 64 } });
    }
    return reply({ type: 4, data: { content: "?", flags: 64 } });
  };

  // ------------------------------------------------------------ autocomplete
  async function autocomplete(env, cmd, opts, val, de) {
    const focused = Object.values(opts).find((o) => o.focused);
    if (!focused) return [];
    const q = String(focused.value || "").toLowerCase();
    const region = val("region", "eu");
    if (!h.REGIONS.has(region)) return [];
    if (cmd === "boss" && focused.name === "server") {
      const { results } = await env.DB.prepare(
        `SELECT server, MAX(server_name) AS name, COUNT(*) AS n FROM boss_live WHERE region = ?1 AND reported > ?2 GROUP BY server ORDER BY MAX(reported) DESC LIMIT 100`
      ).bind(region, Date.now() - 2 * 86400_000).all();
      return results
        .filter((r) => !q || String(r.name || r.server).toLowerCase().includes(q))
        .slice(0, 25)
        .map((r) => ({ name: `${r.name || "Server " + r.server} · ${r.n} ${de ? "Bosse" : "bosses"}`.slice(0, 100), value: String(r.server) }));
    }
    if (cmd === "rank" && focused.name === "boss") {
      const { results } = await env.DB.prepare(
        `SELECT DISTINCT b.boss AS boss, o.name AS name, o.zone AS zone FROM best b JOIN bosses o ON o.boss = b.boss WHERE b.region = ?1 ORDER BY o.zone, o.name`
      ).bind(region).all();
      return results
        .filter((r) => !q || (r.name + " " + (r.zone || "")).toLowerCase().includes(q))
        .slice(0, 25)
        .map((r) => ({ name: (r.zone ? `${r.name} · ${r.zone}` : r.name).slice(0, 100), value: String(r.boss) }));
    }
    return [];
  }

  // ------------------------------------------------------------ /boss
  async function bossMessage(env, region, server, t, de) {
    if (!h.REGIONS.has(region)) return { content: "?", flags: 64 };
    const day = Date.now() - 2 * 86400_000;
    const { results: servers } = await env.DB.prepare(
      `SELECT server, MAX(server_name) AS name, COUNT(*) AS n, MAX(reported) AS last FROM boss_live WHERE region = ?1 AND reported > ?2 GROUP BY server ORDER BY last DESC`
    ).bind(region, day).all();
    const link = `${SITE}/timers`;
    if (!servers.length)
      return {
        embeds: [{
          color: COLOR, title: t("Weltbosse", "World bosses") + " · " + region.toUpperCase(),
          description: t(
            "Aus dieser Region kam noch keine Live-Meldung. Sobald jemand mit PeakMeter dort spielt, erscheint sein Server hier.",
            "No live report from this region yet. As soon as someone plays there with PeakMeter, their server shows up here."),
          url: link,
        }],
        components: buttons([[t("Meter laden", "Get the meter"), SITE + "/"]]),
      };
    const srv = servers.find((s) => String(s.server) === String(server) || String(s.name || "").toLowerCase() === String(server).toLowerCase());
    if (!srv)
      return {
        embeds: [{
          color: COLOR, title: t("Weltbosse", "World bosses") + " · " + region.toUpperCase(), url: link,
          description: t("Wähle einen Server:", "Pick a server:") + "\n" +
            servers.slice(0, 25).map((s) => `• **${s.name || "Server " + s.server}** · ${s.n} ${t("Bosse", "bosses")} · <t:${Math.floor(s.last / 1000)}:R>`).join("\n"),
          footer: { text: t("/boss server:<Name> zeigt die Timer eines Servers", "/boss server:<name> shows one server's timers") },
        }],
      };

    const { results } = await env.DB.prepare(
      `SELECT boss, state, next_at AS next, since, src FROM boss_live WHERE region = ?1 AND server = ?2 AND reported > ?3`
    ).bind(region, srv.server, day).all();
    const now = Date.now();
    const names = bossNames();
    const nm = (code) => { const b = names.get(code); return b ? (de ? b.de : b.en) : "#" + code; };
    const ts = (ms, f) => `<t:${Math.floor(ms / 1000)}:${f}>`;
    const up = results.filter((r) => r.state === "up");
    const due = results.filter((r) => r.state === "down" && r.next <= now).sort((a, b) => b.next - a.next);
    const soon = results.filter((r) => r.state === "down" && r.next > now).sort((a, b) => a.next - b.next);
    const fields = [];
    const add = (title, list) => {
      if (!list.length) return;
      let v = "";
      for (const line of list) {
        if ((v + line + "\n").length > 1000) { v += "…"; break; }
        v += line + "\n";
      }
      fields.push({ name: title, value: v.trim() });
    };
    add("🟢 " + t("Lebt", "Alive"), up.map((r) => `**${nm(r.boss)}**` + (r.since ? ` · ${t("seit", "since")} ${ts(r.since, "t")}` : "")));
    add("⏳ " + t("Kommt als Nächstes", "Coming next"), soon.slice(0, 15).map((r) => `**${nm(r.boss)}** · ${ts(r.next, "R")} (${ts(r.next, "t")})`));
    add("🟠 " + t("Sollte da sein", "Should be up"), due.slice(0, 10).map((r) => `**${nm(r.boss)}** · ${t("seit", "since")} ${ts(r.next, "t")}`));
    return {
      embeds: [{
        color: COLOR,
        title: `${t("Weltbosse", "World bosses")} · ${srv.name || "Server " + srv.server} (${region.toUpperCase()})`,
        url: link,
        description: t(
          `Live von PeakMeter-Spielern auf diesem Server · zuletzt gemeldet ${ts(srv.last, "R")}`,
          `Live from PeakMeter players on this server · last report ${ts(srv.last, "R")}`),
        fields: fields.length ? fields : [{ name: "—", value: t("Gerade nichts Aktuelles.", "Nothing current right now.") }],
        footer: { text: t("Zeiten in deiner Zeitzone · PeakMeter", "Times in your time zone · PeakMeter") },
      }],
      components: buttons([[t("Alle Timer", "All timers"), link]]),
    };
  }

  // ------------------------------------------------------------ /rank
  async function rankMessage(env, region, boss, metric, period, t) {
    if (!h.REGIONS.has(region) || !Number.isInteger(boss) || boss <= 0 || !h.METRICS.includes(metric)) return { content: t("Wähle einen Boss aus der Liste.", "Pick a boss from the list."), flags: 64 };
    const o = await env.DB.prepare("SELECT name, zone FROM bosses WHERE boss = ?1").bind(boss).first();
    if (!o) return { content: t("Diesen Boss kenne ich nicht.", "I don't know that boss."), flags: 64 };
    const { table, where } = h.source(new URL(SITE + "/?period=" + (period === "week" ? "week" : "all")));
    const { results } = await env.DB.prepare(
      `SELECT name, server_name AS server, cls, value, dur_ms AS durMs FROM ${table} WHERE ${where}region = ?1 AND boss = ?2 AND metric = ?3 ORDER BY value DESC LIMIT 10`
    ).bind(region, boss, metric).all();
    const label = { dps: "DPS", hps: t("Heilung", "Healing"), stagger: t("Betäubung", "Stagger") }[metric];
    const medal = ["🥇", "🥈", "🥉"];
    const mmss = (ms) => `${Math.floor(ms / 60000)}:${String(Math.floor(ms / 1000) % 60).padStart(2, "0")}`;
    const lines = results.map((r, i) =>
      `${medal[i] || `\`${String(i + 1).padStart(2)}\``} **${r.name}** · ${r.server || "?"}${r.cls ? " · " + (h.CLASS_EN[String(r.cls).toLowerCase()] || r.cls) : ""} — **${h.short(r.value)}**${r.durMs ? ` (${mmss(r.durMs)})` : ""}`);
    const link = `${SITE}/ranking?region=${region}&boss=${boss}&metric=${metric}` + (period === "week" ? "&period=week" : "");
    return {
      embeds: [{
        color: COLOR,
        title: `${o.name} · ${label}${period === "week" ? " · " + t("diese Woche", "this week") : ""} (${region.toUpperCase()})`,
        url: link,
        description: lines.join("\n") || t("Noch keine Einträge – sei der Erste.", "No entries yet – be the first."),
        footer: { text: (o.zone ? o.zone + " · " : "") + t("Gemessen mit PeakMeter", "Measured with PeakMeter") },
      }],
      components: buttons([[t("Ganze Rangliste", "Full ranking"), link]]),
    };
  }

  // ------------------------------------------------------------ /player
  async function playerMessage(req, env, ctx, region, name, server, t) {
    if (!SEARCH_REGIONS.has(region)) return { content: t("Spielersuche gibt es für EU, NA und Korea.", "Player search works for EU, NA and Korea.") };
    if (name.length < 2 || name.length > 24) return { content: t("Gib einen Namen an.", "Give a name.") };
    let others = [];
    if (!server) {
      const u = h.ncSearchUrl(region, name);
      let list = [];
      try { list = (await h.ncJson(u)).list || []; } catch {}
      const exact = list.filter((c) => h.strip(c.name).toLowerCase() === name.toLowerCase());
      if (!exact.length) return { content: t(`Keinen Spieler „${name}“ gefunden.`, `No player "${name}" found.`) };
      exact.sort((a, b) => (b.level || 0) - (a.level || 0));
      server = exact[0].serverName || String(exact[0].serverId);
      others = exact.slice(1, 6).map((c) => c.serverName).filter(Boolean);
    }
    const d = await h.charData(new URL(req.url), env, ctx, region, server, name);
    if (!d) return { content: t(`Keinen Spieler „${name}“ auf ${server} gefunden.`, `No player "${name}" on ${server} found.`) };
    const p = d.profile || {};
    const link = `${SITE}/player/${region}/${encodeURIComponent(d.server)}/${encodeURIComponent(d.name)}`;
    const cls = h.CLASS_EN[p.cls] || p.className || "";
    const fields = [];
    if (p.level) fields.push({ name: t("Stufe", "Level"), value: String(p.level), inline: true });
    if (cls) fields.push({ name: t("Klasse", "Class"), value: cls, inline: true });
    if (p.cp) fields.push({ name: t("Kampfkraft", "Combat power"), value: h.group(p.cp), inline: true });
    if (d.grade?.tier) fields.push({ name: t("Ausrüstungs-Note", "Gear grade"), value: `**${d.grade.tier}** · ${t("besser als", "better than")} ${Math.round(d.grade.pct ?? 0)} %`, inline: true });
    if (p.title?.name) fields.push({ name: t("Titel", "Title"), value: p.title.name, inline: true });
    const best = (d.bests || []).filter((b) => b.metric === "dps").sort((a, b) => a.place - b.place).slice(0, 5);
    if (best.length)
      fields.push({
        name: t("Beste Kills (DPS)", "Best kills (DPS)"),
        value: best.map((b) => `**${b.bossName}** — ${h.short(b.value)} · ${t("Platz", "place")} ${b.place}/${b.total}`).join("\n"),
      });
    return {
      embeds: [{
        color: COLOR,
        author: { name: `${d.server} · ${region.toUpperCase()}` },
        title: d.name,
        url: link,
        thumbnail: p.img ? { url: p.img } : undefined,
        fields,
        footer: { text: (others.length ? t("Auch auf: ", "Also on: ") + others.join(", ") + " · " : "") + "PeakMeter" },
      }],
      components: buttons([[t("Profil öffnen", "Open profile"), link], [t("Vergleichen", "Compare"), `${SITE}/compare?r=${region}&p1=${encodeURIComponent(d.server + "/" + d.name)}`]]),
    };
  }

  // ------------------------------------------------------------ /peakmeter
  function aboutMessage(t) {
    return {
      embeds: [{
        color: COLOR,
        title: t("PeakMeter – kostenloser DPS-Meter für AION 2", "PeakMeter – free DPS meter for AION 2"),
        url: SITE + "/",
        description: t(
          "Live-DPS, Heilung und Betäubung, dein bester Kill, Leerlaufzeit, Todes-Rückblick, Rangliste pro Boss, Spielerprofile und Weltboss-Timer.",
          "Live DPS, healing and stagger, your best kill, idle time, death recap, a ranking per boss, player profiles and world boss timers."),
        image: { url: h.OG_IMAGE },
      }],
      components: buttons([[t("Download", "Download"), SITE + "/"], [t("Rangliste", "Ranking"), SITE + "/ranking"], [t("Timer", "Timers"), SITE + "/timers"]]),
    };
  }
}

// ------------------------------------------------------------ plumbing
const reply = (body) => new Response(JSON.stringify(body), { headers: { "content-type": "application/json" } });
const buttons = (list) => [{ type: 1, components: list.map(([label, url]) => ({ type: 2, style: 5, label, url })) }];

async function followUp(api, it, make) {
  let data;
  try { data = await make(); } catch { data = { content: "Something went wrong. Try again in a moment." }; }
  await fetch(`${api}/webhooks/${it.application_id}/${it.token}/messages/@original`, {
    method: "PATCH", headers: { "content-type": "application/json" }, body: JSON.stringify(data),
  });
}

const hex = (s) => new Uint8Array((s.match(/../g) || []).map((b) => parseInt(b, 16)));
let keyCache = null;
async function verify(publicKey, sig, timestamp, body) {
  if (!sig || !timestamp || !/^[0-9a-f]{128}$/i.test(sig)) return false;
  try {
    if (!keyCache || keyCache.raw !== publicKey)
      keyCache = { raw: publicKey, key: await crypto.subtle.importKey("raw", hex(publicKey), { name: "Ed25519" }, false, ["verify"]) };
    return await crypto.subtle.verify("Ed25519", keyCache.key, hex(sig), new TextEncoder().encode(timestamp + body));
  } catch {
    return false;
  }
}

// The command list, shared with discord/register.mjs.
const region = (desc, descDe, choices = BOT_REGIONS) => ({
  type: 3, name: "region", description: desc, description_localizations: { de: descDe }, required: false,
  choices: choices.map(([name, value]) => ({ name, value })),
});
export const COMMANDS = [
  {
    name: "boss", description: "Live world boss timers of a server", description_localizations: { de: "Live-Weltboss-Timer eines Servers" },
    options: [
      { type: 3, name: "server", description: "Server (leave empty for the list)", description_localizations: { de: "Server (leer lassen für die Liste)" }, autocomplete: true },
      region("Region (default EU)", "Region (Standard EU)"),
    ],
  },
  {
    name: "player", description: "An AION 2 player's card", description_localizations: { de: "Karte eines AION-2-Spielers" },
    options: [
      { type: 3, name: "name", description: "Character name", description_localizations: { de: "Charaktername" }, required: true, min_length: 2, max_length: 24 },
      { type: 3, name: "server", description: "Server (optional)", description_localizations: { de: "Server (optional)" }, max_length: 30 },
      region("Region (default EU)", "Region (Standard EU)", BOT_REGIONS.filter(([, v]) => SEARCH_REGIONS.has(v))),
    ],
  },
  {
    name: "rank", description: "Top 10 of a dungeon boss", description_localizations: { de: "Top 10 eines Dungeon-Bosses" },
    options: [
      { type: 3, name: "boss", description: "Boss", description_localizations: { de: "Boss" }, required: true, autocomplete: true },
      {
        type: 3, name: "metric", description: "What to rank (default DPS)", description_localizations: { de: "Wonach (Standard DPS)" },
        choices: [{ name: "DPS", value: "dps" }, { name: "Healing", name_localizations: { de: "Heilung" }, value: "hps" }, { name: "Stagger", name_localizations: { de: "Betäubung" }, value: "stagger" }],
      },
      {
        type: 3, name: "period", description: "All time or this week", description_localizations: { de: "Gesamt oder diese Woche" },
        choices: [{ name: "All time", name_localizations: { de: "Gesamt" }, value: "all" }, { name: "This week", name_localizations: { de: "Diese Woche" }, value: "week" }],
      },
      region("Region (default EU)", "Region (Standard EU)"),
    ],
  },
  { name: "peakmeter", description: "What PeakMeter is, with the download", description_localizations: { de: "Was PeakMeter ist, mit Download" } },
];
