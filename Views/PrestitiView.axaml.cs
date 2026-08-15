using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Alexandreia;

public partial class PrestitiView : UserControl, IReloadable
{
    /// <summary>Etichette della tendina, in ordine, e il filtro che ognuna attiva.</summary>
    static readonly (string Label, string Filter)[] Filtri =
    [
        ("Fuori adesso", Alexandreia.Filtri.Fuori),
        ("Solo in ritardo", Alexandreia.Filtri.Ritardo),
        ("Già rientrati", Alexandreia.Filtri.Rientrati),
        ("Tutti", Alexandreia.Filtri.Tutti),
    ];

    readonly Db _db = null!;
    readonly ObservableCollection<Loan> _loans = [];
    Loan? _extending;

    public PrestitiView() => InitializeComponent();

    public PrestitiView(Db db) : this()
    {
        _db = db;
        Grid.ItemsSource = _loans;

        Filtro.ItemsSource = Filtri.Select(f => f.Label).ToList();
        Filtro.SelectedIndex = 0;

        Search.TextChanged += (_, _) => Reload();
        Filtro.SelectionChanged += (_, _) => Reload();
        CancelExtend.Click += (_, _) => ChiudiProroga();
        ConfirmExtend.Click += (_, _) => Prolunga();
    }

    string Selected => Filtri[Math.Max(0, Filtro.SelectedIndex)].Filter;

    /// <summary>Apre la scheda già filtrata: la usano le schede numeriche delle metriche.</summary>
    public void Mostra(string filtro)
    {
        var i = Array.FindIndex(Filtri, f => f.Filter == filtro);
        if (i >= 0) Filtro.SelectedIndex = i;
        Search.Text = "";
        Reload();
    }

    public void Reload()
    {
        _loans.Clear();
        foreach (var l in _db.Loans(Selected, Search.Text)) _loans.Add(l);

        Conteggio.Text = _loans.Count == 1 ? "1 prestito" : $"{_loans.Count} prestiti";
        Empty.IsVisible = _loans.Count == 0;
        Empty.Text = Selected switch
        {
            Alexandreia.Filtri.Ritardo => "Nessun libro in ritardo. Tutto in ordine.",
            Alexandreia.Filtri.Rientrati => "Nessun libro ancora rientrato.",
            Alexandreia.Filtri.Tutti => "Nessun prestito registrato.",
            _ => "Nessun libro attualmente fuori.",
        };
    }

    void OnReturn(object? sender, RoutedEventArgs e)
    {
        var loan = (Loan)((Control)sender!).Tag!;

        Message.Text = _db.Return(loan.Id)
            ? $"«{loan.Title}» rientrato."
            : $"«{loan.Title}» risultava già rientrato.";
        Message.IsVisible = true;
        ChiudiProroga();
        Reload();
    }

    // --- Proroga --------------------------------------------------------

    void OnExtend(object? sender, RoutedEventArgs e)
    {
        _extending = (Loan)((Control)sender!).Tag!;
        ExtendTitle.Text = $"«{_extending.Title}» a {_extending.MemberName}";
        // Parte da oggi più i soliti giorni, non dalla scadenza vecchia: chi prolunga
        // un libro in ritardo vuole altro tempo da adesso, non da un mese fa.
        NewDue.SelectedDate = DateTime.Today.AddDays(Import.DefaultLoanDays);
        ExtendPanel.IsVisible = true;
        Message.IsVisible = false;
    }

    void ChiudiProroga()
    {
        _extending = null;
        ExtendPanel.IsVisible = false;
    }

    void Prolunga()
    {
        if (_extending is null) return;

        var nuova = NewDue.SelectedDate?.Date ?? DateTime.Today.AddDays(Import.DefaultLoanDays);
        Message.Text = _db.Extend(_extending.Id, nuova)
            ? $"«{_extending.Title}» ora rientra entro il {nuova:dd/MM/yyyy}."
            : $"«{_extending.Title}» risulta già rientrato.";
        Message.IsVisible = true;
        ChiudiProroga();
        Reload();
    }
}
