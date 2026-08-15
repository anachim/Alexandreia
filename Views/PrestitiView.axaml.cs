using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Alexandreia;

public partial class PrestitiView : UserControl, IReloadable
{
    readonly Db _db = null!;
    readonly ObservableCollection<Loan> _loans = [];

    public PrestitiView() => InitializeComponent();

    public PrestitiView(Db db) : this()
    {
        _db = db;
        Grid.ItemsSource = _loans;

        Search.TextChanged += (_, _) => Reload();
        ShowAll.IsCheckedChanged += (_, _) => Reload();
    }

    public void Reload()
    {
        _loans.Clear();
        foreach (var l in _db.Loans(openOnly: ShowAll.IsChecked != true, search: Search.Text))
            _loans.Add(l);

        Empty.IsVisible = _loans.Count == 0;
        Empty.Text = ShowAll.IsChecked == true
            ? "Nessun prestito registrato."
            : "Nessun libro attualmente fuori.";
    }

    void OnReturn(object? sender, RoutedEventArgs e)
    {
        var loan = (Loan)((Control)sender!).Tag!;

        Message.Text = _db.Return(loan.Id)
            ? $"«{loan.Title}» rientrato."
            : $"«{loan.Title}» risultava già rientrato.";
        Message.IsVisible = true;
        Reload();
    }
}
