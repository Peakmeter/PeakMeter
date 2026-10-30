// World boss alerts in Discord: a guild pastes a channel webhook on the timers page, picks its server and how many
// minutes ahead it wants to know. Every minute (cron) the worker looks at that server's live timers and posts
// "comes in N min" and "is up". Webhooks that Discord deleted are dropped on the first 404.
//   POST /api/alerts  { webhook, region, server, lead, lang, on }   on=false turns it off
//   scheduled()       the minute tick

const SITE = "https://peakmeter-ranking.peakmeter.workers.dev";
const LOGO = "https://raw.githubusercontent.com/Peakmeter/PeakMeter/main/docs/logo.png";
const HOOK = /^https:\/\/(?:ptb\.|canary\.)?discord(?:app)?\.com\/api(?:\/v\d+)?\/webhooks\/(\d{15,22})\/([A-Za-z0-9_-]{30,100})$/;
const LEADS = [1, 3, 5, 10, 15];
const SETUPS_PER_HOUR = 20;

export function alerts(h) {
  let names = null;
  const bossName = (code, de) => {
    if (!names) {
      names = new Map();
      try {
        for (const [c, en, dn] of JSON.parse(h.timersHtml.match(/const BOSSES = (\[.*\]);/)[1])) names.set(c, { en, de: dn });
      } catch {}
    }
    const b = names.get(code);
    return b ? (de ? b.de : b.en) : "#" + code;
  };

  // DISCORD_WEBHOOK_BASE only for local tests (a catcher instead of discord.com).
  async function post(env, hookId, token, body) {
    return fetch(`${env.DISCORD_WEBHOOK_BASE || "https://discord.com"}/api/webhooks/${hookId}/${token}`, {
      method: "POST", headers: { "content-type": "application/json" },
      body: JSON.stringify({ username: "PeakMeter", avatar_url: LOGO, allowed_mentions: { parse: [] }, ...body }),
    });
  }

  async function save(req, env) {
    let b;
    try { b = await req.json(); } catch { return h.json({ error: "bad json" }, 400); }
    const m = typeof b?.webhook === "string" ? b.webhook.trim().match(HOOK) : null;
    if (!m) return h.json({ error: "webhook" }, 400);
    const [, hookId, token] = m;
    const de = b.lang === "de";
    const ip = await h.hash("a:" + (req.headers.get("cf-connecting-ip") || "?"));
    const hour = Math.floor(Date.now() / 3600_000);
    const row = await env.DB.prepare(
      "INSERT INTO hits (ip, hour, n) VALUES (?1, ?2, 1) ON CONFLICT(ip, hour) DO UPDATE SET n = n + 1 RETURNING n"
    ).bind(ip, hour).first();
    if (row && row.n > SETUPS_PER_HOUR) return h.json({ error: "slow down" }, 429);

    if (b.on === false) {
      const had = await env.DB.prepare("DELETE FROM alerts WHERE id = ?1 AND token = ?2 RETURNING id").bind(hookId, token).first();
      if (had) await post(env, hookId, token, { content: de ? "🔕 PeakMeter-Boss-Alarme sind aus." : "🔕 PeakMeter boss alerts are off." }).catch(() => {});
      return h.json({ ok: true, off: true });
    }

    const region = typeof b.region === "string" && h.REGIONS.has(b.region) ? b.region : null;
    const server = Number.isInteger(b.server) && b.server > 0 && b.server < 100000 ? b.server : null;
    const lead = LEADS.includes(b.lead) ? b.lead : 5;
    if (!region || !server) return h.json({ error: "invalid" }, 400);
    const srv = await env.DB.prepare("SELECT MAX(server_name) AS name FROM boss_live WHERE region = ?1 AND server = ?2").bind(region, server).first();
    const srvName = srv?.name || "Server " + server;

    // Prove the webhook works before keeping it: this is also the "it's on" message in the channel.
    const r = await post(env, hookId, token, {
      embeds: [{
        color: 0x7fd8ff,
        title: de ? `🔔 Boss-Alarme aktiv · ${srvName}` : `🔔 Boss alerts on · ${srvName}`,
        description: de
          ? `Dieser Kanal bekommt eine Nachricht **${lead} Min** bevor ein Weltboss auf **${srvName}** erscheint, und wenn er lebt.\nDie Zeiten kommen live von PeakMeter-Spielern auf diesem Server.`
          : `This channel gets a message **${lead} min** before a world boss appears on **${srvName}**, and when it is up.\nThe times come live from PeakMeter players on this server.`,
        url: SITE + "/timers",
        footer: { text: de ? "Abschalten: auf der Timer-Seite oder den Webhook löschen" : "Turn off: on the timers page, or delete the webhook" },
      }],
    }).catch(() => null);
    if (!r || !r.ok) return h.json({ error: "discord" }, 400);
    await env.DB.prepare(
      `INSERT INTO alerts (id, token, region, server, lead, lang, created) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)
       ON CONFLICT(id) DO UPDATE SET token = excluded.token, region = excluded.region, server = excluded.server, lead = excluded.lead, lang = excluded.lang`
    ).bind(hookId, token, region, server, lead, de ? "de" : "en", Date.now()).run();
    return h.json({ ok: true, server: srvName, lead });
  }

  // Every minute: what changed since the last tick, per alert. Each (alert, boss, moment) is posted once.
  async function tick(env) {
    const { results: list } = await env.DB.prepare("SELECT id, token, region, server, lead, lang FROM alerts").all();
    if (!list.length) return;
    const now = Date.now();
    const groups = new Map();
    for (const a of list) {
      const k = a.region + "|" + a.server;
      if (!groups.has(k)) groups.set(k, []);
      groups.get(k).push(a);
    }
    for (const [k, alertsHere] of groups) {
      const [region, server] = k.split("|");
      const { results: timers } = await env.DB.prepare(
        "SELECT boss, state, next_at AS next, since, src FROM boss_live WHERE region = ?1 AND server = ?2 AND reported > ?3"
      ).bind(region, Number(server), now - 6 * 3600_000).all();
      for (const a of alertsHere) {
        const de = a.lang === "de";
        const due = [];
        for (const t of timers) {
          if (t.state === "down" && t.next && t.next - now <= a.lead * 60_000 && t.next - now > -60_000)
            due.push({ t, key: "soon:" + t.next, line: `⏳ **${bossName(t.boss, de)}** ${de ? "kommt" : "comes"} <t:${Math.floor(t.next / 1000)}:R> (<t:${Math.floor(t.next / 1000)}:t>)` });
          if (t.state === "up" && t.since && now - t.since < 10 * 60_000)
            due.push({ t, key: "up:" + t.since, line: `🟢 **${bossName(t.boss, de)}** ${de ? "lebt" : "is up"}` });
        }
        if (!due.length) continue;
        const fresh = [];
        for (const d of due) {
          const r = await env.DB.prepare("INSERT OR IGNORE INTO alert_sent (id, boss, k, at) VALUES (?1, ?2, ?3, ?4) RETURNING id")
            .bind(a.id, d.t.boss, d.key, now).first();
          if (r) fresh.push(d.line);
        }
        if (!fresh.length) continue;
        const r = await post(env, a.id, a.token, { content: fresh.join("\n") }).catch(() => null);
        if (r && (r.status === 404 || r.status === 401)) await env.DB.prepare("DELETE FROM alerts WHERE id = ?1").bind(a.id).run();
      }
    }
    await env.DB.prepare("DELETE FROM alert_sent WHERE at < ?1").bind(now - 3 * 86400_000).run();
  }

  return { save, tick };
}
