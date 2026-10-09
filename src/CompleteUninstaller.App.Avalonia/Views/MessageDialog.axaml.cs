using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CompleteUninstaller.App.Avalonia.Views;

/// <summary>Caixa de mensagem simples (o Avalonia não tem MessageBox): aviso com OK ou pergunta Sim/Não.</summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, string message)
    {
        var dialog = Create(message, question: false);
        await dialog.ShowDialog<bool>(owner);
    }

    public static async Task<bool> AskAsync(Window owner, string message)
    {
        var dialog = Create(message, question: true);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static MessageDialog Create(string message, bool question)
    {
        var dialog = new MessageDialog();
        dialog.MessageText.Text = message;
        dialog.NoButton.IsVisible = question;
        dialog.YesButton.Content = question ? "Sim" : "OK";
        return dialog;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);
}
