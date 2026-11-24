# Kalidis AI Engine — registro de desenvolvimento
Atualizado em 2026-11-24. Não equivale a uma certificação de cobertura.
V = validado em Revit; C = código compilado, não validado em sessão; P = pendente.
## Infraestrutura
- [V] Comunicação via Desktop Commander e HTTP loopback autenticado, ExternalEvent.
- [V] Instalação da versão inicial, health, catálogo 566 nomes PostableCommand, snapshot.
- [V] Snapshot real: 2 níveis, 28 tipos de parede, 81 famílias; 55 ms internos, 1032 ms requisição local.
- [C] Proteção de escrita em projeto sandbox, transação e rollback.
- [P] Registro de comandos com schemas completos, idempotência persistente, auditoria e recuperação.
- [P] Cache invalidado por DocumentChanged, fila robusta, cancelamento e telemetria.
## Modelagem
- [C] Batch até 200: paredes, pisos, forros, níveis, eixos, ambientes, materiais, vistas 3D, plantas.
- [C] Criação de pranchas e tabelas, snapshot de tipos e consulta de disponibilidade de comandos.
- [P] Portas, janelas, mobiliário, telhados, escadas, rampas, guarda-corpos, paredes cortina, estruturas, armaduras, conexões, topossólidos, MEP, cotas, tags, detalhes, fases, worksets, vínculos, templates e filtros.
## Famílias e arquivos
- [C] RFA exportação, criação de sólido retangular com altura parametrizada, carregamento, inspeção de tipos, pesquisa local de RFA/RFT.
- [C] Exportação PNG e salvamento sandbox.
- [P] Edição de RFA, largura/comprimento parametrizados, fórmulas, planos de referência, geometria avançada, conectores, biblioteca externa e verificação de licenças.
- [P] RVT novo/abrir cópia, IFC/DWG/PDF, tabelas CSV, verificação visual e correções.
## Referências de integração (não incorporadas até agora)
- [P] BIMCraft: estudar licença e código do registro central, command API, schemas, extensões, CLI, MCP, contexto, undo e dry-run; adaptar em vez de copiar motor BIM.
- [P] KenLP: revisar licença e integrar/adaptar comandos e transações; análise inicial anterior não equivale a incorporação.
- [P] pyRevit e AndreyStartsev: execução Python controlada.
- [P] CAD-MCP e Mvclr: execução C# controlada e ferramentas complementares.
- [P] FamFab: geração RFA a partir de referência, revisar dependência de API paga.
- [P] Horizun: funções arquitetura/estrutura/MEP, verificar limites licença gratuita.
- [P] Dynamo: integração de grafos.
- [P] Windows UI Automation: controle complementar, confirmação contextual.
- [P] Kalidis existente: reaproveitar módulos e protocolos após testes de compatibilidade.
## Estado de entrega
Versão instalada: protótipo com catalog, snapshot, test_wall, batch, export_family.
Commit 97a38f2895e119abfb34e4e8c5f9b393dfc17087 compilado em Windows com 0 erros, 3 avisos, 7.67 s; ainda não instalado.
Não há evidência de validação de escrita no Revit. Não há cobertura percentual comprovada.
