from pathlib import Path
from app.risk import RiskSupervisor
from app.portfolio import Portfolio
from app.models import TradeProposal, Action, RiskDecision

ROOT=Path(__file__).resolve().parents[1]

def test_stop_is_mandatory():
    r=RiskSupervisor(ROOT/"config"/"risk_constitution.json")
    p=TradeProposal(Action.OPEN_LONG,"BTC/USDT",0.7,risk_pct=0.3,stop_loss_pct=None,take_profit_pct=2)
    d,_=r.validate(p,Portfolio())
    assert d==RiskDecision.BLOCKED

def test_risk_above_limit_is_reduced():
    r=RiskSupervisor(ROOT/"config"/"risk_constitution.json")
    p=TradeProposal(Action.OPEN_LONG,"BTC/USDT",0.7,risk_pct=5,stop_loss_pct=1,take_profit_pct=2)
    d,_=r.validate(p,Portfolio())
    assert d==RiskDecision.APPROVED_WITH_REDUCTION

def test_bad_rr_is_blocked():
    r=RiskSupervisor(ROOT/"config"/"risk_constitution.json")
    p=TradeProposal(Action.OPEN_LONG,"BTC/USDT",0.7,risk_pct=.2,stop_loss_pct=2,take_profit_pct=1)
    d,_=r.validate(p,Portfolio())
    assert d==RiskDecision.BLOCKED
