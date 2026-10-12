using AgrupaJanela.Hosting;

namespace AgrupaJanela.Tests.Hosting;

/// <summary>Argumentos com cara de segredo não são guardados em groups.json (AppIdentity.WithoutSecrets).</summary>
public class AppIdentitySecretsTests
{
    [Theory]
    [InlineData("-u usuario:senha https://api.exemplo.com")]          // curl -u
    [InlineData("-uusuario:senha https://api.exemplo.com")]           // curl -uusuario:senha
    [InlineData("--user usuario:senha")]
    [InlineData("--user=usuario:senha")]
    [InlineData("--key=C:\\chaves\\id_rsa")]
    [InlineData("--key C:\\chaves\\id_rsa")]
    [InlineData("--cookie=sessao=abc123")]
    [InlineData("sshpass -p minhasenha ssh servidor")]
    [InlineData("https://arquivos.exemplo.com/a.zip?sig=abcdef123456")]
    [InlineData("https://arquivos.exemplo.com/a.zip?id=1&sig=abcdef123456")]
    [InlineData("https://arquivos.exemplo.com/a.zip?X-Amz-Signature=abcdef")]
    [InlineData("clone --token ghp_1234567890abcdefghijklmnopqrstuvwxyz")]
    [InlineData("gho_1234567890abcdefghijklmnopqrstuvwxyz")]
    [InlineData("github_pat_11ABCDEFG0123456789_abcdefghijk")]
    [InlineData("--api sk-abcdefghijklmnopqrstuvwxyz0123")]
    [InlineData("--jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.assinatura")]
    [InlineData("https://usuario:senha@servidor.exemplo.com/pasta")] // usuário:senha na URL (já era pego)
    [InlineData("-pSenhaColada")]                                    // -p encostado (já era pego)
    [InlineData("--password=abc")]                                   // nome com cara de senha (já era pego)
    public void ComSegredo_Descartado(string arguments) =>
        Assert.Null(AppIdentity.WithoutSecrets(arguments));

    [Theory]
    [InlineData("-d \"C:\\proj\"")]                                  // pasta de trabalho
    [InlineData("-p \"PowerShell\"")]                                // -p com espaço: nome de perfil, não senha
    [InlineData("--profile-directory=Default")]                      // perfil do Chrome/Edge
    [InlineData("C:\\x\\arquivo.txt")]
    [InlineData("--tray")]
    [InlineData("-k")]
    [InlineData("https://exemplo.com/pagina?id=5&pagina=2")]        // URL sem assinatura
    [InlineData("task-abcdefghijklmnopqrstuvwxyz0123")]              // "sk-" dentro de outra palavra
    [InlineData("--keyboard-layout=abnt2")]                          // --key só quando é a opção exata
    [InlineData("--user-data-dir=C:\\perfil")]                       // --user só quando é a opção exata
    public void ArgumentosComuns_Mantidos(string arguments) =>
        Assert.Equal(arguments, AppIdentity.WithoutSecrets(arguments));

    [Fact]
    public void Nulo_Null() =>
        Assert.Null(AppIdentity.WithoutSecrets(null));
}
