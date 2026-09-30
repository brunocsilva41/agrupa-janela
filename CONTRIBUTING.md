# Como contribuir

Obrigado pelo interesse no SplitDeck. O projeto é **aberto, mas controlado**: qualquer pessoa pode propor mudanças, e tudo entra por Pull Request revisado e aprovado pelo mantenedor ([@brunocsilva41](https://github.com/brunocsilva41)).

Ao participar, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md). Vulnerabilidades **não** devem ir para issues públicas: veja [SECURITY.md](SECURITY.md).

## Regra de ouro

**Nenhuma mudança pode arriscar perder ou fechar janelas do usuário.**

As janelas agrupadas pertencem a outros apps, com trabalho não salvo dentro. Por isso:

- em qualquer erro, a janela deve ser **devolvida** à área de trabalho (estilo, posição e estado originais), nunca destruída;
- todo caminho que tira uma janela do grupo passa por `IGroupedWindow.Release()`, e todo host novo se registra em `HostRegistry` (é ele que devolve tudo em erro inesperado, em `App.xaml.cs`);
- em janelas incorporadas, a devolução precisa acontecer **antes** de o HWND do grupo ser destruído (veja `GroupWindow.OnClosing`);
- exceções não podem escapar de callbacks nativos (WinEvent, WndProc): capture e registre com `Debug.WriteLine`, como em `DockWatcher.OnEvent`;
- fechar apps só com pedido explícito do usuário, e sempre com `WM_CLOSE` (o app pode perguntar se deve salvar), nunca matando o processo.

PRs que violem essa regra não são aceitos, mesmo que funcionem no caso comum.

## Como o fluxo funciona

1. **Mudanças grandes começam por uma issue.** Recurso novo, mudança de comportamento, refatoração ampla ou dependência nova: abra uma issue e espere o "ok" antes de escrever o código. Correções pequenas e documentação podem ir direto para o PR.
2. Faça um fork e crie um branch a partir de `main` (ex.: `fix/devolver-janela-minimizada`).
3. Faça commits pequenos, com mensagem no padrão abaixo e **assinados com DCO** (`git commit -s`).
4. Abra o Pull Request preenchendo o modelo.
5. O branch `main` é protegido: não aceita push direto e exige aprovação do mantenedor (definido em [`.github/CODEOWNERS`](.github/CODEOWNERS)) e checagens do CI passando.

## DCO (Developer Certificate of Origin)

Todo commit precisa da linha `Signed-off-by` com seu nome e e-mail. Com ela você declara que tem o direito de enviar a contribuição sob a licença do projeto (MIT), conforme o texto do DCO 1.1 abaixo.

Para assinar, use `-s`:

```bash
git commit -s -m "fix: devolve janela minimizada ao desagrupar"
```

Isso acrescenta ao fim da mensagem:

```text
Signed-off-by: Seu Nome <seu-email@exemplo.com>
```

Esqueceu em um commit? `git commit --amend -s`. Em vários commits do branch: `git rebase --signoff main`.

Texto de referência (em inglês, sem alterações):

```text
Developer Certificate of Origin
Version 1.1

Copyright (C) 2004, 2006 The Linux Foundation and its contributors.

Everyone is permitted to copy and distribute verbatim copies of this
license document, but changing it is not allowed.


Developer's Certificate of Origin 1.1

By making a contribution to this project, I certify that:

(a) The contribution was created in whole or in part by me and I
    have the right to submit it under the open source license
    indicated in the file; or

(b) The contribution is based upon previous work that, to the best
    of my knowledge, is covered under an appropriate open source
    license and I have the right under that license to submit that
    work with modifications, whether created in whole or in part
    by me, under the same open source license (unless I am
    permitted to submit under a different license), as indicated
    in the file; or

(c) The contribution was provided directly to me by some other
    person who certified (a), (b) or (c) and I have not modified
    it.

(d) I understand and agree that this project and the contribution
    are public and that a record of the contribution (including all
    personal information I submit with it, including my sign-off) is
    maintained indefinitely and may be redistributed consistent with
    this project or the open source license(s) involved.
```

## Ambiente

- Windows 10 ou 11 (64 bits).
- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0).
- Visual Studio 2022, Rider ou VS Code com a extensão C#.

