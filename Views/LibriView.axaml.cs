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
        // Label e non FullName: con due omonimi la nota è l'unico modo di distinguerli.
        Person.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(Member.Label));

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
            ? "Nessun libro ancora. Comincia da «Nuovo libro», oppure carica un Excel dalla scheda Dati."
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
        var utenti = _db.Members();
        if (utenti.Count == 0)
        {
            Say("Non c'è ancora nessun utente: aggiungilo dalla scheda Utenti.", false);
            return;
        }

        _lending = Row(sender);
        LendTitle.Text = $"Presta «{_lending.Title}»";
        Person.ItemsSource = utenti;
        Person.SelectedItem = null;
        DueAt.SelectedDate = DateTime.Today.AddDays(Import.DefaultLoanDays);
        LendPanel.IsVisible = true;
        Message.IsVisible = false;
    }

    void CloseLendPanel()
    {
        _lending = null;
        LendPanel.IsVisible = false;
    }

    void DoLend()
    {
        if (_lending is null) return;

        if (Person.SelectedItem is not Member chi)
        {
            Say("Scegli a chi lo stai prestando.", false);
            return;
        }

        var entro = DueAt.SelectedDate?.Date ?? DateTime.Today.AddDays(Import.DefaultLoanDays);
        if (_db.Lend(_lending.Id, chi.Id, entro))
        {
            Say($"«{_lending.Title}» prestato a {chi.FullName}, rientro entro {entro:dd/MM/yyyy}.", true);
            CloseLendPanel();
        }
        else
        {
            Say("Quel libro risulta già fuori: qualcuno l'ha preso prima.", false);
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
            _db.SaveBook(book);
            Say($"«{book.Title}» salvato.", true);
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
                : $"«{book.Title}» è ancora fuori in prestito: prima registra il rientro.",
            archiviato);
        Reload();
    }
}
