class MemoryStore:
    def __init__(self, db):
        self.db = db

    def remember(self, kind: str, content: str):
        self.db.add_memory(kind, content)

    def recent(self, limit=10):
        cur = self.db.conn.execute(
            "SELECT ts, kind, content FROM memories ORDER BY id DESC LIMIT ?",
            (limit,)
        )
        return list(cur.fetchall())

    def summarize_for_agent(self, limit=5):
        rows = self.recent(limit)
        if not rows:
            return "Sem memórias relevantes ainda."
        return " | ".join(f"{kind}: {content}" for _, kind, content in rows)
