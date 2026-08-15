# Ἀλεξάνδρεια

Gestione di una piccola biblioteca: anagrafica libri, prestiti e rientri, metriche.

Applicativo **offline**: nessun server, nessuna connessione. Si mette in ascolto su `127.0.0.1`
(porta scelta dal sistema), apre il browser e tiene i dati in un file SQLite.

## Avvio in sviluppo

```
dotnet run
```

## Test

```
dotnet test tests
```

## Import da Excel

Due tempi: prima si guarda cosa c'è nel foglio, poi si scrive. Senza `--apply` non tocca niente.

```
Alexandreia --import libri.xlsx
```

Stampa, per ogni colonna, quante celle sono piene, su quale campo la mapperebbe e tre valori
d'esempio; poi quante righe diventano quanti libri. Se il quadro torna:

```
Alexandreia --import libri.xlsx --apply
```

| Opzione | |
|---|---|
| `--sheet <nome>` | foglio da leggere (default: il primo) |
| `--map "Col=Campo"` | forza una colonna su un campo, ripetibile |
| `--no-merge` | tieni ogni riga come titolo a sé |
| `--apply` | scrivi davvero |
| `--force` | importa anche se in archivio ci sono già dei libri |

Campi: `Title`, `Author`, `Isbn`, `Year`, `Publisher`, `Location`, `Copies`.

Le intestazioni sono riconosciute per **corrispondenza esatta**, senza euristiche: una colonna
mappata sul campo sbagliato su 1400 righe non te ne accorgi finché non è tardi. Quello che non
riconosce lo dice e finisce in `Notes` invece di essere buttato; si corregge con `--map`.

Righe uguali diventano **copie dello stesso libro** — chiave l'ISBN se c'è, altrimenti titolo +
autore. Il conto è nel report prima di applicare, e `--no-merge` lo disattiva.

## Rilascio

Il rilascio lo fa un **tag**, non un merge:

```
git tag v1.0.0
git push origin v1.0.0
```

La pipeline gira i test, compila per `win-x64`, `linux-x64` e `osx-arm64` e allega tre zip alla
release su GitHub. Su ogni push e ogni PR gira invece la sola CI con i test.

Per farlo a mano:

```
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**Non è un file solo**: esce una cartella da ~102 MB con l'eseguibile, `wwwroot` e il manifest degli
asset. Va copiata intera, ma è autonoma — sulla macchina di destinazione non serve installare .NET,
e funziona anche spostandola altrove (verificato).

Gli eseguibili non sono firmati: al primo avvio Windows mostrerà SmartScreen e macOS Gatekeeper li
bloccherà. Se diventa un fastidio serve un certificato di code signing, non una modifica al codice.

## Dati

Un unico file, quindi il backup è copiarlo:

| Sistema | Percorso |
|---|---|
| Windows | `%LOCALAPPDATA%\Alexandreia\alexandreia.db` |
| Linux   | `~/.local/share/Alexandreia/alexandreia.db` |
| macOS   | `~/.local/share/Alexandreia/alexandreia.db` |

Variabili d'ambiente:

- `ALEXANDREIA_DB` — percorso alternativo del database
- `ALEXANDREIA_NO_BROWSER=1` — non aprire il browser all'avvio

## Struttura

| File | Cosa fa |
|---|---|
| `Db.cs` | schema, query e le regole di prestito |
| `Import.cs` | lettura del foglio, riconoscimento colonne, unione doppioni |
| `Cli.cs` | comando `--import` e report |
| `Program.cs` | avvio, binding su loopback, apertura browser |
| `Components/Pages/` | Libri, LibroEdit, Prestiti, Metriche |
| `tests/` | disponibilità copie, prestito, rientro, metriche, import |

## Non c'è (ancora)

Anagrafica di chi prende in prestito (per ora è un campo testo), autenticazione, ricerca full-text,
firma degli eseguibili. Si aggiungono quando servono davvero.
