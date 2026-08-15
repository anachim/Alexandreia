# Ἀλεξάνδρεια

Gestione di una piccola biblioteca: anagrafica libri, prestiti e rientri, metriche.

Applicativo **offline**: nessun server, nessuna connessione. È un solo eseguibile che si mette in
ascolto su `127.0.0.1` (porta scelta dal sistema), apre il browser e tiene i dati in un file SQLite.

## Avvio in sviluppo

```
dotnet run
```

## Test

```
dotnet test tests
```

## Distribuzione

Un eseguibile autonomo, sulla macchina di destinazione non serve installare .NET:

```
dotnet publish -c Release -r win-x64   --self-contained -p:PublishSingleFile=true
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

L'output finisce in `bin/Release/net10.0/<rid>/publish/`.

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
