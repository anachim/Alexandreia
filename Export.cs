using ClosedXML.Excel;

namespace Alexandreia;

/// <summary>
/// Export to an .xlsx with the same columns we can import: an archive moves from one PC
/// to another in a file that stays readable and printable, with no ids or codes.
///
/// Three sheets: the books with who holds them now, the whole loan history, and the
/// member list. Together they carry everything the destination needs.
/// </summary>
public static class Export
{
    public const string SheetArchive = "Archivio";
    public const string SheetHistory = "Storico";
    public const string SheetMembers = "Utenti";

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

    public static readonly string[] MemberHeaders = [Import.FLastName, Import.FFirstName, Import.FPersonNotes];

    public static string SuggestedName(DateTime now) => $"alexandreia-{now:yyyy-MM-dd}.xlsx";

    public record Counts(int Books, int Loans, int Members);

    public static Counts Write(Db db, string path)
    {
        var righe = db.ForExport();
        var storico = db.Loans(Filtri.Tutti, limit: int.MaxValue);
        var utenti = db.Members(limit: int.MaxValue);

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

        // Second sheet: every loan, returned ones included. One row per loan, not two
        // events: when "Rientrato il" is empty, that book is still out.
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

        // Third sheet: the whole member list, including whoever currently has nothing out —
        // without it, those people would be lost moving from one PC to another. First and
        // last name stay apart, so reloading does not glue them together.
        var ut = wb.AddWorksheet(SheetMembers);
        Intestazioni(ut, MemberHeaders);
        for (var r = 0; r < utenti.Count; r++)
        {
            var m = utenti[r];
            var y = r + 2;
            ut.Cell(y, 1).Value = m.LastName;
            ut.Cell(y, 2).Value = m.FirstName;
            ut.Cell(y, 3).Value = m.Notes ?? "";
        }
        Chiudi(ut);

        wb.SaveAs(path);
        return new Counts(righe.Count, storico.Count, utenti.Count);
    }

    static void Intestazioni(IXLWorksheet ws, string[] headers)
    {
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
    }

    // Dates as real dates, not text: whoever opens the file has to be able to filter on them.
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
