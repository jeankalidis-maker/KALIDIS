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

## Avanços 18 e 19
- [C] 8 operações adaptadas de KenLP: templates, detalhamento, section box, duplicação de vistas, ocultar/reexibir elementos, agrupamento e perspectiva. 18 funcionalidades KenLP portadas no total. Build 2357644: 0 erros, 5 avisos, 6.34 s.
- [V] kalidis_recipes.py: geração local de lote de ambiente retangular com paredes/piso/ambiente e componente hospedado. Testes offline RECIPE_TESTS=PASS; modelagem em Revit ainda não validada.
- [V] Nova implantação lado a lado: SHA256 8EB4B2D1C4627E90F4006B7E6ADEF8B5D5B2C13F2C70F802A51C18970F6BAF97; backup preservado. Revit ainda aberto.
- [V] MCP 0.3.1 consulta catálogo real antes de anunciar ferramentas. Teste com DLL 0.1.0: 2 ferramentas read-only, 5 totais, exit 0. Catálogo de desenvolvimento tem 47 ferramentas e 48 ações batch; não equivale ao catálogo ativo.
- [P] Reinício normal do Revit e smoke test em RVT separado. Integrações restantes e execução dinâmica in-process pendentes.

## Avanço 20 — FamFab
- [C] Adaptação efetiva de padrões NewBlend e NewReferencePlane de ArchSmarterFamFab, com licença MIT e revisão fixa em THIRD-PARTY-FamFab.txt. Até 50 blends e 50 planos X/Y; parâmetros adicionais de comprimento, número e texto, tipo/instância, fórmulas. Planos e parâmetros nominais de largura/profundidade não vinculados automaticamente à geometria; sweep e restrições geométricas pendentes.
- [C] Build ec8d15b: 0 erros, 5 avisos, 6.93 s. 47 ferramentas MCP; create_family_spec ampliado.
- [V] family_recipes.py: testes offline mesa de cinco extrusões e pedestal com dois blends, FAMILY_RECIPE_TESTS=PASS. RFA real ainda não gerado.
- [V] DLL implantada lado a lado SHA256 5ADF2B1D6142C469122C4A7BF0799BD9AB2C3CDE1731243ABFB7D96F84EE831B, backup confirmado. Sessão Revit PID 644 ainda usa 0.1.0; templates RFT encontrados. Reinício normal e testes reais pendentes.

## Avanço 21 — edição de RFA, MEP e Mvclr
- [C] edit_family_copy: edição de cópia independente, novos tipos, seleção de tipo, parâmetros de comprimento/número/texto, valores e fórmulas, rollback por padrão, sem recarregar família original.
- [C] Exportação CSV de tabelas com UTF-8 BOM, separador ponto e vírgula e proteção contra interpretação de fórmulas.
- [C] Adaptação de padrões Mvclr para espelhamento e tubulações/dutos dimensionados, licença MIT preservada. Adições próprias: tês e transições MEP. 48 ferramentas MCP e 53 ações batch.
- [C] Build 1bb2233: 0 erros, 5 avisos, 12.85 s. DLL implantada lado a lado SHA256 E8CA036D3B43940240655D89C5C52005796B22E53BCC8164FF670688D619B944, backup confirmado. Revit permanece aberto, testes dentro da sessão pendentes.
- [P] Integração integral Mvclr, Horizun, AndreyStartsev, pyRevit, Dynamo e execução dinâmica completa continuam pendentes.

## Avanços 22 e 23 — Horizun e AndreyStartsev
- [C] Horizun: auditoria de elementos que cruzam níveis por bounding box transformado, aviso paginado com FailureDefinitionId e UniqueId. Licença Apache 2.0 e NOTICE preservados. Resultados consultivos, não prescrição automática de divisão de elementos. Build 2677170: 0 erros, 5 avisos, 7.36 s.
- [V] AndreyStartsev: adaptação dos exemplos de relatório e atualização de parâmetros em lote via cliente Python estruturado, IDs explícitos, dry-run e leitura de retorno; testes com mock passaram (não execução in-process). MIT preservada.
- [C] Smoke-Test.ps1 ampliado para consultas KenLP/Horizun e exportação PNG. Sintaxe PowerShell validada, execução real pendente.
- [V] DLL preparada para próxima abertura, SHA256 A8F63A18095930CE34A119B14155D49B33F20ED267E986B544AB27FB7B512C7C; backup confirmado. 50 ferramentas MCP registradas, 53 ações batch. Sessão original permanece aberta.
- [P] Reinício normal e teste de ponta a ponta; execução dinâmica, integrações completas e autocorreção geométrica ainda pendentes.

