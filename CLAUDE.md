# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Gestionale di biblioteca offline: CRUD libri, prestiti/rientri, metriche. Blazor Server in ascolto
su loopback, SQLite via Dapper, distribuito come cartella autonoma. Nessuna connessione di rete.

Il codice, i commenti, i messaggi a video e i commit sono **in italiano**. Anche i nomi dei test.

## Comandi

```bash
dotnet run                                    # avvia; apre il browser su 127.0.0.1
dotnet build
dotnet test tests                             # tutti i test
dotnet test tests --filter "FullyQualifiedName~Ultima_copia"   # un test solo

# import Excel: senza --apply non tocca il database
dotnet run -- --import libri.xlsx
dotnet run -- --import libri.xlsx --apply

dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Variabili utili in test e script: `ALEXANDREIA_DB` (percorso del database),
`ALEXANDREIA_NO_BROWSER=1` (non aprire il browser). Senza la seconda, ogni avvio apre una scheda.

L'app sceglie una **porta libera** (`127.0.0.1:0`): per interrogarla via HTTP bisogna leggere la
riga `Alexandreia: http://...` dallo stdout, non assumere una porta fissa.

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

`Book.Available` **non è una colonna**: è calcolata nella query (`AvailableExpr`) e riempita da
Dapper come proprietà. Le INSERT/UPDATE elencano le colonne esplicitamente, quindi non la scrivono.

## Cancellazione

I libri si **archiviano** (`Books.Archived = 1`), non si cancellano: eliminare il record porterebbe
via lo storico prestiti, cioè le metriche. Le query di elenco filtrano `Archived = 0`; quelle
storiche no.

## Import Excel

`Import.cs` è diviso in due apposta:

- `ReadSheet(path)` — unica parte che tocca il disco, restituisce `List<object?[]>`
- `Plan(rows, ...)` — pura, lavora su matrici di celle

Quindi **i test dell'import non usano nessun `.xlsx`**: passano array di `object?`. Se aggiungi
logica, mettila in `Plan`.

Le intestazioni sono riconosciute per **corrispondenza esatta** contro `Synonyms`, deliberatamente
senza euristiche di tipo "contiene" o "inizia per": una colonna mappata sul campo sbagliato su 1400
righe non si nota. Ciò che non riconosce finisce in `Notes` e si corregge con `--map "Col=Campo"`.
Non "migliorare" questo in fuzzy matching.

`--apply` è obbligatorio per scrivere, e un secondo import è rifiutato senza `--force`.

## Rilascio

CI su ogni push a `main`/`develop` e su ogni PR. La release parte **solo da un tag `v*`**, mai da un
merge. Un solo runner Ubuntu cross-compila per `win-x64`, `linux-x64` e `osx-arm64`.

Il publish **non produce un file solo**: esce una cartella (~100 MB) con eseguibile, `wwwroot` e il
manifest degli static assets. Va copiata intera; funziona anche spostandola (verificato).

## Struttura del progetto

Il progetto web sta nella root e il progetto di test in `tests/`, dentro il suo glob. Per questo
`Alexandreia.csproj` ha `<Compile Remove="tests\**" />`: senza, i file di test finirebbero
compilati due volte.
