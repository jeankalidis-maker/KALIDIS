import sqlite3, json
from datetime import datetime, timezone

SCHEMA = """
CREATE TABLE IF NOT EXISTS decisions(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 ts TEXT NOT NULL,
 symbol TEXT,
 action TEXT NOT NULL,
 summary TEXT,
 payload TEXT
);
CREATE TABLE IF NOT EXISTS hypotheses(
 id TEXT PRIMARY KEY,
 ts TEXT NOT NULL,
 symbol TEXT NOT NULL,
 thesis TEXT NOT NULL,
 regime TEXT NOT NULL,
 confidence REAL NOT NULL,
 status TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS trades(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 ts TEXT NOT NULL,
 symbol TEXT NOT NULL,
 event TEXT NOT NULL,
 price REAL NOT NULL,
 qty REAL,
 pnl REAL
);
CREATE TABLE IF NOT EXISTS memories(
 id INTEGER PRIMARY KEY AUTOINCREMENT,
 ts TEXT NOT NULL,
 kind TEXT NOT NULL,
 content TEXT NOT NULL
);
"""

def now():
    return datetime.now(timezone.utc).isoformat()

class Database:
    def __init__(self, path="kalidis_trader.sqlite3"):
        self.conn = sqlite3.connect(path)
        self.conn.executescript(SCHEMA)
        self.conn.commit()

    def add_decision(self, symbol, action, summary, payload=None):
        self.conn.execute("INSERT INTO decisions(ts,symbol,action,summary,payload) VALUES(?,?,?,?,?)",
                          (now(),symbol,action,summary,json.dumps(payload or {}, ensure_ascii=False)))
        self.conn.commit()

    def add_hypothesis(self, h):
        self.conn.execute("INSERT OR REPLACE INTO hypotheses VALUES(?,?,?,?,?,?,?)",
                          (h.id,now(),h.symbol,h.thesis,h.regime,h.confidence,h.status))
        self.conn.commit()

    def add_trade(self, symbol, event, price, qty=None, pnl=None):
        self.conn.execute("INSERT INTO trades(ts,symbol,event,price,qty,pnl) VALUES(?,?,?,?,?,?)",
                          (now(),symbol,event,price,qty,pnl))
        self.conn.commit()

    def add_memory(self, kind, content):
        self.conn.execute("INSERT INTO memories(ts,kind,content) VALUES(?,?,?)",(now(),kind,content))
        self.conn.commit()
