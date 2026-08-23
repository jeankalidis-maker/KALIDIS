from __future__ import annotations
from abc import ABC, abstractmethod
from urllib.request import urlopen, Request
from urllib.parse import urlencode
import json, math, random, time
from .models import Candle

class MarketDataProvider(ABC):
    @abstractmethod
    def get_candles(self, symbol: str, interval: str="1h", limit: int=120) -> list[Candle]:
        raise NotImplementedError

class SyntheticProvider(MarketDataProvider):
    BASE = {"BTC/USDT":60000.0, "ETH/USDT":3000.0, "SOL/USDT":150.0}
    def __init__(self, seed=42):
        self.seed = seed
    def get_candles(self, symbol, interval="1h", limit=120):
        rnd = random.Random(self.seed + sum(map(ord, symbol)) + len(interval))
        price = self.BASE.get(symbol, 100.0)
        out = []
        ts = int(time.time()) - limit*3600
        drift = {"BTC/USDT":0.0007, "ETH/USDT":0.0003, "SOL/USDT":-0.0001}.get(symbol,0)
        for i in range(limit):
            cyc = math.sin(i/11)*0.002
            ret = drift + cyc + rnd.uniform(-0.003, 0.003)
            o = price
            c = max(0.01, o*(1+ret))
            wiggle = abs(rnd.uniform(0.0005,0.004))
            h = max(o,c)*(1+wiggle)
            l = min(o,c)*(1-wiggle)
            v = 1000*(1+rnd.random()*2)
            if i == limit-1 and symbol == "BTC/USDT":
                v *= 1.8
            out.append(Candle(ts+i*3600,o,h,l,c,v))
            price=c
        return out

class BinancePublicProvider(MarketDataProvider):
    MAP_INTERVAL = {"15m":"15m","1h":"1h","4h":"4h"}
    def get_candles(self, symbol, interval="1h", limit=120):
        pair = symbol.replace("/","")
        params = urlencode({"symbol":pair,"interval":self.MAP_INTERVAL.get(interval, interval),"limit":limit})
        url = "https://api.binance.com/api/v3/klines?" + params
        req = Request(url, headers={"User-Agent":"KalidisAITrader/1.0"})
        with urlopen(req, timeout=10) as r:
            data = json.loads(r.read().decode())
        return [
            Candle(int(x[0]/1000), float(x[1]), float(x[2]), float(x[3]), float(x[4]), float(x[5]))
            for x in data
        ]
