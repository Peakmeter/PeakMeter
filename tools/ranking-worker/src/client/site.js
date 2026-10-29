// PeakMeter site: faction colours, the aether sky and the scroll reveal. Loaded in <head> (no defer) so the
// remembered faction is set before the first paint.
(function () {
  var root = document.documentElement;
  var KEY = "pm-faction";
  var saved = null;
  try { saved = localStorage.getItem(KEY); } catch (e) {}
  if (saved === "asmo") root.dataset.faction = "asmo";

  window.PM = window.PM || {};
  PM.de = (navigator.language || "").toLowerCase().indexOf("de") === 0;
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

  function setFaction(f) {
    if (f === "asmo") root.dataset.faction = "asmo"; else delete root.dataset.faction;
    try { localStorage.setItem(KEY, f); } catch (e) {}
    document.querySelectorAll(".faction button").forEach(function (b) { b.setAttribute("aria-pressed", String(b.dataset.f === f)); });
    if (sky) sky.recolor();
  }

  var NAV_TEXT = PM.de
    ? { features: "Funktionen", ranking: "Rangliste", players: "Spieler", install: "Installieren", dl: "Laden", elyos: "Elyos", asmo: "Asmodier" }
    : { features: "Features", ranking: "Ranking", players: "Players", install: "Install", dl: "Download", elyos: "Elyos", asmo: "Asmodian" };
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
      '<div class="links">' + a("/#features", "features", "opt") + a("/ranking", "ranking") + a("/player", "players") + a("/#install", "install", "opt") + "</div>" +
      '<div class="faction" role="group" aria-label="Faction">' +
      '<button type="button" data-f="elyos" title="' + NAV_TEXT.elyos + '" aria-label="' + NAV_TEXT.elyos + '">' + SUN + "</button>" +
      '<button type="button" data-f="asmo" title="' + NAV_TEXT.asmo + '" aria-label="' + NAV_TEXT.asmo + '">' + MOON + "</button></div>" +
      '<a class="btn sm dl" href="https://github.com/Peakmeter/PeakMeter/releases/latest">' + NAV_TEXT.dl + "</a></div>";
    nav.querySelectorAll(".faction button").forEach(function (b) { b.onclick = function () { setFaction(b.dataset.f); }; });
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
