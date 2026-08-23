from .models import Position

class Portfolio:
    def __init__(self, cash=10000.0):
        self.starting_cash = cash
        self.cash = cash
        self.positions = {}
        self.realized_pnl = 0.0

    def open_long(self, symbol, price, risk_pct, stop_loss_pct, take_profit_pct):
        if symbol in self.positions:
            raise ValueError("Position already open")
        risk_amount = self.cash * (risk_pct/100)
        stop_distance = price * (stop_loss_pct/100)
        qty_by_risk = risk_amount/stop_distance if stop_distance else 0
        max_notional = self.cash*0.20
        qty = min(qty_by_risk, max_notional/price)
        notional = qty*price
        if notional <= 0:
            raise ValueError("Invalid position size")
        self.cash -= notional
        pos = Position(symbol, qty, price, price*(1-stop_loss_pct/100), price*(1+take_profit_pct/100))
        self.positions[symbol] = pos
        return pos

    def close(self, symbol, price):
        pos = self.positions.pop(symbol)
        proceeds = pos.qty*price
        cost = pos.qty*pos.entry_price
        pnl = proceeds-cost
        self.cash += proceeds
        self.realized_pnl += pnl
        return pnl

    def equity(self, prices):
        return self.cash + sum(p.qty*prices.get(s,p.entry_price) for s,p in self.positions.items())
