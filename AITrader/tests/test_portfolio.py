from app.portfolio import Portfolio

def test_paper_pnl():
    p=Portfolio(10000)
    pos=p.open_long("BTC/USDT",100,0.5,1,2)
    pnl=p.close("BTC/USDT",102)
    assert pnl>0
    assert p.realized_pnl==pnl
