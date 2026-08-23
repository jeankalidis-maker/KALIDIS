from math import fsum

def ema(values, period):
    if not values:
        return 0.0
    alpha = 2 / (period + 1)
    value = values[0]
    for x in values[1:]:
        value = alpha * x + (1-alpha) * value
    return float(value)

def rsi(values, period=14):
    if len(values) < period + 1:
        return 50.0
    gains, losses = [], []
    for a, b in zip(values[-period-1:-1], values[-period:]):
        d = b-a
        gains.append(max(d, 0))
        losses.append(max(-d, 0))
    avg_gain = fsum(gains)/period
    avg_loss = fsum(losses)/period
    if avg_loss == 0:
        return 100.0 if avg_gain > 0 else 50.0
    rs = avg_gain/avg_loss
    return 100 - (100/(1+rs))

def atr_pct(candles, period=14):
    if len(candles) < 2:
        return 0.0
    trs = []
    prev = candles[0].close
    for c in candles[1:]:
        tr = max(c.high-c.low, abs(c.high-prev), abs(c.low-prev))
        trs.append(tr)
        prev = c.close
    sample = trs[-period:]
    atr = sum(sample)/len(sample)
    return atr/candles[-1].close*100 if candles[-1].close else 0.0

def relative_volume(candles, period=20):
    vols = [c.volume for c in candles]
    if len(vols) < 2:
        return 1.0
    base = vols[-period-1:-1] or vols[:-1]
    avg = sum(base)/len(base)
    return vols[-1]/avg if avg else 1.0
