using System.Windows;
using CompleteUninstaller.App.ViewModels;

namespace CompleteUninstaller.App.Views;

public partial class QuarantineWindow : Window
{
    public QuarantineWindow(AppServices services)
    {
        InitializeComponent();
        DataContext = new QuarantineViewModel(services.Quarantine)
        {
            Confirm = message => MessageBox.Show(this, message, "Quarentena", MessageBoxButton.YesNo, MessageBoxImage.Question)
                                 == MessageBoxResult.Yes,
            Notify = message => MessageBox.Show(this, message, "Quarentena", MessageBoxButton.OK, MessageBoxImage.Information),
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
