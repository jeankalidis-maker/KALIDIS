# Integrações externas — revisão de arquitetura (2026-11-24)

O motor do Kalidis continua nativo no Autodesk Revit. Nenhuma DLL externa é instalada automaticamente.
- BIMCraft (https://github.com/IPedrax/bimcraft): MIT ou Apache-2.0; leitor RVT Apache-2.0 apenas. Referência de registro, CLI, schemas, contexto e transações. Registro e CLI originais implementados no Kalidis; undo/redo e simulação de scripts completos ainda pendentes. Não importar o motor Rust.
- KenLP (https://github.com/KenLP/RevitMCPServer): portes já documentados em THIRD-PARTY-KenLP.txt; revisar novos comandos individualmente e garantir transações, risco e dry-run.
- pyRevit (https://github.com/pyrevitlabs/pyRevit): GPL-3.0 e avisos por componente; manter instalação/integração separadas, não copiar código sem revisão de compatibilidade.
- AndreyStartsev (https://github.com/AndreyStartsev/revit-mcp): manter atribuição e revisão fixa existente; execução pyRevit adicional ainda não integrada.
- CAD-MCP (https://github.com/shtirlitsDva/cad-mcp): Roslyn no contexto do host. Revisão da licença e implementação ainda pendentes. Execução arbitrária full-trust não é sandbox; manter compilação e aprovação explícitas.
- Mvclr (https://github.com/Mvclr/revit-mcp-server): portas existentes documentadas; avaliar comandos complementares.
- FamFab (https://github.com/kilkellym/ArchSmarterFamFab): MIT, partes nativas já adaptadas; geração a partir de imagem ainda requer desenho de fluxo de referência e revisão do provedor/custo.
- Horizun (https://github.com/HorizunGroup/horizun-revit-mcp): Community Apache-2.0, avisos existentes; avaliar funções complementares por versão.
- Dynamo: usar instalação Autodesk existente; execução geral de grafos depende de validação e revisão de efeitos externos.
- UI Automation: Get-RevitUIContext.ps1 apenas consulta o contexto; não envia teclas nem clica. Não alterar a política de execução para forçar uso.
- Kalidis legado: reaproveitar protocolos somente após testes de compatibilidade.

Não equivale a incorporação integral ou auditoria jurídica dos projetos.
