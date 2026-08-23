from app.market_data import SyntheticProvider
from app.indicators import ema, rsi, atr_pct

def test_indicators_are_sane():
    candles=SyntheticProvider().get_candles("BTC/USDT", "1h", 120)
    closes=[c.close for c in candles]
    assert ema(closes,12)>0
    assert 0<=rsi(closes)<=100
    assert atr_pct(candles)>0