## Avanço 24 — validação real no Revit 2025.4
- [V] Reinício realizado pelo usuário. PID 20500, engine 0.3.0, catálogo ativo de 50 ferramentas e 566 comandos nativos.
- [V] Smoke-Test.ps1: template brasileiro, RVT independente Smoke-9c3c99fd336746a0b555995ee2728a38.rvt, rollback e commit verificados, parede 325325, 36 parâmetros, movimento rollback, 11 vistas, registro extensões, 33 categorias, 2 fases, auditoria warnings/level span, consulta portas, PNG e salvamento. Exit 0 (32.47 s).
- [V] Família RFA KalidisTable-9747ce5fb743478f8eefcec635a03a5d.rfa criada com 5 extrusões e planos/parâmetros adicionais; carregada no projeto teste família 325326. Instância 326272 verificada. Não implica validação de todas as restrições paramétricas.
- [V] Receita de ambiente corrigida para x/y no nível raiz da operação room. Lote com quatro paredes, piso, ambiente e mesa: rollback 214 ms, commit 201 ms, IDs verificados. Área do ambiente 18.77268225 m². Primeira assertiva de nome falhou porque Room.Name retorna nome com número ('Sala de teste 1'); modelo já confirmado, não repetido. Releitura por ID, salvamento e PNG confirmados, exit 0.
- [P] Validação das demais operações, exportações, blends, MEP, extensões, integrações completas e motores dinâmicos. Nenhum projeto original modificado.

## Avanço 25 — validação de modelagem avançada
- [V] edit_family_copy: novo tipo Kalidis Test e alteração textual, arquivo independente TableEdit-5fdd3d4433574caa89a1d240d6d1868d.rfa salvo, sem recarregar original.
- [V] Lote de vistas: vista 3D, detalhamento Fine, section box, perspectiva e corte. Commit 189 ms; IDs 326306, 326317, 326327. Topossólido 326350, commit 34 ms.
- [V] Dois tubos 50 mm, IDs 326361 e 326364, commit 116 ms; leitura dos conectores. Duto retangular 300x200, ID 326371, commit 40 ms.
- [V] Tabela 326374: quatro campos, leitura de 7 linhas (incluindo cabeçalho/linha vazia), exportação Paredes.csv. Prancha 326381, viewport 326386 e tabela 326387. PNG 3D salvo. RVT de teste salvo.
- [V] Extensão C# própria Kalidis.Sample.dll aprovada localmente por SHA-256, wall_audit executada, 5 paredes e comprimentos confirmados, rollback. Extensões são full-trust, não sandbox de segurança.
- [V] Workflow Andrey adaptado: alteração textual em duas paredes com prévia rollback e leitura de retorno, commit e salvamento confirmados. MCP lista 50 ferramentas ativas.
- [F] Cotovelo: failed to insert elbow; template sem famílias OST_PipeFitting, nenhuma RFA de joelho/cotovelo/elbow encontrada na biblioteca local pesquisada. Causa provável não conclusiva. [F] Telhado: Value cannot be null. [F] Blend: Unexpected internal error: code 1. Não marcar como validados. Lotes falhos ocorreram em dry-run; nenhuma repetição de commit incerto.
- [P] Diagnosticar e corrigir os três erros, validar demais operações, motores dinâmicos e integração completa. Projeto original preservado.

