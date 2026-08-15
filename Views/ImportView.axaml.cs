using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Alexandreia;

/// <summary>
/// Scambio dati: si esporta l'archivio in un Excel, e si ricarica lo stesso formato
/// (o l'Excel che avevano già loro). L'import lavora in due tempi — si guarda foglio per
/// foglio cosa abbiamo capito, e solo dopo si scrive.
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
        Pick.Click += async (_, _) => await PickFile();
        Apply.Click += async (_, _) => await DoImport();
        Replace.IsCheckedChanged += (_, _) => UpdateTotal();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Reload() { }

    /// <summary>I fogli che verranno caricati, nell'ordine del file.</summary>
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
            ExportResult.Text = $"Esportati {n} libri in {System.IO.Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ExportResult.Text = $"Non riesco a scrivere il file: {ex.Message}";
        }
    }

    // --- Scelta del file -------------------------------------------------

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

    public void Load(string path)
    {
        Errore.IsVisible = false;
        Sheets.Children.Clear();
        _sheets.Clear();

        List<Import.SheetData> fogli;
        try
        {
            fogli = Import.ReadWorkbook(path);
        }
        catch (Exception ex)
        {
            FileName.Text = System.IO.Path.GetFileName(path);
            Errore.Text = $"Non riesco a leggere il file: {ex.Message}";
            Errore.IsVisible = true;
            Found.IsVisible = Actions.IsVisible = false;
            return;
        }

        FileName.Text = System.IO.Path.GetFileName(path);
        DropZone.IsVisible = false;

        foreach (var foglio in fogli)
        {
            var vista = new SheetMapping(foglio, soloFoglio: fogli.Count == 1);
            vista.Changed += UpdateTotal;
            _sheets.Add(vista);
            Sheets.Children.Add(vista);
        }

        // Con un foglio solo non c'è niente da annunciare: la schermata resta semplice.
        Found.IsVisible = fogli.Count > 1;
        Found.Text = $"Trovati {fogli.Count} fogli in {System.IO.Path.GetFileName(path)}";
        Actions.IsVisible = true;
        UpdateTotal();
    }

    void UpdateTotal()
    {
        var scelti = Selected.ToList();
        var libri = scelti.Sum(s => s.Report.Rows.Count);
        var prestiti = scelti.Sum(s => s.Report.Loans);
        var righe = scelti.Sum(s => s.Report.DataRows);

        Apply.Content = Replacing ? "Sostituisci" : "Importa";

        var parti = new List<string>();
        if (_sheets.Count > 1)
            parti.Add($"{scelti.Count} {(scelti.Count == 1 ? "foglio" : "fogli")} su {_sheets.Count}");
        parti.Add($"{righe} righe → {libri} libri");
        if (prestiti > 0) parti.Add($"{prestiti} già in prestito");

        Summary.Text = _sheets.Count == 0 ? ""
            : scelti.Count == 0 ? "Nessun foglio da caricare."
            : string.Join("   ·   ", parti);

        Apply.IsEnabled = libri > 0;
    }

    // --- Scrittura -------------------------------------------------------

    async Task DoImport()
    {
        var righe = Selected.SelectMany(s => s.Report.Rows).ToList();
        if (righe.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        var quanti = _db.Books(limit: 1).Count > 0 || _db.Members(limit: 1).Count > 0;

        if (Replacing)
        {
            // La sola operazione irreversibile del programma: va chiesta per nome.
            if (!await Dialogs.Confirm(owner,
                    $"Sto per cancellare tutto l'archivio — libri, utenti e storico dei prestiti — " +
                    $"e rimetterci dentro le {righe.Count} righe di questo file.\n\n" +
                    "Non si torna indietro. Procedo?",
                    "Sostituisci tutto"))
                return;
        }
        else if (quanti && !await Dialogs.Confirm(owner,
                     "In archivio ci sono già dei dati: queste righe si aggiungono a quelli.\n\nProcedo?",
                     "Aggiungi"))
        {
            return;
        }

        try
        {
            var n = _db.Apply(righe, Replacing);
            var prestiti = righe.Count(r => r.HasLoan);
            Summary.Text = $"Caricati {n} libri" + (prestiti > 0 ? $", di cui {prestiti} già in prestito." : ".");
            Apply.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Errore.Text = $"Non sono riuscito a scrivere: {ex.Message}";
            Errore.IsVisible = true;
        }
    }
}
