import json
from pathlib import Path
from .models import RiskDecision, TradeProposal, Action

class RiskSupervisor:
    def __init__(self, constitution_path):
        self.path = Path(constitution_path)
        self.rules = json.loads(self.path.read_text(encoding="utf-8"))

    def validate(self, proposal: TradeProposal, portfolio):
        if proposal.action == Action.NO_TRADE:
            return RiskDecision.APPROVED, "No trade"
        if self.rules.get("real_money_enabled"):
            return RiskDecision.BLOCKED, "V2 must remain paper-only"
        if proposal.symbol not in self.rules["allowed_markets"]:
            return RiskDecision.BLOCKED, "Market not allowed"
        if len(portfolio.positions) >= self.rules["max_open_positions"] and proposal.action == Action.OPEN_LONG:
            return RiskDecision.BLOCKED, "Max open positions reached"
        if proposal.action == Action.OPEN_LONG:
            if self.rules["mandatory_stop_loss"] and not proposal.stop_loss_pct:
                return RiskDecision.BLOCKED, "Stop loss is mandatory"
            if proposal.risk_pct <= 0:
                return RiskDecision.BLOCKED, "Risk must be positive"
            if proposal.risk_pct > self.rules["max_risk_per_trade_pct"]:
                return RiskDecision.APPROVED_WITH_REDUCTION, "Risk reduced to constitution maximum"
            rr = (proposal.take_profit_pct or 0) / proposal.stop_loss_pct
            if rr < self.rules["min_reward_risk_ratio"]:
                return RiskDecision.BLOCKED, "Reward/risk below minimum"
        return RiskDecision.APPROVED, "Approved"
