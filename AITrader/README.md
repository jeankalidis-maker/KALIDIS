# KALIDIS AI TRADER — V2

Agente autônomo de pesquisa e paper trading, construído para evoluir em direção a um sistema adaptativo orientado a objetivo.

## O que já faz
- Paper trading apenas
- Portfólio virtual em USDT
- BTC/USDT, ETH/USDT e SOL/USDT
- Provider sintético offline
- Provider público da Binance sem chave
- EMA, RSI, ATR e volume relativo
- Detecção de regime
- Market scanner
- Criação automática de hipóteses
- Meta-planner
- Loop em múltiplos ciclos
- No-trade streak
- Research Mode básico
- Risk Supervisor obrigatório
- Monitor de posições com stop/take profit
- SQLite para decisões, hipóteses, trades e memórias
- Safe mode básico
- Testes automatizados

## Segurança
Esta versão NÃO opera dinheiro real.
Não possui saque.
Não possui alavancagem.
Toda ordem passa pelo Risk Supervisor.

## Rodar no Windows

```powershell
python -m venv .venv
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\.venv\Scripts\activate
pip install -r requirements.txt
python -m app.main
```

Para usar dados públicos da Binance:

```powershell
$env:KALIDIS_PROVIDER="binance"
python -m app.main
```

## Testes

```powershell
pytest -q
```

## Próximos passos
- backtest automático
- Strategy Lab
- lifecycle de estratégias
- memória semântica
- adaptação baseada em evidência
- crítico/reviewer
- LLM local
- chat com o agente
- dashboard web
- deploy 24/7