## Avanço 27 — correções e documentação
- [C] Telhado: inicialização de ModelCurveArray, criação de CurveArray via Application e iteração explícita conforme exemplo API. Blend: perfis em planos paralelos com cotas distintas, conforme contrato NewBlend; não confirmado em sessão. Regeneração após aliases. Referências: https://www.revitapidocs.com/2025/110741bf-b041-9f78-0832-ec9f5892cebc.htm e https://rvtdocs.com/2025/00173a0d-6075-0270-fd7e-080da420d339/.
- [C] routing_preferences: diagnóstico de segmentos e regras de conexões MEP. 51 ferramentas cadastradas, 53 ações batch. Build ffdccad: 0 erros, 5 avisos, 8.38 s. DLL implantada SHA256 DB262CD91614BE6FCA4E0E0A93B0D926252ED724B62BB6ACDBB023D7BCD45D72, backup confirmado; requer reinício normal para validar correções.
- [V] Exportações reais do RVT teste: PDF 39722 bytes, DWG 18521 bytes + PCP 22648 bytes, IFC 91711 bytes; arquivos existentes e não vazios. Sem validação externa de conteúdo/qualidade.
- [V] Dois eixos 326390/326391 (34 ms), cota 326393 (11 ms), forro 326403 (35 ms), porta hospedada 326410 (416 ms). Dry-run e commit verificados. Projeto original preservado.
- [P] Reteste de telhado, blend, diagnóstico de conexões, estruturas avançadas e integrações completas. Sessão atual continua com DLL anterior.

## Avanços 28 e 29 — estruturas, elétrica e escadas
- [V] Dois pilares e viga, IDs 326417/326418/326420, 441 ms; eletroduto e eletrocalha 326426/326428, 31 ms; janela 326431, 299 ms; identificador ambiente 326434, 22 ms; texto e linha 326437/326438, 13 ms. Dry-run, commit, IDs e save verificados. Geometria, não dimensionamento de engenharia.
- [V] Espelhamento mesa 326443 (58 ms), duplicação vista 326458 (44 ms), ocultar/reexibir em lote (13 ms), grupo metálico 326476 (161 ms). RVT salvo. Auditoria: três avisos de vazão MEP, sistemas ainda não conectados/comissionados. Não suprimidos.
- [C] straight_stair: StairsEditScope, corrida reta, largura, verificação de espelhos e desnível, cancelamento dry-run, falhas recusadas. Mais 5 ações: railing, shaft_opening, wall_flip, conduit_sized, cable_tray_sized. Contratos consultados no RevitAPI.xml 2025 local. 52 ferramentas cadastradas e 58 ações batch; sessão atual 50 ferramentas. Build 6c6367e: 0 erros, 5 avisos, 7.17 s.
- [V] Implantação lado a lado SHA256 0D8B2A8E91EC2AA5D5F04A9ED29D485BE6DA21991A2EF95D3CC465D3D1549EF4, backup confirmado, STAGE_EXIT=0. Projeto original preservado.
- [P] Reinício normal para validar escada, guarda-corpo, shaft, elétrica dimensionada, telhado, Blend e diagnóstico de roteamento. Demais integrações completas ainda pendentes.

## Avanço 30 — edição, biblioteca e robustez
- [V] Testes reais: cópia, rotação, movimento, pin/unpin e exclusão de cópia temporária (326505). Exclusão simulada preservou elemento; commit confirmado por releitura. RVT salvo.
- [V] Inventário: 446 RFA na biblioteca Autodesk instalada; buscas por sapata e cotovelo não localizaram RFA útil, somente templates RFT em inglês.
- [V] library_stage.py: staging explícito por SHA256, criação exclusiva sem sobrescrever, teste fixture de hash incorreto e colisão aprovado; testado com RFA nativo gerado pelo próprio motor.
- [V] Sapata geométrica 1200x1200x400 mm criada em RFA usando Metric Structural Foundation.rft, copiada por hash, carregada (família 326506) e instanciada (327191), dry-run/commit verificados, 44 ms internos, save confirmado. Não dimensionada para cargas reais.
- [C] Batch informa índice e operação falha, separa IDs criados/modificados e limita escopo da verificação à existência/status transacional. Token escrito após bind do listener, remoção somente se pertencer à própria instância. Build ff7beef: 0 erros, 5 avisos, 3.98 s. DLL SHA256 BE4CA9614230AAF1E27BD64697B68B4CE3A206F3DD244C331CE88C7108CC270B implantada com backup. Sessão atual ainda 50 ferramentas; 52 cadastradas e 58 ações batch.
- [P] Reinício e validação das mudanças compiladas, escadas, telhados, blends, conexões, bibliotecas externas e integração completa. Nenhum projeto original modificado.

