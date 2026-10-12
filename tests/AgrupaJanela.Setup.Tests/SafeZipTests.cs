using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using AgrupaJanela.Setup.Tests.Helpers;
using Xunit;

namespace AgrupaJanela.Setup.Tests;

public class SafeZipTests
{
    private static readonly string Root = @"C:\SplitDeck-Teste\destino";

    [Theory]
    [InlineData(@"..\x.txt")]
    [InlineData("../x.txt")]
    [InlineData(@"..\dest2\x")]
    [InlineData(@"\x")]
    [InlineData(@"/x")]
    [InlineData(@"C:\Windows\x.txt")]
    [InlineData(@"x.txt:ads")]
    [InlineData(@"sub\..\..\fora.txt")]
    public void Rejeita_entradas_que_saem_da_pasta_ou_sao_absolutas(string entry)
    {
        Assert.Throws<InvalidDataException>(() => SafeZip.ResolveEntryPath(Root, entry));
    }

    [Fact]
    public void Rejeita_nomes_reservados_do_Windows()
    {
        Assert.Throws<InvalidDataException>(() => SafeZip.ResolveEntryPath(Root, "CON"));
        Assert.Throws<InvalidDataException>(() => SafeZip.ResolveEntryPath(Root, @"sub\NUL.txt"));
    }

    [Theory]
    [InlineData("arquivo.txt", @"C:\SplitDeck-Teste\destino\arquivo.txt")]
    [InlineData("pasta/arquivo.txt", @"C:\SplitDeck-Teste\destino\pasta\arquivo.txt")]
    [InlineData(@"pasta\sub\x.dll", @"C:\SplitDeck-Teste\destino\pasta\sub\x.dll")]
    public void Aceita_caminhos_normais_dentro_da_pasta(string entry, string expected)
    {
        Assert.Equal(expected, SafeZip.ResolveEntryPath(Root, entry));
    }

    [Fact]
    public void Entrada_de_pasta_termina_com_barra_e_entrada_vazia_e_ignorada()
    {
        Assert.Equal(@"C:\SplitDeck-Teste\destino\pasta\", SafeZip.ResolveEntryPath(Root, "pasta/"));
        Assert.Null(SafeZip.ResolveEntryPath(Root, ""));
    }

    [Fact]
    public void Extrai_pacote_legitimo_em_pasta_temporaria()
    {
        using var temp = TempDir.Create();
        var destino = System.IO.Path.Combine(temp.Path, "SplitDeck");
        using var zip = MakeZip(
            ("SplitDeck.exe", "app"),
            ("Themes/", null),
            ("Themes/Dark.xaml", "<ResourceDictionary/>"),
            ("lib/sub/ajuda.txt", "ola"));

        var progressCalls = 0;
        var count = SafeZip.Extract(zip, destino, (_, _) => progressCalls++);

        Assert.Equal(3, count);
        Assert.Equal("app", File.ReadAllText(System.IO.Path.Combine(destino, "SplitDeck.exe")));
        Assert.Equal("<ResourceDictionary/>", File.ReadAllText(System.IO.Path.Combine(destino, "Themes", "Dark.xaml")));
        Assert.Equal("ola", File.ReadAllText(System.IO.Path.Combine(destino, "lib", "sub", "ajuda.txt")));
        Assert.True(progressCalls > 0);
    }

    [Fact]
    public void Entrada_duplicada_por_normalizacao_nao_sobrescreve_o_arquivo()
    {
        // "a/b.txt" e "a\b.txt" viram o mesmo caminho; o segundo não pode trocar o conteúdo do primeiro.
        using var temp = TempDir.Create();
        var destino = System.IO.Path.Combine(temp.Path, "SplitDeck");
        using var zip = MakeZip(
            ("a/b.txt", "primeiro"),
            (@"a\b.txt", "segundo"));

        Assert.ThrowsAny<IOException>(() => SafeZip.Extract(zip, destino));
        Assert.Equal("primeiro", File.ReadAllText(System.IO.Path.Combine(destino, "a", "b.txt")));
    }

    private static MemoryStream MakeZip(params (string Name, string? Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                if (content == null) continue;
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }
}
