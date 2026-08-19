# KALIDIS Revit Bridge — Catálogo de comandos

Ações disponíveis na V0.6:

- selecionar
- listar
- info
- inventario
- alterar_parametro
- mover
- rotacionar
- copiar
- copiar_entre_projetos
- excluir
- trocar_tipo
- fixar
- desfixar
- ocultar_vista
- mostrar_vista
- isolar_temporario
- reset_isolamento
- regenerar
- salvar

## Identificação de elementos

Use `busca` para localizar por categoria, nome, família, tipo ou parâmetros:

```json
{
  "id": "1",
  "acao": "selecionar",
  "busca": "cuba"
}
```

Ou use IDs exatos:

```json
{
  "id": "2",
  "acao": "mover",
  "elementIds": [308243],
  "x": 500,
  "y": 0,
  "z": 0
}
```

X/Y/Z são em milímetros.

## Alterar parâmetro

```json
{
  "id": "3",
  "acao": "alterar_parametro",
  "busca": "cuba",
  "parametro": "Comentários",
  "valor": "VERIFICADO"
}
```

Para parâmetros numéricos de comprimento, use `unidade` como `mm` ou `m`.

## Rotacionar

```json
{
  "id": "4",
  "acao": "rotacionar",
  "elementIds": [308243],
  "angulo": 90
}
```

## Copiar no mesmo projeto

```json
{
  "id": "5",
  "acao": "copiar",
  "elementIds": [308243],
  "x": 1000,
  "y": 0,
  "z": 0
}
```

## Copiar de outro RVT

```json
{
  "id": "6",
  "acao": "copiar_entre_projetos",
  "arquivoOrigem": "C:\\Projetos\\CM_DANIELE.rvt",
  "busca": "cuba"
}
```

Nem todo tipo de elemento do Revit aceita cópia entre documentos da mesma forma. O Bridge retorna erro quando a API do Revit rejeitar a operação.

## Trocar tipo

```json
{
  "id": "7",
  "acao": "trocar_tipo",
  "elementIds": [308243],
  "novoTipo": "nome do tipo"
}
```

## Vista

`ocultar_vista`, `mostrar_vista`, `isolar_temporario` aceitam `busca` ou `elementIds`.

Para encerrar o isolamento temporário:

```json
{
  "id": "8",
  "acao": "reset_isolamento"
}
```

## Segurança

Operações de escrita usam `Transaction`. Restrições do próprio modelo (elemento hospedado, grupo, vínculo, worksharing, parâmetro somente leitura, constraints etc.) podem impedir uma ação específica. O resultado é gravado em `C:\KALIDIS\Bridge\resultado.json`.
