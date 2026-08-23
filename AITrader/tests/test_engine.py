from pathlib import Path
from app.market_data import SyntheticProvider
from app.scanner import MarketScanner
from app.agent import AutonomousAgent
from app.risk import RiskSupervisor
from app.portfolio import Portfolio
from app.db import Database
from app.engine import TradingEngine

ROOT=Path(__file__).resolve().parents[1]

def test_end_to_end(tmp_path):
    db=Database(tmp_path/"test.sqlite3")
    engine=TradingEngine(
        MarketScanner(SyntheticProvider()),
        AutonomousAgent(),
        RiskSupervisor(ROOT/"config"/"risk_constitution.json"),
        Portfolio(10000),
        db,
        ["BTC/USDT","ETH/USDT","SOL/USDT"],
    )
    result=engine.run_cycle()
    assert len(result["snapshots"])==3
    assert result["equity"]>0
    assert all(ev["risk_decision"] is not None for ev in result["trade_events"])
