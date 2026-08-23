import os, json, time
from pathlib import Path
from .market_data import SyntheticProvider, BinancePublicProvider
from .scanner import MarketScanner
from .agent import AutonomousAgent
from .risk import RiskSupervisor
from .portfolio import Portfolio
from .db import Database
from .engine import TradingEngine
from .planner import MetaPlanner
from .memory import MemoryStore
from .position_monitor import PositionMonitor
from .research import ResearchEngine

def build_provider():
    mode=os.getenv("KALIDIS_PROVIDER","synthetic").lower()
    if mode=="binance":
        return BinancePublicProvider(), "BINANCE PUBLIC"
    return SyntheticProvider(), "SYNTHETIC/OFFLINE"

def load_runtime(root):
    return json.loads((root/"config"/"runtime.json").read_text(encoding="utf-8"))

def print_cycle(n, result, portfolio):
    plan=result["plan"]
    print("\n"+"="*70)
    print(f"CICLO {n}")
    if plan:
        print(f"PLANO: {plan.action.value} — {plan.reason}")

    for ev in result["monitor_events"]:
        print(f"MONITOR: {ev['symbol']} fechada por {ev['reason']} | PnL {ev['pnl']:.2f}")

    for r in result["research_results"]:
        print(f"RESEARCH {r.symbol}: {r.conclusion} | confiança {r.confidence:.0%}")

    for ev in result["trade_events"]:
        s=ev["snapshot"]; p=ev["proposal"]
        print(f"{s.symbol} | {s.regime} | score {s.opportunity_score:.2f} | decisão {p.action.value}")
        if "opened_position" in ev:
            pos=ev["opened_position"]
            print(f"  PAPER POSITION qty={pos.qty:.6f} stop={pos.stop_price:.2f} alvo={pos.take_profit_price:.2f}")

    print(f"Cash: {portfolio.cash:,.2f} | Equity: {result['equity']:,.2f} | "
          f"Posições: {len(portfolio.positions)} | No-trade streak: {result['no_trade_streak']}")

def main():
    root=Path(__file__).resolve().parents[1]
    runtime=load_runtime(root)
    provider, provider_name=build_provider()
    db=Database(os.getenv("KALIDIS_DB", str(root/"kalidis_trader.sqlite3")))
    portfolio=Portfolio(10000)
    scanner=MarketScanner(provider)
    risk=RiskSupervisor(root/"config"/"risk_constitution.json")
    agent=AutonomousAgent()
    memory=MemoryStore(db)
    planner=MetaPlanner()
    monitor=PositionMonitor(portfolio, db, memory)
    research=ResearchEngine()

    engine=TradingEngine(
        scanner,agent,risk,portfolio,db,runtime["symbols"],
        planner=planner,memory=memory,position_monitor=monitor,research_engine=research
    )

    max_cycles=int(os.getenv("KALIDIS_MAX_CYCLES", runtime["max_cycles"]))
    interval=float(os.getenv("KALIDIS_LOOP_INTERVAL", runtime["loop_interval_seconds"]))

    print("="*70)
    print("KALIDIS AI TRADER — V2 AUTÔNOMA")
    print(f"MODE: PAPER | PROVIDER: {provider_name}")
    print("Planner + Loop + Memória + Position Monitor + Research Mode básico")
    print("="*70)

    try:
        for i in range(1, max_cycles+1):
            result=engine.run_cycle(runtime["research_trigger_no_trade_cycles"])
            print_cycle(i,result,portfolio)
            if i < max_cycles:
                time.sleep(interval)
    except KeyboardInterrupt:
        print("\nEncerrado pelo usuário.")
        return 0
    except Exception as e:
        print(f"\nSAFE MODE: falha no ciclo: {type(e).__name__}: {e}")
        memory.remember("incident", f"{type(e).__name__}: {e}")
        return 2

    print("\nMemórias recentes:")
    for ts,kind,content in memory.recent(5):
        print(f"- [{kind}] {content}")

    print("\nV2 concluída. Histórico persistido no SQLite.")
    return 0

if __name__=="__main__":
    raise SystemExit(main())
