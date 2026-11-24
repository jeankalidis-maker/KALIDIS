# Kalidis AI Engine — checklist unificado de pendências

Base: histórico até o avanço 125, commit 6a0834d, 24/11/2026. Consolidação de lacunas, não declaração de que todo recurso listado tem código existente. Não reabre testes já aprovados no mesmo escopo. Itens adicionais de homologação são critérios de fechamento propostos, não funcionalidades previamente contratadas.

Legenda: V = validado/encerrado no escopo indicado; B = bloqueado; N = não validado no ambiente/escopo indicado; P = parcial; D = não implementado ou sem evidência de implementação; F = falha não resolvida. Todos os itens permanecem abertos até evidência de aceite.

## A — Implantação e conexão (prioridade crítica)
- [x] 001 [V] DLL atualizada instalada e hash confirmado no modulo carregado pelo Revit26232; backup e DLL anterior preservados (132).
- [ ] 002 [B] Restabelecer o MCP publicado no ChatGPT (SSE 429); testar initialize, catálogo e chamadas reais de leitura e escrita. **SSE429 reconfirmado apos deploy local (132).**
- [x] 003 [V] Contexto nativo validado: documento, vista, selecao vazia consistente, sessao, revisao e troca de documentos (132).
- [ ] 004 [P] Validar cache no Revit: cacheHit, TTL, invalidação por DocumentChanged, abertura e fechamento de documentos. **Falha cacheHit persistente identificada; correcao compilada e 13 testes offline, deploy/runtime pendentes (132).**
- [x] 005 [V] Rejeicao de sessao/revisao incorretas, previa/commit com revisao atual e rejeicao de revisao anterior, em runtime (132).
- [ ] 006 [P] Validar cancelamento cooperativo real: antes da execução, durante lote, rollback e corrida com commit. **Politica de rollback de falhas do batch compilada; teste nativo ainda pendente (129).** **Cancelamento nativo de lote180 materiais aceito e rollback sem residuos; fase exata e corrida real pendentes (132).** **Cancelamento de lote180 materiais com intencao de commit aprovado; fase exata pendente (133).**
- [ ] 007 [P] Atualizador próprio do AI Engine, instalação autorizada, rollback de versão e smoke test pós-instalação. **Instalador existente permanece impedido pela politica de execucao (131).** **Stage autorizado pelo usuario funcionou, backup verificado; restauracao de versao ainda nao executada (132).**
- [ ] 008 [P] Validar reinício/reconexão, perda de conexão, timeout, fila ocupada e recuperação sem repetir escritas. **Estado incerto reconciliado por leitura; limpeza com dependentes recusada (129).** **Reinicio real validado; perda de conexao e timeout ainda pendentes (132).**

## B — Infraestrutura, segurança e autonomia
- [ ] 009 [P] Completar schemas de todas as ferramentas de edição, validação de argumentos e compatibilidade entre versões; preflight em todos os clientes autorizados. **Preflight de formas nativas ampliado (128).** **Largura nativa de parede na prevalidacao de pares novos (129).** **Schema curtain_wall registrado, capacidade ativa verificada (130).**
- [ ] 010 [P] Idempotência persistente e recuperação de resultados após interrupção, reconciliação de estado incerto e auditoria. **Estado unknown persistido e replay recusado em teste (129).**
- [ ] 011 [P] Desfazer/refazer para criação, edição, exclusão, tipos, famílias e operações dependentes (hoje movimento e comentário restritos). **Fixacao reversivel com forward/undo/redo confirmada e estado restaurado (130).**
- [ ] 012 [P] Fila, controle de concorrência, telemetria, métricas e testes de carga em sessões longas.
- [ ] 013 [P] Planejamento multioperação com dependências, dry-run, checkpoints e verificação posterior, incluindo efeitos externos. **Fluxo de fixacao com previa e readback independente (130).**
- [ ] 014 [P] Contexto de projeto persistente, unidades, identificação de elementos, seleção e proteção contra alterações concorrentes. **Leitura de selecao e identidade em snapshot legado testada (131).** **Contexto nativo e troca de documentos conferidos (132).**
- [ ] 015 [P] Fluxo autorizado para trabalhar em projetos reais fora do Sandbox, com cópia, escopo, permissões, backup e proteção de documentos. **Copia RVT externa verificada por SHA256, escrita isolada e origem preservada (131).**
- [ ] 016 [P] Testar limites de confiança das extensões C#, versões, assinaturas, caminhos e permissões; não executar código arbitrário sem revisão.
- [ ] 017 [P] Testar todos os comandos anunciados no catálogo ativo e manter matriz por comando/versão/resultado.

