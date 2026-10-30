// Best in slot, measured against Korea (a year ahead of the global servers): which soul-binding lines the strongest
// Korean players of each class carry on each slot.
//
// A slow crawler on the minute cron, a few official requests per tick, one cycle a week:
//   discover  search Korean characters by common syllables, keep max-level ones (class from pcId)
//   info      combat power of each max-level character
//   list      the top TOP_PER_CLASS of each class by combat power: their equipment list
//   detail    every equipped item: its soul-binding lines (and Arcana skills)
//   aggregate share of players carrying each line per class and slot, and the largest value seen
// Then GET /api/char/bis scores any character against it (see score()).

const NC = "https://aion2.plaync.com/api/character/";
const SEARCH = "https://api-search.plaync.com/aion2/search/v2/character";
// Gentle on NCSOFT and on D1's free daily limits (100k rows written, 5M read): about 15k writes a day at most.
const PER_TICK = 10;            // official requests per minute
const TOP_PER_CLASS = 50;
const PAGES_PER_WORD = 5;       // 40 names a page
const MIN_LEVEL = 50;           // Korea's level cap: only those count
const CYCLE_DAYS = 30;
// The first cycle went over the write limit on 2026-10-30; this one waits for the daily reset.
const RESUME_AT = Date.UTC(2026, 9, 31, 0, 5);
const PC_CLASSES = ["gladiator", "templar", "ranger", "assassin", "elementalist", "sorcerer", "cleric", "chanter"];
const classOf = (pcId) => (Number.isInteger(pcId) && pcId >= 5 ? PC_CLASSES[Math.floor((pcId - 5) / 4)] || null : null);
// Common syllables of Korean names: each search finds names containing it.
const WORDS = ("가 나 다 라 마 바 사 아 자 차 카 타 파 하 김 이 박 최 정 강 조 윤 장 임 한 오 서 신 권 황 안 송 류 홍 " +
  "전 고 문 양 손 배 백 허 유 남 심 노 별 달 꽃 빛 검 용 천 흑 은 금 불 물 루 린 연 진 현 민 수 지 우 혜 미 리 아이 소 영 " +
  "희 준 호 태 성 재 원 경 석 훈 빈 솔 결 람 비 율 온 늘 봄 밤 꿈 왕 신의 마법 기사 전사 궁수 성직").split(" ");
// Slots with two of a kind count as one slot.
export const slotKey = (s) => String(s || "").replace(/^(Earring|Ring|Bracelet)\d$/, "$1");
const num = (v) => { const n = parseFloat(String(v ?? "").replace(/[^\d.]/g, "")); return Number.isFinite(n) ? n : 0; };

