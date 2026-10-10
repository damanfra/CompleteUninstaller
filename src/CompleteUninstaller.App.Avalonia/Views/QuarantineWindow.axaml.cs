using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CompleteUninstaller.App.Avalonia.Views;

public partial class QuarantineWindow : Window
{
    public QuarantineWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
