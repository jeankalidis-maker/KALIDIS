from .indicators import ema, rsi, atr_pct, relative_volume
from .models import MarketSnapshot

def detect_regime(closes, fast, slow, atr):
    spread = (fast-slow)/slow*100 if slow else 0
    if atr > 2.0:
        return "high_volatility"
    if spread > 0.6:
        return "trending_up"
    if spread < -0.6:
        return "trending_down"
    return "ranging"

class MarketScanner:
    def __init__(self, provider):
        self.provider = provider

    def scan_symbol(self, symbol):
        candles = self.provider.get_candles(symbol, "1h", 120)
        closes = [c.close for c in candles]
        fast = ema(closes, 12)
        slow = ema(closes, 26)
        rv = relative_volume(candles)
        a = atr_pct(candles)
        r = rsi(closes)
        regime = detect_regime(closes, fast, slow, a)
        trend_strength = min(abs(fast-slow)/(slow or 1)*100/2.0, 1.0)
        volume_score = min(rv/2.0, 1.0)
        momentum_score = min(abs(r-50)/30, 1.0)
        score = max(0.0,min(1.0,0.45*trend_strength+0.30*volume_score+0.25*momentum_score))
        return MarketSnapshot(
            symbol=symbol, price=closes[-1], regime=regime, rsi=r,
            ema_fast=fast, ema_slow=slow, atr_pct=a,
            relative_volume=rv, opportunity_score=score
        )

    def scan(self, symbols):
        return sorted([self.scan_symbol(s) for s in symbols],
                      key=lambda x:x.opportunity_score, reverse=True)
