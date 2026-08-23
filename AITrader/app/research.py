from dataclasses import dataclass

@dataclass
class ResearchResult:
    symbol: str
    question: str
    conclusion: str
    confidence: float

class ResearchEngine:
    """
    Research Mode V2:
    ainda não é o backtest completo da V3.
    Ele compara regime, score, RSI e volume e registra uma conclusão pesquisável.
    """
    def investigate(self, snapshots):
        results = []
        for s in snapshots[:3]:
            question = f"Existe vantagem investigável em {s.symbol} no regime {s.regime}?"
            if s.regime == "ranging" and s.rsi < 35:
                conclusion = "Possível candidato a mean reversion; requer backtest antes de operar."
                conf = 0.58
            elif s.regime == "trending_up" and s.relative_volume > 1.1:
                conclusion = "Possível candidato a momentum/breakout; requer validação histórica."
                conf = 0.66
            elif s.regime == "trending_down":
                conclusion = "Evitar long por enquanto; pesquisar reversão apenas após mudança de estrutura."
                conf = 0.62
            else:
                conclusion = "Sem vantagem clara; manter observação."
                conf = 0.45
            results.append(ResearchResult(s.symbol, question, conclusion, conf))
        return results
