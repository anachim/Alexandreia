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
    public static readonly string[] Headers =
    [
        Import.FTitle, Import.FAuthor, Import.FNotes,
        Import.FPerson, Import.FPersonNotes, Import.FLoanedAt, Import.FDueAt,
    ];

    public static string SuggestedName(DateTime now) => $"alexandreia-{now:yyyy-MM-dd}.xlsx";

    /// <returns>Quanti libri sono finiti nel file.</returns>
    public static int Write(Db db, string path)
    {
        var righe = db.ForExport();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Archivio");

        for (var i = 0; i < Headers.Length; i++)
            ws.Cell(1, i + 1).Value = Headers[i];
        ws.Row(1).Style.Font.Bold = true;

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

        // Le date come date vere, non come testo: chi apre il file deve poterci filtrare.
        ws.Column(6).Style.DateFormat.Format = "dd/MM/yyyy";
        ws.Column(7).Style.DateFormat.Format = "dd/MM/yyyy";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, 200, 8, 60);

        wb.SaveAs(path);
        return righe.Count;
    }
}