## C — Arquitetura e documentação
- [ ] 018 [P] Rampa nativa (categoria Ramp), em vez de somente piso inclinado, com geometria e patamares.
- [ ] 019 [P] Escadas com patamares, múltiplos lances, curvas e verificação de parâmetros de conforto e acessibilidade.
- [ ] 020 [P] Paredes cortina completas: grids, painéis, montantes, portas e edição. **Criacao reta, grelha e inventario nativos implementados e compilados, sem deploy; edicao de paineis/montantes/portas pendente (130).** **Parede cortina nativa 6x3m com duas grelhas e quatro paineis criada e salva, auditoria positiva; edicao pendente (132).**
- [ ] 021 [P] Telhados complexos, múltiplas águas, aberturas, encontros e anexação de paredes em configurações variadas.
- [ ] 022 [P] Pisos, forros e paredes com curvas, furos, perfis irregulares, limites, alinhamentos e restrições avançadas. **Piso com furo e parede semicircular conferidos (128).**
- [ ] 023 [P] Portas e janelas com famílias variadas, orientação, folgas, vãos livres, restrições e hospedagem em situações complexas. **Tres tipos de porta externa com host confirmado (128).**
- [ ] 024 [P] Ambientes e áreas com curvas, limites complexos, fases, vazios e múltiplos níveis.
- [ ] 025 [P] Fases, worksets, vínculos, coordenadas compartilhadas, templates e filtros: ampliar edição controlada e verificar resultados.
- [x] 026 [V] Filtro nativo: imagens visivel/oculto/restaurado inspecionadas, avanco127.
- [ ] 027 [P] Topografia complexa, fundações, cortes/aterros e documentação integrada.
- [ ] 028 [P] Pranchas completas: colisão de viewports, rótulos, tabelas, limites de carimbo, cotas, tags, detalhes e paginação. **Folha A1 de coordenacao corrigida e PNG conferido; ajuste nativo compilado, deploy e acabamento pendentes (133).**
- [ ] 029 [P] Quantitativos completos por disciplina/material/escopo, tratamento de dados ausentes e conferência independente. **Quantitativo de duas lajes,71m2/10.65m3, nove elementos sem material quantificavel explicitados (133).** **Inventario de quatro armaduras com comprimento, massa teorica e folga AABB (134).**

