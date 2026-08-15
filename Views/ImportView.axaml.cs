using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Alexandreia;

/// <summary>
/// Data exchange: the archive is exported to an Excel file, and the same format is loaded
/// back (or the Excel they already had). The import works in two steps — sheet by sheet you
/// look at what we understood, and only then it writes.
/// </summary>
public partial class ImportView : UserControl, IReloadable
{
    readonly Db _db = null!;
    readonly List<SheetMapping> _sheets = [];

    public ImportView() => InitializeComponent();

    public ImportView(Db db) : this()
    {
        _db = db;

        DoExport.Click += async (_, _) => await SaveExport();
        DropZone.Click += async (_, _) => await PickFile();
        // Handled: without it the click would reach the box below and reopen the file picker.
        CloseFile.Click += (_, e) => { e.Handled = true; Chiudi(); };
        Apply.Click += async (_, _) => await DoImport();
        Replace.IsCheckedChanged += (_, _) => UpdateTotal();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Reload() { }

    /// <summary>The sheets that will be loaded, in file order.</summary>
    public IEnumerable<SheetMapping> Selected => _sheets.Where(s => s.Included);

    bool Replacing => Replace.IsChecked == true;

    // --- Export ----------------------------------------------------------

    async Task SaveExport()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salva l'archivio",
            SuggestedFileName = Export.SuggestedName(DateTime.Today),
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel") { Patterns = ["*.xlsx"] }],
        });

        if (file?.TryGetLocalPath() is not { } path) return;

        try
        {
            var n = Export.Write(_db, path);
            ExportResult.Text = $"Salvati {n.Books} libri, {n.Members} utenti e {n.Loans} prestiti " +
                                $"in {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ExportResult.Text = $"Non riesco a scrivere il file: {ex.Message}";
        }
    }

    // --- Picking the file ------------------------------------------------

    async Task PickFile()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Scegli il file Excel",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Excel") { Patterns = ["*.xlsx", "*.xls"] }],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Load(path);
    }

    void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile() is { } file && file.TryGetLocalPath() is { } path)
            Load(path);
    }

    /// <summary>The sheets of the file, in the order they sit inside it.</summary>
    public IReadOnlyList<SheetMapping> Fogli => _sheets;

    public void Load(string path)
    {
        Errore.IsVisible = false;
        Sheets.Items.Clear();
        _sheets.Clear();

        List<Import.SheetData> fogli;
        try
        {
            fogli = Import.ReadWorkbook(path);
        }
        catch (Exception ex)
        {
            Caricato(path);
            Errore.Text = $"Non riesco a leggere il file: {ex.Message}";
            Errore.IsVisible = true;
            Found.IsVisible = Actions.IsVisible = ReplaceBox.IsVisible = false;
            return;
        }

        Caricato(path);

        foreach (var foglio in fogli)
        {
            var vista = new SheetMapping(foglio);
            vista.Changed += UpdateTotal;
            _sheets.Add(vista);
            Sheets.Items.Add(new TabItem { Header = foglio.Name, Content = vista });
        }
        Sheets.SelectedIndex = 0;

        // With a single sheet there is nothing to announce: the screen stays plain.
        Found.IsVisible = fogli.Count > 1;
        Found.Text = $"{fogli.Count} fogli nel file: controllali uno per uno qui sotto.";
        Actions.IsVisible = ReplaceBox.IsVisible = true;
        UpdateTotal();
    }

    // What a sheet holds is stated by the dropdown, no longer a guess about the columns.
    IEnumerable<SheetMapping> Anagrafica => Selected.Where(s => s.Kind == SheetKinds.Members);
    IEnumerable<SheetMapping> Archivio => Selected.Where(s => s.Kind == SheetKinds.Books);
    IEnumerable<SheetMapping> Storico => Selected.Where(s => s.Kind == SheetKinds.History);

    /// <summary>
    /// The box stays put afterwards too: if it vanished, there would be no way left to
    /// pick another file.
    /// </summary>
    void Caricato(string path)
    {
        DropTitle.Text = System.IO.Path.GetFileName(path);
        DropHint.Text = "Premi qui o trascina un altro file per cambiarlo.";
        CloseFile.IsVisible = true;
    }

    /// <summary>
    /// Closes the file and puts the tab back as it was. Useful above all after an import:
    /// leaving a loaded file on screen invites pressing Import a second time.
    /// </summary>
    public void Chiudi()
    {
        Sheets.Items.Clear();
        _sheets.Clear();

        DropTitle.Text = "Scegli un file Excel";
        DropHint.Text = "oppure trascinalo qui.  Accetta .xlsx e .xls";
        CloseFile.IsVisible = false;

        Replace.IsChecked = false;
        Found.IsVisible = Errore.IsVisible = Actions.IsVisible = ReplaceBox.IsVisible = false;
        Summary.Text = "";
    }

    void UpdateTotal()
    {
        var scelti = Selected.ToList();
        var libri = Archivio.Sum(s => s.Report.Rows.Count);
        var prestiti = Archivio.Sum(s => s.Report.Loans);
        var storici = Storico.Sum(s => s.ChiusiNelloStorico);
        var utenti = Anagrafica.Sum(s => s.Report.Members.Count);

        // When it deletes, the button says so by name and by colour, and the box lights up.
        Apply.Content = Replacing ? "Sostituisci tutto" : "Importa";
        Apply.Classes.Set("primary", !Replacing);
        Apply.Classes.Set("danger", Replacing);
        ReplaceBox.Classes.Set("card", !Replacing);
        ReplaceBox.Classes.Set("warning", Replacing);
        ReplaceHint.Text = Replacing
            ? "Cancella libri, utenti e storico dei prestiti, poi carica questo file. Non si torna indietro."
            : "Lascialo spento per aggiungere questi dati a quelli che ci sono già.";

        var parti = new List<string>();
        if (_sheets.Count > 1)
            parti.Add($"{scelti.Count} {(scelti.Count == 1 ? "foglio" : "fogli")} su {_sheets.Count}");
        parti.Add($"{libri} libri");
        if (utenti > 0) parti.Add($"{utenti} utenti");
        if (prestiti > 0) parti.Add($"{prestiti} già in prestito");
        if (storici > 0) parti.Add($"{storici} nello storico");

        Summary.Text = _sheets.Count == 0 ? ""
            : scelti.Count == 0 ? "Nessun foglio da caricare."
            : string.Join("   ·   ", parti);

        Apply.IsEnabled = libri > 0 || storici > 0 || utenti > 0;
    }

    // --- Writing ---------------------------------------------------------

    async Task DoImport()
    {
        var archivio = Archivio.SelectMany(s => s.Report.Rows).ToList();
        var storico = Storico.SelectMany(s => s.Report.Rows).ToList();
        var anagrafica = Anagrafica.SelectMany(s => s.Report.Members).ToList();
        if (archivio.Count == 0 && storico.Count == 0 && anagrafica.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        if (Replacing)
        {
            // The only irreversible operation in the program: it has to be asked by name.
            if (!await Dialogs.Confirm(owner,
                    "Sto per cancellare tutto l'archivio — libri, utenti e storico dei prestiti — " +
                    $"e rimetterci dentro i {archivio.Count} libri di questo file.\n\n" +
                    "Non si torna indietro. Procedo?",
                    "Sostituisci tutto"))
                return;
        }
        else if ((_db.Books(limit: 1).Count > 0 || _db.Members(limit: 1).Count > 0)
                 && !await Dialogs.Confirm(owner,
                     "In archivio ci sono già dei dati: queste righe si aggiungono a quelli.\n\nProcedo?",
                     "Aggiungi"))
        {
            return;
        }

        try
        {
            var n = _db.ApplyAll(archivio, storico, anagrafica, Replacing);

            var parti = new List<string> { $"Caricati {n.Books} libri" };
            if (n.Members > 0) parti.Add($"{n.Members} utenti");
            if (n.OpenLoans > 0) parti.Add($"{n.OpenLoans} già in prestito");
            if (n.History > 0) parti.Add($"{n.History} prestiti nello storico");
            if (n.HistorySkipped > 0)
                parti.Add($"{n.HistorySkipped} righe di storico saltate: il libro non è in archivio");

            Summary.Text = string.Join(", ", parti) + ".";
            Apply.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Errore.Text = $"Non sono riuscito a scrivere: {ex.Message}";
            Errore.IsVisible = true;
        }
    }
}
