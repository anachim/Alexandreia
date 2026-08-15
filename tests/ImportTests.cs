using Alexandreia;

namespace Alexandreia.Tests;

/// <summary>
/// Lavora su matrici di celle, come le restituisce ExcelDataReader: così la logica
/// che sbaglia davvero (intestazioni, mappatura, prestiti) è testabile senza un .xlsx.
/// </summary>
public class ImportTests
{
    static object?[] R(params object?[] cells) => cells;

    static readonly List<object?[]> Foglio =
    [
        R("Catalogo biblioteca", null, null),      // riga decorativa
        R(null, null, null),                       // riga vuota
        R("Titolo", "Autore", "Prestato a"),       // intestazione vera
        R("Elementi", "Euclide", "Ipazia"),
        R("Almagesto", "Tolomeo", null),
    ];

    [Fact]
    public void Trova_l_intestazione_anche_se_non_e_la_prima_riga()
    {
        var r = Import.Plan(Foglio);

        Assert.Equal(2, r.HeaderRow);
        Assert.Equal(2, r.DataRows);
        Assert.Equal([Import.FTitle, Import.FAuthor, Import.FPerson], r.Columns.Select(c => c.MappedTo));
    }

    [Fact]
    public void La_colonna_prestato_a_diventa_un_prestito()
    {
        var r = Import.Plan(Foglio);

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(1, r.Loans);
        Assert.Equal("Ipazia", r.Rows[0].Person);
        Assert.True(r.Rows[0].HasLoan);
        Assert.False(r.Rows[1].HasLoan);
    }

    [Fact]
    public void Si_importano_solo_titolo_autore_e_nota()
    {
        var r = Import.Plan([
            R("Titolo", "Autore", "ISBN", "Anno", "Collocazione", "Nota"),
            R("Elementi", "Euclide", "978-88-06-12345-6", 1482.0, "A1", "rilegato"),
        ]);

        var libro = Assert.Single(r.Rows).Book;
        Assert.Equal("Elementi", libro.Title);
        Assert.Equal("Euclide", libro.Author);
        Assert.Equal("rilegato", libro.Notes);

        // ISBN, anno e collocazione non sono campi nostri e vengono scartati, non accodati.
        Assert.DoesNotContain("978", libro.Notes);
        Assert.DoesNotContain("A1", libro.Notes);
    }

    [Fact]
    public void Senza_colonna_nota_la_nota_resta_vuota()
    {
        var r = Import.Plan([R("Titolo", "Autore", "Stato"), R("Elementi", "Euclide", "buono")]);

        Assert.Null(Assert.Single(r.Rows).Book.Notes);
    }

    [Fact]
    public void Righe_uguali_restano_schede_separate()
    {
        // Nessuna deduplica: tre copie sono tre righe, e a farle è chi possiede i dati.
        var r = Import.Plan([
            R("Titolo", "Autore"),
            R("Elementi", "Euclide"),
            R("elementi", "EUCLIDE"),
            R("Almagesto", "Tolomeo"),
        ]);

        Assert.Equal(3, r.Rows.Count);
    }

    [Fact]
    public void Le_date_del_prestito_si_leggono_all_italiana()
    {
        var r = Import.Plan([
            R("Titolo", "Prestato a", "Prestato il", "Rientro entro"),
            R("Elementi", "Ipazia", "03/04/2026", new DateTime(2026, 5, 3)),
        ]);

        var riga = Assert.Single(r.Rows);
        Assert.Equal(new DateTime(2026, 4, 3), riga.LoanedAt); // 3 aprile, non 4 marzo
        Assert.Equal(new DateTime(2026, 5, 3), riga.DueAt);
    }

    [Fact]
    public void Righe_senza_titolo_saltate_e_contate()
    {
        var r = Import.Plan([R("Titolo", "Autore"), R("Elementi", "Euclide"), R(null, "Tolomeo"), R("  ", "x")]);

        Assert.Single(r.Rows);
        Assert.Equal(2, r.SkippedNoTitle);
    }

    [Fact]
    public void Si_puo_forzare_una_colonna_che_non_riconosce()
    {
        var rows = new List<object?[]> { R("Denominazione opera", "Chi l'ha scritto"), R("Elementi", "Euclide") };

        var senza = Import.Plan(rows);
        Assert.Contains(senza.Warnings, w => w.Contains("Titolo"));

        var con = Import.Plan(rows, overrides: new Dictionary<string, string>
        {
            ["Denominazione opera"] = Import.FTitle,
            ["Chi l'ha scritto"] = Import.FAuthor,
        });
        Assert.Equal("Elementi", Assert.Single(con.Rows).Book.Title);
        Assert.Equal("Euclide", con.Rows[0].Book.Author);
    }

