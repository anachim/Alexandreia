# Ἀλεξάνδρεια

Gestione di una piccola biblioteca: libri, utenti, prestiti e rientri, metriche.

Applicazione **desktop offline**: finestra nativa, nessun server, nessuna connessione, nessun
browser. I dati stanno in un file SQLite. Gira su Windows 11.

## Avvio in sviluppo

```
dotnet run
```

## Test

```
dotnet test tests
```

## Le schede

**Libri** — elenco, ricerca, prestito. Chi ha un libro fuori si legge nella colonna, e il suo
bottone «Presta» è spento.

**Utenti** — cognome, nome, nota. Nella tendina del prestito la nota compare accanto al nome: con
due omonimi è l'unica cosa che li distingue.

**Prestiti** — filtrabile fra *Fuori adesso*, *Solo in ritardo*, *Già rientrati*, *Tutti*. Ogni riga
dice il suo stato: *In regola*, *In ritardo di N giorni*, *Rientrato il …*. Da qui si registra il
rientro o si prolunga la scadenza.

**Metriche** — lo stato di adesso, e i numeri di un periodo scelto fra sette intervalli pronti o due
date qualsiasi. I prestiti del periodo si confrontano con la finestra precedente di pari durata.
Le schede *Fuori adesso* e *In ritardo* si cliccano e portano all'elenco già filtrato.

**Dati** — export e import, sotto.

In alto a destra si passa fra **tema chiaro e scuro**. La scelta resta: alla prima apertura si
segue il sistema, dopo quello che hai scelto tu.

## Come sono fatti i dati

Un **libro** ha titolo, autore e una nota. Basta.

Un libro è **una copia fisica**: o è libero, o è fuori da qualcuno. Chi ha tre copie dello stesso
titolo mette tre schede. Non deduplichiamo niente e non contiamo copie: ripulire i doppioni sta a
chi possiede i dati.

Un **utente** ha cognome, nome e una nota. Le omonimie si risolvono con la nota.

Libri e utenti si **archiviano**, non si cancellano: sparire dall'elenco senza portarsi via lo
storico dei prestiti, che è quello su cui sono costruite le metriche.

## Portare i dati dentro e fuori

Tutto dalla scheda **Dati**, in un unico formato Excel:

| Colonna | |
|---|---|
| Titolo | obbligatoria |
| Autore | |
| Nota del libro | |
| Prestato a | se c'è un nome, l'utente viene creato e il prestito aperto |
| Nota della persona | |
| Prestato il | opzionale |
| Rientro entro | opzionale, altrimenti 30 giorni |

**Esporta** salva l'archivio in un file con **tre fogli**:

- **Archivio** — i libri e chi ce li ha adesso, con le colonne qui sopra
- **Storico** — un prestito per riga: *Titolo, Autore, Prestato a, Prestato il, Rientro entro,
  Rientrato il*. Se «Rientrato il» è vuoto, quel libro è ancora fuori.
- **Utenti** — *Cognome, Nome, Nota della persona*: l'anagrafica intera, anche chi al momento
  non ha niente in prestito.

Serve sia da copia di sicurezza sia per spostare l'archivio su un altro computer: si esporta di
qua, si ricarica di là con «Sostituisci tutto», e arriva tutto — metriche e anagrafica comprese.

L'ordine conta ed è automatico: **prima gli utenti**, così i nomi che compaiono in «Prestato a»
ritrovano la persona giusta con cognome e nome separati invece di crearne una nuova col nome tutto
appiccicato nel cognome. Le righe dello **storico non creano libri**: si agganciano per titolo +
autore a quelli del primo foglio.

Con più copie dello stesso titolo lo storico finisce tutto sulla prima: quale delle copie fisiche
fosse fuori nel 2019 non lo sa più nessuno, e per le metriche — che raggruppano per libro — il
conto torna comunque.

**Carica** legge lo stesso formato — e anche l'Excel che avevano già loro. Mostra, colonna per
colonna, quante celle sono piene, su quale campo la mapperebbe e tre valori d'esempio. **Niente
viene scritto finché non premi «Importa».**

Le intestazioni sono riconosciute per **corrispondenza esatta**, senza euristiche: una colonna
mappata sul campo sbagliato su 1400 righe non te ne accorgi finché non è tardi. Un errore di
battitura tipo «Titollo» non lo indovinerà mai nessuna lista, e infatti si corregge dalla tendina.
Le colonne non riconosciute vengono **scartate**: si importano solo i campi qui sopra.

Se il file ha **più fogli** ognuno ha la sua linguetta e la **sua** mappatura — lo stesso campo si
chiama «Titolo» in un foglio e «Libro» in un altro.

**Che cosa c'è in un foglio lo dici tu**, da una tendina con quattro voci: *Libri*, *Storico dei
prestiti*, *Anagrafica utenti*, *Non caricare*. Arriva già preselezionata con la nostra ipotesi, che
sul file esportato da qui è sempre giusta — ma la parola finale è dell'utente, perché indovinare
male e tacere significherebbe creare centinaia di schede doppie senza che nessuno se ne accorga.

Di conseguenza **nomi e ordine dei fogli non contano**, e i tre tipi vengono caricati sempre nella
sequenza giusta qualunque sia la loro posizione nel file. Un foglio da cui non si ricava niente si
mette da solo su *Non caricare*, e dice perché.

La casella **«Sostituisci tutto l'archivio»** trasforma il caricamento in un ripristino: cancella
libri, utenti e storico e rimette quello che c'è nel file. È l'unica operazione irreversibile del
programma e chiede conferma per nome.

## Rilascio

Il rilascio lo fa un **tag**, non un merge:

```
git tag v1.0.0
git push origin v1.0.0
```

La pipeline è una sola: i test girano su ogni push a `main` e su ogni PR; quando arriva un tag `v*`
gira anche la release, che compila per Windows e allega lo zip su GitHub. La release **dipende**
dai test: se sono rossi non parte.

Per farlo a mano:

```
dotnet publish -c Release -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Esce **un singolo eseguibile**. Sulla macchina di destinazione non serve installare niente: si copia
il file e si fa doppio clic. Il codice resta portabile (Avalonia + SQLite): per Linux o macOS basta
cambiare il RID.

L'eseguibile non è firmato: al primo avvio Windows mostrerà SmartScreen. Se diventa un fastidio
serve un certificato di code signing, non una modifica al codice.

## Dati

Un unico file, in `%LOCALAPPDATA%\Alexandreia\alexandreia.db`. `ALEXANDREIA_DB` lo punta altrove.

## Struttura

| File | Cosa fa |
|---|---|
| `Db.cs` | schema, query, regole di prestito, import ed export |
| `Import.cs` | lettura del foglio e riconoscimento colonne |
| `Export.cs` | scrittura del file Excel |
| `Views/` | Libri, Utenti, Prestiti, Metriche, Dati, e i due dialoghi |
| `tests/` | prestiti, import, export, interfaccia |

## Non c'è (ancora)

Autenticazione, ricerca full-text, firma dell'eseguibile. Si aggiungono quando servono davvero.
