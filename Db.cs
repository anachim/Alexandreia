using System.ComponentModel.DataAnnotations;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Alexandreia;

/// <summary>
/// One book = one physical copy. Three copies means three records: deduplication and
/// copy counting are the data owner's problem, not ours.
/// </summary>
public class Book
{
    public long Id { get; set; }

    [Required(ErrorMessage = "Il titolo è obbligatorio.")]
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Notes { get; set; }
    public bool Archived { get; set; }

    // Computed by the query, not columns.
    public bool IsAvailable { get; set; }
    public string? LentTo { get; set; }
}

/// <summary>Whoever borrows. Same-name people are the library's problem, solved with the note.</summary>
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

    /// <summary>
    /// How it reads in the lending dropdown: without the note, two people with the same
    /// name are indistinguishable exactly when one of them has to be picked.
    /// </summary>
    public string Label => Notes is { Length: > 0 } n
        ? $"{FullName} — {(n.Length > 40 ? n[..39] + "…" : n)}"
        : FullName;
}

/// <summary>Which loans to show. The Italian labels live in the view.</summary>
public static class Filtri
{
    public const string Fuori = "fuori";
    public const string Ritardo = "ritardo";
    public const string Rientrati = "rientrati";
    public const string Tutti = "tutti";
}

public class Loan
{
    public long Id { get; set; }
    public long BookId { get; set; }
    public long MemberId { get; set; }
    public DateTime LoanedAt { get; set; }
    public DateTime DueAt { get; set; }
    public DateTime? ReturnedAt { get; set; }

    // From the join.
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string MemberName { get; set; } = "";

    public bool IsOpen => ReturnedAt is null;
    public bool Overdue => IsOpen && DueAt.Date < DateTime.Today;

    public int LateDays => Overdue ? (DateTime.Today - DueAt.Date).Days : 0;

    public string DueLabel => DueAt.ToString("dd/MM/yyyy");

    /// <summary>How this loan stands, spelled out. Carries the return date too.</summary>
    public string Stato => !IsOpen ? $"Rientrato il {ReturnedAt:dd/MM/yyyy}"
        : Overdue ? $"In ritardo di {LateDays} {(LateDays == 1 ? "giorno" : "giorni")}"
        : "In regola";
}

// Settable properties, not positional records: SQLite declares no type for computed columns
// (COUNT, AVG) and reports them as byte[] on an empty result. Dapper demands the exact type in
// constructors, while on properties it converts by itself.
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
    public string? Notes { get; set; } // per distinguere quale copia fisica è
    public int Loans { get; set; }
}

public record MonthCount
{
    public string Month { get; set; } = "";
    public int Loans { get; set; }
}

public class Db
{
    /// <summary>Bump when the schema changes in an incompatible way.</summary>
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

