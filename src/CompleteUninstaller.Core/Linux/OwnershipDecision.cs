namespace CompleteUninstaller.Core.Linux;

/// <summary>
/// Decide, a partir do resultado de <c>dpkg-query -S</c> / <c>rpm -qf</c>, se um caminho pertence a um pacote instalado.
/// Regra de ouro: na dúvida, o caminho fica protegido. Só "não encontrado" de verdade libera.
/// </summary>
public static class OwnershipDecision
{
    public const string Unverified = "(posse não verificada)";

    /// <returns>Nome do pacote dono; <see cref="Unverified"/> se não deu para saber; null se ninguém é dono.</returns>
    /// <param name="installed">Pacotes instalados; nulo ou vazio significa "desconhecido": qualquer dono protege.</param>
    public static string? Dpkg(int exitCode, string output, ISet<string>? installed)
    {
        // dpkg-query -S: 0 = achou; 1 = nenhum pacote possui o caminho; qualquer outro valor (2, -1 por
        // tempo esgotado ou falha ao iniciar) é erro e não prova nada.
        if (exitCode == 1)
        {
            return null;
        }

        if (exitCode != 0)
        {
            return Unverified;
        }

        var owners = ToolOutputParsers.ParseDpkgSearch(output).Values.SelectMany(v => v).ToList();
        if (owners.Count == 0)
        {
            return Unverified; // saiu com sucesso mas a saída é ilegível
        }

        if (installed is null or { Count: 0 })
        {
            return owners[0];
        }

        return owners.FirstOrDefault(installed.Contains);
    }

    /// <summary>rpm -qf: sucesso com o nome do pacote; "is not owned by any package" libera; o resto é dúvida.</summary>
    public static string? Rpm(int exitCode, string output)
    {
        if (exitCode == 0)
        {
            var name = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            return string.IsNullOrEmpty(name) ? Unverified : name;
        }

        return output.Contains("is not owned by any package", StringComparison.Ordinal) ? null : Unverified;
    }
}
