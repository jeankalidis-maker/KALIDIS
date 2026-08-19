# KALIDIS Revit Bridge — Catálogo de comandos

## Núcleo de leitura e edição

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

## Camada avançada

- espelhar
- definir_escala_vista
- criar_nivel
- criar_eixo
- criar_parede
- criar_piso
- criar_forro
- criar_ambiente
- carregar_familia
- inserir_familia
- criar_material
- atribuir_material
- criar_vista_3d
- duplicar_vista
- criar_folha
- criar_tubo
- criar_duto
- criar_eletroduto
- criar_bandeja

## Gateway para comandos nativos do Revit

A ação `listar_comandos_revit` devolve todos os nomes disponíveis no enum `Autodesk.Revit.UI.PostableCommand` da versão instalada do Revit. Isso dá acesso ao maior conjunto de comandos nativos que a API permite postar para a interface do Revit.

```json
{
  "id": "native-list",
  "acao": "listar_comandos_revit"
}
```

Você também pode filtrar:

```json
{
  "id": "native-filter",
  "acao": "listar_comandos_revit",
  "busca": "Wall"
}
```

Para enviar um comando nativo:

```json
{
  "id": "native-1",
  "acao": "comando_revit",
  "comando": "NOME_DO_POSTABLE_COMMAND"
}
```

O Revit só aceita o comando se `CanPostCommand` for verdadeiro no contexto atual. Alguns comandos abrem ferramentas ou diálogos e ainda precisam de interação do usuário.

## Exemplos avançados

### Espelhar

```json
{
  "id": "mirror-1",
  "acao": "espelhar",
  "elementIds": [308243],
  "x": 0,
  "y": 0,
  "z": 0,
  "nx": 1,
  "ny": 0,
  "nz": 0,
  "copiar": true
}
```

### Criar nível

```json
{
  "id": "nivel-1",
  "acao": "criar_nivel",
  "nome": "Nível 2",
  "elevacao": 3000
}
```

### Criar parede

```json
{
  "id": "parede-1",
  "acao": "criar_parede",
  "nivel": "Nível 1",
  "x": 0,
  "y": 0,
  "z": 0,
  "x2": 5000,
  "y2": 0,
  "z2": 0
}
```

### Criar piso/forro

`pontos` é uma lista de pontos `[x,y,z]` em milímetros.

```json
{
  "id": "piso-1",
  "acao": "criar_piso",
  "nivel": "Nível 1",
  "tipo": "Concreto",
  "pontos": [[0,0,0],[5000,0,0],[5000,4000,0],[0,4000,0]]
}
```

### Carregar e inserir família

```json
{
  "id": "rfa-1",
  "acao": "carregar_familia",
  "arquivo": "C:\\Familias\\Banco.rfa"
}
```

```json
{
  "id": "fam-1",
  "acao": "inserir_familia",
  "familia": "Banco",
  "tipo": "Banco 1200",
  "x": 1000,
  "y": 2000,
  "z": 0
}
```

### Criar material

```json
{
  "id": "mat-1",
  "acao": "criar_material",
  "nome": "Granitina KALIDIS"
}
```

### Criar MEP

Tubos e dutos usam `nivel`, `tipo`, `sistema` e dois pontos. Eletrodutos e bandejas usam `nivel`, `tipo` e dois pontos.

```json
{
  "id": "pipe-1",
  "acao": "criar_tubo",
  "nivel": "Nível 1",
  "tipo": "PVC",
  "sistema": "Sanitário",
  "x": 0,
  "y": 0,
  "z": 300,
  "x2": 3000,
  "y2": 0,
  "z2": 300
}
```

## Unidades

Coordenadas, deslocamentos e elevações do bridge são informados em milímetros. Ângulos do núcleo são em graus.

## Limites reais

A Revit API é ampla, mas não existe uma função única que represente literalmente todo comando da interface. Algumas operações são específicas de categoria, hospedagem, sistema, sketch, grupo, vínculo, worksharing, fase, design option ou contexto de vista. A camada `comando_revit` amplia muito a cobertura usando `PostableCommand`, enquanto as ações programáticas permitem automação sem interação para os casos implementados.

Operações de escrita usam `Transaction`. Restrições do próprio modelo podem impedir uma ação específica; nesse caso o erro é retornado em `C:\KALIDIS\Bridge\resultado.json`.
