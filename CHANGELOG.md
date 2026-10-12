# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Corrigido

- Grupos salvos não reabriam apps que se atualizam sozinhos: Windows Terminal e outros apps da Loja (a pasta muda a cada versão, e o atalho do Terminal é `wt.exe`) e apps Squirrel como o Discord (`app-<versão>`). O app passa a ser reconhecido pela família do pacote/pasta raiz e é aberto na versão atual.
- A janela reaberta não era gravada no grupo salvo (o HWND/processo antigo ficava no arquivo), então a próxima reabertura não reencontrava a janela e abria outra.
- Um grupo salvo podia perder seus apps do arquivo quando eles não abriam e o grupo era alterado em seguida (ex.: trocar para abas).
- Sair, atualizar ou desligar o Windows com a pergunta "fechar o grupo?" aberta lançava erro e podia impedir o app de sair (e o instalador de atualizar).
- `groups.json` e `settings.json` grandes demais, corrompidos ou de uma versão mais nova nunca são sobrescritos sem antes guardar uma cópia (`.bak-data`); as gravações são atômicas.
- "★ Salvo" apagava o grupo salvo com um clique; agora pede confirmação.

### Adicionado

- Reabrir um grupo salvo devolve o estado de cada painel: aba/painel ativo, painel maximizado e modo de exibição.
- A janela de antes é reaproveitada mesmo minimizada, escondida na bandeja ou em outra área de trabalho virtual. Uma janela que já estava aberta nunca é puxada para o grupo só por ser do mesmo programa.
- Apps que não abrem ficam no lugar como um painel com **Tentar de novo** e **Remover do grupo**; num grupo salvo, um app fechado deixa um painel **Reabrir**.
- Terminais (Windows Terminal e console clássico) reabrem na pasta em que estavam.
- `groups.json` com versão de formato (`SchemaVersion`); o formato antigo continua sendo lido.

### Alterado

- Visual: foco visível pelo teclado, contraste mínimo de acessibilidade nos botões e contornos, ícones nativos do Windows (Segoe MDL2), barra de título escura também no Windows 11.
- Mensagens: rodapé de status no grupo (erros ficam até serem dispensados) e linha de mensagem na janela principal que não some sozinha; plurais corretos.
- Textos: modos de painel passam a "Dentro do painel" e "Sobre o painel"; diálogo de fechar mais claro.
- A barra do grupo se adapta a janelas estreitas e os painéis têm tamanho mínimo.
- Mais leve na bandeja: memória privada de ~76 MB para ~16 MB (os atalhos globais não carregam mais a renderização por GPU), devolução de memória no momento certo, e gravação dos grupos salvos agrupada (1 s após a última mudança).

### Segurança

- Uma pasta de terminal com `;` no nome nunca vai para a linha de comando do Windows Terminal (o `wt` trata `;` como separador de comandos).
- App e instalador só carregam DLLs do System32.
- Rodando como administrador, o SplitDeck não se atualiza sozinho e não sugere mais ser aberto como administrador.
- A atualização baixa para uma pasta aleatória em `%LOCALAPPDATA%` e mantém o arquivo travado desde a conferência do SHA-256 até o instalador iniciar.
- O filtro de argumentos com segredos cobre `curl -u`, `sshpass`, URLs assinadas, tokens do GitHub, chaves `sk-` e JWT.
- A desinstalação não atravessa junctions/links; a conferência do assinante do runtime exige o nome exato.
- CI: actions fixadas por SHA de commit e permissões mínimas; avisos do compilador viram erro; resultados e cobertura dos testes publicados; testes do instalador.

## [1.0.1] - 2026-09-30

### Corrigido

- Ícone do SplitDeck na barra de tarefas, no Menu Iniciar e em "Aplicativos instalados" (o executável estava sem ícone e aparecia o genérico do Windows). Janelas e bandeja passam a usar o mesmo arquivo de ícone, nítido em qualquer escala.

## [1.0.0] - 2026-09-30

Primeira versão pública, feita sobre o **.NET 10 (LTS)**.

### Adicionado