export function korea(h) {
  const get = async (env, k, d) => (await env.DB.prepare("SELECT v FROM kr_job WHERE k = ?1").bind(k).first())?.v ?? d;
  const setStmt = (env, k, v) => env.DB.prepare("INSERT INTO kr_job (k, v) VALUES (?1, ?2) ON CONFLICT(k) DO UPDATE SET v = excluded.v").bind(k, String(v));
  const set = (env, k, v) => setStmt(env, k, v).run();
  const q = (cid, server) => `characterId=${encodeURIComponent(decodeURIComponent(cid))}&serverId=${server}`;
  const fetchJson = async (u) => { const r = await fetch(u); if (!r.ok) throw new Error("http " + r.status); return r.json(); };

  // Once: the item rows keep their lines as JSON (one write per item) and the cycle they belong to.
  async function migrate(env) {
    if (await get(env, "v2", null)) return;
    for (const sql of ["ALTER TABLE kr_items ADD COLUMN lines TEXT", "ALTER TABLE kr_items ADD COLUMN at INTEGER"])
      try { await env.DB.prepare(sql).run(); } catch {}
    await set(env, "v2", 1);
  }

  async function tick(env) {
    if (Date.now() < RESUME_AT && !env.KR_TEST) return;
    await migrate(env);
    const phase = await get(env, "phase", "discover");
    const started = Number(await get(env, "started", 0)) || Date.now();
    let budget = Number(env.KR_PER_TICK) || PER_TICK;
    // Local tests only: a small cycle (KR_WORDS words, KR_PAGES pages, KR_TOP players per class).
    const words = env.KR_WORDS ? WORDS.slice(0, Number(env.KR_WORDS)) : WORDS;
    const pages = Number(env.KR_PAGES) || PAGES_PER_WORD, topN = Number(env.KR_TOP) || TOP_PER_CLASS;
    const minLevel = Number(env.KR_MIN_LEVEL) || MIN_LEVEL;

    if (phase === "idle") {
      if (Date.now() < Number(await get(env, "next", 0))) return;
      // A new cycle: nothing is deleted (that costs a write per row); rows of the old cycle are told apart by time.
      return env.DB.batch([setStmt(env, "word", 0), setStmt(env, "page", 1), setStmt(env, "started", Date.now()),
        setStmt(env, "cursor", 0), setStmt(env, "phase", "discover")]);
    }
    if (phase === "discover") {
      let word = Number(await get(env, "word", 0)), page = Number(await get(env, "page", 1));
      const stmts = [];
      while (budget-- > 0 && word < words.length) {
        let list = [];
        try { list = (await fetchJson(`${SEARCH}?keyword=${encodeURIComponent(words[word])}&page=${page}&size=40`)).list || []; } catch {}
        for (const c of list)
          if (c.level >= minLevel && classOf(c.pcId))
            stmts.push(env.DB.prepare("INSERT OR IGNORE INTO kr_chars (cid, server, cls, level) VALUES (?1, ?2, ?3, ?4)").bind(c.characterId, c.serverId, classOf(c.pcId), c.level));
        if (list.length < 40 || page >= pages) { word++; page = 1; } else page++;
      }
      stmts.push(setStmt(env, "word", word), setStmt(env, "page", page), setStmt(env, "started", started));
      if (word >= words.length) stmts.push(setStmt(env, "phase", "info"), setStmt(env, "cursor", 0));
      return env.DB.batch(stmts);
    }
    if (phase === "info") {
      // Walk the table by rowid from a stored cursor: no full scan per tick.
      const cursor = Number(await get(env, "cursor", 0));
      const { results } = await env.DB.prepare(
        "SELECT rowid AS r, cid, server FROM kr_chars WHERE rowid > ?1 AND level >= ?2 AND (info_at IS NULL OR info_at < ?3) ORDER BY rowid LIMIT ?4")
        .bind(cursor, minLevel, started, budget).all();
      if (!results.length) return env.DB.batch([setStmt(env, "phase", "pick"), setStmt(env, "cursor", 0)]);
      const stmts = [];
      for (const c of results) {
        let cp = 0;
        try { cp = (await fetchJson(`${NC}info?lang=ko-KR&${q(c.cid, c.server)}`))?.profile?.combatPower || 0; } catch {}
        stmts.push(env.DB.prepare("UPDATE kr_chars SET cp = ?1, info_at = ?2 WHERE rowid = ?3").bind(cp, Date.now(), c.r));
      }
      stmts.push(setStmt(env, "cursor", results[results.length - 1].r));
      return env.DB.batch(stmts);
    }
    if (phase === "pick") {
      // Once per cycle: the strongest of each class, kept as a list to walk.
      const { results } = await env.DB.prepare(
        `SELECT cid, server FROM (SELECT cid, server, ROW_NUMBER() OVER (PARTITION BY cls ORDER BY cp DESC) AS rk FROM kr_chars
         WHERE cp > 0 AND level >= ?2 AND info_at >= ?3) WHERE rk <= ?1`).bind(topN, minLevel, started).all();
      return env.DB.batch([setStmt(env, "picked", JSON.stringify(results.map((c) => [c.cid, c.server]))), setStmt(env, "cursor", 0), setStmt(env, "phase", "list")]);
    }
    if (phase === "list") {
      const picked = JSON.parse(await get(env, "picked", "[]"));
      let i = Number(await get(env, "cursor", 0));
      const stmts = [];
      for (; budget > 0 && i < picked.length; budget--, i++) {
        const [cid, server] = picked[i];
        try {
          const eq = await fetchJson(`${NC}equipment?lang=ko-KR&${q(cid, server)}`);
          for (const it of eq?.equipment?.equipmentList || []) {
            if (/^(Rune|Amulet)/.test(it.slotPosName || "")) continue; // no lines
            stmts.push(env.DB.prepare(
              `INSERT INTO kr_items (cid, slot, item, enchant, slot_pos, done, lines, at) VALUES (?1, ?2, ?3, ?4, ?5, NULL, NULL, ?6)
               ON CONFLICT(cid, slot) DO UPDATE SET item = excluded.item, enchant = excluded.enchant, slot_pos = excluded.slot_pos, done = NULL, lines = NULL, at = excluded.at`)
              .bind(cid, it.slotPosName, it.id, it.enchantLevel || 0, it.slotPos, started));
          }
        } catch {}
      }
      stmts.push(setStmt(env, "cursor", i));
      if (i >= picked.length) stmts.push(setStmt(env, "phase", "detail"), setStmt(env, "cursor", 0));
      for (let k = 0; k < stmts.length; k += 90) await env.DB.batch(stmts.slice(k, k + 90));
      return;
    }
    if (phase === "detail") {
      const cursor = Number(await get(env, "cursor", 0));
      const { results } = await env.DB.prepare(
        `SELECT i.rowid AS r, i.cid, i.slot, i.item, i.enchant, i.slot_pos, c.server FROM kr_items i JOIN kr_chars c ON c.cid = i.cid
         WHERE i.rowid > ?1 AND i.done IS NULL AND (i.at >= ?2 OR i.at IS NULL) ORDER BY i.rowid LIMIT ?3`).bind(cursor, started, budget).all();
      if (!results.length) return set(env, "phase", "aggregate");
      const stmts = [];
      for (const it of results) {
        let lines = [];
        try {
          const d = await fetchJson(`${NC}equipment/item?lang=ko-KR&${q(it.cid, it.server)}&id=${it.item}&enchantLevel=${it.enchant}&slotPos=${it.slot_pos}`);
          lines = [...(d.subStats || []).filter((s) => s.id).map((s) => ["stat", s.id, num(s.value)]),
            ...(d.subSkills || []).filter((s) => s.id).map((s) => ["skill", String(s.id), Number(s.level) || 1])];
        } catch {}
        stmts.push(env.DB.prepare("UPDATE kr_items SET done = 1, lines = ?1 WHERE rowid = ?2").bind(JSON.stringify(lines), it.r));
      }
      stmts.push(setStmt(env, "cursor", results[results.length - 1].r));
      return env.DB.batch(stmts);
    }
    if (phase === "aggregate") {
      await aggregate(env, Number(env.KR_MIN) || 5, started);
      return env.DB.batch([setStmt(env, "built", Date.now()), setStmt(env, "next", started + CYCLE_DAYS * 86400_000), setStmt(env, "phase", "idle")]);
    }
  }

  // Per class and slot: how many of the top players carry each line, and the largest value any of them has.
  // Lines come from the item rows (JSON); the very first cycle also kept them as rows in kr_lines.
  async function aggregate(env, minPlayers, started) {
    const { results: items } = await env.DB.prepare(
      `SELECT i.cid AS cid, i.slot AS slot, i.lines AS lines, c.cls AS cls FROM kr_items i JOIN kr_chars c ON c.cid = i.cid
       WHERE i.done = 1 AND (i.at >= ?1 OR i.at IS NULL)`).bind(started).all();
    const legacy = new Map();
    if (items.some((it) => it.lines == null)) {
      const { results } = await env.DB.prepare("SELECT cid, slot, kind, line, value FROM kr_lines").all();
      for (const l of results) {
        const k = l.cid + "|" + l.slot;
        if (!legacy.has(k)) legacy.set(k, []);
        legacy.get(k).push([l.kind, l.line, l.value]);
      }
    }
    const owners = new Map(), carry = new Map();
    for (const it of items) {
      const slot = slotKey(it.slot), ok = it.cls + "|" + slot;
      if (!owners.has(ok)) owners.set(ok, new Set());
      owners.get(ok).add(it.cid);
      const lines = it.lines != null ? JSON.parse(it.lines) : legacy.get(it.cid + "|" + it.slot) || [];
      for (const [kind, line, value] of lines) {
        const k = ok + "|" + kind + "|" + line;
        const a = carry.get(k) || { cls: it.cls, slot, kind, line, who: new Set(), maxv: 0 };
        a.who.add(it.cid); a.maxv = Math.max(a.maxv, Number(value) || 0);
        carry.set(k, a);
      }
    }
    const stmts = [env.DB.prepare("DELETE FROM kr_share")];
    for (const a of carry.values()) {
      const players = owners.get(a.cls + "|" + a.slot)?.size || 0;
      if (players < minPlayers) continue;
      stmts.push(env.DB.prepare("INSERT INTO kr_share (cls, slot, kind, line, share, maxv, players) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)")
        .bind(a.cls, a.slot, a.kind, a.line, Math.min(1, a.who.size / players), a.maxv, players));
    }
    for (let i = 0; i < stmts.length; i += 90) await env.DB.batch(stmts.slice(i, i + 90));
  }

  async function status(env) {
    const keys = ["phase", "word", "cursor", "built", "started", "next"];
    const { results } = await env.DB.prepare(`SELECT k, v FROM kr_job WHERE k IN (${keys.map(() => "?").join(",")})`).bind(...keys).all();
    const o = Object.fromEntries(results.map((r) => [r.k, r.v]));
    const picked = JSON.parse((await env.DB.prepare("SELECT v FROM kr_job WHERE k = 'picked'").first())?.v || "[]").length;
    return { phase: o.phase || "discover", word: Number(o.word || 0), words: WORDS.length, cursor: Number(o.cursor || 0), picked,
      built: Number(o.built || 0), resumes: Date.now() < RESUME_AT ? new Date(RESUME_AT).toISOString() : null };
  }

  return { tick, status };
}

