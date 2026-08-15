using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Alexandreia;

public class Book
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Il titolo è obbligatorio.")]
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Isbn { get; set; }
    public string? Publisher { get; set; }

    [Range(1, 2100, ErrorMessage = "Anno non valido.")]
    public int? Year { get; set; }

    [Range(0, 9999, ErrorMessage = "Il numero di copie non può essere negativo.")]
    public int Copies { get; set; } = 1;
    public string? Location { get; set; }
    public string? Notes { get; set; }

    public int Available { get; set; } // calcolata dalla query, non è una colonna

    public bool IsAvailable => Available > 0;
}

public class Loan
{
    public long Id { get; set; }
    public long BookId { get; set; }
    public string Borrower { get; set; } = "";
    public DateTime LoanedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    public string Title { get; set; } = ""; // dal join
    public string Author { get; set; } = "";
    public bool IsOpen => ReturnedAt is null;
    public bool Overdue => IsOpen && DueAt.Date < DateTime.Today;

    public string DueLabel => Overdue
        ? $"{DueAt:dd/MM/yyyy} — in ritardo di {(DateTime.Today - DueAt.Date).Days} gg"
        : DueAt.ToString("dd/MM/yyyy");
}

// Proprietà settabili, non record posizionali: SQLite non dichiara il tipo delle colonne calcolate
// (COUNT, AVG) e su un risultato vuoto le riporta come byte[]. Dapper pretende il tipo esatto nei
// costruttori, mentre sulle proprietà converte da sé.
public record Summary
{
    public int Books { get; set; }
    public int Copies { get; set; }
    public int OpenLoans { get; set; }
    public int Overdue { get; set; }
    public int NeverLent { get; set; }
    public double AvgDays { get; set; }
    public int TotalLoans { get; set; }
}

public record TopBook
{
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Loans { get; set; }
}

public record MonthCount
{
    public string Month { get; set; } = "";
    public int Loans { get; set; }
}

public class Db
{
    const string Schema = """
        PRAGMA journal_mode=WAL;

        CREATE TABLE IF NOT EXISTS Books (
            Id        INTEGER PRIMARY KEY,
            Title     TEXT    NOT NULL,
            Author    TEXT    NOT NULL DEFAULT '',
            Isbn      TEXT,
            Publisher TEXT,
            Year      INTEGER,
            Copies    INTEGER NOT NULL DEFAULT 1 CHECK (Copies >= 0),
            Location  TEXT,
            Notes     TEXT,
            Archived  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Loans (
            Id         INTEGER PRIMARY KEY,
            BookId     INTEGER NOT NULL REFERENCES Books(Id),
            Borrower   TEXT    NOT NULL,
            LoanedAt   TEXT    NOT NULL,
            DueAt      TEXT    NOT NULL,
            ReturnedAt TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_loans_open  ON Loans(BookId) WHERE ReturnedAt IS NULL;
        CREATE INDEX IF NOT EXISTS ix_loans_when  ON Loans(LoanedAt);
        """;

    // Copie libere = copie possedute meno i prestiti ancora aperti.
    const string AvailableExpr =
        "b.Copies - (SELECT COUNT(*) FROM Loans l WHERE l.BookId = b.Id AND l.ReturnedAt IS NULL)";

    readonly string _cs;

    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable("ALEXANDREIA_DB")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Alexandreia", "alexandreia.db");

