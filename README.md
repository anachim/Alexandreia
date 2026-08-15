# Ἀλεξάνδρεια

Gestione di una piccola biblioteca: anagrafica libri, prestiti e rientri, metriche.

Applicazione **desktop offline**: finestra nativa, nessun server, nessuna connessione, nessun
browser. I dati stanno in un file SQLite.

## Avvio in sviluppo

```
dotnet run
```

## Test

```
dotnet test tests
```

## Import da Excel

Dalla scheda **Import**: si sceglie il file (o lo si trascina nella finestra) e l'applicazione
mostra, colonna per colonna, quante celle sono piene, su quale campo la mapperebbe e tre valori
d'esempio. **Niente viene scritto finché non premi «Importa».**

Le intestazioni sono riconosciute per **corrispondenza esatta**, senza euristiche: una colonna
mappata sul campo sbagliato su 1400 righe non te ne accorgi finché non è tardi. Quello che non
riconosce lo dice e finisce in `Notes` invece di essere buttato — e ogni riga della tabella ha un
menu a tendina per correggere a mano l'accoppiamento.

Se il file ha **più fogli** li mostra tutti, ognuno con la **sua** mappatura e una casella per
includerlo o no. Serve perché lo stesso campo può chiamarsi «Titolo» in un foglio e «Libro» in un
altro, o essere scritto con un errore di battitura — che nessuna lista di sinonimi indovinerà mai,
e che infatti si corregge a mano. Un foglio da cui non si ricava niente **lo dice** e resta fuori,
invece di sparire in silenzio.

**Nessuna deduplica**: i libri si caricano come si trovano, una riga una scheda. Le copie arrivano
solo da una colonna «Copie» esplicita. Se lo stesso libro è su tre righe, in archivio ci finiscono
tre schede: ripulire i doppioni sta a chi possiede i dati.

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

Esce **un singolo eseguibile** da ~107 MB. Sulla macchina di destinazione non serve installare
niente: si copia il file e si fa doppio clic.

Gli eseguibili non sono firmati: al primo avvio Windows mostrerà SmartScreen e macOS Gatekeeper li
bloccherà. Se diventa un fastidio serve un certificato di code signing, non una modifica al codice.

## Dati

Un unico file, quindi il backup è copiarlo:

| Sistema | Percorso |
|---|---|
| Windows | `%LOCALAPPDATA%\Alexandreia\alexandreia.db` |
| Linux   | `~/.local/share/Alexandreia/alexandreia.db` |
| macOS   | `~/.local/share/Alexandreia/alexandreia.db` |

`ALEXANDREIA_DB` punta il database altrove.

## Struttura

| File | Cosa fa |
|---|---|
| `Db.cs` | schema, query e le regole di prestito |
| `Import.cs` | lettura del foglio, riconoscimento colonne, unione doppioni |
| `Views/` | Libri, LibroDialog, Prestiti, Metriche, Import |
| `MainWindow.axaml` | le quattro schede |
| `tests/` | disponibilità copie, prestito, rientro, metriche, import, interfaccia |

## Non c'è (ancora)

Anagrafica di chi prende in prestito (per ora è un campo testo), autenticazione, ricerca full-text,
firma degli eseguibili. Si aggiungono quando servono davvero.
