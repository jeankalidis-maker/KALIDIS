from app.db import Database
from app.memory import MemoryStore
from app.portfolio import Portfolio
from app.position_monitor import PositionMonitor

def test_monitor_closes_take_profit(tmp_path):
    db=Database(tmp_path/"p.sqlite3")
    m=MemoryStore(db)
    p=Portfolio(10000)
    pos=p.open_long("BTC/USDT",100,0.5,1,2)
    mon=PositionMonitor(p,db,m)
    ev=mon.evaluate({"BTC/USDT":103})
    assert len(ev)==1
    assert "BTC/USDT" not in p.positions
    assert ev[0]["pnl"]>0