## D — Estruturas e instalações
- [ ] 030 [P] Modelagem estrutural integrada de fundações, pilares, vigas, lajes e ligações metálicas em cenários complexos. **Portico com duas sapatas, dois pilares metalicos e viga6m confirmado por solidos, sem ligacoes/calculo (134).**
- [ ] 031 [P] Armaduras complexas: distribuição, curvas, emendas, ancoragens, detalhamento e verificação de cobrimento físico; estribos básicos já validados. **rebar_box_review sinaliza folga de caixa insuficiente; cobrimento fisico nao certificado (134).**
- [ ] 032 [D] Integração com cálculo estrutural e verificações normativas, se incluída no escopo profissional; não confundir modelagem com dimensionamento.
- [ ] 033 [P] Redes MEP completas de água, esgoto, ventilação, HVAC e elétrica, com equipamentos, conexões, acessórios e múltiplos pavimentos. **Rede com te, reducao e conectividade independente validada (133).**
- [ ] 034 [P] Roteamento MEP automático com derivações, obstáculos múltiplos, inclinações, elevações, conexões externas e endpoints fixos. **Derivacao e transicao de trajeto explicito validadas; roteamento automatico pendente (133).**
- [ ] 035 [P] Eletrodutos, eletrocalhas, circuitos, quadros, conectividade e documentação elétrica integrada. **Eletroduto DN25 e eletrocalha300x100mm,8m, validados (133).**
- [ ] 036 [P] Dimensionamento e verificações de vazão, perdas, declividade e circuitos, quando o escopo exigir; conectividade não certifica desempenho. **Declividade sanitaria2% conferida por conectores; calculo pendente (133).**
- [ ] 037 [P] Coordenação interdisciplinar com aberturas e shafts, suportes, isolamento, manutenção e interferências de redes complexas. **Shaft1x1m corta laje superior; tubo vertical sem intersecao (133).**

## E — Autocorreção geométrica e coordenação
- [ ] 038 [F] Junção física de paredes e acabamento das extremidades: wall_join_finish / JoinGeometryUtils ainda sem validação positiva. **Join em X aceito; acabamento L e ensaio de sobreposicao pendentes; modal/estado incerto no ultimo ensaio (128).** **Sobreposicao experimental0.7065m3 identificada; acabamento ainda sem resolucao (129).** **Rollback nativo de sobreposicao com aviso registrado, sem modal; acabamento ainda pendente (132).**
- [ ] 039 [P] Reparo de paredes em L/T/X, curvas, múltiplos encontros e dependências de portas, janelas e elementos estruturais. **Join em X sem sobreposicao residual, mas sem prova de correcao de intersecao previa (128).** **Prevencao de paredes paralelas sobrepostas no mesmo lote (129).**
- [ ] 040 [P] Detectar e corrigir perfis 3D, curvas, furos e degenerações além de polígonos XY. **Rejeicao de furo externo e arco degenerado (128).** **Verificacao conservadora de eixos e espessuras (129).**
- [ ] 041 [P] Autocorreção de mobiliário em ambientes não retangulares, circulação, giro de portas, suportes e acessibilidade.
- [ ] 042 [P] Validar reparo e interferência com vínculos RVT rotacionados/transladados e coordenadas compartilhadas.
- [ ] 043 [P] Reparo de redes MEP conectadas com restrições e múltiplos obstáculos; ampliar bypass de tubos para outras configurações.
- [ ] 044 [P] Clash detection global e priorização de conflitos, distinguir interseções intencionais, tolerâncias e falhas booleanas. **Intersecao laje/tubo zero, auditoria de11 elementos sem inconclusivos (133).**
- [ ] 045 [P] Reparos em lote com análise de dependências, preservação de elementos, rollback e auditoria antes/depois. **Previa de exclusao com dependentes impediu commit (129).**
- [x] 046 [V] Substituida pela validacao obrigatoria do adaptador (avancos123/126); prototipo C# separado arquivado, sem instalacao.

