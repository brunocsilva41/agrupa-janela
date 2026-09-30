# Changelog

Todas as mudanças relevantes deste projeto são registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

## [1.0.0] - 2026-09-30

Primeira versão pública.

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
- Instalador por usuário, sem administrador, em `%LOCALAPPDATA%\Programs\SplitDeck`, que oferece baixar o .NET 8 Desktop Runtime oficial quando falta; versão portátil em ZIP; `SHA256SUMS.txt` em cada release.
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

[Não lançado]: https://github.com/brunocsilva41/agrupa-janela/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/brunocsilva41/agrupa-janela/releases/tag/v1.0.0