- Agrupamento de janelas reais de qualquer app em uma janela "grupo".
- Dois modos por painel: **Incorporada** (a janela vira filha do painel) e **Acoplada** (a janela continua independente, mantida sobre o painel). Escolha automática por classe de janela, processo e detecção de renderização por GPU; troca manual por painel, lembrada por programa.
- Layout em árvore com divisórias arrastáveis, 5 layouts prontos (colunas, linhas, grade, principal à esquerda, principal em cima), igualar tamanhos, maximizar painel com duplo clique no cabeçalho e modo abas.
- Reorganizar painéis arrastando o cabeçalho: bordas dividem, centro troca de lugar.
- Formas de agrupar: lista de janelas abertas na janela principal (com seleção múltipla), botão **＋ Adicionar** no grupo, atalho global, itens **Agrupar…** no menu da barra de título de apps de janela clássica, e arrastar uma janela com **Shift** sobre um grupo ou sobre outra janela (cria um grupo com as duas).
- Atalhos globais `Ctrl+Alt+G` (agrupar janela ativa), `Ctrl+Alt+M` (grade/abas), `Ctrl+Alt+←`/`→` (painel anterior/próximo) e `Ctrl+Alt+Enter` (maximizar painel), com alternativa `Ctrl+Alt+Shift` quando a combinação já está em uso.
- Grupos salvos: nome, layout, modo abas, posição e tamanho; ao reabrir, reusa a mesma janela se ainda existir ou relança o app com os mesmos argumentos.
- Ícone na bandeja com acesso aos grupos salvos; fechar a janela principal mantém o app na bandeja.
- Instância única: abrir de novo traz a janela principal para frente.
- Preferências: iniciar com o Windows (na bandeja), reabrir grupos salvos ao iniciar, item "Agrupar" no menu da barra de título, esconder a janela principal depois de agrupar.
- Ao fechar um grupo ou sair: escolha entre devolver as janelas à área de trabalho ou fechar os apps também.
- Proteção das janelas do usuário: em erro inesperado e ao desligar o Windows, as janelas são devolvidas; janelas acopladas escondidas por um encerramento forçado são mostradas de volta na próxima abertura.
- Instalador por usuário, sem administrador, em `%LOCALAPPDATA%\Programs\SplitDeck`, que oferece baixar o .NET 10 Desktop Runtime oficial quando falta; versão portátil em ZIP; `SHA256SUMS.txt` em cada release.
- Atualização automática pelos Releases do GitHub (no máximo 1x por dia, ou manual pela bandeja/janela principal): pergunta antes, baixa só do repositório oficial, confere o SHA-256 e atualiza devolvendo as janelas agrupadas. Pode ser desligada nas preferências.
- Navegadores Chromium e apps Electron (Chrome, Edge, Brave, VS Code, Discord, Docker Desktop…) incorporados sem as faixas pretas da borda interna do Chromium.
- Modo Acoplado usa o grupo como dono (owner) da janela: o Windows cuida da ordem, do minimizar junto e da barra de tarefas; a janela do app sobrevive se o SplitDeck for encerrado.

### Segurança

- Rodando como administrador, nada é relançado a partir dos grupos salvos (só reencontra janelas abertas).
- Relançamento restrito a executáveis locais com caminho absoluto (sem pastas de rede, dispositivos ou `PATH`); apps da Store só pelas pastas `WindowsApps` reais.
- Argumentos de linha de comando com cara de senha/token não são gravados em `groups.json`.
- Arquivos de dados validados ao carregar (tamanho, valores numéricos, profundidade, até 16 apps por grupo); falha ao gravar não desfaz os grupos.
- Instância única e sinais por usuário e sessão (`Local\SplitDeck.{SID}`); callbacks de eventos do Windows nunca deixam exceções escaparem.
- IDs do menu de sistema compatíveis com a máscara de `WM_SYSCOMMAND`; itens de outros apps nunca são alterados.

[Não lançado]: https://github.com/brunocsilva41/agrupa-janela/compare/v1.0.1...HEAD
[1.0.1]: https://github.com/brunocsilva41/agrupa-janela/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/brunocsilva41/agrupa-janela/releases/tag/v1.0.0
