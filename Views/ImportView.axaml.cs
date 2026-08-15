using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Alexandreia;

/// <summary>
/// Import in due tempi: si sceglie il file, si guarda foglio per foglio cosa abbiamo capito,
/// e solo dopo si scrive. La mappatura è correggibile a mano, e ogni foglio ha la sua.
/// </summary>
public partial class ImportView : UserControl, IReloadable
{
    readonly Db _db = null!;
    readonly List<SheetMapping> _sheets = [];

    public ImportView() => InitializeComponent();

    public ImportView(Db db) : this()
    {
        _db = db;

        Pick.Click += async (_, _) => await PickFile();
        Apply.Click += async (_, _) => await DoImport();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Reload() { }

    /// <summary>I fogli che verranno importati, nell'ordine del file.</summary>
    public IEnumerable<SheetMapping> Selected => _sheets.Where(s => s.Included);

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
        Result.Text = "";
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
            FileName.Text = Path.GetFileName(path);
            Errore.Text = $"Non riesco a leggere il file: {ex.Message}";
            Errore.IsVisible = true;
            Found.IsVisible = Actions.IsVisible = false;
            return;
        }

        FileName.Text = Path.GetFileName(path);
        DropZone.IsVisible = false;

        foreach (var foglio in fogli)
        {
            var vista = new SheetMapping(foglio, soloFoglio: fogli.Count == 1);
            vista.Changed += UpdateTotal;
            _sheets.Add(vista);
            Sheets.Children.Add(vista);
        }

        // Con un foglio solo non c'è niente da annunciare: la schermata resta come prima.
        Found.IsVisible = fogli.Count > 1;
        Found.Text = $"Trovati {fogli.Count} fogli in {Path.GetFileName(path)}";
        Actions.IsVisible = true;
        UpdateTotal();
    }

    void UpdateTotal()
    {
        Result.Text = "";

        var scelti = Selected.ToList();
        var libri = scelti.Sum(s => s.Report.Books.Count);
        var righe = scelti.Sum(s => s.Report.DataRows);

        Summary.Text = _sheets.Count == 0
            ? ""
            : scelti.Count == 0
                ? "Nessun foglio da importare."
                : _sheets.Count > 1
                    ? $"{scelti.Count} {(scelti.Count == 1 ? "foglio" : "fogli")} su {_sheets.Count}: " +
                      $"{righe} righe → {libri} libri"
                    : $"{righe} righe → {libri} libri";

        Apply.IsEnabled = libri > 0;
    }

    // --- Scrittura -------------------------------------------------------

    async Task DoImport()
    {
        // Nessuna deduplica: i libri si caricano come si trovano, foglio dopo foglio.
        var libri = Selected.SelectMany(s => s.Report.Books).ToList();
        if (libri.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        if (_db.Books(limit: 1).Count > 0 && !await Dialogs.Confirm(owner,
                "In archivio ci sono già dei libri: questo import si aggiunge a quelli.\n\nProcedo lo stesso?",
                "Importa comunque"))
            return;

        var n = _db.InsertBooks(libri);
        Result.Text = $"Importati {n} libri.";
        Apply.IsEnabled = false;
    }
}