    public Db(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _cs = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString();
        using var c = Open();
        c.Execute(Schema);
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    // --- Libri ---------------------------------------------------------

    public List<Book> Books(string? search = null, bool onlyAvailable = false, int limit = 200)
    {
        using var c = Open();
        // ponytail: LIKE su 1400 righe è istantaneo. Passare a FTS5 solo oltre le ~50k.
        return c.Query<Book>($"""
            SELECT b.*, {AvailableExpr} AS Available
            FROM Books b
            WHERE b.Archived = 0
              AND (@q IS NULL OR b.Title LIKE @like OR b.Author LIKE @like OR b.Isbn LIKE @like)
              AND (@onlyAvailable = 0 OR {AvailableExpr} > 0)
            ORDER BY b.Title
            LIMIT @limit
            """,
            new { q = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search}%", onlyAvailable, limit })
            .ToList();
    }

    public Book? Book(long id)
    {
        using var c = Open();
        return c.QuerySingleOrDefault<Book>(
            $"SELECT b.*, {AvailableExpr} AS Available FROM Books b WHERE b.Id = @id", new { id });
    }

    /// <summary>Inserisce o aggiorna. Rifiuta di scendere sotto il numero di copie già fuori in prestito.</summary>
    public long SaveBook(Book b)
    {
        b.Title = b.Title.Trim();
        b.Author = b.Author.Trim();

        using var c = Open();
        if (b.Id == 0)
            return c.ExecuteScalar<long>("""
                INSERT INTO Books (Title, Author, Isbn, Publisher, Year, Copies, Location, Notes)
                VALUES (@Title, @Author, @Isbn, @Publisher, @Year, @Copies, @Location, @Notes)
                RETURNING Id
                """, b);

        var updated = c.Execute("""
            UPDATE Books SET Title=@Title, Author=@Author, Isbn=@Isbn, Publisher=@Publisher,
                             Year=@Year, Copies=@Copies, Location=@Location, Notes=@Notes
            WHERE Id=@Id
              AND @Copies >= (SELECT COUNT(*) FROM Loans WHERE BookId=@Id AND ReturnedAt IS NULL)
            """, b);

        if (updated == 0)
            throw new InvalidOperationException(
                "Ci sono più copie in prestito di quante ne stai dichiarando: registra prima i rientri.");
        return b.Id;
    }

    /// <summary>Inserimento massivo (import). Una sola transazione: 1400 commit separati sono lentissimi.</summary>
    public int InsertBooks(IEnumerable<Book> books)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        var n = c.Execute("""
            INSERT INTO Books (Title, Author, Isbn, Publisher, Year, Copies, Location, Notes)
            VALUES (@Title, @Author, @Isbn, @Publisher, @Year, @Copies, @Location, @Notes)
            """, books, tx);
        tx.Commit();
        return n;
    }

    /// <summary>Archivia il libro. Rifiuta se ci sono prestiti aperti. La storia dei prestiti resta.</summary>
    public bool ArchiveBook(long id)
    {
        using var c = Open();
        return c.Execute("""
            UPDATE Books SET Archived = 1
            WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Loans WHERE BookId = @id AND ReturnedAt IS NULL)
            """, new { id }) == 1;
    }

    // --- Prestiti ------------------------------------------------------

    /// <summary>Registra un prestito. False se non ci sono copie libere.</summary>
    public bool Lend(long bookId, string borrower, DateTime dueAt)
    {
        borrower = borrower.Trim();
        if (borrower.Length == 0) throw new ArgumentException("Serve il nome di chi prende il libro.", nameof(borrower));

        using var c = Open();
        // Controllo e inserimento in un'unica istruzione: niente finestra per due prestiti sull'ultima copia.
        return c.Execute("""
            INSERT INTO Loans (BookId, Borrower, LoanedAt, DueAt)
            SELECT @bookId, @borrower, @now, @dueAt
            WHERE (SELECT Copies FROM Books WHERE Id = @bookId AND Archived = 0)
                > (SELECT COUNT(*) FROM Loans WHERE BookId = @bookId AND ReturnedAt IS NULL)
            """, new { bookId, borrower, now = DateTime.Now, dueAt = dueAt.Date }) == 1;
    }

    /// <summary>Registra il rientro. False se quel prestito era già chiuso.</summary>
    public bool Return(long loanId)
    {
        using var c = Open();
        return c.Execute("UPDATE Loans SET ReturnedAt = @now WHERE Id = @loanId AND ReturnedAt IS NULL",
            new { loanId, now = DateTime.Now }) == 1;
    }

    public List<Loan> Loans(bool openOnly = true, string? search = null, int limit = 200)
    {
        using var c = Open();
        return c.Query<Loan>("""
            SELECT l.*, b.Title, b.Author
            FROM Loans l JOIN Books b ON b.Id = l.BookId
            WHERE (@openOnly = 0 OR l.ReturnedAt IS NULL)
              AND (@q IS NULL OR b.Title LIKE @like OR l.Borrower LIKE @like)
            ORDER BY l.ReturnedAt IS NOT NULL, l.DueAt, l.Id DESC
            LIMIT @limit
            """,
            new { openOnly, q = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search}%", limit })
            .ToList();
    }

    // --- Metriche ------------------------------------------------------

    public Summary Stats()
    {
        using var c = Open();
        return c.QuerySingle<Summary>("""
            SELECT
              (SELECT COUNT(*)            FROM Books WHERE Archived = 0)                                  AS Books,
              (SELECT IFNULL(SUM(Copies),0) FROM Books WHERE Archived = 0)                                AS Copies,
              (SELECT COUNT(*)            FROM Loans WHERE ReturnedAt IS NULL)                            AS OpenLoans,
              (SELECT COUNT(*)            FROM Loans WHERE ReturnedAt IS NULL AND DueAt < @today)         AS Overdue,
              (SELECT COUNT(*)            FROM Books b WHERE b.Archived = 0
                                            AND NOT EXISTS (SELECT 1 FROM Loans l WHERE l.BookId = b.Id)) AS NeverLent,
              (SELECT IFNULL(AVG(julianday(ReturnedAt) - julianday(LoanedAt)), 0.0)
                                          FROM Loans WHERE ReturnedAt IS NOT NULL)                        AS AvgDays,
              (SELECT COUNT(*)            FROM Loans)                                                     AS TotalLoans
            """, new { today = DateTime.Today });
    }

    public List<TopBook> TopBooks(DateTime since, int limit = 20)
    {
        using var c = Open();
        return c.Query<TopBook>("""
            SELECT b.Title, b.Author, COUNT(*) AS Loans
            FROM Loans l JOIN Books b ON b.Id = l.BookId
            WHERE l.LoanedAt >= @since
            GROUP BY l.BookId
            ORDER BY Loans DESC, b.Title
            LIMIT @limit
            """, new { since, limit }).ToList();
    }

    public List<MonthCount> LoansByMonth(DateTime since)
    {
        using var c = Open();
        return c.Query<MonthCount>("""
            SELECT substr(LoanedAt, 1, 7) AS Month, COUNT(*) AS Loans
            FROM Loans WHERE LoanedAt >= @since
            GROUP BY Month ORDER BY Month
            """, new { since }).ToList();
    }

    public List<Book> NeverLent(int limit = 100)
    {
        using var c = Open();
        return c.Query<Book>($"""
            SELECT b.*, {AvailableExpr} AS Available
            FROM Books b
            WHERE b.Archived = 0 AND NOT EXISTS (SELECT 1 FROM Loans l WHERE l.BookId = b.Id)
            ORDER BY b.Title LIMIT @limit
            """, new { limit }).ToList();
    }
}
