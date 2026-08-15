using Alexandreia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

[assembly: AvaloniaTestApplication(typeof(Alexandreia.Tests.TestApp))]

namespace Alexandreia.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        // Real drawing rather than stubbed: it is what allows capturing the window as an image.
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Actually presses the buttons of the window. The Db tests cover the rules,
/// these cover that the interface is wired to those rules.
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

    // --- Helpers --------------------------------------------------------

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

    // --- Theme ----------------------------------------------------------

    [AvaloniaFact]
    public void Il_tema_si_cambia_e_se_lo_ricorda()
    {
        var w = Open();
        var bottone = Named<Button>(w, "Tema");

        // The icon shows where you are going, not where you are: in light you see the moon.
        Assert.True(Named<Avalonia.Controls.Shapes.Path>(w, "Luna").IsVisible);
        Assert.False(Named<Avalonia.Controls.Shapes.Path>(w, "Sole").IsVisible);
        Assert.Equal("Passa al tema scuro", ToolTip.GetTip(bottone));

        Click(bottone);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.ActualThemeVariant);
        Assert.True(Named<Avalonia.Controls.Shapes.Path>(w, "Sole").IsVisible);
        Assert.False(Named<Avalonia.Controls.Shapes.Path>(w, "Luna").IsVisible);
        Assert.Equal("Passa al tema chiaro", ToolTip.GetTip(bottone));
        Assert.Equal("scuro", _db.Setting(Db.TemaKey));

        Click(bottone);
        Assert.Equal(ThemeVariant.Light, Avalonia.Application.Current!.ActualThemeVariant);
        Assert.True(Named<Avalonia.Controls.Shapes.Path>(w, "Luna").IsVisible);
        Assert.Equal("chiaro", _db.Setting(Db.TemaKey));
    }

    [AvaloniaFact]
    public void I_colori_delle_schede_seguono_il_tema()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-2));

        var w = Open();
        Tab(w, TabMetriche);

        var numero = Named<WrapPanel>(w, "Now").GetVisualDescendants()
            .OfType<TextBlock>().First(t => t.FontSize == 26);
        var chiaro = numero.Foreground;

        Click(Named<Button>(w, "Tema"));

        // The cards are built by hand: with a fixed colour they would stay in the theme
        // they were born in instead of following the new one.
        Assert.NotEqual(chiaro, numero.Foreground);
    }

    // --- Books ----------------------------------------------------------

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

    // --- Members --------------------------------------------------------

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

    // --- Loans ----------------------------------------------------------

    [AvaloniaFact]
    public void Registro_il_rientro_dalla_scheda_prestiti()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, TabPrestiti);

        Click(Labelled(w, "Rientrato"));

        Assert.NotNull(_db.Loans(Filtri.Tutti).Single().ReturnedAt);
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

        // If this comes back false: DataGridTextColumn binds TwoWay and writes ReturnedAt
        // back as default(DateTime), which turns IsOpen off. Mode=OneWay is required.
        Assert.True(loan.Overdue, $"DueAt={loan.DueAt:d} ReturnedAt={loan.ReturnedAt:o}");
        Assert.Equal("In ritardo di 3 giorni", loan.Stato);

        // The state is spelled out in its own column, not left to a symbol.
        var stato = Named<DataGrid>(view, "Grid").GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("pill"));
        Assert.True(stato.Classes.Contains("late"));
        Assert.Equal("In ritardo di 3 giorni",
            stato.GetVisualDescendants().OfType<TextBlock>().Single().Text);
    }

    [AvaloniaFact]
    public void Lo_stato_distingue_in_regola_ritardo_e_rientrato()
    {
        var b = _db.SaveBook(new Book { Title = "Almagesto" });
        var c = _db.SaveBook(new Book { Title = "Coniche" });
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));   // in ritardo
        _db.Lend(b, _ipazia, DateTime.Today.AddDays(10));        // in regola
        _db.Lend(c, _ipazia, DateTime.Today.AddDays(10));
        _db.Return(_db.Loans().Single(l => l.BookId == c).Id);   // rientrato

        var w = Open();
        Tab(w, TabPrestiti);
        var view = w.GetVisualDescendants().OfType<PrestitiView>().First();
        view.Mostra(Filtri.Tutti);
        Settle();

        Assert.Equal(
            ["In ritardo di 3 giorni", "In regola", $"Rientrato il {DateTime.Today:dd/MM/yyyy}"],
            Named<DataGrid>(view, "Grid").ItemsSource!.Cast<Loan>().Select(l => l.Stato));
    }

    [AvaloniaFact]
    public void Prolungo_un_prestito_dalla_scheda()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));

        var w = Open();
        Tab(w, TabPrestiti);

        Click(Labelled(w, "Prolunga"));
        Named<CalendarDatePicker>(w, "NewDue").SelectedDate = DateTime.Today.AddDays(20);
        Settle();
        Click(Labelled(w, "Conferma"));

        var prestito = _db.Loans().Single();
        Assert.Equal(DateTime.Today.AddDays(20), prestito.DueAt);
        Assert.False(prestito.Overdue);
    }

    [AvaloniaFact]
    public void Il_filtro_dei_prestiti_cambia_l_elenco()
    {
        var secondo = _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));   // in ritardo
        _db.Lend(secondo, _ipazia, DateTime.Today.AddDays(10));  // regolare

        var w = Open();
        Tab(w, TabPrestiti);
        var view = w.GetVisualDescendants().OfType<PrestitiView>().First();

        Assert.Equal(2, Named<DataGrid>(view, "Grid").ItemsSource!.Cast<Loan>().Count());

        view.Mostra(Filtri.Ritardo);
        Settle();

        Assert.Equal("Elementi", Assert.Single(Named<DataGrid>(view, "Grid").ItemsSource!.Cast<Loan>()).Title);
        Assert.Equal("1 prestito", Named<TextBlock>(view, "Conteggio").Text);
    }

    [AvaloniaFact]
    public void La_scheda_In_ritardo_porta_all_elenco_gia_filtrato()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));

        var w = Open();
        Tab(w, TabMetriche);

        // The stat card is clickable: a number that leads nowhere forces the user to redo
        // the search by hand.
        var card = Named<WrapPanel>(w, "Now").GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("clickable")
                        && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "In ritardo"));
        card.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            card, new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, true),
            card, default, 0, new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None));
        Settle();

        Assert.Equal(TabPrestiti, Named<TabControl>(w, "Tabs").SelectedIndex);
        var view = w.GetVisualDescendants().OfType<PrestitiView>().First();
        Assert.Equal("Elementi", Assert.Single(Named<DataGrid>(view, "Grid").ItemsSource!.Cast<Loan>()).Title);
    }

    // --- Metrics --------------------------------------------------------

    [AvaloniaFact]
    public void Le_metriche_si_aggiornano_cambiando_scheda()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var w = Open();
        Tab(w, TabMetriche);

        var testi = w.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Fuori adesso", testi);
        Assert.Contains("Elementi", testi);

        // The numbers also have to be visible: assigning null to Foreground does not leave
        // the default colour, it clears it, and the number vanishes without any error.
        var numeri = new[] { "Now", "Cards" }
            .SelectMany(p => Named<WrapPanel>(w, p).GetVisualDescendants().OfType<TextBlock>())
            .Where(t => t.FontSize == 26).ToList();
        Assert.Equal(9, numeri.Count);
        Assert.All(numeri, n => Assert.NotNull(n.Foreground));

        // The average counts the months with activity, not the twelve of the period: on a
        // freshly started archive, one loan in the first month is 1, not 0.1.
        Assert.Equal("1", Valore(w, "Media al mese"));
        Assert.Equal("1", Valore(w, "Fuori adesso"));
    }

    /// <summary>
    /// The big number inside the card carrying that label. Searched inside the card panels
    /// and not the whole window: "Prestiti" is also the name of a tab.
    /// </summary>
    static string? Valore(MainWindow w, string etichetta) =>
        new[] { "Now", "Cards" }
            .SelectMany(p => Named<WrapPanel>(w, p).GetVisualDescendants().OfType<StackPanel>())
            .First(sp => sp.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == etichetta))
            .GetVisualDescendants().OfType<TextBlock>().First().Text;

    [AvaloniaFact]
    public void Il_periodo_delle_metriche_si_puo_restringere()
    {
        _db.Apply(Import.Plan([
            new object?[] { "Titolo", "Prestato a", "Prestato il" },
            new object?[] { "Vecchio", "Ipazia", DateTime.Today.AddMonths(-8) },
            new object?[] { "Recente", "Ipazia", DateTime.Today.AddDays(-2) },
        ]).Rows);

        var w = Open();
        Tab(w, TabMetriche);
        Assert.Equal("2", Valore(w, "Prestiti")); // ultimi 12 mesi

        Named<ComboBox>(w, "Period").SelectedIndex = 0; // ultimi 30 giorni
        Settle();

        Assert.Equal("1", Valore(w, "Prestiti"));
    }

    // --- Data tab: import -------------------------------------------------

    [AvaloniaFact]
    public void L_import_legge_il_file_e_mostra_cosa_ha_capito()
    {
        var w = Open();
        Tab(w, TabDati);

        var foglio = Assert.Single(Carica(w, "catalogo.xlsx").Fogli);
        var scelte = foglio.Choices;

        Assert.Equal(Import.FTitle, scelte.Single(c => c.Header == "Titolo").Field);
        Assert.Equal(Import.FPerson, scelte.Single(c => c.Header == "Prestato a").Field);
        Assert.Equal(ColumnChoice.None, scelte.Single(c => c.Header == "Stato conservazione").Field);

        var riepilogo = Named<TextBlock>(w, "Summary").Text!;
        Assert.Contains("3 libri", riepilogo);
        Assert.Contains("2 già in prestito", riepilogo);
    }

    [AvaloniaFact]
    public void Il_riquadro_del_file_resta_e_dice_quale_file_e_caricato()
    {
        var w = Open();
        Tab(w, TabDati);

        var zona = Named<Button>(w, "DropZone");
        Assert.Equal("Scegli un file Excel", Named<TextBlock>(zona, "DropTitle").Text);

        Carica(w, "catalogo.xlsx");

        // It does not vanish: without it there would be no way left to pick another file.
        Assert.True(zona.IsVisible);
        Assert.Equal("catalogo.xlsx", Named<TextBlock>(zona, "DropTitle").Text);
        Assert.Contains("cambiarlo", Named<TextBlock>(zona, "DropHint").Text!);
    }

    [AvaloniaFact]
    public void Chiudendo_il_file_la_scheda_torna_come_prima()
    {
        var w = Open();
        Tab(w, TabDati);
        var view = Carica(w, "catalogo.xlsx");

        Assert.NotEmpty(view.Fogli);
        Assert.True(Named<Button>(w, "CloseFile").IsVisible);

        Click(Named<Button>(w, "CloseFile"));

        Assert.Empty(view.Fogli);
        Assert.Equal("Scegli un file Excel", Named<TextBlock>(w, "DropTitle").Text);
        Assert.False(Named<Button>(w, "CloseFile").IsVisible);
        Assert.False(Named<Border>(w, "Actions").IsVisible);
        Assert.False(Named<Border>(w, "ReplaceBox").IsVisible);
        Assert.False(Named<CheckBox>(w, "Replace").IsChecked);
    }

    [AvaloniaFact]
    public void Il_tipo_del_foglio_lo_dice_una_tendina_gia_preselezionata()
    {
        var w = Open();
        Tab(w, TabDati);

        var foglio = Assert.Single(Carica(w, "catalogo.xlsx").Fogli);
        Assert.False(Named<TextBlock>(w, "Found").IsVisible);

        // The dropdown is always there, even with a single sheet: that is where a wrong
        // guess gets corrected, and guessing in silence creates duplicate records.
        Assert.Equal(SheetKinds.Books, foglio.Kind);
        Assert.True(foglio.Included);
    }

    [AvaloniaFact]
    public void Mettendo_un_foglio_su_non_caricare_esce_dal_conteggio()
    {
        var w = Open();
        Tab(w, TabDati);

        var foglio = Assert.Single(Carica(w, "catalogo.xlsx").Fogli);
        foglio.Scegli(SheetKinds.Skip);
        Settle();

        Assert.False(foglio.Included);
        Assert.Equal("Nessun foglio da caricare.", Named<TextBlock>(w, "Summary").Text);
        Assert.False(Named<Button>(w, "Apply").IsEnabled);
    }

    [AvaloniaFact]
    public void Con_piu_fogli_ognuno_ha_la_sua_mappatura()
    {
        var w = Open();
        Tab(w, TabDati);

        // From the list and not from the visual tree: with one tab per sheet, only the
        // selected one is rendered.
        var fogli = Carica(w, "multifoglio.xlsx").Fogli;
        Assert.Equal(["Appunti", "Catalogo"], fogli.Select(f => f.Sheet.Name));
        Assert.True(Named<TextBlock>(w, "Found").IsVisible);

        // Nothing can be drawn from "Appunti": it drops out on its own, but says why.
        Assert.True(fogli[0].Report.Empty);
        Assert.False(fogli[0].Included);
        Assert.Equal(SheetKinds.Skip, fogli[0].Kind);
        Assert.Contains("non riesco a ricavare niente", fogli[0].Messaggio!);

        // In the second sheet the title is called "Libro" and the author carries a header
        // no list will ever guess: that is the reason for per-sheet mapping.
        Assert.True(fogli[1].Included);
        Assert.Equal(Import.FTitle, fogli[1].Choices.Single(c => c.Header == "Libro").Field);
        Assert.Equal(ColumnChoice.None, fogli[1].Choices.Single(c => c.Header == "Chi lo ha scritto").Field);
    }

    [AvaloniaFact]
    public void Il_riepilogo_non_resta_tagliato_quando_si_allunga()
    {
        var w = Open();
        Tab(w, TabDati);

        Carica(w, "multifoglio.xlsx");
        Carica(w, "catalogo.xlsx");
        w.UpdateLayout();

        // A horizontal StackPanel used to leave it arranged at the previous text's width:
        // measured right, cut off on screen.
        var s = Named<TextBlock>(w, "Summary");
        Assert.True(s.Bounds.Width >= s.DesiredSize.Width,
            $"tagliato: disposto {s.Bounds.Width:0}, ne servono {s.DesiredSize.Width:0} per «{s.Text}»");
    }

    // --- Data tab: export -------------------------------------------------

    [AvaloniaFact]
    public void L_export_scrive_un_file_che_sappiamo_rileggere()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        var file = Path.Combine(Path.GetTempPath(), $"alexandreia-ui-export-{Guid.NewGuid():N}.xlsx");
        try
        {
            var scritto = Export.Write(_db, file);
            Assert.Equal(1, scritto.Books);
            Assert.Equal(1, scritto.Members);

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
