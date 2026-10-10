using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CompleteUninstaller.App.Avalonia.Mvvm;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Paths;
using CompleteUninstaller.Core.Safety;
using CompleteUninstaller.Core.Text;
using CompleteUninstaller.Infrastructure.Linux.Logging;
using CompleteUninstaller.Infrastructure.Linux.Uninstall;

namespace CompleteUninstaller.App.Avalonia.ViewModels;

public enum UninstallStep
{
    Options,
    Working,
    Review,
    Done,
}

/// <summary>
/// Assistente de desinstalação: opções → simulação → gerenciador de pacotes → varredura de sobras → revisão → quarentena.
/// </summary>
public sealed class UninstallViewModel : ObservableObject
{
    private readonly AppServices _services;
    private IReadOnlyList<InstalledApp> _inventory = [];
    private UninstallStep _step = UninstallStep.Options;
    private bool _isModerate;
    private bool _skipUninstaller;
    private string _workingText = string.Empty;
    private string _reviewSummary = string.Empty;
    private string _doneTitle = "Concluído";
    private string _doneSummary = string.Empty;

    public UninstallViewModel(AppServices services, InstalledApp app)
    {
        _services = services;
        App = app;

        StartCommand = new AsyncRelayCommand(StartAsync, () => Step == UninstallStep.Options);
        RemoveCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => Step == UninstallStep.Review && Leftovers.Any(l => l.IsSelected));
        SkipCleanupCommand = new RelayCommand(
            () => Finish("Limpeza ignorada", "Nenhuma sobra foi removida."),
            () => Step == UninstallStep.Review);
        SelectAllCommand = new RelayCommand(() => SetSelection(_ => true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
        SelectHighCommand = new RelayCommand(() => SetSelection(l => l.IsHigh));
    }

    /// <summary>Pergunta sim/não ao usuário (fornecida pela janela).</summary>
    public Func<string, Task<bool>>? Confirm { get; set; }

    public InstalledApp App { get; }

    public string Title => $"Desinstalar {App.DisplayName}";

    public string Subtitle => string.Join(" · ", new[] { App.Publisher, App.DisplayVersion, App.SourceText }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public ObservableCollection<string> LogLines { get; } = [];

    public ObservableCollection<LeftoverItemViewModel> Leftovers { get; } = [];

    public ICommand StartCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand SkipCleanupCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand SelectNoneCommand { get; }

    public ICommand SelectHighCommand { get; }

    public UninstallStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsOptionsStep));
                OnPropertyChanged(nameof(IsWorkingStep));
                OnPropertyChanged(nameof(IsReviewStep));
                OnPropertyChanged(nameof(IsDoneStep));
                CommandRequery.Invalidate();
            }
        }
    }

    public bool IsOptionsStep => Step == UninstallStep.Options;

    public bool IsWorkingStep => Step == UninstallStep.Working;

    public bool IsReviewStep => Step == UninstallStep.Review;

    public bool IsDoneStep => Step == UninstallStep.Done;

    public bool IsSafe
    {
        get => !_isModerate;
        set
        {
            if (value)
            {
                IsModerate = false;
            }
        }
    }

    public bool IsModerate
    {
        get => _isModerate;
        set
        {
            if (SetProperty(ref _isModerate, value))
            {
                OnPropertyChanged(nameof(IsSafe));
            }
        }
    }

    public bool SkipUninstaller
    {
        get => _skipUninstaller;
        set => SetProperty(ref _skipUninstaller, value);
    }

    public string WorkingText
    {
        get => _workingText;
        private set => SetProperty(ref _workingText, value);
    }

    public string ReviewSummary
    {
        get => _reviewSummary;
        private set => SetProperty(ref _reviewSummary, value);
    }

    public string DoneTitle
    {
        get => _doneTitle;
        private set => SetProperty(ref _doneTitle, value);
    }

    public string DoneSummary
    {
        get => _doneSummary;
        private set => SetProperty(ref _doneSummary, value);
    }

    /// <summary>Explica o que cada gerenciador faz com os dados do programa (aparece na tela de opções).</summary>
    public string DataNote => App.Source switch
    {
        AppSource.Dpkg => "O apt remove o programa e mantém os arquivos de configuração do sistema (/etc); eles aparecem como sobras de confiança alta.",
        AppSource.Rpm => "O dnf remove o programa; cópias de configuração (.rpmsave) e dados ficam como sobras.",
        AppSource.Flatpak => "Os dados do aplicativo ficam em ~/.var/app e entram na lista de sobras (nada é apagado junto com o aplicativo).",
        AppSource.Snap => "O Snap pode guardar um instantâneo automático dos dados (veja \"snap saved\"); o que sobrar em ~/snap entra na lista.",
        _ => string.Empty,
    };

    private async Task StartAsync()
    {
        Step = UninstallStep.Working;
        var progress = new Progress<string>(AddLog);
        var level = IsModerate ? CleanupLevel.Moderate : CleanupLevel.Safe;
        AddLog($"Nível de limpeza: {(level == CleanupLevel.Safe ? "Seguro" : "Moderado")}.");

        // 1) O que o pacote instalou (só dá para ler enquanto ele ainda está instalado).
        WorkingText = "Lendo a lista de arquivos do pacote...";
        if (App.OwnedPaths is null)
        {
            App.OwnedPaths = await Task.Run(() => _services.Inventory.CapturePackageFiles(App));
            AddLog($"{App.OwnedPaths.Count} caminhos registrados pelo pacote.");
        }

        // 2) Simulação e remoção pelo gerenciador de pacotes
        if (!SkipUninstaller)
        {
            WorkingText = "Simulando a remoção...";
            var preview = await _services.Uninstaller.PreviewAsync(App);
            if (preview.Error is not null)
            {
                AddLog(preview.Error);
                Finish("Não foi possível remover", preview.Error);
                return;
            }

            if (preview.OtherPackages.Count > 0)
            {
                var list = string.Join(", ", preview.OtherPackages.Take(25));
                var more = preview.OtherPackages.Count > 25 ? $" e mais {preview.OtherPackages.Count - 25}" : string.Empty;
                AddLog($"A remoção também removerá: {list}{more}.");
                if (!await Ask($"Para remover \"{App.DisplayName}\", o gerenciador de pacotes também removerá {preview.OtherPackages.Count} outro(s) pacote(s) que dependem dele:\n\n{list}{more}\n\nContinuar?"))
                {
                    Finish("Operação cancelada", "Nada foi alterado.");
                    return;
                }
            }

            WorkingText = "Removendo o pacote (pode pedir sua senha)...";
            var outcome = await _services.Uninstaller.RunAsync(App, progress, CancellationToken.None);
            AddLog(outcome.Message);
            if (outcome.Status == UninstallStatus.Cancelled)
            {
                Finish("Desinstalação cancelada", outcome.Message);
                return;
            }
        }
        else
        {
            AddLog("Gerenciador de pacotes não executado (opção escolhida).");
        }

        // 3) O programa saiu mesmo?
        WorkingText = "Conferindo se o programa foi removido...";
        var loaded = await _services.Inventory.LoadAsync();
        _inventory = loaded.Apps;
        foreach (var error in loaded.Errors)
        {
            AddLog($"Aviso: {error}");
        }

        var stillInstalled = await Task.Run(() => _services.Inventory.IsStillInstalled(App));
        if (stillInstalled)
        {
            AddLog("O programa ainda aparece como instalado.");
            var proceed = await Ask(
                "O programa ainda aparece como instalado (a remoção pode ter falhado).\n\n" +
                "Procurar sobras mesmo assim? Os arquivos do pacote continuam protegidos, mas configuração, cache e dados do " +
                "programa (~/.config, ~/.cache, ~/.var/app, ~/snap) podem aparecer na lista. Todos os itens virão desmarcados.");
            if (!proceed)
            {
                Finish("Programa ainda instalado", "Nenhuma sobra foi removida.");
                return;
            }
        }

        // 4) Varredura
        WorkingText = "Procurando sobras...";
        var items = await Task.Run(() => _services.Scanner.Scan(App, level, _inventory, progress));

        Leftovers.Clear();
        foreach (var item in items)
        {
            var row = new LeftoverItemViewModel(item);
            if (stillInstalled)
            {
                row.IsSelected = false; // o programa ainda existe: nada vem marcado
            }

            row.PropertyChanged += OnLeftoverChanged;
            Leftovers.Add(row);
        }

        if (Leftovers.Count == 0)
        {
            Finish("Tudo limpo", "Nenhuma sobra encontrada.");
            return;
        }

        UpdateReviewSummary();
        Step = UninstallStep.Review;
    }

    private async Task RemoveSelectedAsync()
    {
        var selected = Leftovers.Where(l => l.IsSelected).Select(l => l.Item).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var home = _services.Environment.Home;
        var system = selected.Count(i => !UnixPath.IsSameOrUnder(i.Target, home));
        var services = selected.Count(i => i.Kind == LeftoverKind.Service);
        var message = $"Mover {selected.Count} item(ns) para a quarentena?";
        if (system > 0 && !_services.Environment.IsRoot)
        {
            message += $"\n\n{system} item(ns) estão fora da sua pasta pessoal (/opt, /etc...). O sistema vai pedir sua senha para movê-los.";
        }

        if (services > 0)
        {
            message += $"\n\n{services} serviço(s) do systemd serão desativados e o arquivo da unidade vai para a quarentena.";
        }

        if (!await Ask(message))
        {
            return;
        }

        Step = UninstallStep.Working;
        WorkingText = "Removendo os itens selecionados...";
        var progress = new Progress<string>(AddLog);
        PathGuard guard = await Task.Run(() => _services.Scanner.CreateGuard(App, _inventory));
        var report = await Task.Run(() => _services.Removal.Remove(App, selected, guard, progress));

        foreach (var error in report.Errors)
        {
            AddLog($"Erro: {error}");
        }

        var freed = selected.Where(i => i.SizeBytes is not null).Sum(i => i.SizeBytes!.Value);
        var summary =
            $"{report.Succeeded} item(ns) movido(s) para a quarentena ({ByteSize.Format(freed)})." +
            (report.Skipped > 0 ? $"\n{report.Skipped} item(ns) ignorado(s) (já não existiam ou estavam protegidos)." : string.Empty) +
            (report.Failed > 0 ? $"\n{report.Failed} item(ns) não puderam ser movidos (veja o registro abaixo)." : string.Empty) +
            "\n\nPara desfazer, use o botão Quarentena na janela principal.";

        Finish(report.Failed > 0 ? "Concluído com avisos" : "Concluído", summary);
    }

    private void Finish(string title, string summary)
    {
        DoneTitle = title;
        DoneSummary = summary;
        AddLog(summary.Replace("\n", " ", StringComparison.Ordinal));
        Step = UninstallStep.Done;
    }

    private async Task<bool> Ask(string message) => Confirm is null || await Confirm(message);

    private void AddLog(string message)
    {
        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        Log.Info($"[{App.DisplayName}] {message}");
    }

    private void SetSelection(Func<LeftoverItemViewModel, bool> predicate)
    {
        foreach (var row in Leftovers)
        {
            row.IsSelected = predicate(row);
        }
    }

    private void OnLeftoverChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LeftoverItemViewModel.IsSelected))
        {
            UpdateReviewSummary();
            CommandRequery.Invalidate();
        }
    }

    private void UpdateReviewSummary()
    {
        var selected = Leftovers.Where(l => l.IsSelected).ToList();
        var size = selected.Where(l => l.Item.SizeBytes is not null).Sum(l => l.Item.SizeBytes!.Value);
        var medium = Leftovers.Count(l => !l.IsHigh);
        ReviewSummary =
            $"{Leftovers.Count} itens encontrados · {selected.Count} selecionados ({ByteSize.Format(size)})" +
            (medium > 0 ? $" · {medium} de confiança média vieram desmarcados: revise antes de incluir." : ".");
    }
}
