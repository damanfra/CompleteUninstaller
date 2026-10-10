using CompleteUninstaller.Core.Updates;

namespace CompleteUninstaller.Updater;

/// <summary>
/// O fluxo que as duas interfaces (WPF e Avalonia) repetiriam: verificar, perguntar, baixar, instalar e reiniciar.
/// Não conhece nenhuma biblioteca de interface: tudo chega por funções (perguntar, avisar, mostrar andamento, sair).
/// Nunca atualiza sem a confirmação da pessoa.
/// </summary>
public sealed class UpdateFlow
{
    private readonly UpdateService _service;
    private readonly Func<string, Task<bool>> _confirm;
    private readonly Func<string, Task> _notify;
    private readonly Action<string> _status;
    private readonly Action _exit;
    private readonly Action<string> _log;
    private bool _running;

    public UpdateFlow(
        UpdateService service,
        Func<string, Task<bool>> confirm,
        Func<string, Task> notify,
        Action<string> status,
        Action exit,
        Action<string> log)
    {
        _service = service;
        _confirm = confirm;
        _notify = notify;
        _status = status;
        _exit = exit;
        _log = log;
    }

    /// <param name="userInitiated">
    /// Verdadeiro quando a pessoa clicou em "Atualizações": responde sempre (inclusive "já está em dia" e erros).
    /// Falso na verificação silenciosa ao abrir: só aparece algo se houver versão nova.
    /// </param>
    public async Task RunAsync(bool userInitiated, CancellationToken cancellationToken = default)
    {
        if (_running)
        {
            return;
        }

        _running = true;
        try
        {
            var current = UpdateService.CurrentVersion();
            var check = await _service.CheckAsync(current, cancellationToken);
            if (check.Error is not null)
            {
                _log($"Verificação de atualização: {check.Error}");
                if (userInitiated)
                {
                    await _notify(check.Error);
                }

                return;
            }

            var decision = check.Decision!;
            if (decision.Offer is not { } offer)
            {
                _log($"Verificação de atualização: {decision.Message}");
                if (userInitiated)
                {
                    await _notify(decision.Message);
                }

                return;
            }

            var exePath = Environment.ProcessPath;
            if (!UpdateService.CanSelfUpdate(exePath, out var reason))
            {
                _log($"Atualização {offer.Version} disponível, mas não dá para instalar sozinho: {reason}");
                if (userInitiated)
                {
                    await _notify($"Há uma nova versão ({offer.Version}), mas a atualização automática não é possível aqui:\n\n{reason}");
                }

                return;
            }

            var notes = offer.Notes.Trim();
            if (notes.Length > 700)
            {
                notes = notes[..700] + "…";
            }

            var question = $"Há uma nova versão: {offer.Version} (você usa a {current}).\n\n" +
                           (notes.Length > 0 ? $"{notes}\n\n" : string.Empty) +
                           "Baixar e instalar agora? O aplicativo será reiniciado.";
            if (!await _confirm(question))
            {
                _log($"Atualização {offer.Version} adiada pela pessoa.");
                return;
            }

            _log($"Instalando a atualização {offer.Version}.");
            var progress = new Progress<string>(message =>
            {
                _status(message);
                _log(message);
            });

            try
            {
                await _service.ApplyAsync(offer, exePath!, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is UpdateException or HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _log($"Falha na atualização: {ex.Message}");
                await _notify($"Não foi possível atualizar:\n\n{ex.Message}\n\nNada foi alterado.");
                return;
            }

            UpdateService.Restart(exePath!);
            _exit();
        }
        finally
        {
            _running = false;
        }
    }
}
