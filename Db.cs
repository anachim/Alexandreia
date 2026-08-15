using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Alexandreia;

/// <summary>
/// Un libro = una copia fisica. Chi ha tre copie mette tre schede: la deduplica
/// e le copie multiple stanno a chi possiede i dati, non a noi.
/// </summary>
public class Book
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Il titolo è obbligatorio.")]
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Notes { get; set; }
    public bool Archived { get; set; }

    // Calcolate dalla query, non sono colonne.
    public bool IsAvailable { get; set; }
    public string? LentTo { get; set; }
}

/// <summary>Chi prende in prestito. Le omonimie le gestisce la biblioteca, con la nota.</summary>
public class Member
{
    public long Id { get; set; }

    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Il cognome è obbligatorio.")]
    public string LastName { get; set; } = "";
    public string? Notes { get; set; }
    public bool Archived { get; set; }

    public int OpenLoans { get; set; } // calcolata

    public string FullName => $"{LastName} {FirstName}".Trim();
}

public class Loan
{
    public long Id { get; set; }
    public long BookId { get; set; }
    public long MemberId { get; set; }
    public DateTime LoanedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    // Dal join.
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string MemberName { get; set; } = "";

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
    public int Members { get; set; }
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
    /// <summary>Alzare quando lo schema cambia in modo incompatibile.</summary>
    public const int SchemaVersion = 2;

    const string Schema = """
        PRAGMA journal_mode=WAL;

        CREATE TABLE IF NOT EXISTS Books (
            Id       INTEGER PRIMARY KEY,
            Title    TEXT    NOT NULL,
            Author   TEXT    NOT NULL DEFAULT '',
            Notes    TEXT,
            Archived INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Members (
            Id        INTEGER PRIMARY KEY,
            FirstName TEXT    NOT NULL DEFAULT '',
            LastName  TEXT    NOT NULL,
            Notes     TEXT,
            Archived  INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS Loans (
            Id         INTEGER PRIMARY KEY,
            BookId     INTEGER NOT NULL REFERENCES Books(Id),
            MemberId   INTEGER NOT NULL REFERENCES Members(Id),
            LoanedAt   TEXT    NOT NULL,
            DueAt      TEXT    NOT NULL,
            ReturnedAt TEXT
        );

        CREATE INDEX IF NOT EXISTS ix_loans_open ON Loans(BookId) WHERE ReturnedAt IS NULL;
        CREATE INDEX IF NOT EXISTS ix_loans_when ON Loans(LoanedAt);
        """;

    // Un libro è una copia sola: è libero se non ha un prestito ancora aperto.
    const string AvailableExpr =
        "NOT EXISTS (SELECT 1 FROM Loans l WHERE l.BookId = b.Id AND l.ReturnedAt IS NULL)";

    const string LentToExpr = """
        (SELECT TRIM(m.LastName || ' ' || m.FirstName)
         FROM Loans l JOIN Members m ON m.Id = l.MemberId
         WHERE l.BookId = b.Id AND l.ReturnedAt IS NULL LIMIT 1)
        """;

    readonly string _cs;

    public static string DefaultPath() =>
        Environment.GetEnvironmentVariable("ALEXANDREIA_DB")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Alexandreia", "alexandreia.db");

    public string FilePath { get; }

