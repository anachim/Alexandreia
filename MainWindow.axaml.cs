using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Alexandreia;

/// <summary>Ogni scheda si ricarica quando la si apre: i dati cambiati altrove devono comparire.</summary>
public interface IReloadable
{
    void Reload();
}

public partial class MainWindow : Window
{
    public MainWindow() : this(new Db(Db.DefaultPath())) { }

    public MainWindow(Db db)
    {
        InitializeComponent();

        var views = new Control[]
        {
            new LibriView(db),
            new PrestitiView(db),
            new MetricheView(db),
            new ImportView(db),
        };

        for (var i = 0; i < views.Length; i++)
            ((TabItem)Tabs.Items[i]!).Content = views[i];

        Tabs.SelectionChanged += (_, _) => Current?.Reload();
    }

    IReloadable? Current => (Tabs.SelectedItem as TabItem)?.Content as IReloadable;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Current?.Reload();
    }
}
