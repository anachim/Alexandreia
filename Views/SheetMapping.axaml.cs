using Avalonia.Controls;

namespace Alexandreia;

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

/// <summary>
/// Un foglio del file: il suo riconoscimento colonne, correggibile a mano, e se importarlo.
/// Ogni foglio ha la sua mappatura, perché lo stesso campo può chiamarsi diversamente
/// da un foglio all'altro — o essere scritto con un errore di battitura.
/// </summary>
public partial class SheetMapping : UserControl
{
    public Import.SheetData Sheet { get; } = new("", []);
    public ImportReport Report { get; private set; } = new();

    /// <summary>Scatta a ogni correzione della mappatura o della casella di inclusione.</summary>
    public event Action? Changed;

    List<ColumnChoice> _choices = [];
    bool _loading;

    public SheetMapping() => InitializeComponent();

    public SheetMapping(Import.SheetData sheet, bool soloFoglio) : this()
    {
        Sheet = sheet;
        Head.IsVisible = !soloFoglio; // con un foglio solo, niente intestazioni inutili
        Nome.Text = sheet.Name;

        Recompute(first: true);

        Includi.IsCheckedChanged += (_, _) => { if (!_loading) Changed?.Invoke(); };
    }

    public bool Included => Includi.IsChecked == true && !Report.Empty;

    void OnFieldChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Recompute(first: false);
        Changed?.Invoke();
    }

    void Recompute(bool first)
    {
        var eraVuoto = !first && Report.Empty;

        Report = Import.Plan(Sheet.Rows, Sheet.Name, first ? null : Overrides());

        if (first)
        {
            _choices = [.. Report.Columns.Select(c => new ColumnChoice
            {
                Header = c.Header,
                Filled = c.Filled,
                Samples = string.Join("  |  ", c.Samples),
                Field = c.MappedTo ?? ColumnChoice.None,
            })];
            Grid.ItemsSource = _choices;
        }

        _loading = true;
        Includi.IsEnabled = !Report.Empty;
        // Auto-spunta solo quando un foglio prima inutilizzabile diventa buono grazie a una
        // correzione: se l'utente l'ha tolta lui, non gliela rimetto.
        if (Report.Empty) Includi.IsChecked = false;
        else if (first || eraVuoto) Includi.IsChecked = true;
        _loading = false;

        Riassunto.Text = Report.Empty
            ? "niente da caricare"
            : $"{Report.DataRows} righe → {Report.Books.Count} libri"
              + (Report.SkippedNoTitle > 0 ? $", {Report.SkippedNoTitle} senza titolo" : "");

        var messaggi = new List<string>(Report.Warnings);
        if (Report.Empty)
            messaggi.Insert(0, $"Da «{Sheet.Name}» non riesco a ricavare niente.");

        Problema.Text = string.Join("\n", messaggi);
        Problema.IsVisible = messaggi.Count > 0;
        Grid.IsVisible = Report.Columns.Count > 0;
    }

    /// <summary>Ogni scelta, anche «(niente)» come stringa vuota, sennò il riconoscimento la rimette.</summary>
    Dictionary<string, string> Overrides() => _choices
        .GroupBy(c => c.Header)
        .ToDictionary(g => g.Key, g => g.First().Field == ColumnChoice.None ? "" : g.First().Field);
}