## F — Famílias RFA e parametrização
- [ ] 047 [P] Parametrização geométrica generalizada de blends, sweeps, curvas não cilíndricas e geometrias compostas arbitrárias. **Sweep circular em caminho de tres segmentos conferido; flex generalizado pendente (134).**
- [ ] 048 [P] Restrições entre sólidos, rotação, posições relativas, parâmetros compartilhados e relações de fórmula complexas.
- [ ] 049 [P] Cortes por void em blends/sweeps, múltiplos cortes, recortes complexos e verificação de volume resultante.
- [ ] 050 [P] Sweeps complexos, trajetórias 3D e geometrias de revolução/loft além do escopo já testado. **Sweep reta-arco-reta com erro volumetrico0.00234%, apenas planar (134).**
- [ ] 051 [P] Famílias hospedadas, aninhadas, conectores MEP, parâmetros de instância/tipo, materiais e níveis de detalhe. **Parametro de instancia texto de familia sweep conferido (134).**
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
- [x] 075 [V] Encerrado por exclusao justificada do CAD-MCP (licenca TBD no commit fixo), substituido por C# proprio revisado e validado. Nao incorpora codigo upstream (134).
- [ ] 076 [P] Mvclr: completar revisão de comandos úteis e testes de operações complementares.
- [ ] 077 [P] FamFab: fluxo de imagem/referência para RFA, integração de recursos úteis e revisão de dependências/API/custo. **Sweep composto RFA confirmado no motor proprio; imagem ainda pendente (134).**
- [ ] 078 [P] Horizun: ampliar funções úteis de arquitetura, estrutura e MEP e verificar limites de licença/versão.
- [ ] 079 [P] Dynamo: interpretar, construir e validar grafos variados, dependências, bindings, execução e efeitos externos; não executar grafos desconhecidos sem revisão.
- [ ] 080 [P] Windows UI Automation: operações contextualizadas de interface, confirmação de estado e resultado, sem cliques cegos ou contorno de avisos. **Recuperacao de janela modal confirmada por leitura UIA; sem automacao de cliques (129).**
- [ ] 081 [P] Kalidis legado: ampliar compatibilidade de operações e protocolos além de listar/info/inventário, com testes e escopo. **Leitura de selecao, snapshot_elemento e comandos nativos testada (131).**
- [x] 082 [V] Inventario por revisao/licenca, comparacao de funcoes e decisoes de adocao/exclusao documentados em INTEGRATION-REGISTER.md (134); implementacoes individuais continuam abertas.

## J — Homologação e entrega
- [ ] 083 [P] Teste ponta a ponta de um projeto realista de arquitetura, estrutura, elétrica e hidráulica, com documentação e verificação independente. **Prancha e coordenacao de conjunto experimental MEP/estrutura validadas; projeto completo pendente (133).** **Portico estrutural nativo experimental criado e salvo, sem dimensionamento (134).**
- [ ] 084 [P] Teste de desempenho, modelos grandes, sessões prolongadas, dados inconsistentes e falhas/reconexão.
- [ ] 085 [P] Teste em cópias de projetos reais, incluindo links, worksharing e casos protegidos, com autorização explícita. **Copia de fixture isolada validada; modelos reais e worksharing pendentes (131).**
- [ ] 086 [P] Revisão de avisos de compilação, bugs conhecidos, dependências, permissões e segurança do release. **Preprocessador de falhas compilado, quatro avisos restantes; deploy pendente (129).** **Build novo motor 0 erros/4 avisos; catalogo122 ferramentas ativas (130).** **Preprocessador batch verificado em runtime com rollback de aviso de sobreposicao (132).**
- [ ] 087 [P] Manual de instalação, operação, recuperação e limites; versão publicada, matriz de testes e critérios de aceite. **Gates de instalacao, recuperacao e aceite documentados em RELEASE-RECOVERY.md (130).**
- [ ] 088 [P] Homologação final pelo usuário: executar comandos pelo ChatGPT normal, sem Work, do pedido à verificação e entrega.

## Resumo

79 itens abertos: 1 bloqueado, 1 nao validado, 75 parciais, 1 sem implementacao confirmada, 1 com falha nao resolvida. 9 encerrados de 88 (001, 003, 005, 026, 046, 068, 069, 075, 082). Contagem deste checklist, nao percentual da API Revit.

Ordem de execução: A → B → C/D/E/F/G/H/I → J. A e B são prioritários; testes que não dependem da DLL podem prosseguir em paralelo. Licença CAD-MCP indefinida: integração integral não é requisito se alternativa independente satisfizer o objetivo. Dimensionamento e verificações normativas requerem escopo e validação de engenharia.
