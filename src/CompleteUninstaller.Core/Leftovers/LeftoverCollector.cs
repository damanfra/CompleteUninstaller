using CompleteUninstaller.Core.Paths;

namespace CompleteUninstaller.Core.Leftovers;

/// <summary>
/// Acumula as sobras encontradas pelos vários scanners, eliminando duplicatas
/// e itens redundantes (arquivos dentro de uma pasta que já será removida).
/// </summary>
public sealed class LeftoverCollector
{
    private readonly IPathRules _rules;
    private readonly Dictionary<string, LeftoverItem> _items;

    public LeftoverCollector(IPathRules? rules = null)
    {
        _rules = rules ?? WindowsPathRules.Instance;
        _items = new Dictionary<string, LeftoverItem>(_rules.Comparer);
    }

    public int Count => _items.Count;

    public void Add(LeftoverItem item)
    {
        var key = item.IsFileSystem
            ? "fs|" + _rules.Normalize(item.Target)
            : $"{item.Kind}|{item.Target}";

        if (_items.TryGetValue(key, out var existing))
        {
            if (item.Confidence > existing.Confidence)
            {
                existing.Confidence = item.Confidence;
                existing.Reason = item.Reason;
            }

            return;
        }

        _items[key] = item;
    }

    public IReadOnlyList<LeftoverItem> Build(CleanupLevel level)
    {
        var items = _items.Values
            .Where(i => level == CleanupLevel.Moderate || i.Confidence == Confidence.High)
            .ToList();

        var folders = items.Where(i => i.Kind == LeftoverKind.Folder).ToList();

        // Um item dentro de uma pasta de confiança igual ou maior é redundante.
        // (Se a pasta for de confiança média e o item de alta, os dois ficam: o usuário pode desmarcar a pasta.)
        var result = items
            .Where(i => !(i.IsFileSystem && folders.Any(f =>
                !ReferenceEquals(f, i)
                && f.Confidence >= i.Confidence
                && _rules.IsStrictlyUnder(i.Target, f.Target))))
            .OrderBy(i => i.Kind)
            .ThenByDescending(i => i.Confidence)
            .ThenBy(i => i.Target, _rules.Comparer)
            .ToList();

        return result;
    }
}
