from dataclasses import dataclass
from enum import Enum

class PlanAction(str, Enum):
    MONITOR = "MONITOR"
    SCAN = "SCAN"
    RESEARCH = "RESEARCH"
    PAUSE = "PAUSE"

@dataclass
class Plan:
    action: PlanAction
    reason: str

class MetaPlanner:
    """
    V2 planner:
    - prioriza monitoramento se há posição aberta;
    - pesquisa após ciclos seguidos sem operação;
    - caso contrário, escaneia mercado.
    """
    def decide(self, portfolio, no_trade_streak: int, research_trigger: int) -> Plan:
        if portfolio.positions:
            return Plan(PlanAction.MONITOR, "Há posição aberta; monitoramento tem prioridade.")
        if no_trade_streak >= research_trigger:
            return Plan(PlanAction.RESEARCH, "Múltiplos ciclos sem oportunidade; entrar em Research Mode.")
        return Plan(PlanAction.SCAN, "Procurar oportunidades no mercado.")
