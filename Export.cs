using ClosedXML.Excel;

namespace Alexandreia;

/// <summary>
/// Export in un .xlsx con le stesse colonne che sappiamo importare: un archivio si porta
/// da un PC all'altro con un file che resta leggibile e stampabile, senza Id né codici.
///
/// Cosa NON porta con sé, per scelta del formato: lo storico dei prestiti già rientrati
/// (quindi le metriche ripartono da zero sul PC di destinazione) e le persone che al
/// momento non hanno niente fuori.
/// </summary>
public static class Export
{
    public const string SheetArchive = "Archivio";
    public const string SheetHistory = "Storico";

    public static readonly string[] Headers =
    [
        Import.FTitle, Import.FAuthor, Import.FNotes,
        Import.FPerson, Import.FPersonNotes, Import.FLoanedAt, Import.FDueAt,
    ];

    public static readonly string[] HistoryHeaders =
    [
        Import.FTitle, Import.FAuthor, Import.FPerson,
        Import.FLoanedAt, Import.FDueAt, Import.FReturnedAt,
    ];

    public static string SuggestedName(DateTime now) => $"alexandreia-{now:yyyy-MM-dd}.xlsx";

    public record Counts(int Books, int Loans);

    public static Counts Write(Db db, string path)
    {
        var righe = db.ForExport();
        var storico = db.Loans(openOnly: false, limit: int.MaxValue);

        using var wb = new XLWorkbook();

        var ws = wb.AddWorksheet(SheetArchive);
        Intestazioni(ws, Headers);
        for (var r = 0; r < righe.Count; r++)
        {
            var riga = righe[r];
            var y = r + 2;
            ws.Cell(y, 1).Value = riga.Title;
            ws.Cell(y, 2).Value = riga.Author;
            ws.Cell(y, 3).Value = riga.Notes ?? "";
            ws.Cell(y, 4).Value = riga.Person ?? "";
            ws.Cell(y, 5).Value = riga.PersonNotes ?? "";
            if (riga.LoanedAt is { } dal) ws.Cell(y, 6).Value = dal.Date;
            if (riga.DueAt is { } al) ws.Cell(y, 7).Value = al.Date;
        }
        Date(ws, 6, 7);
        Chiudi(ws);

        // Secondo foglio: tutti i prestiti, anche quelli già rientrati. Una riga per
        // prestito, non due eventi: se «Rientrato il» è vuoto, quel libro è ancora fuori.
        var st = wb.AddWorksheet(SheetHistory);
        Intestazioni(st, HistoryHeaders);
        for (var r = 0; r < storico.Count; r++)
        {
            var l = storico[r];
            var y = r + 2;
            st.Cell(y, 1).Value = l.Title;
            st.Cell(y, 2).Value = l.Author;
            st.Cell(y, 3).Value = l.MemberName;
            st.Cell(y, 4).Value = l.LoanedAt.Date;
            st.Cell(y, 5).Value = l.DueAt.Date;
            if (l.ReturnedAt is { } reso) st.Cell(y, 6).Value = reso.Date;
        }
        Date(st, 4, 6);
        Chiudi(st);

        wb.SaveAs(path);
        return new Counts(righe.Count, storico.Count);
    }

    static void Intestazioni(IXLWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
    }

    // Le date come date vere, non come testo: chi apre il file deve poterci filtrare.
    static void Date(IXLWorksheet ws, int da, int a)
    {
        for (var i = da; i <= a; i++) ws.Column(i).Style.DateFormat.Format = "dd/MM/yyyy";
    }

    static void Chiudi(IXLWorksheet ws)
    {
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 200, 8, 60);
    }
}
