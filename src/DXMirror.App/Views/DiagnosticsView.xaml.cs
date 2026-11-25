using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace DXMirror.App.Views;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (LogList.ItemsSource is INotifyCollectionChanged incc)
            {
                incc.CollectionChanged -= ScrollToEnd;
                incc.CollectionChanged += ScrollToEnd;
            }
        };
    }

    private void ScrollToEnd(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!IsVisible || e.Action != NotifyCollectionChangedAction.Add || LogList.Items.Count == 0) return;
        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