        CREATE TABLE IF NOT EXISTS Settings (
            Key   TEXT NOT NULL PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;

    /// <summary>App preferences, kept next to the data instead of in a separate file.</summary>
    public const string TemaKey = "tema";

    // A book is a single copy: it is free when it has no still-open loan.
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

        // An archive from an older schema must not be opened blindly: better to say so
        // than to let the queries fail one at a time.
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

    // --- Preferences ----------------------------------------------------

    public string? Setting(string key)
    {
        using var c = Open();
        return c.QuerySingleOrDefault<string>("SELECT Value FROM Settings WHERE Key = @key", new { key });
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        c.Execute("INSERT INTO Settings (Key, Value) VALUES (@key, @value) " +
                  "ON CONFLICT(Key) DO UPDATE SET Value = @value", new { key, value });
    }

    // --- Books ----------------------------------------------------------

    public List<Book> Books(string? search = null, bool onlyAvailable = false, int limit = 500)
    {
        using var c = Open();
        // ponytail: LIKE over a few thousand rows is instant. FTS5 only past ~50k.
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

    /// <summary>Archives the book. Refuses while it is out on loan. The history stays.</summary>
    public bool ArchiveBook(long id)
    {
        using var c = Open();
        return c.Execute("""
            UPDATE Books SET Archived = 1
            WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Loans WHERE BookId = @id AND ReturnedAt IS NULL)
            """, new { id }) == 1;
    }

    // --- Members --------------------------------------------------------

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

    /// <summary>Archives the member. Refuses while they still have books out.</summary>
    public bool ArchiveMember(long id)
    {
        using var c = Open();
        return c.Execute("""
            UPDATE Members SET Archived = 1
            WHERE Id = @id AND NOT EXISTS (SELECT 1 FROM Loans WHERE MemberId = @id AND ReturnedAt IS NULL)
            """, new { id }) == 1;
    }

    // --- Loans ----------------------------------------------------------

    /// <summary>Records a loan. False when the book is already out.</summary>
    public bool Lend(long bookId, long memberId, DateTime dueAt)
    {
        using var c = Open();
        // Check and insert in a single statement: no window in which the same book
        // goes out twice.
        return c.Execute("""
            INSERT INTO Loans (BookId, MemberId, LoanedAt, DueAt)
            SELECT @bookId, @memberId, @now, @dueAt
            WHERE EXISTS (SELECT 1 FROM Books WHERE Id = @bookId AND Archived = 0)
              AND EXISTS (SELECT 1 FROM Members WHERE Id = @memberId AND Archived = 0)
              AND NOT EXISTS (SELECT 1 FROM Loans WHERE BookId = @bookId AND ReturnedAt IS NULL)
            """, new { bookId, memberId, now = DateTime.Now, dueAt = dueAt.Date }) == 1;
    }

    /// <summary>Moves the due date of an open loan. False when it was already returned.</summary>
    public bool Extend(long loanId, DateTime newDue)
    {
        using var c = Open();
        return c.Execute("UPDATE Loans SET DueAt = @due WHERE Id = @loanId AND ReturnedAt IS NULL",
            new { loanId, due = newDue.Date }) == 1;
    }

    /// <summary>Records the return. False when that loan was already closed.</summary>
    public bool Return(long loanId)
    {
        using var c = Open();
        return c.Execute("UPDATE Loans SET ReturnedAt = @now WHERE Id = @loanId AND ReturnedAt IS NULL",
            new { loanId, now = DateTime.Now }) == 1;
    }

    public List<Loan> Loans(string filter = Filtri.Fuori, string? search = null, int limit = 500)
    {
        using var c = Open();
        return c.Query<Loan>("""
            SELECT l.*, b.Title, b.Author, TRIM(m.LastName || ' ' || m.FirstName) AS MemberName
            FROM Loans l
            JOIN Books b   ON b.Id = l.BookId
            JOIN Members m ON m.Id = l.MemberId
            WHERE (   (@filter = 'tutti')
                   OR (@filter = 'fuori'     AND l.ReturnedAt IS NULL)
                   OR (@filter = 'ritardo'   AND l.ReturnedAt IS NULL AND l.DueAt < @today)
                   OR (@filter = 'rientrati' AND l.ReturnedAt IS NOT NULL))
              AND (@q IS NULL OR b.Title LIKE @like OR m.LastName LIKE @like OR m.FirstName LIKE @like)
            ORDER BY l.ReturnedAt IS NOT NULL, l.DueAt, l.Id DESC
            LIMIT @limit
            """,
            new
            {
                filter,
                today = DateTime.Today,
                q = string.IsNullOrWhiteSpace(search) ? null : search,
                like = $"%{search}%",
                limit,
            })
            .ToList();
    }

    // --- Metrics --------------------------------------------------------

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

    public List<TopBook> TopBooks(DateTime since, DateTime? until = null, int limit = 20)
    {
        using var c = Open();
        return c.Query<TopBook>("""
            SELECT b.Title, b.Author, b.Notes, COUNT(*) AS Loans
            FROM Loans l JOIN Books b ON b.Id = l.BookId
            WHERE l.LoanedAt >= @since AND (@until IS NULL OR l.LoanedAt < @until)
            GROUP BY l.BookId
            ORDER BY Loans DESC, b.Title
            LIMIT @limit
            """, new { since, until, limit }).ToList();
    }

    public List<MonthCount> LoansByMonth(DateTime since, DateTime? until = null)
    {
        using var c = Open();
        return c.Query<MonthCount>("""
            SELECT substr(LoanedAt, 1, 7) AS Month, COUNT(*) AS Loans
            FROM Loans
            WHERE LoanedAt >= @since AND (@until IS NULL OR LoanedAt < @until)
            GROUP BY Month ORDER BY Month
            """, new { since, until }).ToList();
    }

    /// <summary>What happened inside a window, so it can be compared with the previous one.</summary>
    public record Window
    {
        public int Loans { get; set; }
        public double AvgDays { get; set; }
        public int People { get; set; }
    }

    public Window InWindow(DateTime from, DateTime? to = null)
    {
        using var c = Open();
        return c.QuerySingle<Window>("""
            SELECT
              (SELECT COUNT(*) FROM Loans
                 WHERE LoanedAt >= @from AND (@to IS NULL OR LoanedAt < @to))            AS Loans,
              (SELECT IFNULL(AVG(julianday(ReturnedAt) - julianday(LoanedAt)), 0.0) FROM Loans
                 WHERE ReturnedAt IS NOT NULL
                   AND LoanedAt >= @from AND (@to IS NULL OR LoanedAt < @to))            AS AvgDays,
              (SELECT COUNT(DISTINCT MemberId) FROM Loans
                 WHERE LoanedAt >= @from AND (@to IS NULL OR LoanedAt < @to))            AS People
            """, new { from, to });
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

    // --- Import and export ----------------------------------------------

    /// <summary>A row of the exchange file: a book and, if any, who has it right now.</summary>
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
    /// Writes the rows read from a sheet: the books, and for those carrying a person it
    /// creates the member if missing and opens the loan. All in one transaction: if
    /// anything is off, the archive stays as it was instead of ending up half written.
    /// </summary>
    /// <param name="replace">Empties the archive before writing (restore from backup).</param>
    public int Apply(IEnumerable<ImportedRow> rows, bool replace = false) =>
        ApplyAll(rows, [], [], replace).Books;

    public record ApplyCounts(int Books, int OpenLoans, int History, int HistorySkipped, int Members);

    /// <summary>
    /// Loads the three sheets: members, books and history. Members go first, so the names
    /// appearing under "Prestato a" find the right person with first and last name kept
    /// apart, instead of creating a new one with the whole name glued into the surname.
    /// History rows create no books: they attach by title + author.
    /// All in a single transaction, so a half-broken file leaves no half-written archive.
    /// </summary>
    public ApplyCounts ApplyAll(
        IEnumerable<ImportedRow> archivio,
        IEnumerable<ImportedRow> storico,
        IEnumerable<Member> anagrafica,
        bool replace = false)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();

        if (replace)
            c.Execute("DELETE FROM Loans; DELETE FROM Members; DELETE FROM Books;", transaction: tx);

        var persone = c.Query<Member>("SELECT Id, FirstName, LastName FROM Members", transaction: tx)
            .GroupBy(m => NameKey(m.FullName))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var utenti = 0;
        foreach (var m in anagrafica)
        {
            var chiave = NameKey(m.FullName);
            if (persone.ContainsKey(chiave)) continue;

            persone[chiave] = c.ExecuteScalar<long>("""
                INSERT INTO Members (FirstName, LastName, Notes)
                VALUES (@FirstName, @LastName, @Notes) RETURNING Id
                """, m, tx);
            utenti++;
        }

        // With several copies of the same title the history all lands on the first one:
        // which physical copy was out in 2019 is nobody's knowledge any more, and for the
        // metrics — which group by book — the count adds up all the same.
        var libri = replace
            ? []
            : c.Query<Book>("SELECT Id, Title, Author FROM Books", transaction: tx)
                .GroupBy(b => NameKey($"{b.Title}|{b.Author}"))
                .ToDictionary(g => g.Key, g => g.First().Id);

        long PersonaId(ImportedRow row)
        {
            var persona = row.Person!.Trim();
            var chiave = NameKey(persona);
            if (persone.TryGetValue(chiave, out var id)) return id;

            // The whole name lands in the surname: "Rossi Mario" and "Mario Rossi" are
            // indistinguishable, and splitting them wrong is worse than not splitting.
            id = c.ExecuteScalar<long>("""
                INSERT INTO Members (FirstName, LastName, Notes)
                VALUES ('', @persona, @note) RETURNING Id
                """, new { persona, note = row.PersonNotes }, tx);
            persone[chiave] = id;
            return id;
        }

        void Presta(long bookId, ImportedRow row, DateTime? rientrato)
        {
            var prestatoIl = row.LoanedAt ?? DateTime.Today;
            c.Execute("""
                INSERT INTO Loans (BookId, MemberId, LoanedAt, DueAt, ReturnedAt)
                VALUES (@bookId, @memberId, @prestatoIl, @entro, @rientrato)
                """,
                new
                {
                    bookId,
                    memberId = PersonaId(row),
                    prestatoIl,
                    entro = (row.DueAt ?? prestatoIl.AddDays(Import.DefaultLoanDays)).Date,
                    rientrato,
                }, tx);
        }

        int scritti = 0, aperti = 0;
        foreach (var row in archivio)
        {
            var bookId = c.ExecuteScalar<long>(
                "INSERT INTO Books (Title, Author, Notes) VALUES (@Title, @Author, @Notes) RETURNING Id",
                row.Book, tx);
            scritti++;
            libri.TryAdd(NameKey($"{row.Book.Title}|{row.Book.Author}"), bookId);

            if (!row.HasLoan) continue;
            Presta(bookId, row, null);
            aperti++;
        }

        int storici = 0, saltati = 0;
        foreach (var row in storico)
        {
            // Still-open loans already arrived from the books sheet.
            if (row.ReturnedAt is null || !row.HasLoan) continue;

            if (!libri.TryGetValue(NameKey($"{row.Book.Title}|{row.Book.Author}"), out var bookId))
            {
                saltati++;
                continue;
            }

            Presta(bookId, row, row.ReturnedAt);
            storici++;
        }

        tx.Commit();
        return new ApplyCounts(scritti, aperti, storici, saltati, utenti);
    }

    static string NameKey(string s) => string.Join(' ',
        s.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
