# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Gestionale di biblioteca: CRUD libri, prestiti/rientri, metriche, import da Excel. Applicazione
desktop **Avalonia**, SQLite via Dapper, distribuita come singolo eseguibile. Nessuna rete.

Il codice, i commenti, i testi a video e i commit sono **in italiano**. Anche i nomi dei test.

## Comandi

```bash
dotnet run
dotnet build
dotnet test tests

# Un test solo. Attenzione: `--filter` è di VSTest, la Testing Platform lo IGNORA
# in silenzio (warning MTP0001) e gira tutta la suite facendoti credere il contrario.
dotnet test tests -- --filter-method "*Ultima_copia*"

dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`ALEXANDREIA_DB` punta il database altrove: usalo sempre nelle prove, per non sporcare l'archivio
vero in `%LOCALAPPDATA%\Alexandreia\`.

## Trappole di Dapper + SQLite

Le colonne calcolate (`COUNT`, `SUM`, `AVG`) **non hanno un tipo dichiarato** in SQLite. Su un
risultato vuoto Microsoft.Data.Sqlite le riporta come `byte[]`, e con dati come `Int64`.

Conseguenza: i tipi mappati da Dapper devono avere **proprietà settabili**, mai parametri
posizionali di record. Sulle proprietà Dapper converte; sui costruttori pretende il tipo esatto e
lancia `InvalidOperationException` a runtime. Vale per `Summary`, `TopBook`, `MonthCount` in
`Db.cs` — sono `record` con `{ get; set; }` apposta, non è uno stile da "sistemare".

Un test con dati non prende questo bug: serve il caso a risultato **vuoto** (vedi
`Metriche_su_archivio_senza_prestiti`).

## Le regole di prestito sono SQL atomico

`Db.Lend` e `Db.Return` sono ognuna **una singola istruzione**, non un controllo seguito da una
scrittura:

- `Lend` è `INSERT ... SELECT ... WHERE copie > prestiti_aperti` e ritorna `false` se 0 righe
- `Return` è `UPDATE ... WHERE ReturnedAt IS NULL` e ritorna `false` se era già chiuso

Non spezzarle in leggi-poi-scrivi "per leggibilità": si riaprirebbe la finestra in cui l'ultima
copia esce due volte, e servirebbero transazioni che così non servono. Stesso schema in `SaveBook`
(rifiuta di scendere sotto le copie già fuori) e `ArchiveBook`.

`Book.Available` **non è una colonna**: è calcolata nella query (`AvailableExpr`). `IsAvailable`,
`IsOpen`, `Overdue` e `DueLabel` sono proprietà calcolate che esistono per il binding XAML.

I libri si **archiviano** (`Books.Archived = 1`), non si cancellano: eliminare il record porterebbe
via lo storico prestiti, cioè le metriche.

## Trappole di Avalonia

Ognuna di queste è costata un bug vero, due dei quali invisibili finché non ho renderizzato la
finestra su immagine.

- **`DataGridTextColumn` lega in TwoWay anche con `IsReadOnly="True"`** e **riscrive nel modello**:
  una cella vuota torna indietro come `default(DateTime)`, che ha spento `Loan.IsOpen` e fatto
  sparire il bottone «Rientrato». Metti **sempre** `Mode=OneWay` su questi binding.
- **`Foreground = null` non lascia il colore predefinito, lo azzera**: il testo diventa invisibile
  senza alcun errore. Assegna il pennello solo quando serve (vedi `MetricheView.AddCard`).
- I binding sono **compilati**: ogni `DataTemplate` vuole il suo `x:DataType`, e quello sul
  `DataGrid` **non** si propaga dentro `CellTemplate`. I tipi usati nei binding devono essere
  **pubblici e a livello di namespace** (per questo `Bar` e `ColumnChoice` non sono annidati).
- Un controllo `Name="Title"` dentro una `Window` **collide con `Window.Title`**. I controlli dei
  dialoghi hanno nomi italiani anche per questo.
- Avalonia 12 ha sostituito `IDataObject` con `IDataTransfer`: il drag&drop usa
  `e.DataTransfer.TryGetFile()`, non `e.Data.GetFiles()`.
- `TextBox.Watermark` è deprecato, si chiama `PlaceholderText`.

## Test

xunit **v3** sulla Microsoft Testing Platform. Tre vincoli che si tengono a vicenda:

- il progetto di test è `<OutputType>Exe</OutputType>`
- `dotnet.config` alla radice indirizza `dotnet test` al runner giusto (dal SDK .NET 10)
- `xunit.v3` è **pinnato a 3.2.2**, la versione contro cui è compilato `Avalonia.Headless.XUnit`
  12.1.1. Con la 4.0.0 la scoperta dei test `[AvaloniaFact]` fallisce con `MissingMethodException`

I test di interfaccia (`tests/UiTests.cs`) aprono la `MainWindow` headless e premono i bottoni.
La piattaforma headless è configurata con `UseHeadlessDrawing = false` e Skia, quindi si può
**catturare la finestra come immagine** — l'unico modo di vedere davvero la UI da qui:

```csharp
window.CaptureRenderedFrame()?.Save(path);   // Avalonia.Headless
```

Se una vista non compare nell'albero visuale, manca un giro di
`Dispatcher.UIThread.RunJobs()` (vedi `Settle()`).

Attenzione: più viste hanno un `DataGrid` chiamato `Grid`. Cercare per nome dalla finestra intera
può restituire quello sbagliato — parti dalla vista.

## Struttura del progetto

L'applicazione sta nella root e il progetto di test in `tests/`, dentro il suo glob. Per questo
`Alexandreia.csproj` ha `<Compile Remove="tests\**" />`.

Sempre nel csproj, il target `RimuoviPdbDalPacchetto` toglie i `.pdb` nativi di Skia e HarfBuzz
dalla pubblicazione: sono 105 MB di simboli e `DebugType=none` non li tocca, perché arrivano come
asset nativi.

## Rilascio

CI su ogni push a `main`/`develop` e su ogni PR. La release parte **solo da un tag `v*`**, mai da un
merge. Un solo runner Ubuntu cross-compila per le tre piattaforme.
