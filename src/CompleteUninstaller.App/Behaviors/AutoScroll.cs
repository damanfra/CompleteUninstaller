using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CompleteUninstaller.App.Behaviors;

/// <summary>
/// Mantém uma lista rolada até o último item: quando um item é adicionado e quando ela fica visível
/// (por exemplo, ao trocar de etapa no assistente). Uso: <c>behaviors:AutoScroll.ToEnd="True"</c>.
/// </summary>
public static class AutoScroll
{
    public static readonly DependencyProperty ToEndProperty = DependencyProperty.RegisterAttached(
        "ToEnd", typeof(bool), typeof(AutoScroll), new PropertyMetadata(false, OnToEndChanged));

    public static bool GetToEnd(DependencyObject element) => (bool)element.GetValue(ToEndProperty);

    public static void SetToEnd(DependencyObject element, bool value) => element.SetValue(ToEndProperty, value);

    private static void OnToEndChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list)
        {
            return;
        }

        // ItemCollection é a mesma instância durante toda a vida do controle, mesmo se o ItemsSource mudar.
        var items = (INotifyCollectionChanged)list.Items;
        if ((bool)e.NewValue)
        {
            items.CollectionChanged += (_, args) =>
            {
                if (GetToEnd(list) && args.Action == NotifyCollectionChangedAction.Add)
                {
                    ScrollToEnd(list);
                }
            };
            list.IsVisibleChanged += OnIsVisibleChanged;
            list.Loaded += OnLoaded;
        }
        else
        {
            list.IsVisibleChanged -= OnIsVisibleChanged;
            list.Loaded -= OnLoaded;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            ScrollToEnd((ItemsControl)sender);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => ScrollToEnd((ItemsControl)sender);

    private static void ScrollToEnd(ItemsControl list)
    {
        // Espera o layout: o item novo (ou a lista que acabou de aparecer) ainda não foi medido.
        list.Dispatcher.InvokeAsync(() =>
        {
            if (list.IsVisible && FindScrollViewer(list) is { } viewer)
            {
                viewer.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