## Avanços 31 e 32 — extensões compiladas e reteste real
- [V] Compile-ReviewedExtension.ps1 usa Roslyn do SDK local, sem fontes NuGet, identidade por hash de fonte e engine. GeometryReview compilada em 2.63 s, 0 erros, 1 aviso. Approve-ReviewedExtension.ps1 separado; falha inicial File.Replace corrigida com backup explícito, aprovação SHA256 C002ADE4671213267690564771072382060D92785F5D0E6BC22B8170F8F5E659 confirmada. Extensões full-trust; não é sandbox nem execução arbitrária de texto dentro do processo. ReviewedSources excluído da compilação principal.
- [V] Reinício PID 18188; catálogo 52 ferramentas ativas. RVT de teste reaberto. GeometryReview carregada e executada sem recompilar engine principal: parede 4000 mm, mesa 1200x600x750 mm, sapata 1200x1200x400 mm confirmadas por bounding box/curva. Rollback confirmado.
- [V] Routing preferences: segmento aço carbono Schedule 40, nenhuma regra elbows/junctions/transitions/unions. Falta conteúdo e configuração de conexões; não simular sucesso.
- [V] Reteste telhado 327211, 76 ms; guarda-corpo 327250, 90 ms; eletroduto 25 mm e eletrocalha 300x100 mm, 327273/327275, 23 ms. Blend RFA Pedestal-daf0304e29cd45819921f9186e370f48.rfa salvo (1 extrusão e 2 blends solicitados), geometria detalhada ainda não inspecionada. Shaft 327323, 40 ms. Dry-run e commit verificados para lotes.
- [V] Escada reta: tentativas dry-run de 4800/5600/6400/7200 mm recusadas por espelhos excessivos; 3900 mm passou, 19/19 espelhos, desnível 4000 mm, piso 220 mm, espelho 210.53 mm, largura 1200 mm, ID 327341, sem falhas reportadas. Apenas geometria experimental; não atende critérios usuais de conforto/acessibilidade. RVT salvo. Nenhum original alterado.
- [P] Escadas com patamares e critérios normativos, armaduras, sweep, MEP fittings, Python in-process, Dynamo graphs, bibliotecas online, integração completa e revisão visual independente.

## Avanços 34 e 35 — extensões de auditoria, correção e armadura
- [V] SolidAudit.cs compilado em 2.09 s (0 erros, 1 aviso), aprovado e carregado sem reinício. Auditoria real de 5 elementos: parede 1.505095566 m³, mesa 5 sólidos/0.0288 m³, sapata 0.576 m³, telhado 3.145743143 m³, ambiente 18.77268225 m² e quatro segmentos de contorno. Soma de volumes, não união booleana; dados de host não comprovam adequação estrutural.
- [V] VerifiedMove.cs compilado em 1.63 s (0 erros, 1 aviso), aprovado e executado sem reinício. Mesa espelhada 326443: posição original [10500,1500,0], destino [10750,1650,0] mm, erro 0 mm. Rollback verificado independentemente, commit e releitura confirmados. Repetição com posição desatualizada recusada. Escopo: famílias pontuais não hospedadas, não agrupadas e não fixadas; não é autocorreção geral.
- [V] StraightRebar.cs compilado em 5.24 s (0 erros, 1 aviso), aprovado e executado sem reinício. Vergalhão nativo reto 327406 hospedado na sapata 327191, tipo padrão 327400, diâmetro nominal 12.7 mm, comprimento solicitado 1000 mm. Dry-run e commit, releitura da categoria e salvamento confirmados. Apenas geometria experimental; sem cálculo, cobrimento, ancoragem ou detalhamento normativo.
- [V] Sessão 52 ferramentas ativas, 58 ações batch e 3 novas extensões revisadas nesta etapa. Nenhuma alteração na DLL principal, projeto original preservado. Fontes em ReviewedSources.
- [P] Armaduras em conjuntos, estribos, detalhamento e verificação de cobrimento; conectores MEP, Python in-process, Dynamo, rampas, sweep, parametrização completa e integração completa de bibliotecas/plugins permanecem pendentes.

