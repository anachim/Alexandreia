using Alexandreia;

namespace Alexandreia.Tests;

/// <summary>
/// Lavora su matrici di celle, come le restituisce ExcelDataReader: così la logica
/// che sbaglia davvero (intestazioni, mappatura, doppioni) è testabile senza un .xlsx.
/// </summary>
public class ImportTests
{
    static object?[] R(params object?[] cells) => cells;

    static readonly List<object?[]> Foglio =
    [
        R("Catalogo biblioteca", null, null, null),          // riga di intestazione decorativa
        R(null, null, null, null),                           // riga vuota
        R("Titolo", "Autore", "Anno", "Scaffale"),           // intestazione vera
        R("Elementi", "Euclide", 1482.0, "A1"),
        R("Almagesto", "Tolomeo", new DateTime(1515, 1, 1), "A2"),
    ];

    [Fact]
    public void Trova_l_intestazione_anche_se_non_e_la_prima_riga()
    {
        var r = Import.Plan(Foglio);

        Assert.Equal(2, r.HeaderRow);
        Assert.Equal(2, r.DataRows);
        Assert.Equal(["Title", "Author", "Year", "Location"], r.Columns.Select(c => c.MappedTo));
    }

    [Fact]
    public void Converte_i_valori_come_li_da_excel()
    {
        var libri = Import.Plan(Foglio).Books;

        // 1482.0 è un double, non deve diventare "1482.0"; la data diventa l'anno.
        Assert.Equal(1482, libri[0].Year);
        Assert.Equal("A1", libri[0].Location);
        Assert.Equal(1515, libri[1].Year);
        Assert.Equal("Tolomeo", libri[1].Author);
    }

    [Fact]
    public void Anno_estratto_anche_da_testo_sporco()
    {
        var r = Import.Plan([R("Titolo", "Anno"), R("Elementi", "© 1482, rist. 1990"), R("Almagesto", "s.d.")]);

        Assert.Equal(1482, r.Books[0].Year);
        Assert.Null(r.Books[1].Year);
    }

    [Fact]
    public void Righe_uguali_diventano_copie()
    {
        var r = Import.Plan([
            R("Titolo", "Autore"),
            R("Elementi", "Euclide"),
            R("elementi", "EUCLIDE"),   // stesso libro scritto diverso
            R("Almagesto", "Tolomeo"),
        ]);

        Assert.Equal(2, r.Books.Count);
        Assert.Equal(1, r.Merged);
        Assert.Equal(2, r.Books[0].Copies);
        Assert.Equal(1, r.Books[1].Copies);
    }

    [Fact]
    public void Con_no_merge_ogni_riga_resta_un_titolo()
    {
        var rows = new List<object?[]> { R("Titolo", "Autore"), R("Elementi", "Euclide"), R("Elementi", "Euclide") };

        var r = Import.Plan(rows, merge: false);

        Assert.Equal(2, r.Books.Count);
        Assert.Equal(0, r.Merged);
        Assert.All(r.Books, b => Assert.Equal(1, b.Copies));
    }

    [Fact]
    public void L_isbn_vince_sul_titolo_come_chiave()
    {
        var r = Import.Plan([
            R("Titolo", "ISBN"),
            R("Elementi", "978-88-06-12345-6"),
            R("Gli Elementi di Euclide", "9788806123456"),  // titolo diverso, stesso ISBN
        ]);

        Assert.Single(r.Books);
        Assert.Equal(2, r.Books[0].Copies);
    }

    [Fact]
    public void La_colonna_copie_viene_usata_e_sommata()
    {
        var r = Import.Plan([
            R("Titolo", "Copie"),
            R("Elementi", 3.0),
            R("Elementi", 2.0),
        ]);

        Assert.Equal(5, Assert.Single(r.Books).Copies);
    }

    [Fact]
    public void Le_colonne_sconosciute_finiscono_nelle_note()
    {
        var r = Import.Plan([
            R("Titolo", "Stato conservazione", "Donatore"),
            R("Elementi", "buono", "Ipazia"),
        ]);

        Assert.Equal("→ Notes", r.Columns[1].MappedTo ?? "→ Notes");
        Assert.Equal("Stato conservazione: buono\nDonatore: Ipazia", r.Books[0].Notes);
    }

    [Fact]
    public void Righe_senza_titolo_saltate_e_contate()
    {
        var r = Import.Plan([R("Titolo", "Autore"), R("Elementi", "Euclide"), R(null, "Tolomeo"), R("  ", "x")]);

        Assert.Single(r.Books);
        Assert.Equal(2, r.SkippedNoTitle);
    }

    [Fact]
    public void Map_forza_una_colonna_che_non_riconosce()
    {
        var rows = new List<object?[]> { R("Denominazione opera", "Chi l'ha scritto"), R("Elementi", "Euclide") };

        var senza = Import.Plan(rows);
        Assert.Contains(senza.Warnings, w => w.Contains("Titolo"));

        var con = Import.Plan(rows, overrides: new Dictionary<string, string>
        {
            ["Denominazione opera"] = "Title",
            ["Chi l'ha scritto"] = "Author",
        });
        Assert.Equal("Elementi", Assert.Single(con.Books).Title);
        Assert.Equal("Euclide", con.Books[0].Author);
    }

    [Fact]
    public void Due_colonne_sullo_stesso_campo_la_seconda_e_segnalata()
    {
        var r = Import.Plan([R("Titolo", "Title"), R("Elementi", "Elements")]);

        Assert.Equal("Elementi", Assert.Single(r.Books).Title);
        Assert.Contains(r.Warnings, w => w.Contains("già preso"));
        Assert.Contains("Elements", r.Books[0].Notes);
    }

    [Fact]
    public void Foglio_vuoto_non_esplode()
    {
        var r = Import.Plan([]);

        Assert.Empty(r.Books);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void Dal_foglio_al_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"alexandreia-import-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Db(path);
            var r = Import.Plan(Foglio);

            Assert.Equal(2, db.InsertBooks(r.Books));

            var salvati = db.Books();
            Assert.Equal(["Almagesto", "Elementi"], salvati.Select(b => b.Title));
            Assert.All(salvati, b => Assert.Equal(1, b.Available));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
