using System.ComponentModel.DataAnnotations;
using Avalonia.Controls;

namespace Alexandreia;

public partial class UtenteDialog : Window
{
    readonly Member _member = new();

    public UtenteDialog() => InitializeComponent();

    public UtenteDialog(Member member) : this()
    {
        _member = member;
        Title = member.Id == 0 ? "Nuovo utente" : "Modifica utente";

        Cognome.Text = member.LastName;
        Nome.Text = member.FirstName;
        Nota.Text = member.Notes;

        Annulla.Click += (_, _) => Close(false);
        Salva.Click += (_, _) => TrySave();
        Opened += (_, _) => Cognome.Focus();
    }

    void TrySave()
    {
        // La validazione riusa gli attributi su Member: la regola sta in un posto solo.
        var candidato = new Member
        {
            Id = _member.Id,
            LastName = Cognome.Text?.Trim() ?? "",
            FirstName = Nome.Text?.Trim() ?? "",
            Notes = string.IsNullOrWhiteSpace(Nota.Text) ? null : Nota.Text.Trim(),
        };

        var errori = new List<ValidationResult>();
        if (!Validator.TryValidateObject(candidato, new ValidationContext(candidato), errori, true))
        {
            Errore.Text = string.Join("\n", errori.Select(e => e.ErrorMessage));
            Errore.IsVisible = true;
            return;
        }

        _member.LastName = candidato.LastName;
        _member.FirstName = candidato.FirstName;
        _member.Notes = candidato.Notes;
        Close(true);
    }
}
