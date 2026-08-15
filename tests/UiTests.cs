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
    const int TabLibri = 0, TabUtenti = 1, TabPrestiti = 2, TabMetriche = 3, TabDati = 4;

    readonly string _path = Path.Combine(Path.GetTempPath(), $"alexandreia-ui-{Guid.NewGuid():N}.db");
    readonly Db _db;
    readonly long _libro;
    readonly long _ipazia;

    public UiTests()
    {
        _db = new Db(_path);
        _libro = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide" });
        _ipazia = _db.SaveMember(new Member { LastName = "Ipazia" });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(f);
        GC.SuppressFinalize(this);
    }

    // --- Appoggio -------------------------------------------------------

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

    static ImportView Carica(MainWindow w, string fixture)
    {
        var view = w.GetVisualDescendants().OfType<ImportView>().First();
        view.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture));
        Settle();
        return view;
    }

    // --- Libri ----------------------------------------------------------

    [AvaloniaFact]
    public void La_finestra_si_apre_e_mostra_i_libri()
    {
        var w = Open();

        var grid = Named<DataGrid>(w, "Grid");
        Assert.Equal("Elementi", Assert.Single(grid.ItemsSource!.Cast<Book>()).Title);
    }

    [AvaloniaFact]
    public void Presto_un_libro_scegliendo_la_persona()
    {
        var w = Open();

        Click(Labelled(w, "Presta"));
        Named<ComboBox>(w, "Person").SelectedIndex = 0;
        Settle();
        Click(Labelled(w, "Conferma"));

        var prestito = Assert.Single(_db.Loans());
        Assert.Equal(_libro, prestito.BookId);
        Assert.Equal("Ipazia", prestito.MemberName);
        Assert.False(_db.Book(_libro)!.IsAvailable);
    }

    [AvaloniaFact]
    public void Senza_scegliere_la_persona_il_prestito_non_parte()
    {
        var w = Open();

        Click(Labelled(w, "Presta"));
        Click(Labelled(w, "Conferma"));

        Assert.Empty(_db.Loans());
        Assert.Contains("Scegli a chi", Named<TextBlock>(w, "Message").Text!);
    }

    [AvaloniaFact]
    public void Senza_utenti_lo_dice_invece_di_aprire_il_pannello()
    {
        _db.ArchiveMember(_ipazia);
        var w = Open();

        Click(Labelled(w, "Presta"));

        Assert.False(Named<Border>(w, "LendPanel").IsVisible);
        Assert.Contains("nessun utente", Named<TextBlock>(w, "Message").Text!);
    }

    [AvaloniaFact]
    public void Un_libro_gia_fuori_non_si_ripresta()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
        var w = Open();

        Assert.False(Labelled(w, "Presta").IsEnabled);
        Assert.Equal("Ipazia", Named<DataGrid>(w, "Grid").ItemsSource!.Cast<Book>().Single().LentTo);
    }

    [AvaloniaFact]
    public void La_ricerca_filtra_l_elenco()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        var w = Open();

        Named<TextBox>(w, "Search").Text = "tolomeo";
        Settle();

        var righe = Named<DataGrid>(w, "Grid").ItemsSource!.Cast<Book>().ToList();
        Assert.Equal("Almagesto", Assert.Single(righe).Title);
    }

    // --- Utenti ---------------------------------------------------------

    [AvaloniaFact]
    public void La_scheda_utenti_mostra_chi_ha_libri_fuori()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, TabUtenti);

        var utente = Assert.Single(Named<DataGrid>(w, "Grid").ItemsSource!.Cast<Member>());
        Assert.Equal("Ipazia", utente.LastName);
        Assert.Equal(1, utente.OpenLoans);
    }

    // --- Prestiti -------------------------------------------------------

    [AvaloniaFact]
    public void Registro_il_rientro_dalla_scheda_prestiti()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, TabPrestiti);

        Click(Labelled(w, "Rientrato"));

        Assert.NotNull(_db.Loans(openOnly: false).Single().ReturnedAt);
        Assert.True(_db.Book(_libro)!.IsAvailable);
    }

    [AvaloniaFact]
    public void Il_ritardo_si_vede_nella_scheda_prestiti()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));

        var w = Open();
        Tab(w, TabPrestiti);

        var view = w.GetVisualDescendants().OfType<PrestitiView>().First();
        var loan = Named<DataGrid>(view, "Grid").ItemsSource!.Cast<Loan>().Single();

        // Se questo torna falso: i DataGridTextColumn legano in TwoWay e riscrivono
        // ReturnedAt a default(DateTime), che spegne IsOpen. Servono Mode=OneWay.
        Assert.True(loan.Overdue, $"DueAt={loan.DueAt:d} ReturnedAt={loan.ReturnedAt:o}");
        Assert.Contains("in ritardo di 3 gg", loan.DueLabel);
    }

    // --- Metriche -------------------------------------------------------

    [AvaloniaFact]
    public void Le_metriche_si_aggiornano_cambiando_scheda()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, TabMetriche);

        var testi = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Fuori ora", testi);
        Assert.Contains("Elementi", testi);

        // I numeri devono anche essere visibili: assegnare null a Foreground non lascia
        // il colore predefinito, lo azzera, e il numero sparisce senza errori.
        var numeri = Named<WrapPanel>(w, "Cards").GetVisualDescendants()
            .OfType<TextBlock>().Where(t => t.FontSize == 24).ToList();
        Assert.Equal(7, numeri.Count);
        Assert.All(numeri, n => Assert.NotNull(n.Foreground));
    }

    // --- Dati: import ----------------------------------------------------

    [AvaloniaFact]
    public void L_import_legge_il_file_e_mostra_cosa_ha_capito()
    {
        var w = Open();
        Tab(w, TabDati);
        Carica(w, "catalogo.xlsx");

        var foglio = Assert.Single(w.GetVisualDescendants().OfType<SheetMapping>());
        var scelte = Named<DataGrid>(foglio, "Grid").ItemsSource!.Cast<ColumnChoice>().ToList();

        Assert.Equal(Import.FTitle, scelte.Single(c => c.Header == "Titolo").Field);
        Assert.Equal(Import.FPerson, scelte.Single(c => c.Header == "Prestato a").Field);
        Assert.Equal(ColumnChoice.None, scelte.Single(c => c.Header == "Stato conservazione").Field);

        var riepilogo = Named<TextBlock>(w, "Summary").Text!;
        Assert.Contains("3 libri", riepilogo);
        Assert.Contains("2 già in prestito", riepilogo);
    }

    [AvaloniaFact]
    public void Con_un_foglio_solo_niente_intestazioni_di_foglio()
    {
        var w = Open();
        Tab(w, TabDati);
        Carica(w, "catalogo.xlsx");

        var foglio = Assert.Single(w.GetVisualDescendants().OfType<SheetMapping>());
        Assert.False(Named<TextBlock>(w, "Found").IsVisible);
        Assert.False(Named<DockPanel>(foglio, "Head").IsVisible);
    }

    [AvaloniaFact]
    public void Con_piu_fogli_ognuno_ha_la_sua_mappatura()
    {
        var w = Open();
        Tab(w, TabDati);
        Carica(w, "multifoglio.xlsx");

        var fogli = w.GetVisualDescendants().OfType<SheetMapping>().ToList();
        Assert.Equal(["Appunti", "Catalogo"], fogli.Select(f => f.Sheet.Name));
        Assert.True(Named<TextBlock>(w, "Found").IsVisible);

        // Da «Appunti» non si ricava niente, e va detto invece che sparire in silenzio.
        Assert.True(fogli[0].Report.Empty);
        Assert.False(fogli[0].Included);
        Assert.Contains("non riesco a ricavare niente", Named<TextBlock>(fogli[0], "Problema").Text!);

        // Nel secondo foglio il titolo si chiama «Libro» e l'autore ha un'intestazione
        // che nessuna lista indovinerà mai: è il motivo della mappatura per foglio.
        Assert.True(fogli[1].Included);
        var scelte = Named<DataGrid>(fogli[1], "Grid").ItemsSource!.Cast<ColumnChoice>().ToList();
        Assert.Equal(Import.FTitle, scelte.Single(c => c.Header == "Libro").Field);
        Assert.Equal(ColumnChoice.None, scelte.Single(c => c.Header == "Chi lo ha scritto").Field);
    }

    [AvaloniaFact]
    public void Il_riepilogo_non_resta_tagliato_quando_si_allunga()
    {
        var w = Open();
        Tab(w, TabDati);

        Carica(w, "multifoglio.xlsx");
        Carica(w, "catalogo.xlsx");
        w.UpdateLayout();

        // Uno StackPanel orizzontale lo lasciava disposto con la larghezza del testo
        // precedente: misurato giusto, tagliato a video.
        var s = Named<TextBlock>(w, "Summary");
        Assert.True(s.Bounds.Width >= s.DesiredSize.Width,
            $"tagliato: disposto {s.Bounds.Width:0}, ne servono {s.DesiredSize.Width:0} per «{s.Text}»");
    }

    // --- Dati: export ----------------------------------------------------

    [AvaloniaFact]
    public void L_export_scrive_un_file_che_sappiamo_rileggere()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var file = Path.Combine(Path.GetTempPath(), $"alexandreia-ui-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            Assert.Equal(1, Export.Write(_db, file).Books);

            var w = Open();
            Tab(w, TabDati);
            w.GetVisualDescendants().OfType<ImportView>().First().Load(file);
            Settle();

            var riepilogo = Named<TextBlock>(w, "Summary").Text!;
            Assert.Contains("1 libri", riepilogo);
            Assert.Contains("1 già in prestito", riepilogo);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
