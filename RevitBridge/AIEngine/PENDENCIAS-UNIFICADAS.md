# Kalidis AI Engine — checklist unificado de pendências

Base: histórico até o avanço 125, commit 6a0834d, 24/11/2026. Consolidação de lacunas, não declaração de que todo recurso listado tem código existente. Não reabre testes já aprovados no mesmo escopo. Itens adicionais de homologação são critérios de fechamento propostos, não funcionalidades previamente contratadas.

Legenda: V = validado/encerrado no escopo indicado; B = bloqueado; N = não validado no ambiente/escopo indicado; P = parcial; D = não implementado ou sem evidência de implementação; F = falha não resolvida. Todos os itens permanecem abertos até evidência de aceite.

## A — Implantação e conexão (prioridade crítica)
- [ ] 001 [B] Instalar a DLL atualizada com backup, reinício normal e verificação do hash carregado.
- [ ] 002 [B] Restabelecer o MCP publicado no ChatGPT (SSE 429); testar initialize, catálogo e chamadas reais de leitura e escrita.
- [ ] 003 [N] Validar project_context na DLL nova: documento, vista, seleção, sessão e sinal de revisão.
- [ ] 004 [N] Validar cache no Revit: cacheHit, TTL, invalidação por DocumentChanged, abertura e fechamento de documentos.
- [ ] 005 [N] Validar guarda de revisão real, incluindo rejeição de sessão/revisão obsoleta.
- [ ] 006 [N] Validar cancelamento cooperativo real: antes da execução, durante lote, rollback e corrida com commit.
- [ ] 007 [P] Atualizador próprio do AI Engine, instalação autorizada, rollback de versão e smoke test pós-instalação.
- [ ] 008 [P] Validar reinício/reconexão, perda de conexão, timeout, fila ocupada e recuperação sem repetir escritas.

## B — Infraestrutura, segurança e autonomia
- [ ] 009 [P] Completar schemas de todas as ferramentas de edição, validação de argumentos e compatibilidade entre versões; preflight em todos os clientes autorizados. **Preflight de formas nativas ampliado (128).**
- [ ] 010 [P] Idempotência persistente e recuperação de resultados após interrupção, reconciliação de estado incerto e auditoria.
- [ ] 011 [P] Desfazer/refazer para criação, edição, exclusão, tipos, famílias e operações dependentes (hoje movimento e comentário restritos).
- [ ] 012 [P] Fila, controle de concorrência, telemetria, métricas e testes de carga em sessões longas.
- [ ] 013 [P] Planejamento multioperação com dependências, dry-run, checkpoints e verificação posterior, incluindo efeitos externos.
- [ ] 014 [P] Contexto de projeto persistente, unidades, identificação de elementos, seleção e proteção contra alterações concorrentes.
- [ ] 015 [P] Fluxo autorizado para trabalhar em projetos reais fora do Sandbox, com cópia, escopo, permissões, backup e proteção de documentos.
- [ ] 016 [P] Testar limites de confiança das extensões C#, versões, assinaturas, caminhos e permissões; não executar código arbitrário sem revisão.
- [ ] 017 [P] Testar todos os comandos anunciados no catálogo ativo e manter matriz por comando/versão/resultado.

## C — Arquitetura e documentação
- [ ] 018 [P] Rampa nativa (categoria Ramp), em vez de somente piso inclinado, com geometria e patamares.
- [ ] 019 [P] Escadas com patamares, múltiplos lances, curvas e verificação de parâmetros de conforto e acessibilidade.
- [ ] 020 [D] Paredes cortina completas: grids, painéis, montantes, portas e edição.
- [ ] 021 [P] Telhados complexos, múltiplas águas, aberturas, encontros e anexação de paredes em configurações variadas.
- [ ] 022 [P] Pisos, forros e paredes com curvas, furos, perfis irregulares, limites, alinhamentos e restrições avançadas. **Piso com furo e parede semicircular conferidos (128).**
- [ ] 023 [P] Portas e janelas com famílias variadas, orientação, folgas, vãos livres, restrições e hospedagem em situações complexas. **Tres tipos de porta externa com host confirmado (128).**
- [ ] 024 [P] Ambientes e áreas com curvas, limites complexos, fases, vazios e múltiplos níveis.
- [ ] 025 [P] Fases, worksets, vínculos, coordenadas compartilhadas, templates e filtros: ampliar edição controlada e verificar resultados.
- [x] 026 [V] Filtro nativo: imagens visivel/oculto/restaurado inspecionadas, avanco127.
- [ ] 027 [P] Topografia complexa, fundações, cortes/aterros e documentação integrada.
- [ ] 028 [P] Pranchas completas: colisão de viewports, rótulos, tabelas, limites de carimbo, cotas, tags, detalhes e paginação.
- [ ] 029 [P] Quantitativos completos por disciplina/material/escopo, tratamento de dados ausentes e conferência independente.

