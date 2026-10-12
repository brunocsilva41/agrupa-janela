namespace AgrupaJanela;

/// <summary>Textos de interface com plural correto (nada de "janela(s)").</summary>
public static class Texto
{
    /// <summary>"1 janela", "3 janelas": escolhe o singular ou o plural conforme n. Inclui o número.</summary>
    public static string Plural(int n, string singular, string plural) => $"{n} {(n == 1 ? singular : plural)}";
}
