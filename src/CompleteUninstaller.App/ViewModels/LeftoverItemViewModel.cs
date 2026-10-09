using CompleteUninstaller.App.Mvvm;
using CompleteUninstaller.Core.Leftovers;
using CompleteUninstaller.Core.Text;

namespace CompleteUninstaller.App.ViewModels;

public sealed class LeftoverItemViewModel : ObservableObject
{
    private bool _isSelected;

    public LeftoverItemViewModel(LeftoverItem item)
    {
        Item = item;

        // Só os itens de confiança alta vêm marcados; os de confiança média exigem decisão do usuário.
        _isSelected = item.Confidence == Confidence.High;
    }

    public LeftoverItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsHigh => Item.Confidence == Confidence.High;

    public string ConfidenceText => IsHigh ? "Alta" : "Média";

    public string KindText => Item.Kind switch
    {
        LeftoverKind.Folder => "Pasta",
        LeftoverKind.File => "Arquivo",
        LeftoverKind.Shortcut => "Atalho",
        LeftoverKind.Service => "Serviço",
        LeftoverKind.ScheduledTask => "Tarefa agendada",
        _ => Item.Kind.ToString(),
    };

    public string Target => Item.Target;

    public string Reason => Item.Reason;

    public string Details => Item.Details ?? string.Empty;

    public bool HasDetails => !string.IsNullOrWhiteSpace(Item.Details);

    public string Size => ByteSize.Format(Item.SizeBytes);

    public long SortSize => Item.SizeBytes ?? -1;
}
