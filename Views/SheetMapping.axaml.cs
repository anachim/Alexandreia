using Avalonia.Controls;

namespace Alexandreia;

/// <summary>A row of the mapping table. <c>Field</c> is writable: it is the manual correction.</summary>
public class ColumnChoice
{
    public const string None = "(non importare)";

    public required string Header { get; init; }
    public required int Filled { get; init; }
    public required string Samples { get; init; }
    public string Field { get; set; } = None;
    public IReadOnlyList<string> Options { get; } = [None, .. Import.Fields];
}

/// <summary>
/// What a sheet holds. The user decides: guessing it from the columns works nearly always,
/// but when it misses it creates duplicate records in silence, and nobody notices.
/// </summary>
public static class SheetKinds
{
    public const string Books = "Libri";
    public const string History = "Storico dei prestiti";
    public const string Members = "Anagrafica utenti";
    public const string Skip = "Non caricare";

    public static readonly string[] All = [Books, History, Members, Skip];
}

/// <summary>
/// One sheet of the file: what it holds, the column recognition that can be corrected by
/// hand, and its summary. Every sheet has its own mapping, because the same field can go by
/// a different name from one sheet to the next — or carry a typo.
/// </summary>
public partial class SheetMapping : UserControl
{
    public Import.SheetData Sheet { get; } = new("", []);
    public ImportReport Report { get; private set; } = new();

    /// <summary>Fires on every correction: sheet type or column mapping.</summary>
    public event Action? Changed;

    List<ColumnChoice> _choices = [];
    bool _loading;

    public SheetMapping() => InitializeComponent();

    public SheetMapping(Import.SheetData sheet) : this()
    {
        Sheet = sheet;
        Nome.Text = sheet.Name;
        Tipo.ItemsSource = SheetKinds.All;

        Recompute(first: true);

        Tipo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            Recompute(first: false);
            Changed?.Invoke();
        };
    }

    public string Kind => (string?)Tipo.SelectedItem ?? SheetKinds.Skip;

    /// <summary>Changes the sheet type, the way the dropdown would.</summary>
    public void Scegli(string kind) => Tipo.SelectedItem = kind;

    /// <summary>The mapping row by row, without going through the rendered table.</summary>
    public IReadOnlyList<ColumnChoice> Choices => _choices;

    /// <summary>The message on screen, if there is one.</summary>
    public string? Messaggio => Problema.IsVisible ? Problema.Text : null;

    public bool Included => Kind != SheetKinds.Skip && !Report.Empty;

    /// <summary>Closed loans only: the open ones arrive from the books sheet.</summary>
    public int ChiusiNelloStorico => Report.Rows.Count(r => r.ReturnedAt is not null && r.HasLoan);

    void OnFieldChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Recompute(first: false);
        Changed?.Invoke();
    }

    void Recompute(bool first)
    {
        Report = Import.Plan(
            Sheet.Rows,
            Sheet.Name,
            first ? null : Overrides(),
            first ? null : Kind == SheetKinds.Members);

        _loading = true;

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

            // Our guess is only the dropdown's starting value.
            Tipo.SelectedItem =
                Report.Empty ? SheetKinds.Skip
                : Report.LooksLikeMembers ? SheetKinds.Members
                : Report.LooksLikeHistory ? SheetKinds.History
                : SheetKinds.Books;
        }
        else if (Report.Empty && Kind != SheetKinds.Skip)
        {
            Tipo.SelectedItem = SheetKinds.Skip;
        }

        _loading = false;

        var righe = Plurale(Report.DataRows, "riga", "righe");
        Riassunto.Text = Report.Empty ? "niente da caricare"
            : Kind switch
            {
                SheetKinds.Skip => "escluso",
                SheetKinds.Members => $"{righe} → {Plurale(Report.Members.Count, "persona", "persone")}",
                SheetKinds.History =>
                    $"{righe} → {Plurale(ChiusiNelloStorico, "prestito già rientrato", "prestiti già rientrati")}",
                _ => $"{righe} → {Plurale(Report.Books.Count, "libro", "libri")}"
                     + (Report.SkippedNoTitle > 0 ? $", {Report.SkippedNoTitle} senza titolo" : ""),
            };

        var messaggi = new List<string>(Report.Warnings);
        if (Report.Empty) messaggi.Insert(0, $"Da «{Sheet.Name}» non riesco a ricavare niente.");

        // The reason matters most when the sheet drops out on its own: if we exclude it and
        // say nothing, the user cannot tell whether the problem is theirs or ours. When they
        // set it to "do not load" themselves, the message is just noise.
        Problema.Text = string.Join("\n", messaggi);
        Problema.IsVisible = messaggi.Count > 0 && (Report.Empty || Kind != SheetKinds.Skip);
        Grid.IsVisible = Report.Columns.Count > 0 && Kind != SheetKinds.Skip;
    }

    static string Plurale(int n, string uno, string molti) => $"{n} {(n == 1 ? uno : molti)}";

    /// <summary>Every choice, "do not import" included as an empty string, or recognition puts it back.</summary>
    Dictionary<string, string> Overrides() => _choices
        .GroupBy(c => c.Header)
        .ToDictionary(g => g.Key, g => g.First().Field == ColumnChoice.None ? "" : g.First().Field);
}
