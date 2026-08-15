using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;

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

        Tema.Click += (_, _) => Applica(Scuro ? "chiaro" : "scuro", db);
        Mostra();
    }

    IReloadable? Current => (Tabs.SelectedItem as TabItem)?.Content as IReloadable;

    static bool Scuro => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>Applica il tema e se lo ricorda: un tema che si dimentica non è una scelta.</summary>
    void Applica(string tema, Db db)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = tema == "scuro" ? ThemeVariant.Dark : ThemeVariant.Light;

        db.SetSetting(Db.TemaKey, tema);
        Mostra();
    }

    void Mostra()
    {
        Luna.IsVisible = !Scuro;
        Sole.IsVisible = Scuro;
        ToolTip.SetTip(Tema, Scuro ? "Passa al tema chiaro" : "Passa al tema scuro");
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Current?.Reload();
    }
}