    [Fact]
    public void Due_colonne_sullo_stesso_campo_la_seconda_e_segnalata()
    {
        var r = Import.Plan([R("Titolo", "Libro"), R("Elementi", "Elements")]);

        Assert.Equal("Elementi", Assert.Single(r.Rows).Book.Title);
        Assert.Contains(r.Warnings, w => w.Contains("già preso"));
    }

    [Fact]
    public void Legge_tutti_i_fogli_del_file()
    {
        // Il ciclo su NextResult() è facile da sbagliare fermandosi al primo foglio.
        var fogli = Import.ReadWorkbook(Fixture("multifoglio.xlsx"));

        Assert.Equal(["Appunti", "Catalogo"], fogli.Select(f => f.Name));
        Assert.Equal(2, Import.Plan(fogli[1].Rows).Rows.Count);
    }

    [Fact]
    public void Foglio_vuoto_non_esplode()
    {
        var r = Import.Plan([]);

        Assert.Empty(r.Rows);
        Assert.True(r.Empty);
        Assert.NotEmpty(r.Warnings);
    }

    [Fact]
    public void Dal_foglio_al_database_con_i_prestiti()
    {
        Con(db =>
        {
            var r = Import.Plan(Foglio);

            Assert.Equal(2, db.Apply(r.Rows));

            Assert.Equal(["Almagesto", "Elementi"], db.Books().Select(b => b.Title));
            Assert.Equal("Ipazia", db.Books().Single(b => b.Title == "Elementi").LentTo);

            // L'utente è stato creato al volo, col nome intero nel cognome.
            var utente = Assert.Single(db.Members());
            Assert.Equal("Ipazia", utente.LastName);
            Assert.Equal(1, utente.OpenLoans);
        });
    }

    [Fact]
    public void Lo_stesso_nome_su_piu_righe_e_un_utente_solo()
    {
        Con(db =>
        {
            var r = Import.Plan([
                R("Titolo", "Prestato a"),
                R("Elementi", "Ipazia"),
                R("Almagesto", "  ipazia  "),
            ]);

            db.Apply(r.Rows);

            Assert.Single(db.Members());
            Assert.Equal(2, db.Members().Single().OpenLoans);
        });
    }

    [Fact]
    public void Senza_scadenza_il_prestito_ne_prende_una_di_default()
    {
        Con(db =>
        {
            db.Apply(Import.Plan([R("Titolo", "Prestato a"), R("Elementi", "Ipazia")]).Rows);

            var prestito = Assert.Single(db.Loans());
            Assert.Equal(DateTime.Today.AddDays(Import.DefaultLoanDays), prestito.DueAt);
        });
    }

    [Fact]
    public void Sostituisci_svuota_prima_di_scrivere()
    {
        Con(db =>
        {
            db.SaveBook(new Book { Title = "Vecchio" });
            db.SaveMember(new Member { LastName = "Vecchia" });

            db.Apply(Import.Plan([R("Titolo"), R("Elementi")]).Rows, replace: true);

            Assert.Equal("Elementi", Assert.Single(db.Books()).Title);
            Assert.Empty(db.Members());
        });
    }

    // --- Export ---------------------------------------------------------

    [Fact]
    public void L_export_rilegge_quello_che_ha_scritto()
    {
        Con(db =>
        {
            db.Apply(Import.Plan([
                R("Titolo", "Autore", "Nota", "Prestato a"),
                R("Elementi", "Euclide", "rilegato", "Ipazia"),
                R("Almagesto", "Tolomeo", null, null),
            ]).Rows);

            var file = Path.Combine(Path.GetTempPath(), $"alexandreia-export-{Guid.NewGuid():N}.xlsx");
            try
            {
                Assert.Equal(2, Export.Write(db, file).Books);

                // Il giro completo: quello che esce si rilegge senza mappature a mano.
                var fogli = Import.ReadWorkbook(file);
                var r = Import.Plan(fogli[0].Rows);

                Assert.Equal(2, r.Rows.Count);
                Assert.Equal(1, r.Loans);

                var elementi = r.Rows.Single(x => x.Book.Title == "Elementi");
                Assert.Equal("Euclide", elementi.Book.Author);
                Assert.Equal("rilegato", elementi.Book.Notes);
                Assert.Equal("Ipazia", elementi.Person);
                Assert.NotNull(elementi.DueAt);
            }
            finally
            {
                File.Delete(file);
            }
        });
    }

