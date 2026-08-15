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

- `Lend` è `INSERT ... SELECT ... WHERE NOT EXISTS (prestito aperto)` e ritorna `false` se 0 righe
- `Return` è `UPDATE ... WHERE ReturnedAt IS NULL` e ritorna `false` se era già chiuso

Non spezzarle in leggi-poi-scrivi "per leggibilità": si riaprirebbe la finestra in cui lo stesso
libro esce due volte, e servirebbero transazioni che così non servono. Stesso schema in
`ArchiveBook` e `ArchiveMember`.

`Book.IsAvailable` e `LentTo` **non sono colonne**: sono calcolate nella query (`AvailableExpr`,
`LentToExpr`). Come `IsOpen`, `Overdue`, `DueLabel` e `FullName`, esistono per il binding XAML.

Libri e utenti si **archiviano** (`Archived = 1`), non si cancellano: eliminare il record porterebbe
via lo storico prestiti, cioè le metriche.

`Db.Apply(rows, replace)` è l'unico punto che scrive un import: crea l'utente se il nome non c'è
già (confronto su `NameKey`, cioè minuscolo e spazi normalizzati) e apre il prestito. Il nome
intero finisce nel **cognome**: «Rossi Mario» e «Mario Rossi» sono indistinguibili, e sbagliare a
spezzarli è peggio che non spezzarli.

Lo schema ha un `PRAGMA user_version` confrontato con `Db.SchemaVersion`: un archivio più vecchio
fa fallire il costruttore con un messaggio chiaro invece di rompersi query per query. Alzarlo
quando lo schema cambia in modo incompatibile.

## Decisioni del committente, non sviste

Vanno rispettate: sembrano scorciatoie, sono richieste esplicite.

- **Un libro ha solo titolo, autore e nota.** Niente ISBN, anno, editore, collocazione, copie.
- **Un libro è una copia fisica**: o è libero o è fuori. Tre copie = tre schede.
- **Nessuna deduplica, mai.** Non reintrodurre chiavi su ISBN o titolo + autore.
- **L'import scarta** le colonne non riconosciute: non accodarle alla nota.
- **Il backup è un file Excel leggibile, senza Id.** So che non è quello che si farebbe
  normalmente; è stato chiesto e riconfermato.
