using Alexandreia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(Alexandreia.Tests.TestApp))]

namespace Alexandreia.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        // Disegno vero e non finto: serve a poter catturare la finestra come immagine.
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Preme davvero i bottoni della finestra. I test su Db coprono le regole,
/// questi coprono che l'interfaccia sia cablata a quelle regole.
/// </summary>
public class UiTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"alexandreia-ui-{Guid.NewGuid():N}.db");
    readonly Db _db;
    readonly long _libro;

    public UiTests()
    {
        _db = new Db(_path);
        _libro = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide", Copies = 1 });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(f);
        GC.SuppressFinalize(this);
    }

    MainWindow Open()
    {
        var window = new MainWindow(_db);
        window.Show();
        Settle();
        return window;
    }

    static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        Dispatcher.UIThread.RunJobs();
    }

    static T Named<T>(Visual root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    static Button Labelled(Visual root, string text) =>
        root.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Content == text);

    static void Click(Button b)
    {
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle();
    }

    static void Tab(MainWindow w, int index)
    {
        Named<TabControl>(w, "Tabs").SelectedIndex = index;
        Settle();
    }

    [AvaloniaFact]
    public void La_finestra_si_apre_e_mostra_i_libri()
    {
        var w = Open();

        var grid = Named<DataGrid>(w, "Grid");
        Assert.Equal("Elementi", Assert.Single(grid.ItemsSource.Cast<Book>()).Title);
    }

    [AvaloniaFact]
    public void Presto_un_libro_dall_elenco()
    {
        var w = Open();

        Click(Labelled(w, "Presta"));
        Named<TextBox>(w, "Borrower").Text = "Ipazia";
        Click(Labelled(w, "Conferma"));

        var prestito = Assert.Single(_db.Loans());
        Assert.Equal("Ipazia", prestito.Borrower);
        Assert.Equal(_libro, prestito.BookId);
        Assert.Equal(0, _db.Book(_libro)!.Available);
    }

    [AvaloniaFact]
    public void Senza_nome_il_prestito_non_parte()
    {
        var w = Open();

        Click(Labelled(w, "Presta"));
        Click(Labelled(w, "Conferma"));

        Assert.Empty(_db.Loans());
        Assert.Contains("a chi", Named<TextBlock>(w, "Message").Text!);
    }

    [AvaloniaFact]
    public void Senza_copie_libere_il_bottone_presta_e_spento()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var w = Open();

        Assert.False(Labelled(w, "Presta").IsEnabled);
    }

    [AvaloniaFact]
    public void La_ricerca_filtra_l_elenco()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        var w = Open();

        Named<TextBox>(w, "Search").Text = "tolomeo";
        Settle();

        var righe = Named<DataGrid>(w, "Grid").ItemsSource.Cast<Book>().ToList();
        Assert.Equal("Almagesto", Assert.Single(righe).Title);
    }

    [AvaloniaFact]
    public void Registro_il_rientro_dalla_scheda_prestiti()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, 1);

        Click(Labelled(w, "Rientrato"));

        Assert.NotNull(_db.Loans(openOnly: false).Single().ReturnedAt);
        Assert.Equal(1, _db.Book(_libro)!.Available);
    }

    [AvaloniaFact]
    public void Il_ritardo_si_vede_nella_scheda_prestiti()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(-3));

        var w = Open();
        Tab(w, 1);

        var loan = Named<DataGrid>(w, "Grid").ItemsSource.Cast<Loan>().Single();
        // Se questo torna falso: i DataGridTextColumn legano in TwoWay e riscrivono
        // ReturnedAt a default(DateTime), che spegne IsOpen. Servono Mode=OneWay.
        Assert.True(loan.Overdue, $"DueAt={loan.DueAt:d} ReturnedAt={loan.ReturnedAt:o}");
        Assert.Contains("in ritardo di 3 gg", loan.DueLabel);
    }

    [AvaloniaFact]
    public void Le_metriche_si_aggiornano_cambiando_scheda()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, 2);

        var testi = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Fuori ora", testi);
        Assert.Contains("Elementi", testi); // fra i più prestati

        // I numeri delle schede devono anche essere visibili: assegnare null a Foreground
        // non lascia il colore predefinito, lo azzera, e il numero sparisce senza errori.
        var numeri = Named<WrapPanel>(w, "Cards").GetVisualDescendants()
            .OfType<TextBlock>().Where(t => t.FontSize == 24).ToList();
        Assert.Equal(7, numeri.Count);
        Assert.All(numeri, n => Assert.NotNull(n.Foreground));
    }

    // --- Import ---------------------------------------------------------

    static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "catalogo.xlsx");

    [AvaloniaFact]
    public void L_import_legge_il_file_e_mostra_cosa_ha_capito()
    {
        var w = Open();
        Tab(w, 3);

        var view = w.GetVisualDescendants().OfType<ImportView>().First();
        view.Load(Fixture);
        Settle();

        var scelte = Named<DataGrid>(w, "Grid").ItemsSource.Cast<ColumnChoice>().ToList();
        Assert.Equal("Title", scelte.Single(c => c.Header == "Titolo").Field);
        Assert.Equal(ColumnChoice.None, scelte.Single(c => c.Header == "Stato conservazione").Field);
        Assert.Contains("2 libri", Named<TextBlock>(w, "Summary").Text!);
    }

    [AvaloniaFact]
    public void L_import_scrive_i_libri()
    {
        _db.ArchiveBook(_libro); // parto da archivio vuoto, così non chiede conferma

        var w = Open();
        Tab(w, 3);

        var view = w.GetVisualDescendants().OfType<ImportView>().First();
        view.Load(Fixture);
        Settle();

        Click(Named<Button>(w, "Apply"));

        Assert.Equal(["Almagesto", "Elementi"], _db.Books().Select(b => b.Title));
        Assert.Equal(2, _db.Books().Single(b => b.Title == "Elementi").Copies);
    }
}
