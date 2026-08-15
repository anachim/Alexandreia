using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;

namespace Alexandreia;

public record ColumnInfo(int Index, string Header, int Filled, string? MappedTo, string[] Samples);

/// <summary>
/// Una riga del foglio: il libro e, se c'è, a chi è prestato.
/// Nel foglio dello storico il libro non è da creare, è la chiave per ritrovarlo.
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
    public int SkippedNoTitle { get; init; }
    public List<string> Warnings { get; init; } = [];

    public List<Book> Books => [.. Rows.Select(r => r.Book)];
    public int Loans => Rows.Count(r => r.HasLoan);

    /// <summary>
    /// Un foglio con una colonna «Rientrato il» è lo storico: le sue righe non creano
    /// libri, si agganciano a quelli che ci sono già. È il secondo foglio del nostro export.
    /// </summary>
    public bool IsHistory => Columns.Any(c => c.MappedTo == Import.FReturnedAt);

    /// <summary>Niente da caricare: foglio vuoto, o nessuna colonna riconosciuta come titolo.</summary>
    public bool Empty => Rows.Count == 0;
}

/// <summary>
/// Lettura di un foglio Excel in due tempi: prima si guarda cosa c'e', poi si scrive.
/// ReadWorkbook e' l'unica parte che tocca il disco; Plan e' pura e lavora su matrici di
/// celle, cosi' la parte che puo' sbagliare e' testabile senza un .xlsx di prova.
///
/// E' anche il formato in cui esportiamo: stesse colonne, cosi' un archivio si porta da un
/// PC all'altro con un file che resta leggibile.
///
/// Nessuna deduplica: i libri si caricano come si trovano, una riga una scheda.
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

    /// <summary>Quanti giorni dura un prestito importato che non porta con sé una scadenza.</summary>
    public const int DefaultLoanDays = 30;

    // Riconoscimento per corrispondenza esatta dell'intestazione, niente euristiche furbe:
    // una colonna mappata sul campo sbagliato su 1400 righe non te ne accorgi finche' non e' tardi.
    // Un typo tipo "Titollo" non lo prendera' mai nessuna lista: per quello c'e' la tendina.
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
    ];

    public static readonly string[] Fields = [.. Synonyms.Select(s => s.Field)];

    // --- Lettura del file (unica parte che tocca il disco) ---------------

    public record SheetData(string Name, List<object?[]> Rows);

    /// <summary>
    /// Legge tutti i fogli in una passata: quale sia quello buono lo decide chi guarda, non noi.
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

    // --- Analisi e conversione (pura, testabile) -------------------------

    /// <param name="overrides">
    /// Correzioni manuali intestazione -> campo. Un valore vuoto significa "non importare".
    /// </param>
    public static ImportReport Plan(
        IReadOnlyList<object?[]> rows,
        string sheet = "",
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        var warnings = new List<string>();
        if (rows.Count == 0)
            return new ImportReport { Sheet = sheet, Warnings = ["Il foglio non contiene niente."] };

        // L'intestazione non e' per forza la prima riga: sopra ci finiscono titoli e righe vuote.
        // Prendo, fra le prime 10, quella che riconosce piu' campi.
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
            // Se due colonne puntano allo stesso campo tiene la prima e lo segnala.
            if (field is not null && !claimed.Add(field))
            {
                warnings.Add($"Colonna «{Text(headers[i])}» ignorata: {field} è già preso da un'altra colonna.");
                field = null;
            }
            map[i] = field;
        }

        if (!claimed.Contains(FTitle))
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
        var skipped = 0;

        foreach (var row in data)
        {
            object? Cell(string field)
            {
                var i = Array.IndexOf(map, field);
                return i >= 0 && i < row.Length ? row[i] : null;
            }

            var title = Text(Cell(FTitle));
            if (title is null) { skipped++; continue; }

            // Le colonne non mappate si scartano: si importano solo i campi riconosciuti.
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
            SkippedNoTitle = skipped,
            Warnings = warnings,
        };
    }

    static string? Map(string? header, IReadOnlyDictionary<string, string>? overrides)
    {
        if (header is null) return null;
        // Un override vuoto significa "questa colonna non va importata": serve a poter
        // togliere a mano un accoppiamento che il riconoscimento automatico rimetterebbe.
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

        // Prima l'italiano: in un foglio loro «03/04/2026» è il 3 aprile, non il 4 marzo.
        return DateTime.TryParse(s, Italian, DateTimeStyles.None, out var it) ? it
             : DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var inv) ? inv
             : null;
    }
}
