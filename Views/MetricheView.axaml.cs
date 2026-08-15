using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Alexandreia;

/// <summary>Riga con barra: la larghezza la calcola la vista, non il database.</summary>
public record Bar(string Label, string Sub, string Note, int Count, double Width);

public partial class MetricheView : UserControl, IReloadable
{
    const double BarMax = 200;

    /// <summary>Periodi pronti, più l'ultimo che apre i due calendari.</summary>
    static readonly string[] Periodi =
    [
        "Ultimi 30 giorni", "Ultimi 3 mesi", "Ultimi 6 mesi", "Ultimi 12 mesi",
        "Anno corrente", "Anno scorso", "Da sempre", "Scegli le date…",
    ];

    const int Personalizzato = 7;

    readonly Db _db = null!;

    /// <summary>Le schede «Fuori adesso» e «In ritardo» portano all'elenco già filtrato.</summary>
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

        // --- Adesso: lo stato della biblioteca, indipendente dal periodo ---
        var s = _db.Stats();
        Now.Children.Clear();
        Now.Children.Add(Scheda(s.Books, "Libri"));
        Now.Children.Add(Scheda(s.Members, "Utenti"));
        Now.Children.Add(Scheda(s.OpenLoans, "Fuori adesso", vai: Filtri.Fuori));
        Now.Children.Add(Scheda(s.Overdue, "In ritardo", allarme: s.Overdue > 0, vai: Filtri.Ritardo));
        Now.Children.Add(Scheda(s.NeverLent, "Mai usciti"));

        // --- Nel periodo, con il confronto sulla finestra precedente di pari durata ---
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

    /// <summary>Confronto con la finestra precedente di pari durata. Null se non ha senso.</summary>
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
    /// Mesi coperti, contati dal primo con movimento se è più recente dell'inizio del
    /// periodo: su un archivio appena avviato dividere comunque per dodici darebbe una
    /// media finta — un prestito nel primo mese è uno al mese, non 0,1.
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

    Border Scheda(object valore, string etichetta, bool allarme = false, string? delta = null, string? vai = null)
    {
        var numero = new TextBlock
        {
            Text = valore.ToString(),
            FontSize = 26,
            Foreground = allarme
                ? this.FindResource("Late") as IBrush
                : this.FindResource("Ink") as IBrush,
        };

        var contenuto = new StackPanel
        {
            Spacing = 1,
            Children =
            {
                numero,
                new TextBlock
                {
                    Text = etichetta,
                    FontSize = 12,
                    Foreground = this.FindResource("Muted") as IBrush,
                },
            },
        };

        if (delta is not null)
            contenuto.Children.Add(new TextBlock
            {
                Text = delta,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = this.FindResource("Muted") as IBrush,
            });

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
            contenuto.Children.Add(new TextBlock
            {
                Text = "vedi l'elenco →",
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = this.FindResource("Stamp") as IBrush,
            });
        }

        return card;
    }
}
