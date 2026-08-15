using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Alexandreia;

/// <summary>A row with a bar: the width is computed by the view, not by the database.</summary>
public record Bar(string Label, string Sub, string Note, int Count, double Width);

public partial class MetricheView : UserControl, IReloadable
{
    const double BarMax = 200;

    /// <summary>Ready-made periods, plus a last one that opens the two date pickers.</summary>
    static readonly string[] Periodi =
    [
        "Ultimi 30 giorni", "Ultimi 3 mesi", "Ultimi 6 mesi", "Ultimi 12 mesi",
        "Anno corrente", "Anno scorso", "Da sempre", "Scegli le date…",
    ];

    const int Personalizzato = 7;

    readonly Db _db = null!;

    /// <summary>The "out now" and "overdue" cards lead to the already filtered list.</summary>
    public event Action<string>? ApriPrestiti;

    public MetricheView() => InitializeComponent();

    public MetricheView(Db db) : this()
    {
        _db = db;

        Period.ItemsSource = Periodi;
        Period.SelectedIndex = 3; // ultimi 12 mesi
        Period.SelectionChanged += (_, _) => Reload();

        From.SelectedDate = DateTime.Today.AddMonths(-3);
        To.SelectedDate = DateTime.Today;
        From.SelectedDateChanged += (_, _) => Reload();
        To.SelectedDateChanged += (_, _) => Reload();
    }

    (DateTime Da, DateTime? A) Finestra()
    {
        var oggi = DateTime.Today;
        return Period.SelectedIndex switch
        {
            0 => (oggi.AddDays(-30), null),
            1 => (oggi.AddMonths(-3), null),
            2 => (oggi.AddMonths(-6), null),
            4 => (new DateTime(oggi.Year, 1, 1), null),
            5 => (new DateTime(oggi.Year - 1, 1, 1), new DateTime(oggi.Year, 1, 1)),
            6 => (DateTime.MinValue, null),
            Personalizzato => (From.SelectedDate?.Date ?? oggi.AddMonths(-3),
                               (To.SelectedDate?.Date ?? oggi).AddDays(1)),
            _ => (oggi.AddMonths(-12), null),
        };
    }

    public void Reload()
    {
        var personalizzato = Period.SelectedIndex == Personalizzato;
        From.IsVisible = To.IsVisible = personalizzato;

        var (da, a) = Finestra();
        Range.Text = da == DateTime.MinValue
            ? "dal primo prestito registrato"
            : $"dal {da:dd/MM/yyyy} al {(a ?? DateTime.Today.AddDays(1)).AddDays(-1):dd/MM/yyyy}";

        // --- Now: the state of the library, independent of the period ---
        var s = _db.Stats();
        Now.Children.Clear();
        Now.Children.Add(Scheda(s.Books, "Libri"));
        Now.Children.Add(Scheda(s.Members, "Utenti"));
        Now.Children.Add(Scheda(s.OpenLoans, "Fuori adesso", vai: Filtri.Fuori));
        Now.Children.Add(Scheda(s.Overdue, "In ritardo", allarme: s.Overdue > 0, vai: Filtri.Ritardo));
        Now.Children.Add(Scheda(s.NeverLent, "Mai usciti"));

        // --- Within the period, compared against the preceding window of equal length ---
        var ora = _db.InWindow(da, a);
        var months = _db.LoansByMonth(da, a);
        var mesi = Mesi(da, a, months);

        Cards.Children.Clear();
        Cards.Children.Add(Scheda(ora.Loans, "Prestiti", delta: Delta(da, a, ora.Loans)));
        Cards.Children.Add(Scheda(ora.People, "Persone attive"));
        Cards.Children.Add(Scheda((ora.Loans / (double)mesi).ToString("0.#"), "Media al mese"));
        Cards.Children.Add(Scheda(ora.AvgDays.ToString("0.#"), "Giorni medi"));

        var maxMonth = months.Count > 0 ? months.Max(m => m.Loans) : 1;
        Months.ItemsSource = months
            .Select(m => new Bar(m.Month, "", "", m.Loans, BarMax * m.Loans / maxMonth)).ToList();
        NoMonths.IsVisible = months.Count == 0;

        var top = _db.TopBooks(da, a);
        var maxTop = top.Count > 0 ? top[0].Loans : 1;
        Top.ItemsSource = top
            .Select(t => new Bar(t.Title, t.Author, t.Notes ?? "", t.Loans, BarMax * t.Loans / maxTop)).ToList();
        NoTop.IsVisible = top.Count == 0;

        var never = _db.NeverLent();
        Never.ItemsSource = never;
        NoNever.IsVisible = never.Count == 0;
    }

    /// <summary>Comparison with the preceding window of equal length. Null when meaningless.</summary>
    string? Delta(DateTime da, DateTime? a, int adesso)
    {
        if (da == DateTime.MinValue) return null;

        var fine = a ?? DateTime.Today.AddDays(1);
        var durata = fine - da;
        var prima = _db.InWindow(da - durata, da).Loans;
        if (prima == 0) return null;

        var scarto = (adesso - prima) * 100 / prima;
        return scarto == 0 ? "uguale a prima" : $"{(scarto > 0 ? "↑" : "↓")} {Math.Abs(scarto)}% sul periodo prima";
    }

    /// <summary>
    /// Months covered, counted from the first one with activity when that is later than the
    /// start of the period: on a freshly started archive, dividing by twelve anyway would
    /// give a fake average — one loan in the first month is one a month, not 0.1.
    /// </summary>
    static int Mesi(DateTime da, DateTime? a, List<MonthCount> months)
    {
        var primo = months.Count > 0 && DateTime.TryParse($"{months[0].Month}-01", out var p)
            ? p
            : DateTime.Today;
        if (da == DateTime.MinValue || da < primo) da = primo;

        var fine = (a ?? DateTime.Today.AddDays(1)).AddDays(-1);
        return Math.Max(1, (fine.Year - da.Year) * 12 + fine.Month - da.Month + 1);
    }

    /// <summary>
    /// Colour from a dynamic resource rather than resolved once: the cards are built by
    /// hand, and with a fixed colour they would stay in the theme they were born in.
    /// </summary>
    static TextBlock Testo(string testo, double corpo, string colore) =>
        new TextBlock { Text = testo, FontSize = corpo }
            .Tinta(colore);

    Border Scheda(object valore, string etichetta, bool allarme = false, string? delta = null, string? vai = null)
    {
        var contenuto = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                Testo(valore.ToString() ?? "", 26, allarme ? "Late" : "Ink"),
                Testo(etichetta, 12, "Muted"),
            },
        };

        if (delta is not null)
        {
            var d = Testo(delta, 11, "Muted");
            d.Margin = new Thickness(0, 4, 0, 0);
            contenuto.Children.Add(d);
        }

        var card = new Border
        {
            Classes = { "card", "stat" },
            Margin = new Thickness(0, 0, 12, 12),
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = contenuto,
        };

        if (vai is not null)
        {
            card.Classes.Add("clickable");
            card.PointerPressed += (_, _) => ApriPrestiti?.Invoke(vai);

            var link = Testo("vedi l'elenco →", 11, "Stamp");
            link.Margin = new Thickness(0, 4, 0, 0);
            contenuto.Children.Add(link);
        }

        return card;
    }
}

static class Tinte
{
    /// <summary>Binds the colour to the resource, so it follows the theme instead of freezing.</summary>
    public static TextBlock Tinta(this TextBlock t, string risorsa)
    {
        t[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(risorsa);
        return t;
    }
}