// ------------------------------------------------------------------ scoring
// A piece's line is worth share × min(1, value ÷ the largest Korean value). The best piece of a slot has the slot's most
// carried lines at their largest values, as many as the piece has lines. Two scores per piece:
//   pick   the lines alone (shares), ignoring their size: are these the lines the top players chose?
//   power  shares × size: how close the piece is to the best one in Korea
export function score(shares, items) {
  const bySlot = new Map();
  for (const s of shares) {
    const k = s.slot + "|" + s.kind;
    if (!bySlot.has(k)) bySlot.set(k, []);
    bySlot.get(k).push(s);
  }
  for (const list of bySlot.values()) list.sort((a, b) => b.share - a.share);
  const out = [];
  for (const it of items) {
    const slot = slotKey(it.slot);
    const res = { slot: it.slot, kinds: [] };
    for (const kind of ["stat", "skill"]) {
      const mine = kind === "stat" ? it.stats : it.skills;
      const ref = bySlot.get(slot + "|" + kind);
      if (!ref || !mine?.length) continue;
      const k = Math.max(mine.length, kind === "stat" ? (it.statCount || mine.length) : (it.skillCount || mine.length));
      const best = ref.slice(0, k).reduce((a, s) => a + s.share, 0) || 1;
      // Korea runs a different system on this slot (no line in common, e.g. bracelets): no score, only what they carry.
      const comparable = mine.some((m) => ref.some((s) => s.line === String(m.id)));
      let pick = 0, power = 0;
      const lines = mine.map((m) => {
        const r = ref.find((s) => s.line === String(m.id));
        const share = r?.share || 0;
        const size = r?.maxv ? Math.min(1, m.value / r.maxv) : 0;
        pick += share; power += share * size;
        return { id: String(m.id), name: m.name, value: m.raw ?? m.value, share, size };
      });
      const missing = ref.filter((s) => s.share >= 0.3 && !mine.some((m) => String(m.id) === s.line)).slice(0, 4)
        .map((s) => ({ id: s.line, share: s.share }));
      res.kinds.push({
        kind, comparable, pick: comparable ? Math.round(pick / best * 100) : null, power: comparable ? Math.round(power / best * 100) : null,
        lines, missing: comparable ? missing : ref.slice(0, 4).map((s) => ({ id: s.line, share: s.share })), players: ref[0]?.players || 0,
      });
    }
    if (res.kinds.length) out.push(res);
  }
  return out;
}
