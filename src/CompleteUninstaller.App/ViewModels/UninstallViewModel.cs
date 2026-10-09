using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CompleteUninstaller.App.Mvvm;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Models;
using CompleteUninstaller.Core.Text;
using CompleteUninstaller.Infrastructure.Logging;
using CompleteUninstaller.Infrastructure.Removal;
using CompleteUninstaller.Infrastructure.Uninstall;

namespace CompleteUninstaller.App.ViewModels;

public enum UninstallStep
{
    Options,
    Working,
    Review,
    Done,
}

/// <summary>
/// Assistente de desinstalação: opções → desinstalador oficial → varredura de sobras → revisão → quarentena.
/// </summary>
public sealed class UninstallViewModel : ObservableObject
{
    private readonly AppServices _services;
    private CancellationTokenSource? _waitCts;
    private UninstallStep _step = UninstallStep.Options;
    private bool _isModerate;
    private bool _createRestorePoint = true;
    private bool _preferQuiet;
    private bool _skipUninstaller;
    private bool _isWaitingUninstaller;
    private bool _needsReboot;
    private string _workingText = string.Empty;
    private string _reviewSummary = string.Empty;
    private string _doneTitle = "Concluído";
    private string _doneSummary = string.Empty;

    public UninstallViewModel(AppServices services, InstalledApp app)
    {
        _services = services;
        App = app;

        StartCommand = new AsyncRelayCommand(StartAsync, () => Step == UninstallStep.Options);
        StopWaitingCommand = new RelayCommand(() => _waitCts?.Cancel(), () => IsWaitingUninstaller);
        RemoveCommand = new AsyncRelayCommand(RemoveSelectedAsync, () => Step == UninstallStep.Review && Leftovers.Any(l => l.IsSelected));
        SkipCleanupCommand = new RelayCommand(
            () => Finish("Limpeza ignorada", "Nenhuma sobra foi removida."),
            () => Step == UninstallStep.Review);
        SelectAllCommand = new RelayCommand(() => SetSelection(_ => true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
        SelectHighCommand = new RelayCommand(() => SetSelection(l => l.IsHigh));
    }

    /// <summary>Pergunta sim/não ao usuário (fornecida pela janela).</summary>
    public Func<string, bool>? Confirm { get; set; }

    public InstalledApp App { get; }

    public string Title => $"Desinstalar {App.DisplayName}";

    public string Subtitle => string.Join(" · ", new[] { App.Publisher, App.DisplayVersion, App.SourceText }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public ObservableCollection<string> LogLines { get; } = [];

    public ObservableCollection<LeftoverItemViewModel> Leftovers { get; } = [];

    public ICommand StartCommand { get; }

    public ICommand StopWaitingCommand { get; }

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
                CommandManager.InvalidateRequerySuggested();
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

    public bool CreateRestorePoint
    {
        get => _createRestorePoint;
        set => SetProperty(ref _createRestorePoint, value);
    }

    public bool HasQuietOption => App.HasQuietUninstall;

    public bool PreferQuiet
    {
        get => _preferQuiet;
        set => SetProperty(ref _preferQuiet, value);
    }

    public bool SkipUninstaller
    {
        get => _skipUninstaller;
        set => SetProperty(ref _skipUninstaller, value);
    }

    public bool IsWaitingUninstaller
    {
        get => _isWaitingUninstaller;
        private set
        {
            if (SetProperty(ref _isWaitingUninstaller, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
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

    public bool NeedsReboot
    {
        get => _needsReboot;
        private set => SetProperty(ref _needsReboot, value);
    }

    private async Task StartAsync()
    {
        Step = UninstallStep.Working;
        var progress = new Progress<string>(AddLog);
        var level = IsModerate ? CleanupLevel.Moderate : CleanupLevel.Safe;
        AddLog($"Nível de limpeza: {(level == CleanupLevel.Safe ? "Seguro" : "Moderado")}.");

        // 1) Ponto de restauração
        if (CreateRestorePoint)
        {
            WorkingText = "Criando ponto de restauração do sistema (pode levar um minuto)...";
            var restorePoint = await _services.RestorePoints.CreateAsync($"Complete Uninstaller - antes de remover {App.DisplayName}");
            AddLog(restorePoint.Message);
            if (restorePoint.Status == RestorePointStatus.Failed
                && !Ask($"{restorePoint.Message}\n\nDeseja continuar mesmo assim?"))
            {
                Finish("Operação cancelada", "Nada foi alterado.");
                return;
            }
        }

        // 2) Desinstalador oficial
        if (!SkipUninstaller)
        {
            WorkingText = "Executando o desinstalador do programa...";
            _waitCts = new CancellationTokenSource();
            IsWaitingUninstaller = true;
            UninstallOutcome outcome;
            try
            {
                outcome = await _services.Uninstaller.RunAsync(App, PreferQuiet, progress, _waitCts.Token);
            }
            finally
            {
                IsWaitingUninstaller = false;
                _waitCts.Dispose();
                _waitCts = null;
            }

            AddLog(outcome.Message);
            NeedsReboot |= outcome.RebootRequired;

            if (outcome.Status == UninstallStatus.Cancelled)
            {
                Finish("Desinstalação cancelada", "O desinstalador foi cancelado. Nenhuma sobra foi procurada.");
                return;
            }
        }
        else
        {
            AddLog("Desinstalador oficial não executado (opção escolhida).");
        }

        // 3) O programa saiu mesmo?
        WorkingText = "Conferindo se o programa foi removido...";
        var inventory = await _services.Inventory.LoadAsync();
        var stillInstalled = await Task.Run(() => _services.Inventory.IsStillInstalled(App));
        if (stillInstalled)
        {
            AddLog("O programa ainda aparece como instalado.");
            var proceed = Ask(
                "O programa ainda aparece como instalado (o desinstalador pode ter sido cancelado ou falhado).\n\n" +
                "Procurar sobras mesmo assim? Arquivos ainda usados pelo programa podem aparecer na lista.");
            if (!proceed)
            {
                Finish("Programa ainda instalado", "Nenhuma sobra foi removida.");
                return;
            }
        }

        // 4) Varredura
        WorkingText = "Procurando sobras...";
        var items = await Task.Run(() => _services.Scanner.Scan(App, level, inventory.Apps, progress));

        Leftovers.Clear();
        foreach (var item in items)
        {
            var row = new LeftoverItemViewModel(item);
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

        var services = selected.Count(i => i.Kind == LeftoverKind.Service);
        var message = $"Mover {selected.Count} item(ns) para a quarentena?";
        if (services > 0)
        {
            message += $"\n\n{services} serviço(s) serão parados e excluídos (a configuração fica guardada para restauração).";
        }

        if (!Ask(message))
        {
            return;
        }

        Step = UninstallStep.Working;
        WorkingText = "Removendo os itens selecionados...";
        var progress = new Progress<string>(AddLog);
        var report = await Task.Run(() => _services.Removal.Remove(App, selected, progress));

        foreach (var error in report.Errors)
        {
            AddLog($"Erro: {error}");
        }

        NeedsReboot |= report.PendingReboot > 0;
        var freed = selected.Where(i => i.SizeBytes is not null).Sum(i => i.SizeBytes!.Value);
        var summary =
            $"{report.Succeeded} item(ns) removido(s) e guardado(s) na quarentena ({ByteSize.Format(freed)})." +
            (report.PendingReboot > 0 ? $"\n{report.PendingReboot} item(ns) em uso serão apagados na próxima reinicialização." : string.Empty) +
            (report.Skipped > 0 ? $"\n{report.Skipped} item(ns) já não existiam." : string.Empty) +
            (report.Failed > 0 ? $"\n{report.Failed} item(ns) não puderam ser removidos (veja o registro abaixo)." : string.Empty) +
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

    private bool Ask(string message) => Confirm?.Invoke(message) ?? true;

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
            CommandManager.InvalidateRequerySuggested();
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