## Avanços 36 e 37 — conjuntos de armaduras e interferência física
- [V] RebarSet.cs, build 1.75 s, 0 erros/1 aviso, extensão SHA256 170E34FEC0281FE534828365E900CCD35D6BF7FC61D5BEEA199503DCC3D36BBF aprovada e carregada sem reinício. Barra 327406 convertida em 5 posições/5 barras distribuídas em 800 mm, transformações de 0/200/400/600/800 mm em Y. Dry-run e commit confirmados.
- [V] Segunda direção: barra 327410 criada com tipo existente 327400, 12.7 mm, comprimento solicitado 1000 mm, nível geométrico z=-270 mm, convertida em 5 barras a 200 mm em X. Releitura independente de quantidade 5 em ambos os conjuntos. Parâmetro genérico Espaçamento retornou 0 no layout FixedNumber; espaçamento geométrico confirmado pelas transformações individuais. Malha experimental 5+5 barras, não projeto estrutural, sem certificação de cobrimento/ancoragem/cargas.
- [V] SolidClash.cs, build 1.65 s, 0 erros/1 aviso, extensão SHA256 E84EA78813C3C9A2F40BA8DD84A37E5434A354C185C4E7257B1EA5154BB328A9 aprovada e carregada sem reinício. Dois móveis deliberadamente sobrepostos: 0.0198 m³ em 25 combinações de sólidos, nenhum erro. Móvel e sapata distantes: 0 m³ em 5 combinações. Cópia temporária 327408 excluída e save confirmado. Falhas booleanas são inconclusivas; soma de interseções pode contar volumes repetidos, sobreposição pode ser intencional.
- [V] Sessão mantém 52 ferramentas MCP, 58 ações em lote, duas novas extensões executadas sem reinício. RVT de teste salvo; original preservado.
- [P] Estribos, barras curvas, detalhamento e dimensionamento; MEP conexões, Sweep, rampas, Python in-process, Dynamo, bibliotecas externas, autocorreção ampla e integrações completas continuam pendentes.

## Avanços 38 e 39 — flex paramétrico, sweep e Blend auditado
- [V] FamilyTypeFlex.cs, compilado 1.57 s, 0 erros/1 aviso; SHA256 41CE1D7F4D55EDC4E363B996833A5EF383328811AD1392E92F784B963C7A237F aprovado, executado sem reinício. Mesa original 326272 tampo 30 mm/altura 750 mm; novo tipo 327415, instância 327418 tampo 50 mm/altura 770 mm. Dry-run, commit, leitura geométrica independente e save confirmados; original inalterado. Não implica parametrização de largura/profundidade.
- [C] AdvancedOperations.create_family_spec: até 50 sweeps, caminho planar contínuo de 1–100 segmentos de linha/arco, perfil circular ou retangular, elevação, sólido/vazio; permite criar apenas sweep ou blend sem extrusão. Contratos consultados no RevitAPI.xml instalado. tools.json ampliado, 52 ferramentas e 58 ações batch. Build 57fd04c: 0 erros/5 avisos, 3.21 s. DLL SHA256 05BC41E7C63F47B70FB6B0D1D0F46EEEE73587879BE777B86C7B9A870288B56E preparada com backup; sweep requer reinício e teste real.
- [V] Pedestal com 1 extrusão e 2 blends previamente gerado carregado (família 327419), instância 328223, 38 ms internos. SolidAudit confirmou 3 sólidos, volume somado 0.0883333333 m³; GeometryReview confirmou bounding box 600x600x750 mm. RVT salvo, original preservado.
- [P] Validar sweep após reinício, restrições de largura/profundidade, estribos, conexões MEP, rampas, Python in-process, Dynamo e integração completa das bibliotecas/plugins.

