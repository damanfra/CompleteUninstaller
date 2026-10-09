using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;

namespace CompleteUninstaller.App.Avalonia.Mvvm;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// O Avalonia não tem o CommandManager do WPF. Este aviso global faz o papel dele: os view models chamam
/// <see cref="Invalidate"/> quando o estado muda e todos os botões reavaliam o CanExecute.
/// </summary>
public static class CommandRequery
{
    public static event EventHandler? Requested;

    public static void Invalidate()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Requested?.Invoke(null, EventArgs.Empty);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Requested?.Invoke(null, EventArgs.Empty));
        }
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandRequery.Requested += value;
        remove => CommandRequery.Requested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>Exceções não tratadas em comandos assíncronos (a janela principal exibe uma mensagem).</summary>
    public static event Action<Exception>? UnhandledException;

    public event EventHandler? CanExecuteChanged
    {
        add => CommandRequery.Requested += value;
        remove => CommandRequery.Requested -= value;
    }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        CommandRequery.Invalidate();
        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            UnhandledException?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            CommandRequery.Invalidate();
        }
    }
}