    public Db(string path)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = FilePath, ForeignKeys = true }.ToString();

        using var c = Open();

        // Un archivio di uno schema precedente non va aperto alla cieca: meglio dirlo
        // che far fallire le query una alla volta.
        var esiste = c.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Books'") > 0;
        var versione = c.ExecuteScalar<long>("PRAGMA user_version");
        if (esiste && versione < SchemaVersion)
            throw new InvalidOperationException(
                $"L'archivio {FilePath} è di una versione precedente del programma. " +
                "Spostalo altrove e ricomincia, oppure esportalo con la versione vecchia.");

        c.Execute(Schema);
        c.Execute($"PRAGMA user_version = {SchemaVersion}");
    }

    SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        return c;
    }

    // --- Libri ---------------------------------------------------------

    public List<Book> Books(string? search = null, bool onlyAvailable = false, int limit = 500)
    {
        using var c = Open();
        // ponytail: LIKE su qualche migliaio di righe è istantaneo. FTS5 solo oltre le ~50k.
        return c.Query<Book>($"""
            SELECT b.Id, b.Title, b.Author, b.Notes,
                   {AvailableExpr} AS IsAvailable,
                   {LentToExpr}    AS LentTo
            FROM Books b
            WHERE b.Archived = 0
              AND (@q IS NULL OR b.Title LIKE @like OR b.Author LIKE @like OR b.Notes LIKE @like)
              AND (@onlyAvailable = 0 OR {AvailableExpr})
            ORDER BY b.Title, b.Author
            LIMIT @limit
            """,
            new { q = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search}%", onlyAvailable, limit })
            .ToList();
    }

    public Book? Book(long id)
    {
        using var c = Open();
        return c.QuerySingleOrDefault<Book>($"""
            SELECT b.Id, b.Title, b.Author, b.Notes,
                   {AvailableExpr} AS IsAvailable, {LentToExpr} AS LentTo
            FROM Books b WHERE b.Id = @id
            """, new { id });
    }

    public long SaveBook(Book b)
    {
        b.Title = b.Title.Trim();
        b.Author = b.Author.Trim();

        using var c = Open();
        if (b.Id == 0)
            return c.ExecuteScalar<long>(
                "INSERT INTO Books (Title, Author, Notes) VALUES (@Title, @Author, @Notes) RETURNING Id", b);

        c.Execute("UPDATE Books SET Title=@Title, Author=@Author, Notes=@Notes WHERE Id=@Id", b);
        return b.Id;
    }

    /// <summary>Archivia il libro. Rifiuta se è fuori in prestito. Lo storico resta.</summary>
    public bool ArchiveBook(long id)
    {
        using var c = Open();
        return c.Execute("""
            UPDATE Books SET Archived = 1
            WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Loans WHERE BookId = @id AND ReturnedAt IS NULL)
            """, new { id }) == 1;
    }

    // --- Utenti --------------------------------------------------------

    public List<Member> Members(string? search = null, int limit = 500)
    {
        using var c = Open();
        return c.Query<Member>("""
            SELECT m.Id, m.FirstName, m.LastName, m.Notes,
                   (SELECT COUNT(*) FROM Loans l WHERE l.MemberId = m.Id AND l.ReturnedAt IS NULL) AS OpenLoans
            FROM Members m
            WHERE m.Archived = 0
              AND (@q IS NULL OR m.LastName LIKE @like OR m.FirstName LIKE @like OR m.Notes LIKE @like)
            ORDER BY m.LastName, m.FirstName
            LIMIT @limit
            """,
            new { q = string.IsNullOrWhiteSpace(search) ? null : search, like = $"%{search}%", limit })
            .ToList();
    }

    public long SaveMember(Member m)
    {
        m.FirstName = m.FirstName.Trim();
        m.LastName = m.LastName.Trim();

        using var c = Open();
        if (m.Id == 0)
            return c.ExecuteScalar<long>("""
                INSERT INTO Members (FirstName, LastName, Notes)
                VALUES (@FirstName, @LastName, @Notes) RETURNING Id
                """, m);

        c.Execute("UPDATE Members SET FirstName=@FirstName, LastName=@LastName, Notes=@Notes WHERE Id=@Id", m);
        return m.Id;
    }

    /// <summary>Archivia l'utente. Rifiuta se ha ancora libri fuori.</summary>
    public bool ArchiveMember(long id)
    {
        using var c = Open();
        return c.Execute("""
            UPDATE Members SET Archived = 1
            WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Loans WHERE MemberId = @id AND ReturnedAt IS NULL)
            """, new { id }) == 1;
    }

    // --- Prestiti ------------------------------------------------------

    /// <summary>Registra un prestito. False se il libro è già fuori.</summary>
    public bool Lend(long bookId, long memberId, DateTime dueAt)
    {
        using var c = Open();
        // Controllo e inserimento in un'unica istruzione: niente finestra in cui lo stesso
        // libro esce due volte.
        return c.Execute("""
            INSERT INTO Loans (BookId, MemberId, LoanedAt, DueAt)
            SELECT @bookId, @memberId, @now, @dueAt
            WHERE EXISTS (SELECT 1 FROM Books WHERE Id = @bookId AND Archived = 0)
              AND EXISTS (SELECT 1 FROM Members WHERE Id = @memberId AND Archived = 0)
              AND NOT EXISTS (SELECT 1 FROM Loans WHERE BookId = @bookId AND ReturnedAt IS NULL)
            """, new { bookId, memberId, now = DateTime.Now, dueAt = dueAt.Date }) == 1;
    }

    /// <summary>Registra il rientro. False se quel prestito era già chiuso.</summary>
    public bool Return(long loanId)
    {
        using var c = Open();
        return c.Execute("UPDATE Loans SET ReturnedAt = @now WHERE Id = @loanId AND ReturnedAt IS NULL",
            new { loanId, now = DateTime.Now }) == 1;
    }

    public List<Loan> Loans(bool openOnly = true, string? search = null, int limit = 500)
    {
        using var c = Open();
        return c.Query<Loan>("""
            SELECT l.*, b.Title, b.Author, TRIM(m.LastName || ' ' || m.FirstName) AS MemberName
            FROM Loans l
            JOIN Books b   ON b.Id = l.BookId
            JOIN Members m ON m.Id = l.MemberId
            WHERE (@openOnly = 0 OR l.ReturnedAt IS NULL)
              AND (@q IS NULL OR b.Title LIKE @like OR m.LastName LIKE @like OR m.FirstName LIKE @like)
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
              (SELECT COUNT(*) FROM Books   WHERE Archived = 0)                                  AS Books,
              (SELECT COUNT(*) FROM Members WHERE Archived = 0)                                  AS Members,
              (SELECT COUNT(*) FROM Loans   WHERE ReturnedAt IS NULL)                            AS OpenLoans,
              (SELECT COUNT(*) FROM Loans   WHERE ReturnedAt IS NULL AND DueAt < @today)         AS Overdue,
              (SELECT COUNT(*) FROM Books b WHERE b.Archived = 0
                            AND NOT EXISTS (SELECT 1 FROM Loans l WHERE l.BookId = b.Id))        AS NeverLent,
              (SELECT IFNULL(AVG(julianday(ReturnedAt) - julianday(LoanedAt)), 0.0)
                                            FROM Loans WHERE ReturnedAt IS NOT NULL)             AS AvgDays,
              (SELECT COUNT(*) FROM Loans)                                                       AS TotalLoans
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

    public List<Book> NeverLent(int limit = 200)
    {
        using var c = Open();
        return c.Query<Book>("""
            SELECT b.Id, b.Title, b.Author, b.Notes, 1 AS IsAvailable
            FROM Books b
            WHERE b.Archived = 0 AND NOT EXISTS (SELECT 1 FROM Loans l WHERE l.BookId = b.Id)
            ORDER BY b.Title LIMIT @limit
            """, new { limit }).ToList();
    }

    // --- Import ed export ----------------------------------------------

    /// <summary>Riga del file di scambio: un libro e, se c'è, chi ce l'ha adesso.</summary>
    public class ExportRow
    {
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string? Notes { get; set; }
        public string? Person { get; set; }
        public string? PersonNotes { get; set; }
        public DateTime? LoanedAt { get; set; }
        public DateTime? DueAt { get; set; }
    }

    public List<ExportRow> ForExport()
    {
        using var c = Open();
        return c.Query<ExportRow>("""
            SELECT b.Title, b.Author, b.Notes,
                   TRIM(m.LastName || ' ' || m.FirstName) AS Person,
                   m.Notes AS PersonNotes,
                   l.LoanedAt, l.DueAt
            FROM Books b
            LEFT JOIN Loans l   ON l.BookId = b.Id AND l.ReturnedAt IS NULL
            LEFT JOIN Members m ON m.Id = l.MemberId
            WHERE b.Archived = 0
            ORDER BY b.Title, b.Author
            """).ToList();
    }

    /// <summary>
    /// Scrive le righe lette da un foglio: i libri, e per quelle con una persona crea
    /// l'utente se non c'è e apre il prestito. Tutto in una transazione: se qualcosa non
    /// torna, l'archivio resta quello di prima invece di restare a metà.
    /// </summary>
    /// <param name="replace">Svuota l'archivio prima di scrivere (ripristino da backup).</param>
    public int Apply(IEnumerable<ImportedRow> rows, bool replace = false)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        if (replace)
            c.Execute("DELETE FROM Loans; DELETE FROM Members; DELETE FROM Books;", transaction: tx);

        var noti = c.Query<Member>("SELECT Id, FirstName, LastName FROM Members", transaction: tx)
            .GroupBy(m => NameKey(m.FullName))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var n = 0;
        foreach (var row in rows)
        {
            var bookId = c.ExecuteScalar<long>(
                "INSERT INTO Books (Title, Author, Notes) VALUES (@Title, @Author, @Notes) RETURNING Id",
                row.Book, tx);
            n++;

            if (!row.HasLoan) continue;

            var persona = row.Person!.Trim();
            var chiave = NameKey(persona);
            if (!noti.TryGetValue(chiave, out var memberId))
            {
                // Il nome intero finisce nel cognome: «Rossi Mario» e «Mario Rossi» sono
                // indistinguibili, e sbagliare a spezzarli è peggio che non spezzarli.
                memberId = c.ExecuteScalar<long>("""
                    INSERT INTO Members (FirstName, LastName, Notes)
                    VALUES ('', @persona, @note) RETURNING Id
                    """, new { persona, note = row.PersonNotes }, tx);
                noti[chiave] = memberId;
            }

            var prestatoIl = row.LoanedAt ?? DateTime.Today;
            c.Execute("""
                INSERT INTO Loans (BookId, MemberId, LoanedAt, DueAt)
                VALUES (@bookId, @memberId, @prestatoIl, @entro)
                """,
                new
                {
                    bookId,
                    memberId,
                    prestatoIl,
                    entro = (row.DueAt ?? prestatoIl.AddDays(Import.DefaultLoanDays)).Date,
                }, tx);
        }

        tx.Commit();
        return n;
    }

    static string NameKey(string s) => string.Join(' ',
        s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