## D — Estruturas e instalações
- [ ] 030 [P] Modelagem estrutural integrada de fundações, pilares, vigas, lajes e ligações metálicas em cenários complexos.
- [ ] 031 [P] Armaduras complexas: distribuição, curvas, emendas, ancoragens, detalhamento e verificação de cobrimento físico; estribos básicos já validados.
- [ ] 032 [D] Integração com cálculo estrutural e verificações normativas, se incluída no escopo profissional; não confundir modelagem com dimensionamento.
- [ ] 033 [P] Redes MEP completas de água, esgoto, ventilação, HVAC e elétrica, com equipamentos, conexões, acessórios e múltiplos pavimentos.
- [ ] 034 [P] Roteamento MEP automático com derivações, obstáculos múltiplos, inclinações, elevações, conexões externas e endpoints fixos.
- [ ] 035 [P] Eletrodutos, eletrocalhas, circuitos, quadros, conectividade e documentação elétrica integrada.
- [ ] 036 [P] Dimensionamento e verificações de vazão, perdas, declividade e circuitos, quando o escopo exigir; conectividade não certifica desempenho.
- [ ] 037 [P] Coordenação interdisciplinar com aberturas e shafts, suportes, isolamento, manutenção e interferências de redes complexas.

## E — Autocorreção geométrica e coordenação
- [ ] 038 [F] Junção física de paredes e acabamento das extremidades: wall_join_finish / JoinGeometryUtils ainda sem validação positiva. **Join em X aceito; acabamento L e ensaio de sobreposicao pendentes; modal/estado incerto no ultimo ensaio (128).**
- [ ] 039 [P] Reparo de paredes em L/T/X, curvas, múltiplos encontros e dependências de portas, janelas e elementos estruturais. **Join em X sem sobreposicao residual, mas sem prova de correcao de intersecao previa (128).**
- [ ] 040 [P] Detectar e corrigir perfis 3D, curvas, furos e degenerações além de polígonos XY. **Rejeicao de furo externo e arco degenerado (128).**
- [ ] 041 [P] Autocorreção de mobiliário em ambientes não retangulares, circulação, giro de portas, suportes e acessibilidade.
- [ ] 042 [P] Validar reparo e interferência com vínculos RVT rotacionados/transladados e coordenadas compartilhadas.
- [ ] 043 [P] Reparo de redes MEP conectadas com restrições e múltiplos obstáculos; ampliar bypass de tubos para outras configurações.
- [ ] 044 [P] Clash detection global e priorização de conflitos, distinguir interseções intencionais, tolerâncias e falhas booleanas.
- [ ] 045 [P] Reparos em lote com análise de dependências, preservação de elementos, rollback e auditoria antes/depois.
- [x] 046 [V] Substituida pela validacao obrigatoria do adaptador (avancos123/126); prototipo C# separado arquivado, sem instalacao.

## F — Famílias RFA e parametrização
- [ ] 047 [P] Parametrização geométrica generalizada de blends, sweeps, curvas não cilíndricas e geometrias compostas arbitrárias.
- [ ] 048 [P] Restrições entre sólidos, rotação, posições relativas, parâmetros compartilhados e relações de fórmula complexas.
- [ ] 049 [P] Cortes por void em blends/sweeps, múltiplos cortes, recortes complexos e verificação de volume resultante.
- [ ] 050 [P] Sweeps complexos, trajetórias 3D e geometrias de revolução/loft além do escopo já testado.
- [ ] 051 [P] Famílias hospedadas, aninhadas, conectores MEP, parâmetros de instância/tipo, materiais e níveis de detalhe.
- [ ] 052 [P] Edição e atualização controlada de famílias existentes, recarga com preservação de tipos/instâncias e parâmetros. **Copia RFA editada sem recarga (128).**
- [ ] 053 [P] Validação de todos os tipos de cada família, flex extremo, fórmulas e restrições; não apenas a geometria corrente. **Oito simbolos externos inventariados, sete instanciados (128).**
- [ ] 054 [P] Gerar famílias complexas nativas a partir de imagem, desenho ou referência dimensional, com conferência física.

## G — Imagens, plantas e materiais
- [ ] 055 [P] Interpretar plantas PDF vetoriais e rasterizadas, fotos e croquis reais; extrair cotas, escala e evidência.
- [ ] 056 [P] Detectar paredes, portas, janelas, espessuras, símbolos, escadas, níveis, ambientes e elementos curvos.
- [ ] 057 [P] Reconciliação de cotas conflitantes, incerteza e confirmação de hipóteses antes da modelagem (croqui real ainda não reconstruído).
- [ ] 058 [P] Reconstrução multipavimento a partir de documentação real e revisão por sobreposição geométrica e métrica.
- [ ] 059 [P] Conversão de referências fotográficas em objetos e famílias editáveis, sem inventar medidas ocultas.
- [ ] 060 [P] Materiais PBR completos: mapas de cor, relevo, rugosidade, transparência e propriedades físicas.
- [ ] 061 [P] Escala UV comercial, orientação, repetição, acabamento e verificação visual de diferentes geometrias.
- [ ] 062 [P] Renderização fotorealista com iluminação, câmeras, sombras e validação visual independente; modo Realistic básico já testado.
- [ ] 063 [P] Biblioteca de materiais pesquisável com metadados, licenças, seleção automática e prevenção de caminhos quebrados.

