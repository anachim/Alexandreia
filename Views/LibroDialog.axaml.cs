using System.ComponentModel.DataAnnotations;
using Avalonia.Controls;

namespace Alexandreia;

/// <summary>
/// Scheda del libro. La validazione riusa gli attributi già su <see cref="Book"/>,
/// così la regola sta in un posto solo.
/// </summary>
public partial class LibroDialog : Window
{
    readonly Book _book = new();

    public LibroDialog() => InitializeComponent();

    public LibroDialog(Book book) : this()
    {
        _book = book;
        Title = book.Id == 0 ? "Nuovo libro" : "Modifica libro";

        Titolo.Text = book.Title;
        Autore.Text = book.Author;
        Isbn.Text = book.Isbn;
        Anno.Text = book.Year?.ToString();
        Editore.Text = book.Publisher;
        Collocazione.Text = book.Location;
        Copie.Text = book.Copies.ToString();
        Note.Text = book.Notes;

        Annulla.Click += (_, _) => Close(false);
        Salva.Click += (_, _) => TrySave();
        Opened += (_, _) => Titolo.Focus();
    }

    void TrySave()
    {
        if (!int.TryParse(Copie.Text, out var copie)) copie = 0;

        var candidate = new Book
        {
            Id = _book.Id,
            Title = Titolo.Text?.Trim() ?? "",
            Author = Autore.Text?.Trim() ?? "",
            Isbn = Empty(Isbn.Text),
            Year = int.TryParse(Anno.Text, out var anno) ? anno : null,
            Publisher = Empty(Editore.Text),
            Location = Empty(Collocazione.Text),
            Copies = copie,
            Notes = Empty(Note.Text),
        };

        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(candidate, new ValidationContext(candidate), errors, validateAllProperties: true))
        {
            Errore.Text = string.Join("\n", errors.Select(e => e.ErrorMessage));
            Errore.IsVisible = true;
            return;
        }

        // Ricopia sull'istanza del chiamante solo dopo che la validazione è passata.
        _book.Title = candidate.Title;
        _book.Author = candidate.Author;
        _book.Isbn = candidate.Isbn;
        _book.Year = candidate.Year;
        _book.Publisher = candidate.Publisher;
        _book.Location = candidate.Location;
        _book.Copies = candidate.Copies;
        _book.Notes = candidate.Notes;

        Close(true);
    }

    static string? Empty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
