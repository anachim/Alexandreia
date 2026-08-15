using Avalonia.Controls;

namespace Alexandreia;

/// <summary>Riga della tabella di mappatura. <c>Field</c> è scrivibile: è la correzione manuale.</summary>
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
/// Che cosa c'è in un foglio. Lo decide l'utente: indovinarlo dalle colonne funziona quasi
/// sempre, ma quando sbaglia crea schede doppie in silenzio, e nessuno se ne accorge.
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
/// Un foglio del file: che cosa contiene, il riconoscimento delle colonne correggibile a mano,
/// e il suo riepilogo. Ogni foglio ha la sua mappatura, perché lo stesso campo può chiamarsi
/// diversamente da un foglio all'altro — o essere scritto con un errore di battitura.
/// </summary>
public partial class SheetMapping : UserControl
{
    public Import.SheetData Sheet { get; } = new("", []);
    public ImportReport Report { get; private set; } = new();

    /// <summary>Scatta a ogni correzione: tipo del foglio o mappatura di una colonna.</summary>
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

    /// <summary>Cambia il tipo del foglio, come farebbe la tendina.</summary>
    public void Scegli(string kind) => Tipo.SelectedItem = kind;

    /// <summary>La mappatura riga per riga, senza passare dalla tabella disegnata.</summary>
    public IReadOnlyList<ColumnChoice> Choices => _choices;

    /// <summary>Il messaggio mostrato, se ce n'è uno.</summary>
    public string? Messaggio => Problema.IsVisible ? Problema.Text : null;

    public bool Included => Kind != SheetKinds.Skip && !Report.Empty;

    /// <summary>Solo i prestiti chiusi: quelli aperti arrivano dal foglio dei libri.</summary>
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

            // La nostra ipotesi è solo il valore di partenza della tendina.
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

        // Il perché va detto soprattutto quando il foglio resta fuori da solo: se lo escludiamo
        // noi e tacciamo, l'utente non sa se è un problema suo o nostro. Se invece è stato lui a
        // metterlo su «Non caricare», il messaggio è rumore.
        Problema.Text = string.Join("\n", messaggi);
        Problema.IsVisible = messaggi.Count > 0 && (Report.Empty || Kind != SheetKinds.Skip);
        Grid.IsVisible = Report.Columns.Count > 0 && Kind != SheetKinds.Skip;
    }

    static string Plurale(int n, string uno, string molti) => $"{n} {(n == 1 ? uno : molti)}";

    /// <summary>Ogni scelta, anche «(non importare)» come stringa vuota, sennò il riconoscimento la rimette.</summary>
    Dictionary<string, string> Overrides() => _choices
        .GroupBy(c => c.Header)
        .ToDictionary(g => g.Key, g => g.First().Field == ColumnChoice.None ? "" : g.First().Field);
}
