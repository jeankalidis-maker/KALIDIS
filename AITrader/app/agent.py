import uuid
from .models import Hypothesis, TradeProposal, Action

class AutonomousAgent:
    """
    Arquitetura orientada a objetivo.
    O comportamento inteligente avançado entra via LLMAdapter/Research Engine.
    """
    def create_hypothesis(self, snapshot):
        if snapshot.regime == "trending_up":
            thesis = f"{snapshot.symbol} mostra tendência positiva; investigar continuação com confirmação de volume."
            invalidation = "Perda da estrutura de tendência / stop técnico."
        elif snapshot.regime == "trending_down":
            thesis = f"{snapshot.symbol} está em tendência negativa; não comprar enquanto a estrutura não melhorar."
            invalidation = "Mudança confirmada de regime."
        else:
            thesis = f"{snapshot.symbol} não apresenta tendência clara; priorizar observação/pesquisa."
            invalidation = "Rompimento confirmado da faixa."
        confidence = max(0.35, min(0.85, 0.35 + snapshot.opportunity_score*0.55))
        return Hypothesis("hyp_"+uuid.uuid4().hex[:10], snapshot.symbol, thesis,
                          snapshot.regime, confidence, invalidation)

    def decide(self, snapshot, hypothesis):
        if snapshot.regime != "trending_up" or snapshot.opportunity_score < 0.42:
            return TradeProposal(
                Action.NO_TRADE, snapshot.symbol, hypothesis.confidence,
                hypothesis_id=hypothesis.id,
                summary="Evidência insuficiente. Permanecer em observação.",
                invalidation=hypothesis.invalidation
            )
        stop = max(0.8, min(2.0, snapshot.atr_pct*1.2))
        take = stop*2
        return TradeProposal(
            Action.OPEN_LONG, snapshot.symbol, hypothesis.confidence,
            risk_pct=0.35, stop_loss_pct=stop, take_profit_pct=take,
            hypothesis_id=hypothesis.id,
            summary="Baseline: tendência positiva + oportunidade mínima atingida.",
            invalidation=hypothesis.invalidation
        )
