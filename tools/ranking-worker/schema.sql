-- Best value per player, boss and metric (dps / hps / stagger). Only players who turned on "Join the ranking".
CREATE TABLE IF NOT EXISTS best (
  region TEXT NOT NULL,
  boss INTEGER NOT NULL,
  metric TEXT NOT NULL,
  name TEXT NOT NULL,
  server INTEGER NOT NULL,
  server_name TEXT,
  cls TEXT,
  cp INTEGER,
  value REAL NOT NULL,
  dur_ms INTEGER,
  party INTEGER,
  at INTEGER NOT NULL,
  PRIMARY KEY (region, boss, metric, name, server)
);
CREATE INDEX IF NOT EXISTS best_top ON best (region, boss, metric, value DESC);

-- The same, per week (weeks start Monday 00:00 UTC); weeks older than 8 are dropped.
CREATE TABLE IF NOT EXISTS week_best (
  week INTEGER NOT NULL,
  region TEXT NOT NULL,
  boss INTEGER NOT NULL,
  metric TEXT NOT NULL,
  name TEXT NOT NULL,
  server INTEGER NOT NULL,
  server_name TEXT,
  cls TEXT,
  cp INTEGER,
  value REAL NOT NULL,
  dur_ms INTEGER,
  party INTEGER,
  at INTEGER NOT NULL,
  PRIMARY KEY (week, region, boss, metric, name, server)
);
CREATE INDEX IF NOT EXISTS week_top ON week_best (week, region, boss, metric, value DESC);

-- Item level of every character looked up on the site, for the gear grade (one row per character).
CREATE TABLE IF NOT EXISTS gear (
  region TEXT NOT NULL,
  server INTEGER NOT NULL,
  name TEXT NOT NULL,
  cls TEXT NOT NULL,
  level INTEGER NOT NULL,
  il INTEGER NOT NULL,
  cp INTEGER,
  at INTEGER NOT NULL,
  PRIMARY KEY (region, server, name)
);
CREATE INDEX IF NOT EXISTS gear_cohort ON gear (cls, level, il);

-- World boss timers per server, as the meters of that server report them (no player data).
CREATE TABLE IF NOT EXISTS boss_live (
  region TEXT NOT NULL,
  server INTEGER NOT NULL,
  boss INTEGER NOT NULL,
  server_name TEXT,
  state TEXT NOT NULL,
  next_at INTEGER,
  since INTEGER,
  src TEXT,
  reported INTEGER NOT NULL,
  PRIMARY KEY (region, server, boss)
);

-- Boss names and HP as first reported (the HP check keeps made-up damage out).
CREATE TABLE IF NOT EXISTS bosses (
  boss INTEGER PRIMARY KEY,
  name TEXT,
  zone TEXT,
  max_hp INTEGER
);

-- Submissions per hashed IP and hour, against spam.
CREATE TABLE IF NOT EXISTS hits (
  ip TEXT NOT NULL,
  hour INTEGER NOT NULL,
  n INTEGER NOT NULL,
  PRIMARY KEY (ip, hour)
);

-- Shared fight reports ("share as link" in the meter): one fight per row, the page lives at /r/<id>.
CREATE TABLE IF NOT EXISTS reports (
  id TEXT PRIMARY KEY,
  region TEXT,
  boss INTEGER,
  title TEXT,
  at INTEGER,
  created INTEGER NOT NULL,
  data TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS reports_created ON reports (created);

-- Census: players seen in fights by meters with "Count me anonymously" on. No names: k is a one-way fingerprint.
CREATE TABLE IF NOT EXISTS census (
  region TEXT NOT NULL,
  k TEXT NOT NULL,
  server INTEGER NOT NULL,
  server_name TEXT,
  cls TEXT NOT NULL,
  cp INTEGER,
  gs INTEGER,
  seen INTEGER NOT NULL,
  PRIMARY KEY (region, k)
);
CREATE INDEX IF NOT EXISTS census_seen ON census (region, seen);
