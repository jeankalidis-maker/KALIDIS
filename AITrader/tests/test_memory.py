from app.db import Database
from app.memory import MemoryStore

def test_memory_persists(tmp_path):
    db=Database(tmp_path/"m.sqlite3")
    m=MemoryStore(db)
    m.remember("research","teste")
    rows=m.recent(1)
    assert rows and rows[0][2]=="teste"
