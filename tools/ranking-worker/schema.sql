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
