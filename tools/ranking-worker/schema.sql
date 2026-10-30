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
