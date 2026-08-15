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
| `Program.cs` | avvio, binding su loopback, apertura browser |
| `Components/Pages/` | Libri, LibroEdit, Prestiti, Metriche |
| `tests/PrestitiTests.cs` | disponibilità copie, prestito, rientro, metriche |

## Non c'è (ancora)

Anagrafica di chi prende in prestito (per ora è un campo testo), autenticazione, import da Excel,
ricerca full-text. Si aggiungono quando servono davvero.
