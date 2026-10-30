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
const PER_TICK = 20;            // official requests per minute: gentle on NCSOFT's servers
const TOP_PER_CLASS = 50;
const PAGES_PER_WORD = 5;       // 40 names a page
const CYCLE_DAYS = 7;
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
  const set = (env, k, v) => env.DB.prepare("INSERT INTO kr_job (k, v) VALUES (?1, ?2) ON CONFLICT(k) DO UPDATE SET v = excluded.v").bind(k, String(v)).run();
  const q = (cid, server) => `characterId=${encodeURIComponent(decodeURIComponent(cid))}&serverId=${server}`;
  const fetchJson = async (u) => { const r = await fetch(u); if (!r.ok) throw new Error("http " + r.status); return r.json(); };

  async function tick(env) {
    const phase = await get(env, "phase", "discover");
    let budget = PER_TICK;
    // Local tests only: a small cycle (KR_WORDS words, KR_PAGES pages, KR_TOP players per class).
    const words = env.KR_WORDS ? WORDS.slice(0, Number(env.KR_WORDS)) : WORDS;
    const pages = Number(env.KR_PAGES) || PAGES_PER_WORD, topN = Number(env.KR_TOP) || TOP_PER_CLASS;
    if (phase === "idle") {
      if (Date.now() < Number(await get(env, "next", 0))) return;
      await env.DB.batch([env.DB.prepare("DELETE FROM kr_chars"), env.DB.prepare("DELETE FROM kr_items"), env.DB.prepare("DELETE FROM kr_lines")]);
      await set(env, "word", 0); await set(env, "page", 1); await set(env, "started", Date.now());
      return set(env, "phase", "discover");
    }
    if (phase === "discover") {
      let word = Number(await get(env, "word", 0)), page = Number(await get(env, "page", 1));
      if (!Number(await get(env, "started", 0))) await set(env, "started", Date.now());
      while (budget-- > 0 && word < words.length) {
        let list = [];
        try { list = (await fetchJson(`${SEARCH}?keyword=${encodeURIComponent(words[word])}&page=${page}&size=40`)).list || []; } catch {}
        const stmts = list.filter((c) => c.level >= 45 && classOf(c.pcId)).map((c) => env.DB.prepare(
          "INSERT OR IGNORE INTO kr_chars (cid, server, cls, level) VALUES (?1, ?2, ?3, ?4)").bind(c.characterId, c.serverId, classOf(c.pcId), c.level));
        if (stmts.length) await env.DB.batch(stmts);
        if (list.length < 40 || page >= pages) { word++; page = 1; } else page++;
      }
      await set(env, "word", word); await set(env, "page", page);
      if (word >= words.length) await set(env, "phase", "info");
      return;
    }
    if (phase === "info") {
      const top = (await env.DB.prepare("SELECT MAX(level) AS m FROM kr_chars").first())?.m || 50;
      const { results } = await env.DB.prepare("SELECT cid, server FROM kr_chars WHERE level >= ?1 AND info_at IS NULL LIMIT ?2").bind(top, budget).all();
      if (!results.length) return set(env, "phase", "list");
      const stmts = [];
      for (const c of results) {
        let cp = 0;
        try { cp = (await fetchJson(`${NC}info?lang=ko-KR&${q(c.cid, c.server)}`))?.profile?.combatPower || 0; } catch {}
        stmts.push(env.DB.prepare("UPDATE kr_chars SET cp = ?1, info_at = ?2 WHERE cid = ?3").bind(cp, Date.now(), c.cid));
      }
      await env.DB.batch(stmts);
      return;
    }
    if (phase === "list") {
      // The strongest of each class not listed yet.
      const { results } = await env.DB.prepare(
        `SELECT cid, server FROM (SELECT cid, server, listed, ROW_NUMBER() OVER (PARTITION BY cls ORDER BY cp DESC) AS rk FROM kr_chars WHERE cp > 0)
         WHERE rk <= ?1 AND listed IS NULL LIMIT ?2`).bind(topN, budget).all();
      if (!results.length) return set(env, "phase", "detail");
      for (const c of results) {
        const stmts = [env.DB.prepare("UPDATE kr_chars SET listed = 1 WHERE cid = ?1").bind(c.cid)];
        try {
          const eq = await fetchJson(`${NC}equipment?lang=ko-KR&${q(c.cid, c.server)}`);
          for (const it of eq?.equipment?.equipmentList || []) {
            if (/^(Rune|Amulet)/.test(it.slotPosName || "")) continue; // no lines
            stmts.push(env.DB.prepare("INSERT OR IGNORE INTO kr_items (cid, slot, item, enchant, slot_pos) VALUES (?1, ?2, ?3, ?4, ?5)")
              .bind(c.cid, it.slotPosName, it.id, it.enchantLevel || 0, it.slotPos));
          }
        } catch {}
        await env.DB.batch(stmts);
      }
      return;
    }
    if (phase === "detail") {
      const { results } = await env.DB.prepare(
        "SELECT i.cid, i.slot, i.item, i.enchant, i.slot_pos, c.server FROM kr_items i JOIN kr_chars c ON c.cid = i.cid WHERE i.done IS NULL LIMIT ?1").bind(budget).all();
      if (!results.length) return set(env, "phase", "aggregate");
      for (const it of results) {
        const stmts = [env.DB.prepare("UPDATE kr_items SET done = 1 WHERE cid = ?1 AND slot = ?2").bind(it.cid, it.slot)];
        try {
          const d = await fetchJson(`${NC}equipment/item?lang=ko-KR&${q(it.cid, it.server)}&id=${it.item}&enchantLevel=${it.enchant}&slotPos=${it.slot_pos}`);
          for (const s of d.subStats || [])
            if (s.id) stmts.push(env.DB.prepare("INSERT OR REPLACE INTO kr_lines (cid, slot, kind, line, value) VALUES (?1, ?2, 'stat', ?3, ?4)").bind(it.cid, it.slot, s.id, num(s.value)));
          for (const s of d.subSkills || [])
            if (s.id) stmts.push(env.DB.prepare("INSERT OR REPLACE INTO kr_lines (cid, slot, kind, line, value) VALUES (?1, ?2, 'skill', ?3, ?4)").bind(it.cid, it.slot, String(s.id), Number(s.level) || 1));
        } catch {}
        await env.DB.batch(stmts);
      }
      return;
    }
    if (phase === "aggregate") {
      await aggregate(env, Number(env.KR_MIN) || 5);
      await set(env, "built", Date.now());
      await set(env, "next", Number(await get(env, "started", Date.now())) + CYCLE_DAYS * 86400_000);
      return set(env, "phase", "idle");
    }
  }

  // Per class and slot: how many of the top players carry each line, and the largest value any of them has.
  async function aggregate(env, minPlayers) {
    const { results: lines } = await env.DB.prepare(
      `SELECT c.cls AS cls, l.slot AS slot, l.kind AS kind, l.line AS line, COUNT(DISTINCT l.cid) AS n, MAX(l.value) AS maxv
       FROM kr_lines l JOIN kr_chars c ON c.cid = l.cid GROUP BY c.cls, l.slot, l.kind, l.line`).all();
    const { results: owners } = await env.DB.prepare(
      `SELECT c.cls AS cls, i.slot AS slot, COUNT(DISTINCT i.cid) AS n FROM kr_items i JOIN kr_chars c ON c.cid = i.cid WHERE i.done = 1 GROUP BY c.cls, i.slot`).all();
    // Pairs (Earring1/2 …) fold into one slot: a player counts once per slot kind.
    const own = new Map();
    for (const o of owners) { const k = o.cls + "|" + slotKey(o.slot); own.set(k, Math.max(own.get(k) || 0, o.n)); }
    const agg = new Map();
    for (const l of lines) {
      const k = l.cls + "|" + slotKey(l.slot) + "|" + l.kind + "|" + l.line;
      const a = agg.get(k) || { cls: l.cls, slot: slotKey(l.slot), kind: l.kind, line: l.line, n: 0, maxv: 0 };
      a.n = Math.max(a.n, l.n); a.maxv = Math.max(a.maxv, l.maxv);
      agg.set(k, a);
    }
    const stmts = [env.DB.prepare("DELETE FROM kr_share")];
    for (const a of agg.values()) {
      const players = own.get(a.cls + "|" + a.slot) || 0;
      if (players < minPlayers) continue;
      stmts.push(env.DB.prepare("INSERT INTO kr_share (cls, slot, kind, line, share, maxv, players) VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7)")
        .bind(a.cls, a.slot, a.kind, a.line, Math.min(1, a.n / players), a.maxv, players));
    }
    for (let i = 0; i < stmts.length; i += 80) await env.DB.batch(stmts.slice(i, i + 80));
  }

  async function status(env) {
    const phase = await get(env, "phase", "discover");
    const [chars, items, built] = await Promise.all([
      env.DB.prepare("SELECT COUNT(*) AS n, SUM(info_at IS NOT NULL) AS info, SUM(listed = 1) AS listed FROM kr_chars").first(),
      env.DB.prepare("SELECT COUNT(*) AS n, SUM(done = 1) AS done FROM kr_items").first(),
      get(env, "built", 0),
    ]);
    return { phase, word: Number(await get(env, "word", 0)), words: WORDS.length, chars, items, built: Number(built) };
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
