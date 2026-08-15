using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Alexandreia;

/// <summary>Riga con barra: la larghezza la calcola la vista, non il database.</summary>
public record Bar(string Label, string Sub, string Note, int Count, double Width);

public partial class MetricheView : UserControl, IReloadable
{
    const double BarMax = 220;

    readonly Db _db = null!;

    public MetricheView() => InitializeComponent();

    public MetricheView(Db db) : this()
    {
        _db = db;
        Period.SelectionChanged += (_, _) => Reload();
    }

    public void Reload()
    {
        var s = _db.Stats();
        Cards.Children.Clear();
        AddCard(s.Books, "Libri");
        AddCard(s.Members, "Utenti");
        AddCard(s.OpenLoans, "Fuori ora");
        AddCard(s.Overdue, "In ritardo", alert: s.Overdue > 0);
        AddCard(s.TotalLoans, "Prestiti totali");
        AddCard(s.AvgDays.ToString("0.#"), "Giorni medi");
        AddCard(s.NeverLent, "Mai prestati");

        var since = Period.SelectedIndex switch
        {
            1 => new DateTime(DateTime.Today.Year, 1, 1),
            2 => DateTime.MinValue,
            _ => DateTime.Today.AddMonths(-12),
        };

        var months = _db.LoansByMonth(since);
        var maxMonth = months.Count > 0 ? months.Max(m => m.Loans) : 1;
        Months.ItemsSource = months
            .Select(m => new Bar(m.Month, "", "", m.Loans, BarMax * m.Loans / maxMonth)).ToList();
        NoMonths.IsVisible = months.Count == 0;

        // Media sui mesi coperti dal periodo, non solo su quelli in cui c'è stato movimento:
        // un mese senza prestiti è un dato, non un buco da saltare.
        AddCard((months.Sum(m => m.Loans) / (double)MesiNelPeriodo(since, months)).ToString("0.#"),
            "Media al mese");

        var top = _db.TopBooks(since);
        var maxTop = top.Count > 0 ? top[0].Loans : 1;
        Top.ItemsSource = top
            .Select(t => new Bar(t.Title, t.Author, t.Notes ?? "", t.Loans, BarMax * t.Loans / maxTop)).ToList();
        NoTop.IsVisible = top.Count == 0;

        var never = _db.NeverLent();
        Never.ItemsSource = never;
        NoNever.IsVisible = never.Count == 0;
    }

    /// <summary>
    /// Si conta dal primo mese in cui c'è stato un prestito, se è più recente dell'inizio del
    /// periodo: su un archivio appena avviato, dividere comunque per dodici darebbe una media
    /// finta — quattro prestiti nel primo mese sono quattro al mese, non 0,3.
    /// </summary>
    static int MesiNelPeriodo(DateTime since, List<MonthCount> months)
    {
        var primo = months.Count > 0 && DateTime.TryParse($"{months[0].Month}-01", out var p)
            ? p
            : DateTime.Today;
        var da = since == DateTime.MinValue || since < primo ? primo : since;

        return Math.Max(1, (DateTime.Today.Year - da.Year) * 12 + DateTime.Today.Month - da.Month + 1);
    }

    void AddCard(object value, string label, bool alert = false)
    {
        var number = new TextBlock { Text = value.ToString(), FontSize = 24 };
        // Solo se serve: assegnare null a Foreground non lascia il colore
        // predefinito, lo azzera, e il numero sparisce.
        if (alert) number.Foreground = Brushes.IndianRed;

        Cards.Children.Add(new Border
        {
            Classes = { "card" },
            Margin = new Avalonia.Thickness(0, 0, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Children = { number, new TextBlock { Text = label, Opacity = 0.6, FontSize = 12 } },
            },
        });
    }
}
