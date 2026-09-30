## O que muda

<!-- Descreva a mudança e o motivo. Se resolve uma issue, escreva "Resolve #123". -->

## Como foi testado

<!-- Apps usados (nome/versão), modos (Incorporada/Acoplada), versão do Windows e o que foi verificado. -->

## Checklist

- [ ] Mudança grande? Existe uma issue aprovada pelo mantenedor.
- [ ] Todos os commits têm `Signed-off-by` (DCO, `git commit -s`).
- [ ] `dotnet build AgrupaJanela.sln` sem erros e sem avisos novos.
- [ ] `dotnet test tests/AgrupaJanela.Tests` passando; testes novos para a lógica nova, quando possível.
- [ ] Testado à mão com ao menos um app no modo Incorporada e um no modo Acoplada.
- [ ] **Nenhuma janela do usuário pode ser perdida**: em qualquer erro, a janela é devolvida à área de trabalho.
- [ ] Sem dependências novas não discutidas; sem injeção em outros processos, sem administrador, sem rede nova.
- [ ] Textos e comentários em português; `CHANGELOG.md` atualizado em "Não lançado" quando afeta o usuário.