## Avanços 40–43 — armaduras dobradas, MEP, folgas e mobiliário
- [V] BentRebar.cs compilado 2.26 s, sem erros, vergalhão U 328231 na sapata 327191, 3 segmentos solicitados, 5 curvas reais incluindo 2 arcos, comprimento de eixo 1380.2128 mm. Dry-run e commit confirmados. Geometria experimental, sem dimensionamento.
- [V] MepTopology.cs compilado 2.35 s, sem erros, inspeção de cinco elementos e dez extremidades inicialmente abertas, referências físicas distinguidas das lógicas. Clearance.cs compilado 3.04 s, sem erros, folgas conservadoras de bounding boxes 10737.78 e 1430.75 mm; não são distâncias exatas entre sólidos.
- [V] MepConnect.cs compilado 1.78 s, sem erros, conexão direta de tubos colineares 328238/328241, dry-run rollback e commit, duas extremidades conectadas confirmadas por auditoria independente; tentativa perpendicular recusada. mep_candidates.py teste fixture e dados reais confirmados. Ainda faltam famílias e regras para conexões em ângulo, junções e transições; não implica comissionamento.
- [V] cabinet_recipe.py: testes offline e geração real de armário RFA 1200x600x1800 mm, painéis 18 mm, fundo 6 mm, três prateleiras, oito sólidos. Instância 329083, volume de sólidos 0.11367864 m³ confirmado contra cálculo da especificação; bounding box exata. Parâmetros nominais não vinculados a todas as geometrias. Não é reconhecimento de imagem.
- [V] Quatro extensões C# revisadas, compiladas, aprovadas por hash e executadas sem reinício; cada build com um aviso de referência e zero erros. Dois módulos Python adicionados e testados. 52 ferramentas principais e 58 ações batch permanecem; extensão adicional não equivale a ferramenta MCP independente. Projeto de teste salvo, original preservado.
- [P] Sweep preparado na etapa anterior ainda requer reinício. Estribos fechados com ganchos, rampas, Dynamo, Python in-process, parâmetros geométricos completos, bibliotecas online e reconstrução por imagem continuam pendentes.

## Avanços 44–46 — pisos inclinados, Sweep, materiais e integração de extensões
- [V] SlopedFloor.cs compilado em 7.37 s, 0 erros/2 avisos; extensão SHA256 9247AAD4CB748974E94F9D42175DE8A00EA7D4E6CD1FBD0460637473D2B82668 aprovada. Piso inclinado 329095, comprimento 6000 mm, largura 1500 mm, desnível real 450 mm e inclinação 7.5% verificados pela normal da face superior. Dry-run e commit confirmados. Categoria Floor, não Ramp nativo; sem certificação de acessibilidade.
- [V] MaterialTakeoff.cs compilado em 2.01 s, 0 erros/1 aviso; extensão SHA256 C48DD4D6CD1D2C704D96F5828E28A1CAB1CC81DAEC2F435307044ED26B5074C5 aprovada. Cinco elementos inspecionados, quantitativos agrupados por material: suporte metal 24.750816 m²/2.277075072 m³, gesso 74.252448 m²/1.150912944 m³, telhado 25.16594514 m²/3.14574314 m³. Mesa e sapata sem quantitativo de material retornado pela API, explicitamente listadas. Não são quantidades de compra.
- [V] Reinício do Revit detectado (PID 26928, conexão interrompida inicialmente e restabelecida na tela início). RVT de teste reaberto. DLL de Sweep já preparada na etapa 39 ativa: família Sweep-048257fbd8e54478b812cb62ad80aac2.rfa, perfil circular R30 mm sobre arco semicircular R500 mm, 1 Sweep/0 extrusões, instância 329887, 63 ms, um sólido e volume 0.004441324028 m³. Perfil retangular, múltiplos segmentos e cortes por void ainda não validados.
- [V] reviewed_client.py: 13 operações nomeadas, resolução por aprovação e SHA256, recusa ambiguidade e commit de inspeções; fixture de adulteração e testes reais geometry_review/material_takeoff aprovados. Cliente Python externo, não equivale a 13 ferramentas MCP novas. Extensões full-trust e inspeções usam transação com rollback. RVT salvo e leitura posterior de geometria confirmada. Original preservado.
- [P] Ramp nativo, perfis Sweep adicionais, estribos fechados, Python dentro do Revit, Dynamo, bibliotecas online, parametrização completa e roteamento MEP complexo. 52 ferramentas principais/58 ações batch, extensões dinâmicas adicionais.
