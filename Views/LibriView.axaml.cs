using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Alexandreia;

public partial class LibriView : UserControl, IReloadable
{
    readonly Db _db = null!;
    readonly ObservableCollection<Book> _books = [];
    Book? _lending;

    public LibriView() => InitializeComponent();

    public LibriView(Db db) : this()
    {
        _db = db;
        Grid.ItemsSource = _books;

        Search.TextChanged += (_, _) => Reload();
        OnlyAvailable.IsCheckedChanged += (_, _) => Reload();
        New.Click += async (_, _) => await EditBook(new Book());
        CancelLend.Click += (_, _) => CloseLendPanel();
        ConfirmLend.Click += (_, _) => DoLend();
    }

    public void Reload()
    {
        _books.Clear();
        foreach (var b in _db.Books(Search.Text, OnlyAvailable.IsChecked == true))
            _books.Add(b);

        Empty.IsVisible = _books.Count == 0;
        Empty.Text = string.IsNullOrWhiteSpace(Search.Text)
            ? "Nessun libro ancora. Comincia da «Nuovo libro», oppure carica un Excel dalla scheda Import."
            : "Nessun libro trovato.";
    }

    void Say(string text, bool ok)
    {
        Message.Text = text;
        Message.IsVisible = true;
        Message.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
    }

    static Book Row(object? sender) => (Book)((Control)sender!).Tag!;

    // --- Prestito -------------------------------------------------------

    void OnLend(object? sender, RoutedEventArgs e)
    {
        _lending = Row(sender);
        LendTitle.Text = $"Presta «{_lending.Title}»";
        Borrower.Text = "";
        DueAt.SelectedDate = DateTime.Today.AddDays(30);
        LendPanel.IsVisible = true;
        Message.IsVisible = false;
        Borrower.Focus();
    }

    void CloseLendPanel()
    {
        _lending = null;
        LendPanel.IsVisible = false;
    }

    void DoLend()
    {
        if (_lending is null) return;

        var chi = Borrower.Text?.Trim() ?? "";
        if (chi.Length == 0) { Say("Scrivi a chi lo stai prestando.", false); return; }

        var entro = DueAt.SelectedDate?.Date ?? DateTime.Today.AddDays(30);
        if (_db.Lend(_lending.Id, chi, entro))
        {
            Say($"«{_lending.Title}» prestato a {chi}, rientro entro {entro:dd/MM/yyyy}.", true);
            CloseLendPanel();
        }
        else
        {
            Say("Nessuna copia disponibile: qualcuno l'ha già presa.", false);
        }
        Reload();
    }

    // --- Scheda e archiviazione -----------------------------------------

    async void OnEdit(object? sender, RoutedEventArgs e) => await EditBook(Row(sender));

    async Task EditBook(Book book)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        if (await new LibroDialog(book).ShowDialog<bool>(owner))
        {
            try
            {
                _db.SaveBook(book);
                Say($"«{book.Title}» salvato.", true);
            }
            catch (InvalidOperationException ex)
            {
                Say(ex.Message, false);
            }
            Reload();
        }
    }

    async void OnArchive(object? sender, RoutedEventArgs e)
    {
        var book = Row(sender);
        var owner = TopLevel.GetTopLevel(this) as Window;

        if (!await Dialogs.Confirm(owner,
                $"Archiviare «{book.Title}»?\n\nSparisce dall'elenco, ma lo storico dei suoi prestiti resta nelle metriche.",
                "Archivia"))
            return;

        var archiviato = _db.ArchiveBook(book.Id);
        Say(archiviato
                ? $"«{book.Title}» archiviato."
                : $"«{book.Title}» ha ancora copie in prestito: prima registra il rientro.",
            archiviato);
        Reload();
    }
}
