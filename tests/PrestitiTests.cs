using Alexandreia;

namespace Alexandreia.Tests;

/// <summary>
/// L'unica logica non banale: un libro è una copia sola, quindi o è libero o è fuori.
/// Il resto è CRUD e query di aggregazione che il database garantisce da sé.
/// </summary>
public class PrestitiTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"alexandreia-test-{Guid.NewGuid():N}.db");
    readonly Db _db;
    readonly long _libro;
    readonly long _ipazia;

    public PrestitiTests()
    {
        _db = new Db(_path);
        _libro = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide" });
        _ipazia = _db.SaveMember(new Member { LastName = "Ipazia" });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(f);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Il_prestito_occupa_il_libro()
    {
        Assert.True(_db.Book(_libro)!.IsAvailable);

        Assert.True(_db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30)));

        var dopo = _db.Book(_libro)!;
        Assert.False(dopo.IsAvailable);
        Assert.Equal("Ipazia", dopo.LentTo);
    }

    [Fact]
    public void Lo_stesso_libro_non_esce_due_volte()
    {
        var altro = _db.SaveMember(new Member { LastName = "Eratostene" });

        Assert.True(_db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30)));
        Assert.False(_db.Lend(_libro, altro, DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Due_copie_sono_due_schede_e_si_prestano_a_due_persone()
    {
        var seconda = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide" });
        var altro = _db.SaveMember(new Member { LastName = "Eratostene" });

        Assert.True(_db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30)));
        Assert.True(_db.Lend(seconda, altro, DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Rientro_libera_il_libro_e_non_si_registra_due_volte()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
        var prestito = _db.Loans().Single();

        Assert.True(_db.Return(prestito.Id));
        Assert.True(_db.Book(_libro)!.IsAvailable);
        Assert.False(_db.Return(prestito.Id));
        Assert.True(_db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Non_si_presta_a_un_utente_archiviato()
    {
        _db.ArchiveMember(_ipazia);

        Assert.False(_db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Archiviazione_bloccata_finche_il_libro_e_fuori()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
        Assert.False(_db.ArchiveBook(_libro));

        _db.Return(_db.Loans().Single().Id);
        Assert.True(_db.ArchiveBook(_libro));
        Assert.Empty(_db.Books());
    }

    [Fact]
    public void Un_utente_con_libri_fuori_non_si_archivia()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
        Assert.False(_db.ArchiveMember(_ipazia));
        Assert.Equal(1, _db.Members().Single().OpenLoans);

        _db.Return(_db.Loans().Single().Id);
        Assert.True(_db.ArchiveMember(_ipazia));
        Assert.Empty(_db.Members());
    }

    [Fact]
    public void Il_ritardo_finisce_nelle_metriche()
    {
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));

        var s = _db.Stats();
        Assert.Equal(1, s.OpenLoans);
        Assert.Equal(1, s.Overdue);
        Assert.Equal(1, s.Members);
        Assert.Equal(0, s.NeverLent);

        var prestito = _db.Loans().Single();
        Assert.True(prestito.Overdue);
        Assert.Equal(3, prestito.LateDays);
        Assert.Equal("3 GIORNI DI RITARDO", prestito.LateLabel);
        Assert.Equal("Ipazia", prestito.MemberName);
    }

    [Fact]
    public void I_filtri_dei_prestiti_selezionano_quello_che_dicono()
    {
        var secondo = _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(-3));   // in ritardo
        _db.Lend(secondo, _ipazia, DateTime.Today.AddDays(10));  // regolare
        var chiuso = _db.SaveBook(new Book { Title = "Coniche" });
        _db.Lend(chiuso, _ipazia, DateTime.Today.AddDays(10));
        _db.Return(_db.Loans().Single(l => l.BookId == chiuso).Id);

        Assert.Equal(2, _db.Loans(Filtri.Fuori).Count);
        Assert.Equal("Elementi", Assert.Single(_db.Loans(Filtri.Ritardo)).Title);
        Assert.Equal("Coniche", Assert.Single(_db.Loans(Filtri.Rientrati)).Title);
        Assert.Equal(3, _db.Loans(Filtri.Tutti).Count);
    }

    [Fact]
    public void La_finestra_conta_solo_i_prestiti_che_ci_stanno_dentro()
    {
        _db.Apply(Import.Plan([
            new object?[] { "Titolo", "Prestato a", "Prestato il" },
            new object?[] { "Vecchio", "Ipazia", DateTime.Today.AddMonths(-8) },
            new object?[] { "Recente", "Eratostene", DateTime.Today.AddDays(-2) },
        ]).Rows);

        Assert.Equal(1, _db.InWindow(DateTime.Today.AddMonths(-1)).Loans);
        Assert.Equal(2, _db.InWindow(DateTime.Today.AddMonths(-12)).Loans);
        Assert.Equal(2, _db.InWindow(DateTime.Today.AddMonths(-12)).People);

        // Finestra chiusa: il prestito recente resta fuori.
        Assert.Equal(1, _db.InWindow(DateTime.Today.AddMonths(-12), DateTime.Today.AddMonths(-1)).Loans);
    }

    [Fact]
    public void Metriche_su_archivio_senza_prestiti()
    {
        // Risultato vuoto: SQLite non dichiara il tipo delle colonne calcolate. Deve reggere.
        Assert.Empty(_db.TopBooks(DateTime.Today.AddMonths(-12)));
        Assert.Empty(_db.LoansByMonth(DateTime.Today.AddMonths(-12)));

        var s = _db.Stats();
        Assert.Equal(1, s.Books);
        Assert.Equal(0, s.TotalLoans);
        Assert.Equal(0, s.AvgDays);
        Assert.Equal(1, s.NeverLent);
    }

    [Fact]
    public void Mai_prestati_esclude_quelli_usciti()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));

        Assert.Equal("Almagesto", Assert.Single(_db.NeverLent()).Title);
        Assert.Equal(1, _db.Stats().NeverLent);
    }

    [Fact]
    public void Classifica_ordinata_per_numero_di_prestiti()
    {
        var altro = _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        for (var i = 0; i < 2; i++)
        {
            _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
            _db.Return(_db.Loans().Single(l => l.BookId == _libro).Id);
        }
        _db.Lend(altro, _ipazia, DateTime.Today.AddDays(30));

        var top = _db.TopBooks(DateTime.Today.AddMonths(-1));
        Assert.Equal("Elementi", top[0].Title);
        Assert.Equal(2, top[0].Loans);
        Assert.Equal(1, top[1].Loans);
    }

    [Fact]
    public void Ricerca_su_titolo_autore_nota_e_disponibilita()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo", Notes = "scaffale B7" });

        Assert.Equal("Elementi", Assert.Single(_db.Books("eucl")).Title);
        Assert.Equal("Almagesto", Assert.Single(_db.Books("B7")).Title);
        Assert.Equal(2, _db.Books().Count);

        _db.Lend(_libro, _ipazia, DateTime.Today.AddDays(30));
        Assert.Equal("Almagesto", Assert.Single(_db.Books(onlyAvailable: true)).Title);
    }

    [Fact]
    public void Ricerca_utenti_per_cognome_e_nota()
    {
        _db.SaveMember(new Member { LastName = "Rossi", FirstName = "Mario", Notes = "classe 3B" });

        Assert.Equal("Rossi Mario", Assert.Single(_db.Members("rossi")).FullName);
        Assert.Equal("Rossi Mario", Assert.Single(_db.Members("3B")).FullName);
    }
}