- **L'archivio vive su un PC alla volta**: si esporta e si ricarica con «Sostituisci tutto».
  Non è pensato per unire due archivi, e infatti non c'è nessuna chiave univoca sui prestiti.
  Se un giorno servisse la fusione, prima vanno decisi i conflitti (libro rientrato su un PC e
  ancora fuori sull'altro) e va sciolta l'incoerenza coi libri, che non deduplichiamo.
- Gira su **Windows 11**. La release compila solo `win-x64`; il codice resta portabile.

## Import Excel

`Import.cs` è diviso in due apposta: `ReadWorkbook(path)` è l'unica parte che tocca il disco,
`Plan(rows, ...)` è pura e lavora su matrici di celle. Quindi **i test dell'import non usano
nessun `.xlsx`**: passano array di `object?`. Logica nuova va in `Plan`.

Le intestazioni sono riconosciute per **corrispondenza esatta** contro `Synonyms`, deliberatamente
senza euristiche di tipo "contiene" o "inizia per": una colonna mappata sul campo sbagliato su 1400
righe non si nota. Ciò che non riconosce finisce in `Notes` e si corregge dalla tendina nella
tabella. Non trasformarlo in fuzzy matching: un typo tipo «Titollo» non va indovinato, va chiesto.

`ReadWorkbook` legge **tutti** i fogli, e ogni foglio è un `SheetMapping` con la **sua** mappatura,
perché lo stesso campo cambia nome da un foglio all'altro. Un foglio da cui non esce niente
(`ImportReport.Empty`) resta escluso e lo dichiara, invece di sparire in silenzio.

I nomi dei campi (`Import.FTitle` e compagnia) sono **le etichette italiane** che l'utente vede
nella tendina, non identificatori interni: cambiarli cambia la UI e va fatto lì.

`Export.Write` scrive **lo stesso formato** che `Plan` sa leggere, su tre fogli: `Archivio`
(libri e prestiti aperti), `Storico` (tutti i prestiti, uno per riga) e `Utenti` (anagrafica
intera). Se aggiungi un campo va in `Synonyms`, in `Export.Headers` e in `Db.ExportRow`,
altrimenti il giro si rompe a metà.

Il tipo di foglio **lo sceglie l'utente** da una tendina (`SheetKinds`), non lo deduciamo noi.
`ImportReport.LooksLikeMembers` e `LooksLikeHistory` guardano le colonne (`FLastName`,
`FReturnedAt`) ma servono solo a **preselezionare** quella tendina: l'inferenza sbagliata creava
schede doppie in silenzio, ed è il tipo di errore che nessuno nota. `SheetMapping.Kind` è la
verità, `ImportView` instrada su quello.

Quando l'utente sceglie *Anagrafica utenti*, `Plan` viene rieseguito con `asMembers: true`: il
tipo forza la costruzione delle righe, non lo si legge più dalle colonne.

`Db.ApplyAll` scrive nell'ordine anagrafica → libri → storico, e **l'ordine è il punto**: senza l'anagrafica per prima,
i nomi in «Prestato a» creerebbero persone nuove col nome intero nel cognome, e il giro
export → import degraderebbe i dati a ogni passaggio. Il foglio `Utenti` esiste anche per non
perdere chi al momento non ha niente fuori.

Le righe dello storico **non creano libri**: si agganciano per titolo + autore, e quelle che non
trovano un libro sono contate a parte invece di sparire. Le righe di storico ancora aperte
vengono ignorate: quel prestito è già arrivato dal foglio `Archivio`, e senza quel salto
arriverebbe due volte.

Il giro export → import è coperto da `Il_giro_completo_porta_anche_i_prestiti_gia_rientrati`.

ClosedXML scrive, ExcelDataReader legge. Due librerie Excel di proposito: ClosedXML garantisce
file che Excel apre davvero, ExcelDataReader è l'unico dei due che regge i vecchi `.xls`.

## Aspetto

Tutti i colori e gli stili stanno in `App.axaml`, in un blocco solo, dentro
`ThemeDictionaries` — **una voce chiara e una scura per ogni chiave**. Non mettere colori
letterali da nessun'altra parte: usa `{DynamicResource ...}`, o nel codice
`t[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("Ink")`. Un colore risolto una
volta sola resta del tema di quando è nato, e le schede delle metriche sono costruite a mano.

Lo scuro **non è l'inverso del chiaro**: i colori conservano il significato, non la luminosità.
Viola e vermiglio si schiariscono per restare leggibili su fondo scuro, e il fondo resta blu
d'inchiostro invece di diventare nero. Di conseguenza il testo sopra un bottone pieno cambia:
`OnAccent` è bianco nel chiaro e scuro nello scuro — bianco su viola chiaro non si legge.

La scelta del tema sta nella tabella `Settings` del database, insieme ai dati: alla prima
apertura si segue il sistema, dopo si segue l'utente.

L'icona del tema mostra **dove vai, non dove sei** — la luna quando c'è da passare allo scuro.
Un'icona da sola è ambigua in entrambe le letture, quindi il suggerimento lo dice a parole.
I due simboli sono `Path` disegnati, non emoji: Inter non ne ha e si finirebbe su un ripiego
di sistema, diverso da macchina a macchina.

Il riferimento non è il libro antico ma **l'amministrazione della biblioteca** — registri,
cartellini, e il timbro viola della data di restituzione, da cui viene l'accento `Stamp`. È una
scelta presa per non finire nel crema + terracotta che è la risposta prevedibile per
«biblioteca» (e dove ero finito alla prima passata).

Il colore è informazione, non decorazione. Il **pieno viola** (`Button.primary`) è riservato ai
bottoni che **cambiano l'archivio**: nuovo libro, nuovo utente, conferma prestito, salva, importa.
Scegliere un file o esportarlo non lo cambiano, e infatti sono riquadri neutri (`Button.drop`) —
mettere il pieno anche lì rendeva la scheda Dati sbilanciata, con una metà che urlava e l'altra
che sussurrava. Il **vermiglio** è per il ritardo e per ciò che cancella (`Button.danger`,
`Border.warning`, `Border.pill.late`).

Lo stato di un prestito si dice **a parole** (`Loan.Stato` + `Border.pill`), non con un simbolo.
C'era un timbro grafico al suo posto: era una bella idea che nessuno capiva, ed è il motivo per
cui non c'è più. Se una cosa va spiegata, in un applicativo da banco non funziona.

Una sola famiglia di caratteri (Inter, già dentro Avalonia): la personalità viene da scala, peso e
`LetterSpacing`. Aggiungere un file di font per decorare le linguette sarebbe peso senza funzione.

I bottoni dentro le celle di una `DataGrid` hanno un riempimento più stretto
(`DataGridCell Button`): con quello dei bottoni normali escono dalla colonna.

Le colonne hanno **larghezze fisse** e `CanUserResizeColumns`: con una colonna elastica la
tabella si accartoccia invece di far scorrere. Vanno però tenute sotto la larghezza della
finestra, altrimenti la colonna «Azioni» finisce fuori schermo e il bottone principale sparisce
finché non scorri — è già successo due volte, e non lo prende nessun test.

I fogli dell'import stanno in un `TabControl`, uno per linguetta. Quindi **solo il foglio
selezionato è nell'albero visuale**: nei test si passa da `ImportView.Fogli` e dalle proprietà di
`SheetMapping` (`Choices`, `Kind`, `Messaggio`), non da `GetVisualDescendants`.

## Trappole di Avalonia

Ognuna di queste è costata un bug vero, tre dei quali invisibili finché non ho renderizzato la
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
- **Uno `StackPanel` orizzontale dimensiona i figli sul contenuto**, e un `TextBlock` il cui testo
  cambia resta **disposto con l'ingombro di prima**: misurato giusto (`DesiredSize` aggiornato),
  tagliato a video (`Bounds.Width` vecchio), e nemmeno `UpdateLayout()` lo sistema. Un testo che
  cambia va in un contenitore che si allarga — vedi `Actions` in `ImportView` e `Head` in
  `SheetMapping`, entrambi `DockPanel`. Il test `Il_riepilogo_non_resta_tagliato_quando_si_allunga`
  confronta `Bounds.Width` con `DesiredSize.Width`.

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

Una pipeline sola (`.github/workflows/ci.yml`): i test su ogni push a `main` e su ogni PR, la
release **solo su un tag `v*`** e **solo dopo** i test, legata con `needs`. Il vincolo sta nel
grafo dei lavori, non in un comando dentro lo script: così non può passare una release con i test
rossi. Compila solo `win-x64`.
