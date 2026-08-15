namespace Alexandreia;

/// <summary>Import da riga di comando. Di default non scrive: prima si guarda, poi si applica.</summary>
public static class Cli
{
    public const string Usage = """
        Alexandreia --import <file.xlsx> [opzioni]

          --sheet <nome>        foglio da leggere (default: il primo)
          --map "Col=Campo"     forza una colonna su un campo, ripetibile
          --no-merge            tieni ogni riga come titolo a sé, senza unire i doppioni
          --apply               scrivi davvero (senza, è solo una prova a vuoto)
          --force               importa anche se in archivio ci sono già dei libri

        Campi validi per --map: Title, Author, Isbn, Year, Publisher, Location, Copies
        """;

    public static int RunImport(string[] args)
    {
        var path = Arg(args, "--import");
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine(path is null ? Usage : $"File non trovato: {path}");
            return 1;
        }

        var overrides = new Dictionary<string, string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--map") continue;
            var parts = args[i + 1].Split('=', 2);
            if (parts.Length != 2 || !Import.Fields.Contains(parts[1]))
            {
                Console.Error.WriteLine($"--map «{args[i + 1]}» non valido. Campi: {string.Join(", ", Import.Fields)}");
                return 1;
            }
            overrides[parts[0]] = parts[1];
        }

        ImportReport report;
        try
        {
            var (sheet, rows) = Import.ReadSheet(path, Arg(args, "--sheet"));
            report = Import.Plan(rows, sheet, merge: !args.Contains("--no-merge"), overrides);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Non riesco a leggere il file: {ex.Message}");
            return 1;
        }

        Print(report, path);

        var apply = args.Contains("--apply");
        if (!apply)
        {
            Console.WriteLine();
            Console.WriteLine("Prova a vuoto: non ho scritto niente. Se il quadro sopra torna, rilancia con --apply.");
            return 0;
        }

        if (report.Books.Count == 0)
        {
            Console.Error.WriteLine("Niente da importare.");
            return 1;
        }

        var db = new Db(Db.DefaultPath());
        var existing = db.Books(limit: 1).Count;
        if (existing > 0 && !args.Contains("--force"))
        {
            Console.Error.WriteLine(
                "In archivio ci sono già dei libri: un secondo import li duplicherebbe. --force per procedere comunque.");
            return 1;
        }

        var n = db.InsertBooks(report.Books);
        Console.WriteLine($"\nImportati {n} libri in {Db.DefaultPath()}");
        return 0;
    }

    static void Print(ImportReport r, string path)
    {
        Console.WriteLine($"File    {path}");
        Console.WriteLine($"Foglio  {r.Sheet} — intestazione alla riga {r.HeaderRow + 1}, {r.DataRows} righe di dati");
        Console.WriteLine();

        var w = Math.Max(8, r.Columns.Count == 0 ? 8 : r.Columns.Max(c => c.Header.Length));
        Console.WriteLine($"{"COLONNA".PadRight(w)}  {"PIENE",6}  {"CAMPO".PadRight(11)}  ESEMPI");
        foreach (var c in r.Columns)
        {
            var samples = string.Join(" | ", c.Samples.Select(s => s.Length > 24 ? s[..23] + "…" : s));
            Console.WriteLine($"{c.Header.PadRight(w)}  {c.Filled,6}  {(c.MappedTo ?? "→ Notes").PadRight(11)}  {samples}");
        }

        Console.WriteLine();
        Console.WriteLine($"{Plural(r.DataRows, "riga", "righe")}  →  {Plural(r.Books.Count, "libro", "libri")}");
        if (r.Merged > 0)
            Console.WriteLine($"{r.Merged,12} {(r.Merged == 1 ? "riga uguale unita" : "righe uguali unite")} in copie (--no-merge per tenerle separate)");
        if (r.SkippedNoTitle > 0)
            Console.WriteLine($"{r.SkippedNoTitle,12} {(r.SkippedNoTitle == 1 ? "riga saltata" : "righe saltate")} perché senza titolo");

        foreach (var warning in r.Warnings)
            Console.WriteLine($"\n! {warning}");
    }

    static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }
}
