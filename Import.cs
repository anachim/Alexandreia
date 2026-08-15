using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace Alexandreia;

public record ColumnInfo(int Index, string Header, int Filled, string? MappedTo, string[] Samples);

/// <summary>
/// A row of the sheet: the book and, if any, who it is lent to.
/// In the history sheet the book is not to be created, it is the key to find it again.
/// </summary>
public class ImportedRow
{
    public required Book Book { get; init; }
    public string? Person { get; init; }
    public string? PersonNotes { get; init; }
    public DateTime? LoanedAt { get; init; }
    public DateTime? DueAt { get; init; }
    public DateTime? ReturnedAt { get; init; }

    public bool HasLoan => !string.IsNullOrWhiteSpace(Person);
}

public record ImportReport
{
    public string Sheet { get; init; } = "";
    public int HeaderRow { get; init; }
    public int DataRows { get; init; }
    public List<ColumnInfo> Columns { get; init; } = [];
    public List<ImportedRow> Rows { get; init; } = [];
    public List<Member> Members { get; init; } = [];
    public int SkippedNoTitle { get; init; }
    public List<string> Warnings { get; init; } = [];

    public List<Book> Books => [.. Rows.Select(r => r.Book)];
    public int Loans => Rows.Count(r => r.HasLoan);

    /// <summary>Whether people were built instead of books.</summary>
    public bool IsMembers { get; init; }

    /// <summary>
    /// A guess at the sheet type, from the columns: "Cognome" suggests a member list,
    /// "Rientrato il" the history. It is only the dropdown's default value — the final say
    /// is the user's, because guessing wrong here creates duplicate records in silence.
    /// </summary>
    public bool LooksLikeMembers => Columns.Any(c => c.MappedTo == Import.FLastName);
    public bool LooksLikeHistory => !LooksLikeMembers && Columns.Any(c => c.MappedTo == Import.FReturnedAt);

    /// <summary>Nothing to load.</summary>
    public bool Empty => Rows.Count == 0 && Members.Count == 0;
}

/// <summary>
/// Reading an Excel sheet in two steps: first you look at what is there, then you write.
/// ReadWorkbook is the only part touching the disk; Plan is pure and works on cell
/// matrices, so the part that can get things wrong is testable without a sample .xlsx.
///
/// It is also the format we export: same columns, so an archive moves from one PC to
/// another in a file that stays readable.
///
/// No deduplication: books are loaded as found, one row one record.
/// </summary>
public static class Import
{
    public const string FTitle = "Titolo";
    public const string FAuthor = "Autore";
    public const string FNotes = "Nota del libro";
    public const string FPerson = "Prestato a";
    public const string FPersonNotes = "Nota della persona";
    public const string FLoanedAt = "Prestato il";
    public const string FDueAt = "Rientro entro";
    public const string FReturnedAt = "Rientrato il";
    public const string FLastName = "Cognome";
    public const string FFirstName = "Nome";

    /// <summary>How many days an imported loan lasts when it carries no due date of its own.</summary>
    public const int DefaultLoanDays = 30;

    // Header matching is exact, no clever heuristics: a column mapped to the wrong field
    // across 1400 rows goes unnoticed until it is too late. A typo like "Titollo" will
    // never be caught by any list: that is what the dropdown is for.
    static readonly (string Field, string[] Names)[] Synonyms =
    [
        (FTitle,       ["titolo", "title", "opera", "libro", "volume", "denominazione", "descrizione"]),
        (FAuthor,      ["autore", "autori", "author", "authors", "curatore", "scrittore"]),
        (FNotes,       ["nota", "note", "nota libro", "nota del libro", "annotazioni", "osservazioni",
                        "commento", "commenti"]),
        (FPerson,      ["prestato a", "a chi", "a chi e prestato", "in prestito a", "chi lo ha",
                        "chi ce l ha", "persona", "utente", "lettore", "preso da"]),
        (FPersonNotes, ["nota persona", "nota della persona", "nota utente", "note persona",
                        "nota lettore"]),
        (FLoanedAt,    ["prestato il", "data prestito", "data del prestito", "dal"]),
        (FDueAt,       ["rientro entro", "scadenza", "da restituire entro", "restituzione", "al"]),
        (FReturnedAt,  ["rientrato il", "reso il", "restituito il", "data rientro", "data restituzione"]),
        (FLastName,    ["cognome", "cognome utente"]),
        (FFirstName,   ["nome", "nome utente"]),
    ];

    public static readonly string[] Fields = [.. Synonyms.Select(s => s.Field)];

    // --- Reading the file (the only part touching the disk) --------------

    public record SheetData(string Name, List<object?[]> Rows);

    /// <summary>
    /// Reads every sheet in one pass: which one is the good one is decided by whoever
    /// looks at it, not by us.
    /// </summary>
    public static List<SheetData> ReadWorkbook(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // serve ai vecchi .xls
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);