    [Fact]
    public void Il_giro_completo_porta_anche_i_prestiti_gia_rientrati()
    {
        var file = Path.Combine(Path.GetTempPath(), $"alexandreia-giro-{Guid.NewGuid():N}.xlsx");
        try
        {
            Con(db =>
            {
                db.Apply(Import.Plan([
                    R("Titolo", "Autore", "Prestato a"),
                    R("Elementi", "Euclide", "Ipazia"),
                    R("Almagesto", "Tolomeo", null),
                ]).Rows);

                // Elementi rientra (diventa storico), Almagesto esce (resta aperto).
                db.Return(db.Loans().Single().Id);
                db.Lend(db.Books().Single(b => b.Title == "Almagesto").Id,
                        db.Members().Single().Id, DateTime.Today.AddDays(10));

                var n = Export.Write(db, file);
                Assert.Equal(2, n.Books);
                Assert.Equal(2, n.Loans); // uno chiuso e uno aperto
            });

            Con(altro =>
            {
                var fogli = Import.ReadWorkbook(file);
                Assert.Equal([Export.SheetArchive, Export.SheetHistory, Export.SheetMembers],
                    fogli.Select(f => f.Name));

                var archivio = Import.Plan(fogli[0].Rows, fogli[0].Name);
                var storico = Import.Plan(fogli[1].Rows, fogli[1].Name);
                var anagrafica = Import.Plan(fogli[2].Rows, fogli[2].Name);
                Assert.False(archivio.LooksLikeHistory);
                Assert.True(storico.LooksLikeHistory);
                Assert.True(anagrafica.LooksLikeMembers);

                var c = altro.ApplyAll(archivio.Rows, storico.Rows, anagrafica.Members, replace: true);
                Assert.Equal(2, c.Books);
                Assert.Equal(1, c.OpenLoans);
                Assert.Equal(1, c.History);      // il prestito già rientrato
                Assert.Equal(0, c.HistorySkipped);

                var s = altro.Stats();
                Assert.Equal(2, s.TotalLoans);
                Assert.Equal(1, s.OpenLoans);
                Assert.Equal(0, s.NeverLent);
                Assert.Equal("Ipazia", altro.Books().Single(b => b.Title == "Almagesto").LentTo);
            });
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void L_anagrafica_salva_chi_non_ha_niente_in_prestito_e_tiene_i_nomi_separati()
    {
        var file = Path.Combine(Path.GetTempPath(), $"alexandreia-utenti-{Guid.NewGuid():N}.xlsx");
        try
        {
            Con(db =>
            {
                db.SaveBook(new Book { Title = "Elementi", Author = "Euclide" });
                var mario = db.SaveMember(new Member { LastName = "Rossi", FirstName = "Mario", Notes = "3B" });
                db.SaveMember(new Member { LastName = "Verdi", FirstName = "Luca" }); // mai un prestito
                db.Lend(db.Books().Single().Id, mario, DateTime.Today.AddDays(20));

                Assert.Equal(2, Export.Write(db, file).Members);
            });

            Con(altro =>
            {
                var fogli = Import.ReadWorkbook(file);
                var archivio = Import.Plan(fogli[0].Rows);
                var anagrafica = Import.Plan(fogli[2].Rows);

                altro.ApplyAll(archivio.Rows, [], anagrafica.Members, replace: true);

                // «Verdi Luca» non aveva prestiti: senza il foglio Utenti sarebbe sparito.
                Assert.Equal(["Rossi", "Verdi"], altro.Members().Select(m => m.LastName));

                // E i nomi restano divisi, invece di finire tutti nel cognome.
                var mario = altro.Members().Single(m => m.LastName == "Rossi");
                Assert.Equal("Mario", mario.FirstName);
                Assert.Equal("3B", mario.Notes);
                Assert.Equal(1, mario.OpenLoans);
            });
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void I_fogli_si_riconoscono_dalle_colonne_non_dal_nome_ne_dall_ordine()
    {
        var file = Path.Combine(Path.GetTempPath(), $"alexandreia-ordine-{Guid.NewGuid():N}.xlsx");
        try
        {
            Con(db =>
            {
                db.Apply(Import.Plan([
                    R("Titolo", "Autore", "Prestato a"),
                    R("Elementi", "Euclide", "Rossi Mario"),
                    R("Almagesto", "Tolomeo", null),
                ]).Rows);
                db.Return(db.Loans().Single().Id);
                db.SaveMember(new Member { LastName = "Verdi", FirstName = "Luca" });
                Export.Write(db, file);
            });

            Con(altro =>
            {
                var fogli = Import.ReadWorkbook(file);

                // Nomi inventati e ordine sparso: quello che conta sono le colonne.
                var storico = Import.Plan(fogli[1].Rows, "Foglio2");
                var anagrafica = Import.Plan(fogli[2].Rows, "Elenco soci");
                var archivio = Import.Plan(fogli[0].Rows, "Dati");

                Assert.True(storico.LooksLikeHistory);
                Assert.True(anagrafica.LooksLikeMembers);
                Assert.False(archivio.LooksLikeHistory);
                Assert.False(archivio.LooksLikeMembers);

                var c = altro.ApplyAll(archivio.Rows, storico.Rows, anagrafica.Members, replace: true);
                Assert.Equal(2, c.Books);
                Assert.Equal(1, c.History);
                Assert.Equal(0, c.HistorySkipped);
                Assert.Equal(["Rossi Mario", "Verdi Luca"], altro.Members().Select(m => m.FullName));
            });
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Un_intestazione_che_non_riconosce_si_corregge_e_il_foglio_cambia_tipo()
    {
        var rows = new List<object?[]>
        {
            R("Titolo", "Autore", "Prestato a", "Quando e' tornato"),
            R("Elementi", "Euclide", "Rossi Mario", "10/01/2026"),
        };

        // «Quando e' tornato» non è in nessuna lista: il foglio sembra un elenco di libri.
        Assert.False(Import.Plan(rows).LooksLikeHistory);

        // Basta indicarlo a mano dalla tendina e il foglio diventa storico.
        var corretto = Import.Plan(rows, overrides: new Dictionary<string, string>
        {
            ["Quando e' tornato"] = Import.FReturnedAt,
        });
        Assert.True(corretto.LooksLikeHistory);
        Assert.Equal(new DateTime(2026, 1, 10), Assert.Single(corretto.Rows).ReturnedAt);
    }

    [Fact]
    public void L_anagrafica_non_duplica_chi_c_e_gia()
    {
        Con(db =>
        {
            db.SaveMember(new Member { LastName = "Rossi", FirstName = "Mario" });

            var anagrafica = Import.Plan([
                R("Cognome", "Nome", "Nota della persona"),
                R("Rossi", "Mario", "già presente"),
                R("Bianchi", "Anna", null),
            ]);
            Assert.True(anagrafica.LooksLikeMembers);

            var c = db.ApplyAll([], [], anagrafica.Members);

            Assert.Equal(1, c.Members); // solo Bianchi
            Assert.Equal(["Bianchi", "Rossi"], db.Members().Select(m => m.LastName));
        });
    }

    [Fact]
    public void Nella_tendina_del_prestito_gli_omonimi_si_distinguono_dalla_nota()
    {
        var senza = new Member { LastName = "Rossi", FirstName = "Mario" };
        var con = new Member { LastName = "Rossi", FirstName = "Mario", Notes = "classe 3B" };

        Assert.Equal("Rossi Mario", senza.Label);
        Assert.Equal("Rossi Mario — classe 3B", con.Label);
    }

    [Fact]
    public void Lo_storico_di_un_libro_che_non_c_e_viene_saltato_e_contato()
    {
        Con(db =>
        {
            var storico = Import.Plan([
                R("Titolo", "Autore", "Prestato a", "Prestato il", "Rientrato il"),
                R("Fantasma", "Nessuno", "Ipazia", "01/01/2026", "10/01/2026"),
            ]);

            var c = db.ApplyAll([], storico.Rows, []);

            Assert.Equal(0, c.History);
            Assert.Equal(1, c.HistorySkipped);
            Assert.Equal(0, db.Stats().TotalLoans);
        });
    }

    [Fact]
    public void Lo_storico_non_crea_libri_doppi()
    {
        Con(db =>
        {
            var archivio = Import.Plan([R("Titolo", "Autore"), R("Elementi", "Euclide")]);
            var storico = Import.Plan([
                R("Titolo", "Autore", "Prestato a", "Prestato il", "Rientrato il"),
                R("Elementi", "Euclide", "Ipazia", "01/01/2026", "10/01/2026"),
                R("Elementi", "Euclide", "Eratostene", "01/02/2026", "10/02/2026"),
            ]);

            var c = db.ApplyAll(archivio.Rows, storico.Rows, []);

            Assert.Equal(1, c.Books);       // un libro solo, non tre
            Assert.Equal(2, c.History);
            Assert.Single(db.Books());
            Assert.Equal(2, db.Members().Count);
            Assert.Equal(2, db.TopBooks(DateTime.Today.AddYears(-1))[0].Loans);
        });
    }

    // --- Appoggio -------------------------------------------------------

    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    static void Con(Action<Db> prova)
    {
        var path = Path.Combine(Path.GetTempPath(), $"alexandreia-import-{Guid.NewGuid():N}.db");
        try
        {
            prova(new Db(path));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }
}
