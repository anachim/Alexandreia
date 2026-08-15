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

        var prestiti = new PrestitiView(db);
        var metriche = new MetricheView(db);

        var views = new Control[]
        {
            new LibriView(db),
            new UtentiView(db),
            prestiti,
            metriche,
            new ImportView(db),
        };

        for (var i = 0; i < views.Length; i++)
            ((TabItem)Tabs.Items[i]!).Content = views[i];

        // Dalle schede numeriche si salta all'elenco già filtrato: un numero che non
        // porta da nessuna parte costringe a rifare la ricerca a mano.
        metriche.ApriPrestiti += filtro =>
        {
            Tabs.SelectedIndex = Array.IndexOf(views, prestiti);
            prestiti.Mostra(filtro);
        };

        Tabs.SelectionChanged += (_, _) => Current?.Reload();
    }

    IReloadable? Current => (Tabs.SelectedItem as TabItem)?.Content as IReloadable;

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Current?.Reload();
    }
}
