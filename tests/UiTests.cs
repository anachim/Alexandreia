using Alexandreia.Components.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Alexandreia.Tests;

/// <summary>
/// Preme davvero i bottoni: i test su Db coprono le regole, questi coprono che
/// l'interfaccia sia cablata a quelle regole. Restano fuori SignalR e il browser vero.
/// </summary>
public class UiTests : BunitContext
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"alexandreia-ui-{Guid.NewGuid():N}.db");
    readonly Db _db;
    readonly long _libro;

    public UiTests()
    {
        _db = new Db(_path);
        _libro = _db.SaveBook(new Book { Title = "Elementi", Author = "Euclide", Copies = 1 });
        Services.AddSingleton(_db);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(f);
    }

    [Fact]
    public void L_elenco_mostra_i_libri_con_le_copie()
    {
        var page = Render<Libri>();

        Assert.Contains("Elementi", page.Markup);
        Assert.Contains("Euclide", page.Markup);
        Assert.Contains("1 / 1", page.Markup);
    }

    [Fact]
    public void Presto_un_libro_dall_elenco()
    {
        var page = Render<Libri>();

        page.FindAll("button").Single(b => b.TextContent == "Presta").Click();
        page.Find("input[placeholder='A chi?']").Input("Ipazia");
        page.Find("form").Submit();

        var prestito = Assert.Single(_db.Loans());
        Assert.Equal("Ipazia", prestito.Borrower);
        Assert.Equal(_libro, prestito.BookId);
        Assert.Contains("prestato a Ipazia", page.Markup);
    }

    [Fact]
    public void Senza_copie_libere_il_bottone_presta_e_disabilitato()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var page = Render<Libri>();

        Assert.True(page.FindAll("button").Single(b => b.TextContent == "Presta").HasAttribute("disabled"));
        Assert.Contains("0 / 1", page.Markup);
    }

    [Fact]
    public void La_ricerca_filtra_l_elenco()
    {
        _db.SaveBook(new Book { Title = "Almagesto", Author = "Tolomeo" });
        var page = Render<Libri>();

        page.Find("input[type=search]").Input("tolomeo");

        Assert.Contains("Almagesto", page.Markup);
        Assert.DoesNotContain("Elementi", page.Markup);
    }

    [Fact]
    public void Registro_il_rientro_dalla_pagina_prestiti()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var page = Render<Prestiti>();
        Assert.Contains("Ipazia", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Rientrato").Click();

        Assert.NotNull(_db.Loans(openOnly: false).Single().ReturnedAt);
        Assert.Equal(1, _db.Book(_libro)!.Available);
    }

    [Fact]
    public void Il_ritardo_e_evidenziato()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(-3));

        var page = Render<Prestiti>();

        Assert.Contains("overdue", page.Markup);
        Assert.Contains("in ritardo di 3 gg", page.Markup);
    }

    [Fact]
    public void Aggiungo_un_libro_dalla_scheda()
    {
        var page = Render<LibroEdit>();

        page.Find("input").Change("Almagesto"); // InputText si aggiorna su onchange, non oninput
        page.Find("form").Submit();

        Assert.Contains(_db.Books(), b => b.Title == "Almagesto");
    }

    [Fact]
    public void Senza_titolo_la_scheda_non_salva_e_lo_dice()
    {
        var page = Render<LibroEdit>();

        page.Find("form").Submit();

        Assert.Single(_db.Books()); // solo quello di partenza
        Assert.Contains("Il titolo è obbligatorio", page.Markup);
    }

    [Fact]
    public void Archivio_un_libro_previa_conferma()
    {
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);

        var page = Render<Libri>();
        page.FindAll("button").Single(b => b.TextContent == "Archivia").Click();

        Assert.Empty(_db.Books());
        Assert.Contains("archiviato", page.Markup);
    }

    [Fact]
    public void Le_metriche_contano_i_prestiti()
    {
        _db.Lend(_libro, "Ipazia", DateTime.Today.AddDays(30));

        var page = Render<Metriche>();

        Assert.Contains("Elementi", page.Markup);   // fra i più prestati
        Assert.Contains("Fuori ora", page.Markup);
    }
}
