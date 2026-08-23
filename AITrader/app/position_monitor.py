class PositionMonitor:
    """
    Fecha posições simuladas quando preço atinge stop/alvo.
    Em V2 usa o último preço disponível do scanner.
    """
    def __init__(self, portfolio, db, memory):
        self.portfolio = portfolio
        self.db = db
        self.memory = memory

    def evaluate(self, prices: dict[str, float]):
        events = []
        for symbol, pos in list(self.portfolio.positions.items()):
            price = prices.get(symbol)
            if price is None:
                continue

            reason = None
            if price <= pos.stop_price:
                reason = "STOP_LOSS"
            elif price >= pos.take_profit_price:
                reason = "TAKE_PROFIT"

            if reason:
                pnl = self.portfolio.close(symbol, price)
                self.db.add_trade(symbol, "CLOSE_"+reason, price, pos.qty, pnl)
                lesson = (
                    f"{symbol}: posição encerrada por {reason}; "
                    f"PnL={pnl:.2f} USDT. Revisar hipótese e contexto antes da próxima entrada."
                )
                self.memory.remember("trade_review", lesson)
                events.append({"symbol": symbol, "reason": reason, "price": price, "pnl": pnl})
        return events
