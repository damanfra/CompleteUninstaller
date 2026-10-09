using System.Collections.ObjectModel;
using System.Windows.Input;
using CompleteUninstaller.App.Avalonia.Mvvm;
using CompleteUninstaller.App.Avalonia.Services;
using CompleteUninstaller.Infrastructure.Linux.Removal;

namespace CompleteUninstaller.App.Avalonia.ViewModels;

public sealed class QuarantineViewModel : ObservableObject
{
    private readonly QuarantineStore _store;
    private QuarantineSession? _selectedSession;
    private string _statusText = string.Empty;
    private bool _isBusy;

    public QuarantineViewModel(QuarantineStore store)
    {
        _store = store;
        RefreshCommand = new RelayCommand(Refresh);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => SelectedSession is { RestorableCount: > 0 } && !_isBusy);
        PurgeCommand = new AsyncRelayCommand(PurgeAsync, () => SelectedSession is not null && !_isBusy);
        OpenFolderCommand = new RelayCommand(OpenFolder, () => SelectedSession is not null);
        Refresh();
    }

    public Func<string, Task<bool>>? Confirm { get; set; }

    public Func<string, Task>? Notify { get; set; }

    public ObservableCollection<QuarantineSession> Sessions { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand RestoreCommand { get; }

    public ICommand PurgeCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public QuarantineSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (SetProperty(ref _selectedSession, value))
            {
                OnPropertyChanged(nameof(SelectedEntries));
                CommandRequery.Invalidate();
            }
        }
    }

    public IReadOnlyList<QuarantineEntry> SelectedEntries =>
        SelectedSession?.Entries.ToList() ?? (IReadOnlyList<QuarantineEntry>)Array.Empty<QuarantineEntry>();

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private void Refresh()
    {
        var selectedId = SelectedSession?.Id;
        Sessions.Clear();
        foreach (var session in _store.ListSessions())
        {
            Sessions.Add(session);
        }

        SelectedSession = Sessions.FirstOrDefault(s => s.Id == selectedId) ?? Sessions.FirstOrDefault();
        StatusText = Sessions.Count == 0 ? "A quarentena está vazia." : $"{Sessions.Count} limpeza(s) guardada(s).";
    }

    private async Task RestoreAsync()
    {
        var session = SelectedSession;
        if (session is null || !await Ask(
                $"Restaurar {session.RestorableCount} item(ns) de \"{session.AppName}\" aos locais originais?"))
        {
            return;
        }

        _isBusy = true;
        StatusText = "Restaurando...";
        try
        {
            var report = await Task.Run(() => _store.Restore(session));
            var message = $"{report.Restored} item(ns) restaurado(s).";
            if (report.Failed > 0)
            {
                message += $"\n{report.Failed} falha(s):\n• " + string.Join("\n• ", report.Errors);
            }

            await Tell(message);
        }
        finally
        {
            _isBusy = false;
            Refresh();
        }
    }

    private async Task PurgeAsync()
    {
        var session = SelectedSession;
        if (session is null || !await Ask(
                $"Excluir definitivamente a quarentena de \"{session.AppName}\"? Não será mais possível restaurar esses itens."))
        {
            return;
        }

        _isBusy = true;
        StatusText = "Excluindo...";
        try
        {
            var errors = await Task.Run(() => _store.Purge(session));
            if (errors.Count > 0)
            {
                await Tell("Não foi possível excluir tudo:\n• " + string.Join("\n• ", errors));
            }
        }
        finally
        {
            _isBusy = false;
            Refresh();
        }
    }

    private void OpenFolder()
    {
        if (SelectedSession is { } session && Directory.Exists(session.DirectoryPath))
        {
            ShellOpen.Folder(session.DirectoryPath);
        }
    }

    private async Task<bool> Ask(string message) => Confirm is null || await Confirm(message);

    private async Task Tell(string message)
    {
        if (Notify is not null)
        {
            await Notify(message);
        }
    }
}
