using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Alexandreia;

/// <summary>
/// Import in due tempi, come deve essere: si sceglie il file, si guarda cosa ha capito
/// e solo dopo si scrive. La mappatura è correggibile a mano riga per riga.
/// </summary>
/// <summary>Riga della tabella di mappatura. <c>Field</c> è scrivibile: è la correzione manuale.</summary>
public class ColumnChoice
{
    public const string None = "(niente — finisce nelle note)";

    public required string Header { get; init; }
    public required int Filled { get; init; }
    public required string Samples { get; init; }
    public string Field { get; set; } = None;
    public IReadOnlyList<string> Options { get; } = [None, .. Import.Fields];
}

public partial class ImportView : UserControl, IReloadable
{
    const string None = ColumnChoice.None;

    readonly Db _db = null!;
    List<object?[]> _rows = [];
    string _sheet = "";
    List<ColumnChoice> _choices = [];

    public ImportView() => InitializeComponent();

    public ImportView(Db db) : this()
    {
        _db = db;

        Pick.Click += async (_, _) => await PickFile();
        Merge.IsCheckedChanged += (_, _) => Recompute();
        Apply.Click += async (_, _) => await DoImport();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void Reload() { }

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
        try
        {
            (_sheet, _rows) = Import.ReadSheet(path);
        }
        catch (Exception ex)
        {
            Fail($"Non riesco a leggere il file: {ex.Message}");
            return;
        }

        var plan = Import.Plan(_rows, _sheet, merge: Merge.IsChecked == true);
        _choices = [.. plan.Columns.Select(c => new ColumnChoice
        {
            Header = c.Header,
            Filled = c.Filled,
            Samples = string.Join("  |  ", c.Samples),
            Field = c.MappedTo ?? None,
        })];

        FileName.Text = Path.GetFileName(path);
        Grid.ItemsSource = _choices;
        Grid.IsVisible = true;
        Details.IsVisible = true;
        Actions.IsVisible = true;
        DropZone.IsVisible = false;
        Show(plan);
    }

    void Fail(string text)
    {
        Warnings.Text = text;
        Warnings.IsVisible = true;
        Grid.IsVisible = Actions.IsVisible = false;
        Details.IsVisible = true;
    }

    // --- Riepilogo, ricalcolato a ogni correzione ------------------------

    void OnFieldChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rows.Count > 0) Recompute();
    }

    void Recompute()
    {
        if (_rows.Count == 0) return;
        Show(Plan());
    }

    ImportReport Plan()
    {
        // Passo ogni scelta, anche "(niente)" come stringa vuota: altrimenti il
        // riconoscimento automatico rimetterebbe l'accoppiamento appena tolto a mano.
        var overrides = _choices
            .GroupBy(c => c.Header)
            .ToDictionary(g => g.Key, g => g.First().Field == None ? "" : g.First().Field);

        return Import.Plan(_rows, _sheet, merge: Merge.IsChecked == true, overrides);
    }

    void Show(ImportReport r)
    {
        SheetInfo.Text = $"Foglio «{r.Sheet}» — intestazione alla riga {r.HeaderRow + 1}, {r.DataRows} righe di dati";

        var parts = new List<string> { $"{r.DataRows} righe → {r.Books.Count} libri" };
        if (r.Merged > 0) parts.Add($"{r.Merged} unite in copie");
        if (r.SkippedNoTitle > 0) parts.Add($"{r.SkippedNoTitle} saltate senza titolo");
        Summary.Text = string.Join("   ·   ", parts);

        Warnings.Text = string.Join("\n", r.Warnings);
        Warnings.IsVisible = r.Warnings.Count > 0;
        Apply.IsEnabled = r.Books.Count > 0;
    }

    // --- Scrittura -------------------------------------------------------

    async Task DoImport()
    {
        var report = Plan();
        if (report.Books.Count == 0) return;

        var owner = TopLevel.GetTopLevel(this) as Window;

        if (_db.Books(limit: 1).Count > 0 && !await Dialogs.Confirm(owner,
                "In archivio ci sono già dei libri: questo import si aggiunge a quelli e può creare doppioni.\n\nProcedo lo stesso?",
                "Importa comunque"))
            return;

        var n = _db.InsertBooks(report.Books);
        Result.Text = $"Importati {n} libri.";
        Apply.IsEnabled = false;
    }
}
