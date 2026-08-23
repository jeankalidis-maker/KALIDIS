from .models import Action, RiskDecision
from .planner import PlanAction

class TradingEngine:
    def __init__(self, scanner, agent, risk, portfolio, db, symbols,
                 planner=None, memory=None, position_monitor=None, research_engine=None):
        self.scanner=scanner
        self.agent=agent
        self.risk=risk
        self.portfolio=portfolio
        self.db=db
        self.symbols=symbols
        self.planner=planner
        self.memory=memory
        self.position_monitor=position_monitor
        self.research_engine=research_engine
        self.no_trade_streak=0

    def scan_market(self):
        snapshots = self.scanner.scan(self.symbols)
        return snapshots, {s.symbol:s.price for s in snapshots}

    def execute_scan(self, snapshots):
        events=[]
        opened=0
        for snap in snapshots:
            hyp=self.agent.create_hypothesis(snap)
            self.db.add_hypothesis(hyp)
            proposal=self.agent.decide(snap,hyp)
            decision, reason=self.risk.validate(proposal,self.portfolio)
            self.db.add_decision(
                snap.symbol, proposal.action.value, proposal.summary,
                {"risk_decision":decision.value,"risk_reason":reason,
                 "confidence":proposal.confidence}
            )
            event={"snapshot":snap,"hypothesis":hyp,"proposal":proposal,
                   "risk_decision":decision,"risk_reason":reason}

            if proposal.action == Action.OPEN_LONG and snap.symbol not in self.portfolio.positions:
                if decision in (RiskDecision.APPROVED, RiskDecision.APPROVED_WITH_REDUCTION):
                    risk_pct=proposal.risk_pct
                    if decision == RiskDecision.APPROVED_WITH_REDUCTION:
                        risk_pct=self.risk.rules["max_risk_per_trade_pct"]
                    pos=self.portfolio.open_long(
                        snap.symbol,snap.price,risk_pct,
                        proposal.stop_loss_pct,proposal.take_profit_pct
                    )
                    self.db.add_trade(snap.symbol,"OPEN",snap.price,pos.qty,None)
                    if self.memory:
                        self.memory.remember(
                            "trade_open",
                            f"{snap.symbol}: posição aberta por {proposal.summary} "
                            f"(confiança={proposal.confidence:.0%})."
                        )
                    event["opened_position"]=pos
                    opened += 1
            events.append(event)

        self.no_trade_streak = 0 if opened else self.no_trade_streak + 1
        return events

    def run_cycle(self, research_trigger=2):
        snapshots, prices = self.scan_market()

        plan = self.planner.decide(
            self.portfolio, self.no_trade_streak, research_trigger
        ) if self.planner else None

        monitor_events = self.position_monitor.evaluate(prices) if self.position_monitor else []

        research_results=[]
        trade_events=[]

        if plan and plan.action == PlanAction.RESEARCH:
            research_results = self.research_engine.investigate(snapshots) if self.research_engine else []
            if self.memory:
                for r in research_results:
                    self.memory.remember(
                        "research",
                        f"{r.symbol}: {r.conclusion} (confiança={r.confidence:.0%})"
                    )
            self.no_trade_streak = 0
        else:
            trade_events = self.execute_scan(snapshots)

        equity=self.portfolio.equity(prices)
        return {
            "plan": plan,
            "snapshots": snapshots,
            "prices": prices,
            "monitor_events": monitor_events,
            "research_results": research_results,
            "trade_events": trade_events,
            "equity": equity,
            "no_trade_streak": self.no_trade_streak,
        }
