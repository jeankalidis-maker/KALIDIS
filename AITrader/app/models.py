from dataclasses import dataclass, field
from enum import Enum
from typing import Optional
from datetime import datetime, timezone

def utcnow():
    return datetime.now(timezone.utc)

class Action(str, Enum):
    NO_TRADE = "NO_TRADE"
    OPEN_LONG = "OPEN_LONG"
    CLOSE_POSITION = "CLOSE_POSITION"

class RiskDecision(str, Enum):
    APPROVED = "APPROVED"
    APPROVED_WITH_REDUCTION = "APPROVED_WITH_REDUCTION"
    BLOCKED = "BLOCKED"

@dataclass
class Candle:
    ts: int
    open: float
    high: float
    low: float
    close: float
    volume: float

@dataclass
class MarketSnapshot:
    symbol: str
    price: float
    regime: str
    rsi: float
    ema_fast: float
    ema_slow: float
    atr_pct: float
    relative_volume: float
    opportunity_score: float

@dataclass
class Hypothesis:
    id: str
    symbol: str
    thesis: str
    regime: str
    confidence: float
    invalidation: str
    status: str = "proposed"

@dataclass
class TradeProposal:
    action: Action
    symbol: str
    confidence: float
    risk_pct: float = 0.0
    stop_loss_pct: Optional[float] = None
    take_profit_pct: Optional[float] = None
    hypothesis_id: Optional[str] = None
    summary: str = ""
    invalidation: str = ""

@dataclass
class Position:
    symbol: str
    qty: float
    entry_price: float
    stop_price: float
    take_profit_price: float
    opened_at: object = field(default_factory=utcnow)
