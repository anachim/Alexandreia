using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Alexandreia;

public partial class UtentiView : UserControl, IReloadable
{
    readonly Db _db = null!;
    readonly ObservableCollection<Member> _members = [];

    public UtentiView() => InitializeComponent();

    public UtentiView(Db db) : this()
    {
        _db = db;
        Grid.ItemsSource = _members;

        Search.TextChanged += (_, _) => Reload();
        New.Click += async (_, _) => await Edit(new Member());
    }

    public void Reload()
    {
        _members.Clear();
        foreach (var m in _db.Members(Search.Text)) _members.Add(m);

        Empty.IsVisible = _members.Count == 0;
        Empty.Text = string.IsNullOrWhiteSpace(Search.Text)
            ? "Nessun utente ancora. Comincia da «Nuovo utente»."
            : "Nessun utente trovato.";
    }

    void Say(string text, bool ok)
    {
        Message.Text = text;
        Message.IsVisible = true;
        Message.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
    }

    async void OnEdit(object? sender, RoutedEventArgs e) => await Edit((Member)((Control)sender!).Tag!);

    async Task Edit(Member member)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        if (await new UtenteDialog(member).ShowDialog<bool>(owner))
        {
            _db.SaveMember(member);
            Say($"«{member.FullName}» salvato.", true);
            Reload();
        }
    }

    async void OnArchive(object? sender, RoutedEventArgs e)
    {
        var member = (Member)((Control)sender!).Tag!;
        var owner = TopLevel.GetTopLevel(this) as Window;

        if (!await Dialogs.Confirm(owner,
                $"Archiviare «{member.FullName}»?\n\nSparisce dall'elenco, ma resta nello storico dei prestiti.",
                "Archivia"))
            return;

        var fatto = _db.ArchiveMember(member.Id);
        Say(fatto
                ? $"«{member.FullName}» archiviato."
                : $"«{member.FullName}» ha ancora dei libri fuori: prima registra i rientri.",
            fatto);
        Reload();
    }
}