```powershell
dotnet build AgrupaJanela.sln
dotnet test tests/AgrupaJanela.Tests
```

Os testes usam xUnit e ficam em `tests/AgrupaJanela.Tests`, com as mesmas pastas do app (`Hosting/`, `Layout/`, `Persistence/`). Eles cobrem a lógica que não depende de janelas reais (árvore de layout, zonas de soltura, regras de modo, serialização). Lógica nova desse tipo deve vir com teste.

Antes de abrir o PR, teste à mão o que você mudou com apps reais: pelo menos um app no modo **Incorporada** (ex.: `cmd.exe`, Bloco de Notas) e um no modo **Acoplada** (ex.: um navegador). Confira que fechar o grupo com "Devolver as janelas" deixa os apps abertos e no lugar.

## Padrões de código

Imite o código vizinho. Os pontos principais:

- **C# com .NET 8**, `Nullable` e `ImplicitUsings` ligados. Namespaces por arquivo (`namespace AgrupaJanela.Hosting;`), 4 espaços, chaves em linha própria. O [`.editorconfig`](.editorconfig) descreve a formatação.
- **Comentários e textos da interface em português do Brasil.** Comente o *porquê* (limitação do Windows, bug de um app, decisão de segurança), não o óbvio.
- **Handles e ponteiros como `nint`**, não `IntPtr`.
- **Classes `sealed`** por padrão; `static class` para utilitários sem estado.
- **P/Invoke:** declarações compartilhadas ficam em [`Native/Win32.cs`](src/AgrupaJanela/Native/Win32.cs). Declarações usadas por um único módulo podem ficar privadas no próprio arquivo, como já acontece em `DockNative` (`Hosting/DockSync.cs`), `SystemMenuIntegration` e `EmbedPolicy`. Não duplique o que já existe em `Win32.cs`.
- **Delegates passados ao Windows** (WinEvent, EnumWindows de longa duração) precisam ficar guardados em um campo, para o GC não coletá-los.
- **Chamadas que cruzam para outro processo** (ShowWindow, SetWindowPos) não podem travar a interface se o outro app estiver travado: prefira as variantes assíncronas quando fizer sentido (veja `DockNative`).
- **Falhas de gravação de preferências ou grupos não podem derrubar o app.**
- **Sem dependências novas** (pacotes NuGet, ferramentas, serviços) sem discutir antes em uma issue. Hoje o app (`src/AgrupaJanela`) não tem nenhuma dependência NuGet; o instalador (`src/AgrupaJanela.Setup`) usa só `Microsoft.NETFramework.ReferenceAssemblies`, para compilar e sem ir para a saída.
- **Sem injeção de código em outros processos**, sem pedir administrador e sem acesso à rede além da verificação de atualizações. Veja os princípios em [SECURITY.md](SECURITY.md).

Para adicionar uma regra de compatibilidade (qual modo um app usa por padrão), veja [docs/ARQUITETURA.md](docs/ARQUITETURA.md#regras-de-compatibilidade-embedpolicy).

## Mensagens de commit

Use [Conventional Commits](https://www.conventionalcommits.org/pt-br/v1.0.0/), em português:

| Tipo | Quando |
|---|---|
| `feat` | recurso novo |
| `fix` | correção de bug |
| `docs` | só documentação |
| `refactor` | mudança de código sem mudar comportamento |
| `test` | testes |
| `chore` | build, CI, configuração, manutenção |

Exemplos:

```text
feat: permite renomear grupo pela bandeja
fix(docked): não esconde janela acoplada ao trocar de aba rapidamente
docs: explica modo acoplado no README
```

## Checklist do Pull Request

- [ ] Existe uma issue aprovada, se a mudança for grande.
- [ ] Todos os commits têm `Signed-off-by` (DCO).
- [ ] `dotnet build AgrupaJanela.sln` sem erros e sem avisos novos.
- [ ] `dotnet test tests/AgrupaJanela.Tests` passando; testes novos para a lógica nova, quando possível.
- [ ] Testado à mão com ao menos um app Incorporado e um Acoplado.
- [ ] **Nenhuma janela do usuário pode ser perdida**: em erro, a janela é devolvida.
- [ ] Sem dependências novas não discutidas.
- [ ] Textos e comentários em português; `CHANGELOG.md` atualizado em "Não lançado" quando a mudança afeta o usuário.
