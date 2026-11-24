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

## Validação MCP
- [V] JSON-RPC initialize, tools/list (6 ferramentas somente leitura), snapshot real (81 famílias, 2 níveis), bloqueio de test_wall em read-only; processo 24324, exit 0, 6.52 s.
- [C] Build avançado 97a38f2: 0 erros, 3 avisos, 7.67 s; ainda não instalado.

## Avanços 06 e 07
- [C] Batch: vigas, pilares, componentes livres/hospedados, tubos, dutos, eletrodutos, bandejas, aberturas, textos, linhas de detalhe, viewport. Build 1cff4cd: 0 erros, 3 avisos, 4.48 s.
- [C] Novo RVT de teste, abertura, consulta de elementos e bounding boxes, mover/copiar/rotacionar/alterar texto/trocar tipo/excluir em lote, exportação IFC/DWG/PDF. Build e0c9022: 0 erros, 3 avisos, 3.62 s.
- [C] Catálogo MCP de 20 ferramentas principais; 21 operações de criação em lote. Não instalado nem validado em sessão.

## Avanços 08 e 09
- [C] create_family_spec: múltiplas extrusões, perfis poligonais, vazios, alturas compartilhadas e fórmulas. Corte efetivo de vazios ainda não comprovado.
- [C] Referências $alias entre operações de criação no mesmo lote.
- [C] Build 87ef510: 0 erros, 4 avisos, 4.47 s. Ainda não instalado.

## Avanços 10 e 11
- [C] Fundações, topossólidos, telhado de perímetro, cortes, linhas de modelo, tags, parâmetros tipados, fixar/renomear, proteção de caminhos e transação IFC. Build e8034b1: 0 erros, 4 avisos, 3.86 s.
- [C] Cotas entre eixos, campos de tabelas, duplicação de tipos, curvas/uniões MEP, inspeção de conectores, vistas, seleção, campos de tabela e candidatos a interferências por bounding box. Build 104dee3: 0 erros, 4 avisos, 4.25 s.
- [C] 27 ferramentas MCP, 32 operações de criação/edição em lote. Não instaladas/testadas em sessão.

## Avanço 12
- [C] Carregador C# de DLLs locais aprovadas por SHA-256; execução full-trust, não sandbox de segurança. Não executado em sessão; não equivale a Roslyn dinâmico nem integração CAD-MCP.
- [V] Cliente Python externo: snapshot real com 2 níveis e 81 famílias, PY_EXIT=0. Não equivale a Python in-process ou integração pyRevit.
- [C] Build d5bb502: 0 erros, 4 avisos, 3.97 s; 29 ferramentas MCP.
- [P] Smoke-Test.ps1 criado para RVT independente; executar após instalar DLL ampliada. Update-Engine.ps1 criado; exige fechar Revit normalmente e cria backup.
- Revit 2025.4 ainda aberto no projeto original; nenhuma escrita feita neste projeto. Integrações diretas externas continuam pendentes.

## Avanço 13
- [C] Extensão de exemplo WallAudit em AIEngine.Sample, compilada (7168 bytes); execução no Revit pendente.
- [C] model_health, clash_confirm (filtro Revit, limitações para geometria não suportada e elementos unidos), schedule_data.
- [C] activate_view, zoom_fit, preview_view, dynamo_open. 36 ferramentas MCP.
- [V] Presença local de DynamoForRevit e DynamoPlayerForRevit e enum nativo Dynamo/DynamoPlayer; pyRevit não detectado nos locais consultados. Execução de grafos não implementada.
- [C] Build 0c9ce7e: 0 erros, 5 avisos, 6.64 s; DLL SHA256 F1D828653407D4FF9E8EE35D8139B01F84214DD55A1D18AA2BDC5CEA35486508. Atualizador preflight OK, Revit aberto: instalação não efetuada.
- [P] Fechar Revit normalmente após salvar; instalar com backup e rodar Smoke-Test.ps1 em RVT separado. Nenhuma alteração no projeto original.

## Avanço 14
- [V] Catálogo da DLL atualmente instalada reconfirmado: 566 nomes PostableCommand, incluindo ArchitecturalWall, ArchitecturalFloor, RoofByFootprint, RoofByExtrusion, Door, Window, Stair, Pipe, Dynamo.
- [C] native_status: busca textual, filtro de disponibilidade contextual e indicação de comandos aprovados. native_post: lista restrita de seis ações de interface (TileViews, ThinLines, ProjectBrowser, TogglePropertiesPalette, SystemBrowser, ToggleRevealHiddenElementsMode), em RVT de teste. Retorno confirma enfileiramento, não conclusão da ação. Não instalado/testado em sessão.
- [C] Build 5e05fd3: 0 erros, 5 avisos, 5.81 s. 37 ferramentas MCP registradas. Integração direta externa e motores dinâmicos completos ainda pendentes.

## Avanço 15
- [C] 8 operações adicionais: restrições de topo de paredes, união/separação de geometrias, room tags, inserção de tabelas em pranchas, plantas/limites/elementos de área. Build ebac2a4: 0 erros, 5 avisos, 5.41 s.
- [C] 10 ferramentas KenLP efetivamente portadas (MIT, atribuição e revisão fixa em THIRD-PARTY-KenLP.txt): list_family_types, list_phases, list_view_templates, get_linked_files, get_worksets, list_categories, get_doors, get_element_rooms, get_tags_in_view, get_linked_elements. Correção de bounding boxes de vínculos rotacionados com oito cantos. Build 47857c8: 0 erros, 5 avisos, 5.59 s.
- [C] 47 ferramentas MCP cadastradas, 40 ações de batch. Testes no Revit pendentes.
- [V] Stage-Engine.ps1: implantação lado a lado testada; falha inicial de atribuição XML corrigida. Manifesto do próprio plugin atualizado e backup criado; DLL SHA256 8E9776CFA30D5482C4CD182270429CD628B38C6584DB426A34C74BD41027A12A. Nenhum projeto alterado. Revit atual ainda usa 0.1.0; nova DLL 0.3.0 carregará na próxima abertura normal, sujeita à validação.
- [P] Smoke test após reinício normal; integração integral de KenLP e demais projetos ainda pendente.