## H — Bibliotecas BIM e interoperabilidade
- [ ] 064 [P] Ampliar catálogo além das quatro famílias de um repositório, com pesquisa real de fornecedores e categorias.
- [ ] 065 [P] Verificar licenças, procedência, integridade, versões, dependências e compatibilidade RFA antes de importar.
- [ ] 066 [P] Auditar todos os tipos, parâmetros, conectores, geometria e comportamento de famílias externas em projetos isolados. **Sete tipos com solidos medidos; perfil e conectores pendentes (128).**
- [ ] 067 [P] Adaptar famílias importadas a padrões de projeto sem alterar os arquivos originais; evitar duplicatas. **Copia adaptada com tipo e metadado, original preservado; deduplicacao pendente (128).**
- [x] 068 [V] Exportacao experimental Integrated121 validada em leitor independente web-ifc, geometria, unidades, categorias, propriedades selecionadas e relacoes (avanco126). Fidelidade de estilos e round-trip permanecem no item070.
- [x] 069 [V] PDF renderizado e inspecionado em leitor independente e DWG aberto/auditado no AutoCAD Core Console, avanco127. Plotagem/fidelidade universal fora do ensaio.
- [ ] 070 [P] Fluxo de importação/coordenação IFC, round-trip e interoperabilidade com outros formatos.

## I — Integrações externas
- [ ] 071 [P] BIMCraft: completar registro, schemas, contexto, recuperação, undo/redo e simulação; adaptar conceitos, não copiar motor.
- [ ] 072 [P] KenLP: comparar inventário upstream e adaptar comandos complementares úteis com transações e testes.
- [ ] 073 [N] pyRevit: instalação separada e autorizada; executar e validar a extensão de auditoria desenvolvida.
- [ ] 074 [P] AndreyStartsev: ampliar workflows Python estruturados para outras categorias/parâmetros e integração pyRevit.
- [ ] 075 [B] CAD-MCP: esclarecer licença antes de reutilizar código; avaliar integração C# revisada e independente.
- [ ] 076 [P] Mvclr: completar revisão de comandos úteis e testes de operações complementares.
- [ ] 077 [P] FamFab: fluxo de imagem/referência para RFA, integração de recursos úteis e revisão de dependências/API/custo.
- [ ] 078 [P] Horizun: ampliar funções úteis de arquitetura, estrutura e MEP e verificar limites de licença/versão.
- [ ] 079 [P] Dynamo: interpretar, construir e validar grafos variados, dependências, bindings, execução e efeitos externos; não executar grafos desconhecidos sem revisão.
- [ ] 080 [P] Windows UI Automation: operações contextualizadas de interface, confirmação de estado e resultado, sem cliques cegos ou contorno de avisos.
- [ ] 081 [P] Kalidis legado: ampliar compatibilidade de operações e protocolos além de listar/info/inventário, com testes e escopo.
- [ ] 082 [P] Inventário comparativo por versão/licença de todos os projetos externos; decidir o que integrar, substituir ou excluir justificadamente.

## J — Homologação e entrega
- [ ] 083 [P] Teste ponta a ponta de um projeto realista de arquitetura, estrutura, elétrica e hidráulica, com documentação e verificação independente.
- [ ] 084 [P] Teste de desempenho, modelos grandes, sessões prolongadas, dados inconsistentes e falhas/reconexão.
- [ ] 085 [P] Teste em cópias de projetos reais, incluindo links, worksharing e casos protegidos, com autorização explícita.
- [ ] 086 [P] Revisão de avisos de compilação, bugs conhecidos, dependências, permissões e segurança do release.
- [ ] 087 [P] Manual de instalação, operação, recuperação e limites; versão publicada, matriz de testes e critérios de aceite.
- [ ] 088 [P] Homologação final pelo usuário: executar comandos pelo ChatGPT normal, sem Work, do pedido à verificação e entrega.

## Resumo

84 itens abertos: 3 bloqueados, 5 nao validados, 73 parciais, 2 sem implementacao confirmada, 1 com falha nao resolvida. 4 encerrados de 88 (026, 046, 068, 069). Contagem deste checklist, nao percentual da API Revit.

Ordem de execução: A → B → C/D/E/F/G/H/I → J. A e B são prioritários; testes que não dependem da DLL podem prosseguir em paralelo. Licença CAD-MCP indefinida: integração integral não é requisito se alternativa independente satisfizer o objetivo. Dimensionamento e verificações normativas requerem escopo e validação de engenharia.
