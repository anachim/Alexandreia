using Alexandreia;

namespace Alexandreia.Tests;

/// <summary>
/// Copre l'unica logica non banale: disponibilità delle copie, prestito, rientro.
/// Tutto il resto è CRUD e query di aggregazione che il DB garantisce da sé.
/// </summary>
public class PrestitiTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"alexandreia-test-{Guid.NewGuid():N}.db");
    readonly Db _db;
    readonly long _libro;

    public PrestitiTests()
    {
        _db = new Db(_path);
        _libro = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide", Copies = 1 });
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(f);
    }

    [Fact]
    public void Prestito_scala_la_disponibilita()
    {
        Assert.Equal(1, _db.Book(_libro)!.Available);
        Assert.True(_db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30)));
        Assert.Equal(0, _db.Book(_libro)!.Available);
    }

    [Fact]
    public void Ultima_copia_non_si_presta_due_volte()
    {
        Assert.True(_db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30)));
        Assert.False(_db.Lend(_libro, "Eratostene", DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Due_copie_due_prestiti()
    {
        var b = _db.Book(_libro)!;
        b.Copies = 2;
        _db.SaveBook(b);

        Assert.True(_db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30)));
        Assert.True(_db.Lend(_libro, "Eratostene", DateTime.Today.AddDays(30)));
        Assert.False(_db.Lend(_libro, "Archimede", DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Rientro_libera_la_copia_e_non_si_registra_due_volte()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));
        var prestito = _db.Loans().Single();

        Assert.True(_db.Return(prestito.Id));
        Assert.Equal(1, _db.Book(_libro)!.Available);
        Assert.False(_db.Return(prestito.Id));
        Assert.True(_db.Lend(_libro, "Eratostene", DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Nome_di_chi_prende_il_libro_obbligatorio()
    {
        Assert.Throws<ArgumentException>(() => _db.Lend(_libro, "   ", DateTime.Today.AddDays(30)));
    }

    [Fact]
    public void Non_si_scende_sotto_le_copie_gia_fuori()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));
        var b = _db.Book(_libro)!;
        b.Copies = 0;

        Assert.Throws<InvalidOperationException>(() => _db.SaveBook(b));
    }

    [Fact]
    public void Archiviazione_bloccata_finche_il_libro_e_fuori()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));
        Assert.False(_db.ArchiveBook(_libro));

        _db.Return(_db.Loans().Single().Id);
        Assert.True(_db.ArchiveBook(_libro));
        Assert.Empty(_db.Books());
    }

    [Fact]
    public void Il_ritardo_finisce_nelle_metriche()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(-1));

        var s = _db.Stats();
        Assert.Equal(1, s.OpenLoans);
        Assert.Equal(1, s.Overdue);
        Assert.Equal(0, s.NeverLent);
        Assert.True(_db.Loans().Single().Overdue);
    }

    [Fact]
    public void Mai_prestati_esclude_quelli_usciti()
    {
        var altro = _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        Assert.Equal("Almagesto", Assert.Single(_db.NeverLent()).Title);
        Assert.Equal(1, _db.Stats().NeverLent);
    }

    [Fact]
    public void Classifica_ordinata_per_numero_di_prestiti()
    {
        var altro = _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        foreach (var chi in new[] { "Ipazia", "Eratostene" })
        {
            _db.Lend(_libro, chi, DateTime.Today.AddDays(30));
            _db.Return(_db.Loans().Single(l => l.BookId == _libro).Id);
        }
        _db.Lend(altro, "Archimede", DateTime.Today.AddDays(30));

        var top = _db.TopBooks(DateTime.Today.AddMonths(-1));
        Assert.Equal("Elementi", top[0].Title);
        Assert.Equal(2, top[0].Loans);
        Assert.Equal(1, top[1].Loans);
    }

    [Fact]
    public void Metriche_su_archivio_senza_prestiti()
    {
        // Risultato vuoto: SQLite non dichiara il tipo delle colonne calcolate. Deve reggere lo stesso.
        Assert.Empty(_db.TopBooks(DateTime.Today.AddMonths(-12)));
        Assert.Empty(_db.LoansByMonth(DateTime.Today.AddMonths(-12)));

        var s = _db.Stats();
        Assert.Equal(1, s.Books);
        Assert.Equal(0, s.TotalLoans);
        Assert.Equal(0, s.AvgDays);
        Assert.Equal(1, s.NeverLent);
    }

    [Fact]
    public void Ricerca_su_titolo_autore_e_disponibilita()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });

        Assert.Equal("Elementi", Assert.Single(_db.Books("eucl")).Title);
        Assert.Equal("Almagesto", Assert.Single(_db.Books("almag")).Title);
        Assert.Equal(2, _db.Books().Count);

        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));
        Assert.Equal("Almagesto", Assert.Single(_db.Books(onlyAvailable: true)).Title);
    }
}
