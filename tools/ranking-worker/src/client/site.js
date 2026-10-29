// PeakMeter site: faction colours, the aether sky and the scroll reveal. Loaded in <head> (no defer) so the
// remembered faction is set before the first paint.
(function () {
  var root = document.documentElement;
  var KEY = "pm-faction";
  var saved = null;
  try { saved = localStorage.getItem(KEY); } catch (e) {}
  if (saved === "asmo") root.dataset.faction = "asmo";

  window.PM = window.PM || {};
  var savedLang = null;
  try { savedLang = localStorage.getItem("pm-lang"); } catch (e) {}
  PM.lang = savedLang === "de" || savedLang === "en" ? savedLang : ((navigator.language || "").toLowerCase().indexOf("de") === 0 ? "de" : "en");
  PM.de = PM.lang === "de";
  if (PM.de) root.lang = "de";
  PM.still = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;

  // Class emblems (the meter's own icons). Takes "Gladiator", "gladiator" or the official "Spiritmaster".
  var EMBLEMS = ["gladiator", "templar", "ranger", "assassin", "sorcerer", "elementalist", "cleric", "chanter", "brawler"];
  PM.classIcon = function (cls) {
    var k = String(cls || "").toLowerCase();
    if (k === "spiritmaster") k = "elementalist";
    return EMBLEMS.indexOf(k) >= 0 ? "https://raw.githubusercontent.com/Peakmeter/PeakMeter/main/src/PeakMeter/Assets/Classes/" + k + ".png" : null;
  };
  // Class colours, as in the meter.
  PM.classColor = function (cls) {
    return ({ gladiator: "#e8a84c", templar: "#5e9bf0", ranger: "#6fcf7c", assassin: "#b07cf0", sorcerer: "#47c8f5", elementalist: "#f26ca7",
      spiritmaster: "#f26ca7", cleric: "#f5e06e", chanter: "#3fd1b5", brawler: "#ff744d" })[String(cls || "").toLowerCase()] || "var(--line)";
  };

  // Swap texts marked data-i for the German ones of a page's dictionary.
  PM.translate = function (dict) {
    if (!PM.de) return;
    document.querySelectorAll("[data-i]").forEach(function (el) { var v = dict[el.dataset.i]; if (v) el.innerHTML = v; });
  };

  // Things below the first screen fade in as they arrive; nothing on the first screen is ever hidden.
  PM.reveal = function (selector) {
    if (PM.still || !("IntersectionObserver" in window)) return;
    var io = new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); } });
    }, { rootMargin: "0px 0px -8% 0px" });
    var fold = innerHeight;
    document.querySelectorAll(selector).forEach(function (el) {
      if (el.getBoundingClientRect().top > fold) { el.classList.add("pre"); io.observe(el); }
    });
  };

  // A row of share buttons: copy link, a ready message for Discord, Reddit, X and WhatsApp.
  var SHARE_ICONS = {
    link: '<path d="M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1 1 M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1-1" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>',
    discord: '<path fill="currentColor" d="M19.3 5.3A16.5 16.5 0 0 0 15.2 4l-.5 1a15.4 15.4 0 0 0-5.4 0l-.5-1a16.5 16.5 0 0 0-4.1 1.3C2.1 9.2 1.4 13 1.7 16.7a16.6 16.6 0 0 0 5 2.5l1.1-1.7-1.8-.9.4-.3a11.8 11.8 0 0 0 11.2 0l.4.3-1.8.9 1.1 1.7a16.6 16.6 0 0 0 5-2.5c.4-4.3-.6-8-3-11.4zM8.7 14.5c-1 0-1.8-.9-1.8-2s.8-2 1.8-2 1.8.9 1.8 2-.8 2-1.8 2zm6.6 0c-1 0-1.8-.9-1.8-2s.8-2 1.8-2 1.8.9 1.8 2-.8 2-1.8 2z"/>',
    reddit: '<path fill="currentColor" d="M22 12.1a2.2 2.2 0 0 0-3.7-1.6 10.8 10.8 0 0 0-5.8-1.8l1-4.6 3.2.7a1.6 1.6 0 1 0 .2-1l-3.6-.8a.5.5 0 0 0-.6.4l-1.1 5.3a10.8 10.8 0 0 0-5.9 1.8 2.2 2.2 0 1 0-2.4 3.6 4.3 4.3 0 0 0 0 .7c0 3.5 4.1 6.4 9.1 6.4s9.1-2.9 9.1-6.4a4.3 4.3 0 0 0 0-.7 2.2 2.2 0 0 0 1.2-2zM7.3 13.6a1.6 1.6 0 1 1 1.6 1.6 1.6 1.6 0 0 1-1.6-1.6zm8.9 4.2a5.8 5.8 0 0 1-4.2 1.3 5.8 5.8 0 0 1-4.2-1.3.4.4 0 0 1 .6-.6 4.9 4.9 0 0 0 3.6 1 4.9 4.9 0 0 0 3.6-1 .4.4 0 1 1 .6.6zm-.3-2.6a1.6 1.6 0 1 1 1.6-1.6 1.6 1.6 0 0 1-1.6 1.6z"/>',
    x: '<path fill="currentColor" d="M17.8 3h3.1l-6.8 7.7 8 10.3h-6.3l-4.9-6.4L5.3 21H2.2l7.2-8.3L1.8 3h6.4l4.4 5.9zm-1.1 16.2h1.7L7.3 4.7H5.5z"/>',
    whatsapp: '<path fill="currentColor" d="M12 2a10 10 0 0 0-8.6 15.1L2 22l5-1.3A10 10 0 1 0 12 2zm5.8 14.2c-.2.7-1.4 1.3-2 1.4-.5.1-1.2.1-1.9-.1a17.4 17.4 0 0 1-1.7-.6 13.3 13.3 0 0 1-5.1-4.5 5.8 5.8 0 0 1-1.2-3.1 3.4 3.4 0 0 1 1-2.5 1.1 1.1 0 0 1 .8-.4h.6c.2 0 .4 0 .6.5l.9 2.1a.6.6 0 0 1 0 .5 2 2 0 0 1-.3.5l-.4.5c-.2.1-.3.3-.1.6a9 9 0 0 0 1.6 2 8 8 0 0 0 2.4 1.5c.3.1.5.1.6-.1l.9-1.1c.2-.3.4-.2.6-.1l2 1c.3.1.5.2.5.3a2.4 2.4 0 0 1-.2 1.3z"/>',
  };
  PM.share = function (host, opts) {
    var el = typeof host === "string" ? document.getElementById(host) : host;
    if (!el) return;
    var t = PM.de
      ? { label: "Teilen", copy: "Link kopieren", copied: "Kopiert ✓", discord: "Nachricht für Discord kopieren", dcDone: "Kopiert – in Discord mit Strg+V" }
      : { label: "Share", copy: "Copy link", copied: "Copied ✓", discord: "Copy a message for Discord", dcDone: "Copied – paste in Discord with Ctrl+V" };
    var url = opts.url || location.href, text = opts.text || document.title;
    var enc = encodeURIComponent;
    var btn = function (k, title, href) {
      var svg = '<svg viewBox="0 0 24 24" width="17" height="17" aria-hidden="true">' + SHARE_ICONS[k] + "</svg>";
      return href ? '<a class="sh" data-k="' + k + '" href="' + href + '" target="_blank" rel="noopener" title="' + title + '" aria-label="' + title + '">' + svg + "</a>"
                  : '<button class="sh" type="button" data-k="' + k + '" title="' + title + '" aria-label="' + title + '">' + svg + "</button>";
    };
    el.classList.add("share");
    el.innerHTML = '<span class="hud">' + t.label + "</span>" + btn("link", t.copy) + btn("discord", t.discord) +
      btn("reddit", "Reddit", "https://www.reddit.com/submit?url=" + enc(url) + "&title=" + enc(text)) +
      btn("x", "X", "https://twitter.com/intent/tweet?text=" + enc(text) + "&url=" + enc(url)) +
      btn("whatsapp", "WhatsApp", "https://wa.me/?text=" + enc(text + " " + url)) + '<span class="sh-msg" role="status"></span>';
    var msg = el.querySelector(".sh-msg");
    var say = function (s) { msg.textContent = s; clearTimeout(el._t); el._t = setTimeout(function () { msg.textContent = ""; }, 2600); };
    var copy = function (s, done) {
      if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(s).then(function () { say(done); }, function () { prompt("", s); });
      else prompt("", s);
    };
    el.querySelector('[data-k="link"]').onclick = function () { copy(url, t.copied); };
    el.querySelector('[data-k="discord"]').onclick = function () { copy((opts.discord || ("**" + text + "**")) + "\n" + url, t.dcDone); };
  };

  function setFaction(f) {
    if (f === "asmo") root.dataset.faction = "asmo"; else delete root.dataset.faction;
    try { localStorage.setItem(KEY, f); } catch (e) {}
    document.querySelectorAll(".faction button").forEach(function (b) { b.setAttribute("aria-pressed", String(b.dataset.f === f)); });
    if (sky) sky.recolor();
  }

  var NAV_TEXT = PM.de
    ? { features: "Funktionen", ranking: "Rangliste", players: "Spieler", compare: "Duell", install: "Installieren", lang: "Sprache", dl: "Laden", elyos: "Elyos", asmo: "Asmodier" }
    : { features: "Features", ranking: "Ranking", players: "Players", compare: "Duel", install: "Install", lang: "Language", dl: "Download", elyos: "Elyos", asmo: "Asmodian" };
  var SUN = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round"><circle cx="12" cy="12" r="4" fill="currentColor" stroke="none"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1"/></svg>';
  var MOON = '<svg viewBox="0 0 24 24" fill="currentColor"><path d="M15.5 3.2A9 9 0 1 0 20.8 15 7.2 7.2 0 0 1 15.5 3.2z"/></svg>';

  // The shared top bar: <nav class="top" data-page="ranking"></nav> on every page.
  function buildNav() {
    var nav = document.querySelector("nav.top");
    if (!nav || nav.dataset.built) return;
    nav.dataset.built = "1";
    var page = nav.dataset.page || "";
    var a = function (href, key, cls) { return '<a href="' + href + '" class="' + (cls || "") + (page === key ? " on" : "") + '">' + NAV_TEXT[key] + "</a>"; };
    nav.innerHTML = '<div class="wrap"><a class="brand" href="/"><img src="https://raw.githubusercontent.com/Peakmeter/PeakMeter/main/docs/logo.png" alt=""><span>PeakMeter</span></a>' +
      '<div class="links">' + a("/#all", "features", "opt") + a("/ranking", "ranking") + a("/player", "players") + a("/compare", "compare", "opt") + "</div>" +
      '<button type="button" class="lang" id="pmLang" title="' + NAV_TEXT.lang + '" aria-label="' + NAV_TEXT.lang + '"><span' + (PM.de ? ' class="on"' : "") + '>DE</span><span' + (PM.de ? "" : ' class="on"') + ">EN</span></button>" +
      '<div class="faction" role="group" aria-label="Faction">' +
      '<button type="button" data-f="elyos" title="' + NAV_TEXT.elyos + '" aria-label="' + NAV_TEXT.elyos + '">' + SUN + "</button>" +
      '<button type="button" data-f="asmo" title="' + NAV_TEXT.asmo + '" aria-label="' + NAV_TEXT.asmo + '">' + MOON + "</button></div>" +
      '<a class="btn sm dl" href="https://github.com/Peakmeter/PeakMeter/releases/latest">' + NAV_TEXT.dl + "</a></div>";
    nav.querySelectorAll(".faction button").forEach(function (b) { b.onclick = function () { setFaction(b.dataset.f); }; });
    nav.querySelector("#pmLang").onclick = function () {
      try { localStorage.setItem("pm-lang", PM.de ? "en" : "de"); } catch (e) {}
      location.reload();
    };
    setFaction(root.dataset.faction === "asmo" ? "asmo" : "elyos");
  }

  // Aether motes drifting up through the night: a few dozen soft dots, paused while the tab is hidden.
  var sky = null;
  function buildSky() {
    var host = document.createElement("div");
    host.className = "sky";
    host.setAttribute("aria-hidden", "true");
    var c = document.createElement("canvas");
    host.appendChild(c);
    document.body.prepend(host);
    var ctx = c.getContext("2d"), dpr = Math.min(2, window.devicePixelRatio || 1), W = 0, H = 0, motes = [], colA = "#7fd8ff", colB = "#e3c27a";
    function size() { W = c.width = innerWidth * dpr; H = c.height = innerHeight * dpr; }
    function seed() {
      var n = Math.round(Math.min(90, innerWidth / 16));
      motes = [];
      for (var i = 0; i < n; i++) motes.push({ x: Math.random() * W, y: Math.random() * H, r: (Math.random() * 1.6 + .4) * dpr, v: (Math.random() * .25 + .08) * dpr,
        s: Math.random() * Math.PI * 2, a: Math.random() * .6 + .2, b: Math.random() < .5 });
    }
    function draw() {
      ctx.clearRect(0, 0, W, H);
      for (var i = 0; i < motes.length; i++) {
        var m = motes[i];
        ctx.globalAlpha = m.a * (.6 + .4 * Math.sin(m.s * 2));
        ctx.fillStyle = m.b ? colA : colB;
        ctx.shadowColor = ctx.fillStyle; ctx.shadowBlur = 8 * dpr;
        ctx.beginPath(); ctx.arc(m.x, m.y, m.r, 0, Math.PI * 2); ctx.fill();
      }
    }
    function step() {
      if (!document.hidden) {
        for (var i = 0; i < motes.length; i++) {
          var m = motes[i];
          m.y -= m.v; m.s += .01; m.x += Math.sin(m.s) * .15 * dpr;
          if (m.y < -10) { m.y = H + 10; m.x = Math.random() * W; }
        }
        draw();
      }
      requestAnimationFrame(step);
    }
    sky = {
      recolor: function () {
        var cs = getComputedStyle(root);
        colA = cs.getPropertyValue("--fa").trim() || colA;
        colB = cs.getPropertyValue("--fb").trim() || colB;
        if (PM.still) draw();
      },
    };
    size(); seed(); sky.recolor();
    addEventListener("resize", function () { size(); seed(); if (PM.still) draw(); });
    if (PM.still) draw(); else requestAnimationFrame(step);
  }

  document.addEventListener("DOMContentLoaded", function () {
    buildSky();
    buildNav();
    // Newest release: download buttons fetch its ZIP straight away (else the release page).
    fetch("https://api.github.com/repos/Peakmeter/PeakMeter/releases/latest").then(function (r) { return r.ok ? r.json() : null; }).then(function (j) {
      if (!j) return;
      var zip = (j.assets || []).filter(function (a) { return /\.zip$/i.test(a.name); })[0];
      if (zip) document.querySelectorAll("a.dl").forEach(function (a) { a.href = zip.browser_download_url; });
      PM.version = j.tag_name;
      document.querySelectorAll("[data-version]").forEach(function (el) { el.textContent = j.tag_name; });
    }).catch(function () {});
  });
})();
