using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace AgrupaJanela.Setup;

/// <summary>O zip do app embutido no Setup (EmbeddedResource "AgrupaJanela.Payload.zip") e seu hash de build.</summary>
internal static class Payload
{
    private const string ResourceName = "AgrupaJanela.Payload.zip";
    private static Assembly Assembly => typeof(Payload).Assembly;

    public static bool IsPresent => Assembly.GetManifestResourceInfo(ResourceName) != null;

    public static string? ExpectedSha256 =>
        Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false).OfType<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "PayloadSha256")?.Value;

    public static Stream Open() =>
        Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException("Este instalador não contém o pacote do app (build de desenvolvimento).");

    /// <summary>Confere o SHA-256 do pacote com o valor gravado no build; lança se não bater.</summary>
    public static void Verify()
    {
        var expected = ExpectedSha256;
        if (string.IsNullOrEmpty(expected))
            throw new InvalidDataException("O instalador não tem o hash do pacote; ele pode estar corrompido.");
        using var stream = Open();
        using var sha = SHA256.Create();
        var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O pacote dentro do instalador está corrompido (hash diferente). Baixe o instalador de novo.");
        Log.Info($"Pacote conferido (SHA-256 {actual.ToLowerInvariant()}).");
    }
}
