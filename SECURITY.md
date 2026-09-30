# Política de segurança

## Versões suportadas

Só a **versão mais recente** publicada em [Releases](https://github.com/brunocsilva41/agrupa-janela/releases/latest) recebe correções de segurança. Antes de relatar, confira se o problema acontece na última versão.

| Versão | Suportada |
|---|---|
| Última versão publicada | Sim |
| Versões anteriores | Não |

## Como relatar uma vulnerabilidade

**Não abra uma issue pública.** Relate de forma privada pelo GitHub Security Advisories:

<https://github.com/brunocsilva41/agrupa-janela/security/advisories/new>

Inclua, se possível:

- versão do Agrupa-Janela e do Windows;
- descrição do problema e do impacto (o que um atacante consegue fazer);
- passos para reproduzir ou prova de conceito;
- se já é público ou se você sabe de exploração ativa.

## O que esperar

- **Confirmação de recebimento:** em até 7 dias.
- **Avaliação inicial** (se é vulnerabilidade e qual a gravidade): em até 14 dias.
- **Correção:** depende da gravidade e da complexidade; você será informado do andamento pelo próprio advisory.
- Depois da correção publicada, o advisory é divulgado. Se quiser, você recebe crédito pelo relato.

O projeto é mantido por uma pessoa, sem garantia de prazo. Os prazos acima são o compromisso de melhor esforço.

## Escopo

O Agrupa-Janela é um app desktop que roda com os privilégios do usuário logado. O modelo de ameaça parte daí: quem já executa código como o mesmo usuário já pode fazer o que o app faz.

**É vulnerabilidade**, por exemplo:

- o instalador, o app ou a atualização executarem ou gravarem arquivos que não sejam os do release oficial (ex.: falha na verificação de integridade, download por canal inseguro);
- o app ganhar ou repassar privilégios (ex.: permitir que um processo não elevado controle uma janela elevada por meio do Agrupa-Janela);
- grupos salvos (`groups.json`) ou preferências levarem o app a executar algo diferente do que o usuário salvou de forma inesperada, em um cenário que não exija já ter acesso de escrita à pasta do usuário;
- vazamento de dados do usuário pela rede;
- o instalador pedir ou usar administrador sem necessidade, ou gravar fora da pasta do usuário.

**Não é vulnerabilidade** (abra uma issue normal, se for bug):

- o app fechar ou travar (sem impacto de segurança), ou uma janela não ser devolvida corretamente: é bug grave, relate como bug;
- ações que exigem que o atacante já execute código como o usuário, ou que já possa editar `%APPDATA%\AgrupaJanela` ou `%LOCALAPPDATA%\Programs\AgrupaJanela`;
- o aviso do SmartScreen por o instalador não ser assinado (limitação conhecida, veja o [README](README.md#aviso-do-smartscreen));
- apps elevados não poderem ser agrupados (é a proteção do Windows funcionando);
- problemas em apps de terceiros que o usuário escolheu agrupar.

## Princípios de segurança do app

Estes princípios orientam o código. Uma mudança que viole algum deles é tratada como problema de segurança.

- **Sem administrador.** O app roda como o usuário (`requestedExecutionLevel level="asInvoker"`). A instalação é por usuário, em `%LOCALAPPDATA%\Programs\AgrupaJanela`, e "Iniciar com o Windows" usa só a chave do usuário (`HKCU\...\Run`).
- **Sem injeção de código em outros processos.** Nada de DLLs injetadas nem ganchos *in-context*. O app usa APIs públicas sobre janelas (`SetParent`, `SetWindowPos`, estilos, menu de sistema) e ganchos WinEvent **out-of-context** (`WINEVENT_OUTOFCONTEXT`), que rodam no próprio processo do Agrupa-Janela.
- **Sem rede, exceto atualização.** A única conexão é a verificação de atualizações nos Releases do GitHub (`api.github.com`), que pode ser desligada nas preferências. Não há telemetria.
- **Integridade por SHA256.** Cada release publica `SHA256SUMS.txt`. As atualizações baixadas pelo app são conferidas por SHA256 antes de serem usadas, e o instalador confere o hash do conteúdo embutido antes de extrair. O download só é aceito se vier do repositório oficial (`github.com/brunocsilva41/agrupa-janela/releases/download/`) e tiver no máximo 200 MB.
- **Falha fechada (fail-closed).** Na dúvida, o app não age: não relança executáveis que não tenham caminho absoluto `.exe` existente (nada resolvido pelo `PATH`); não reaproveita uma janela salva se o processo não for o mesmo (o Windows reusa handles); não agrupa janelas de processos elevados quando ele próprio não é elevado; e, em qualquer erro, devolve as janelas agrupadas em vez de continuar.
