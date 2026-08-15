using System.ComponentModel.DataAnnotations;
using Avalonia.Controls;

namespace Alexandreia;

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
        Nota.Text = book.Notes;

        Annulla.Click += (_, _) => Close(false);
        Salva.Click += (_, _) => TrySave();
        Opened += (_, _) => Titolo.Focus();
    }

    void TrySave()
    {
        // La validazione riusa gli attributi su Book: la regola sta in un posto solo.
        var candidato = new Book
        {
            Id = _book.Id,
            Title = Titolo.Text?.Trim() ?? "",
            Author = Autore.Text?.Trim() ?? "",
            Notes = string.IsNullOrWhiteSpace(Nota.Text) ? null : Nota.Text.Trim(),
        };

        var errori = new List<ValidationResult>();
        if (!Validator.TryValidateObject(candidato, new ValidationContext(candidato), errori, true))
        {
            Errore.Text = string.Join("\n", errori.Select(e => e.ErrorMessage));
            Errore.IsVisible = true;
            return;
        }

        _book.Title = candidato.Title;
        _book.Author = candidato.Author;
        _book.Notes = candidato.Notes;
        Close(true);
    }
}