        var sheets = new List<SheetData>();
        do
        {
            var rows = new List<object?[]>();
            while (reader.Read())
            {
                var r = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++) r[i] = reader.GetValue(i);
                rows.Add(r);
            }
            sheets.Add(new SheetData(reader.Name, rows));
        } while (reader.NextResult());

        if (sheets.Count == 0) throw new InvalidOperationException("Il file non contiene fogli.");
        return sheets;
    }

    // --- Analysis and conversion (pure, testable) ------------------------

    /// <param name="overrides">
    /// Manual header -> field corrections. An empty value means "do not import".
    /// </param>
    /// <param name="asMembers">
    /// Forces the sheet to be (or not be) a member list. Null = decide it from the columns.
    /// </param>
    public static ImportReport Plan(
        IReadOnlyList<object?[]> rows,
        string sheet = "",
        IReadOnlyDictionary<string, string>? overrides = null,
        bool? asMembers = null)
    {
        var warnings = new List<string>();
        if (rows.Count == 0)
            return new ImportReport { Sheet = sheet, Warnings = ["Il foglio non contiene niente."] };

        // The header is not necessarily the first row: titles and blank rows end up above it.
        // Among the first 10, take the one that recognises the most fields.
        var headerRow = 0;
        var best = -1;
        for (var r = 0; r < Math.Min(10, rows.Count); r++)
        {
            var score = rows[r].Count(cell => Map(Text(cell), overrides) is not null);
            if (score > best) { best = score; headerRow = r; }
        }

        var headers = rows[headerRow];
        var map = new string?[headers.Length];
        var claimed = new HashSet<string>();
        for (var i = 0; i < headers.Length; i++)
        {
            var field = Map(Text(headers[i]), overrides);
            // If two columns point at the same field, keep the first and say so.
            if (field is not null && !claimed.Add(field))
            {
                warnings.Add($"Colonna «{Text(headers[i])}» ignorata: {field} è già preso da un'altra colonna.");
                field = null;
            }
            map[i] = field;
        }

        var anagrafica = asMembers ?? claimed.Contains(FLastName);
        if (anagrafica && !claimed.Contains(FLastName))
            warnings.Add("Nessuna colonna riconosciuta come Cognome: indica a mano quale colonna lo contiene.");
        if (!anagrafica && !claimed.Contains(FTitle))
            warnings.Add("Nessuna colonna riconosciuta come Titolo: indica a mano quale colonna lo contiene.");

        var data = rows.Skip(headerRow + 1).ToList();

        var columns = new List<ColumnInfo>();
        for (var i = 0; i < headers.Length; i++)
        {
            var values = data.Select(r => i < r.Length ? Text(r[i]) : null).ToList();
            columns.Add(new ColumnInfo(
                i,
                Text(headers[i]) ?? $"(colonna {i + 1})",
                values.Count(v => v is not null),
                map[i],
                [.. values.Where(v => v is not null).Distinct().Take(3)!]));
        }

        var righe = new List<ImportedRow>();
        var persone = new List<Member>();
        var skipped = 0;

        foreach (var row in data)
        {
            object? Cell(string field)
            {
                var i = Array.IndexOf(map, field);
                return i >= 0 && i < row.Length ? row[i] : null;
            }

            // Member sheet: creates people, not books. It is there so nobody who currently
            // has nothing on loan gets lost, and so first and last name survive the round trip.
            if (anagrafica)
            {
                var cognome = Text(Cell(FLastName));
                if (cognome is null) { skipped++; continue; }

                persone.Add(new Member
                {
                    LastName = cognome,
                    FirstName = Text(Cell(FFirstName)) ?? "",
                    Notes = Text(Cell(FPersonNotes)) ?? Text(Cell(FNotes)),
                });
                continue;
            }

            var title = Text(Cell(FTitle));
            if (title is null) { skipped++; continue; }

            // Unmapped columns are dropped: only the recognised fields are imported.
            righe.Add(new ImportedRow
            {
                Book = new Book
                {
                    Title = title,
                    Author = Text(Cell(FAuthor)) ?? "",
                    Notes = Text(Cell(FNotes)),
                },
                Person = Text(Cell(FPerson)),
                PersonNotes = Text(Cell(FPersonNotes)),
                LoanedAt = ToDate(Cell(FLoanedAt)),
                DueAt = ToDate(Cell(FDueAt)),
                ReturnedAt = ToDate(Cell(FReturnedAt)),
            });
        }

        return new ImportReport
        {
            Sheet = sheet,
            HeaderRow = headerRow,
            DataRows = data.Count,
            Columns = columns,
            Rows = righe,
            Members = persone,
            IsMembers = anagrafica,
            SkippedNoTitle = skipped,
            Warnings = warnings,
        };
    }

    static string? Map(string? header, IReadOnlyDictionary<string, string>? overrides)
    {
        if (header is null) return null;
        // An empty override means "do not import this column": it is what lets the user
        // remove by hand a match that the automatic recognition would put back.
        if (overrides is not null)
            foreach (var (k, v) in overrides)
                if (Normalize(k) == Normalize(header)) return v.Length == 0 ? null : v;

        var n = Normalize(header);
        return Synonyms.FirstOrDefault(s => s.Names.Contains(n)).Field;
    }

    static string Normalize(string s) =>
        Regex.Replace(
            new string(s.Normalize(NormalizationForm.FormD)
                        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                        .ToArray())
                .ToLowerInvariant(),
            @"[^a-z0-9]+", " ").Trim();

    static string? Text(object? v)
    {
        var s = v switch
        {
            null => null,
            DateTime d => d.ToString("dd/MM/yyyy"),
            double d => d == Math.Floor(d) ? ((long)d).ToString(CultureInfo.InvariantCulture)
                                           : d.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        };
        s = s?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    static DateTime? ToDate(object? v)
    {
        if (v is DateTime d) return d;
        var s = Text(v);
        if (s is null) return null;

        // Italian first: in one of their sheets "03/04/2026" is 3 April, not 4 March.
        return DateTime.TryParse(s, Italian, DateTimeStyles.None, out var it) ? it
             : DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var inv) ? inv
             : null;
    }
}
