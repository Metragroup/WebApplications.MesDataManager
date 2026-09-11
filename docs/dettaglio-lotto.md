# Gestione dei lotti

Gestione completa dei dati di lotto: visualizzazione, diagnostica e modifica.

Le decisioni aperte nella prima stesura di questo documento sono state chiuse l'8 settembre 2026:
qui sta **cosa** fa l'applicazione, in [`piano-modifica-lotto.md`](piano-modifica-lotto.md) sta
l'ordine in cui si costruisce e il perché delle scelte.

## Comandi: barra al posto del menu contestuale

Nella versione WinForms i comandi stavano in un menu contestuale (tasto destro). Qui sono una
**barra comandi sopra la griglia**, con selezione multipla a caselle e pulsanti che si abilitano
in base al numero di righe selezionate; il clic sulla riga apre il lotto. Vale sia per
*ProductionData* sia per la griglia delle billette nella scheda del lotto.

Il tasto destro non è raggiungibile da tastiera né da tablet, e con la barra la regola "attivo su
selezione singola" è visibile invece che scoperta al clic.

## Inserimento/Eliminazione

Funzioni gestite dalla pagina *ProductionData*:

- **Segna da riconciliare**: attivo per selezione multipla, richiede conferma mostrando il numero
  di lotti selezionati. Alla conferma imposta `Batch.IsErpMarked = 1`.
  La diagnostica **non** viene rilanciata: nel 99% dei casi i lotti l'hanno già. Vengono saltati
  i lotti **senza diagnostica**, quelli con diagnostica in `ERR`, quelli già importati
  (`IsErpImported`) e quelli bloccati (`IsLock`); al termine un riepilogo dice all'operatore
  quanti lotti sono stati marcati e quali sono stati saltati, con il motivo.
- **Annulla da riconciliare**: attivo per selezione multipla, richiede conferma mostrando il
  numero di lotti selezionati. Alla conferma imposta `Batch.IsErpMarked = 0`. Nessuna
  limitazione.
- **Apri lotto**: solo per lotto selezionato (selezione singola).
- **Nuovo lotto**: procedura guidata per la creazione di un nuovo lotto e dei dati correlati
  (vedere `frmNewBatch` nel sorgente WinForms). Vedi "Nuovo lotto".
- **Elimina lotto**: solo per lotto selezionato (selezione singola). Vedi "Eliminazione del
  lotto".
- **Chiusura forzata pressa/sega**: stored procedure `usp_Batch_PressClose` e
  `usp_Batch_SawClose`.

### Nuovo lotto

- Chiave del lotto: `PressID` + `yyMMddHHmmss`. Due lotti creati nello stesso secondo sulla
  stessa pressa danno chiave duplicata, segnalata con il messaggio di chiave duplicata già
  esistente.
- Alla creazione: `IsPressClosed` e `IsSawClosed` a `true`, `EditStatusID = 'N'`, marcatori di
  apertura e chiusura (`BatchBillet.TypeID` 0 e 2), N billette con tempo e kg divisi in parti
  uguali; poi `usp_Batch_Elab`.
- La **matrice viene validata** come nel cambio matrice (esistenza e stato): il WinForms
  controllava solo che il campo non fosse vuoto.
- Il controllo di **sovrapposizione del periodo** con gli altri lotti della stessa pressa è
  completo: il controllo del WinForms non vedeva il caso di un lotto nuovo che ne inghiotte uno
  esistente.

### Eliminazione del lotto

- Prima conferma, riportando numero lotto e codice matrice.
- **Seconda conferma** se `EditStatusID <> 'N'`, cioè se il lotto viene dalla produzione e non è
  stato inserito a mano.
- Un lotto già riconciliato (`IsErpImported`) **non è eliminabile** (il WinForms lo permetteva).
- Cascata: `BatchProdOrders`, `BatchBilletProdOrders`, `BatchBillet`, **`Press.BatchWorker`**,
  `Batch`. `BatchWorker` è una correzione: il WinForms lasciava quelle righe orfane.
- Messaggio asincrono verso l'ERP (`ERP.usp_SendAsyncMessage`, classe `NPOPackingManager`,
  metodo `processBatchDeletion`), inviato dentro la stessa transazione dell'eliminazione. È
  l'unica azione irreversibile verso l'esterno di tutto il modulo.

## Diagnostica

Le informazioni di diagnostica normalmente arrivano già compilate per i lotti (campi
`SvcDiag*`, scritti **dal servizio di diagnostica**). È sempre possibile rieseguire la
diagnostica dalla scheda lotto (campi `UsrDiag*`): se il lotto è in modifica può eseguirla
solo l'utente che lo sta modificando.

La esegue chiunque abbia la scrittura sulla produzione (`Production.Editor` o `Administrator`),
non un `Reader`: la diagnostica **scrive** su `Batch`.

I campi di pertinenza della diagnostica sono due gruppi di quattro, rinominati l'11 settembre
2026:

| Servizio | Utente | Contenuto |
|---|---|---|
| `SvcDiagStatus` | `UsrDiagStatus` | esito: `OK`, `ATT` o `ERR` — `char(3)` |
| `SvcDiagTs` | `UsrDiagTs` | istante dell'esecuzione — `datetime` |
| `SvcDiagMsg` | `UsrDiagMsg` | referto leggibile, `nvarchar(2000)` |
| `SvcDiagJson` | `UsrDiagJson` | risposta completa del servizio, `nvarchar(max)` |

**All'esecuzione della diagnostica si scrivono solo i campi `UsrDiag*`.** I campi `SvcDiag*` non
si toccano mai, **nemmeno quando sono vuoti**: un lotto senza esito del servizio è un buco nel
calcolo automatico, e riempirlo con l'esito di un'esecuzione chiesta a mano cancellerebbe proprio
l'informazione che serve a trovarlo. Fino all'11 settembre 2026 l'applicazione li compilava "se
vuoti", come rete di sicurezza: quella rete nascondeva il problema invece di segnalarlo.

**Messaggio e risposta si conservano separati.** Il referto non si costruisce più a partire dal
JSON: arriva dal servizio, nel campo `diagnostics.message` della risposta, e va in `UsrDiagMsg`;
la risposta intera va in `UsrDiagJson`.

### L'esito corrente, con tre produttori

Dove serve "lo stato della diagnostica" — l'elenco dei dati di produzione, la marcatura per la
riconciliazione, l'effetto sulle presse senza MES — vale **l'esito corrente**, che non è un campo
ma una lettura in quest'ordine:

1. `DiagnosticsStatus`, le vecchie colonne, finché il **vecchio applicativo è in servizio**: è lui
   a scriverle, ed è lui a dire come sta il lotto;
2. `UsrDiagStatus`, se la diagnostica è stata rieseguita da qui;
3. `SvcDiagStatus`, l'esito del servizio.

È la stessa precedenza delle funzioni `EF.ufn_BatchByLength` e `EF.ufn_BatchByLengthShift`
(`COALESCE(B.DiagnosticsStatus, B.UsrDiagStatus, B.SvcDiagStatus)`, allineate l'11 settembre
2026): l'elenco dati di produzione legge dalla tabella in una modalità e dalla funzione
nell'altra, e due ordini diversi farebbero dire alle due modalità cose diverse sullo stesso lotto.

**Conseguenza da conoscere.** Sul database di produzione 273.899 lotti hanno già un esito nella
vecchia colonna: su quelli, una diagnostica rieseguita da qui **non cambia** ciò che gli elenchi
mostrano, finché quella colonna esiste. Si vede nella scheda, che tiene i tre produttori
distinti e affiancati. Quando il vecchio applicativo uscirà di servizio, va tolto il primo dei
tre — qui e nelle due funzioni.

### Servizio

- Endpoint REST: `http://192.168.3.8:8000/api/v1/batches/{lotto}/analyze`, timeout 30 secondi,
  parametro `ignoreManualAddedBillets=false` quando la chiamata parte dalla scheda del lotto
  (cioè dopo gli aggiustamenti manuali). L'indirizzo non è un segreto e sta nella
  configurazione dell'applicazione.
- Esito: `ERR` se lo stato del servizio è `ERR`, `ATT` se ci sono avvisi, altrimenti `OK`.
- **Nessun ripiego locale**: se il servizio non risponde, l'operazione si ferma con un messaggio.
  Le regole di diagnosi locale del WinForms (~340 righe) non vengono riportate: il servizio è la
  fonte di verità e regole duplicate divergono.
- Sulle presse con `Press.HasMes = false` l'esito della diagnostica decide anche `IsPressClosed`
  e `IsSawClosed` (`= status == "OK"`), ma **al salvataggio del lotto**, non a ogni esecuzione
  della diagnostica come faceva il WinForms.

### Billette mancanti

Il servizio può restituire billette mancanti (`missingBillets`), che vengono inserite o
aggiornate in `Press.BatchBillet`:

- chiave `BatchID` + `BilletNo`;
- se la billetta esiste con `EditStatusID <> 'A'` viene ignorata (una correzione manuale non si
  sovrascrive);
- altrimenti viene scritta con `EditStatusID = 'A'`.

In griglia le billette automatiche sono **riconoscibili** (da `EditStatusID = 'A'`), e il
dettaglio di ciascuna — `decision`, `confidenceScore`, `reasonCodes`, il vuoto rilevato — si legge
nella scheda Diagnostica, dalla risposta conservata in `UsrDiagJson` (o in `SvcDiagJson` per
quella del servizio). Funziona anche riaprendo la scheda a distanza di tempo.

### Conservazione della risposta

Su `UsrDiagJson` si conserva la **risposta completa del servizio**, così come arriva: è
l'informazione che serve in fase di manutenzione del lotto, quando si correggono i problemi che
la diagnostica ha segnalato. Il referto leggibile sta accanto, in `UsrDiagMsg`, e lo compone il
servizio.

Le due colonne JSON sono `nvarchar(max)` su `MES40_RDP` e `MES40_RDP_TEST`. Nessun taglio è
ammissibile: una risposta reale senza billette mancanti misura **4.253 caratteri**, e cresce di
~884 byte per billetta mancante e di ~100 byte per ogni errore o avviso — e gli errori si contano
**per billetta**.

Conseguenze per l'applicazione:

- la risposta si salva **verbatim**, senza reindentarla né riserializzarla: ciò che si rilegge fra
  sei mesi deve essere quello che il servizio ha detto, carattere per carattere;
- il referto si **tronca** a 2.000 caratteri prima di scriverlo, perché la colonna li tiene: un
  lotto con molti errori ha un referto lungo quanto l'elenco degli errori, e sarebbe il
  salvataggio a fallire proprio sul lotto messo peggio. Il testo intero resta nel JSON accanto;
- la scheda interpreta il JSON per mostrare la diagnostica in forma sintetica; il contenuto che
  non è JSON viene mostrato come testo. È il caso delle righe storiche nei vecchi campi
  `Diagnostics*`, che restano a database per gli altri consumatori;
- le colonne JSON non devono entrare nelle query di elenco: si leggono solo aprendo il lotto.

Se un domani lo spazio diventasse un problema, la leva è una **pulizia periodica** delle risposte
più vecchie di sei mesi (esito, data e referto restano), non un limite di lunghezza. Non è deciso
e non serve oggi.

## Visualizzazione/modifica

Pagina di sintesi con tutti i dati di lotto, integra la manutenzione completa. Il lotto viene
normalmente aperto in modalità "read". Per modificare, se concesso dal ruolo dell'utente, è
necessario passare alla modalità "modifica".

### Modalità visualizzazione

I dati del lotto sono divisibili nelle seguenti categorie:

- **Dati di testata**: `Press.Batch`
- **Billette**: `Press.BatchBillet` (solo billette vere, `TypeID = 1`: i tipi 0 e 2 sono i
  marcatori di apertura e chiusura del lotto)
- **Incestamento**
  - Transazioni: `EF.Module_ModuleTrans`
    - Numero cesta (`ModuleID`)
    - Lunghezza
    - Ordine di produzione
    - Qta
    - Creato (timestamp)
    - Oper. n. [2]
    - Operazione [2]
    - Centro di lavoro [2]
    - Qta scarto [2]
  - Rettifiche: `Press.BatchBarQty`
    - Lunghezza
    - Ordine produzione
    - Qta
    - Creato (timestamp)
- **Fermi macchina**: `Press.BatchDowntime`
  - Inizio
  - Fine
  - Durata
  - Codice fermo
  - Numero fermo
  - Descrizione fermo
  - Tipo fermo (solo Macrofermi)
- **Ordini di produzione** (nel WinForms "Cartellini"): la lista degli ordini di produzione
  collegati al lotto [1]
  - Ordine di produzione: `EF.NPOPRODUCTIONTAG.PRODID`
  - Ragione sociale: `EF.NPOPRODUCTIONTAG.CUSTNAME`
  - Lega: `EF.NPOPRODUCTIONTAG.SALESALLOYID` **e** `EF.NPOPRODUCTIONTAG.PRODALLOYID`, entrambe,
    perché possono differire
  - Trattamento: `EF.NPOPRODUCTIONTAG.SALESHEATTREATMENT`
- **Diagnostica**: mostra i dati di diagnostica in forma sintetica, interpretando i dati della
  risposta json del servizio di diagnostica (vedere sezione "Diagnostica")

Le categorie sono presentate come schede, come nella scheda lotto del WinForms.

> [1]: La lista degli ordini associati al lotto viene composta su due livelli:
> 1. Select distinct su `BatchBilletProdOrders` (per `BatchID`)
> 2. Se non trovato nulla al 1., select distinct di `BatchProdOrders` (per `BatchID`)
>
> È una correzione: la scheda del WinForms leggeva la vista `MetraPQ.Cartellini` e i soli
> `BatchProdOrders`.

> [2]: Questi quattro campi **non** sono colonne di `EF.Module_ModuleTrans`. "Oper. n.",
> "Operazione" e "Centro di lavoro" vengono dall'ultimo passo lavorato di
> `Module_ModuleTransRoute` (`IsProcessed = true`, ordinato per `OprNumPriority`); "Qta scarto" è
> la somma di `Module_ModuleTransScrap.Qty` per la transazione.

### Modalità modifica

Il lotto diventa modificabile e viene contrassegnato come bloccato dall'utente mediante i campi:

- `IsLock`
- `Lock_Ts`
- `Lock_Usr`: l'**UPN** dell'utente (`nome.cognome@metra.it`). Nel WinForms era
  `utente\NOMEPC`; i lock già presenti in quel formato non vengono gestiti.

Il lotto in stato di modifica può essere modificato o contrassegnato per la riconciliazione
solamente dall'utente assegnatario della modifica (`Batch.Lock_Usr`); un tentativo di modifica da
parte di altri utenti viene bloccato con messaggio informativo che riporta `Lock_Usr` e
`Lock_Ts`.

Un utente con ruolo `Administrator` può forzare lo sblocco di un lotto (causando la perdita delle
modifiche, da usare con cautela). L'utente che perde il lock lo scopre al **primo salvataggio**,
con un avviso esplicito.

Al termine della modifica il lotto deve essere esplicitamente salvato dall'utente e ritorna
disponibile.

**Il lock si rilascia da solo** in due casi, che nel WinForms non esistevano: alla chiusura della
sessione del browser (dismissione del circuito) e per **scadenza dopo 60 minuti** da `Lock_Ts`.
Senza, nel web i lock orfani sarebbero la norma e non l'eccezione.

**Precondizioni.** Si entra in modifica solo se:

- `IsBatchProcessed = true` — altrimenti messaggio "elaborazione in corso" e nessun lock;
- `IsErpImported = false`;
- l'utente ha la scrittura sulla produzione e il lotto non è bloccato da altri.

**Le modifiche si accumulano e si salvano in blocco.** Le modifiche fatte in modalità modifica
restano in sospeso fino al salvataggio, che le scrive tutte in **una transazione**: è il
comportamento del WinForms ed è ciò che rende possibile annullare. Dopo il salvataggio vengono
eseguiti `usp_Batch_Elab` e `usp_LogScaleImportUpdateByBatchID`, e la scheda si **ricarica**: i
valori di riepilogo (pesi, conteggi, tempi di ciclo) li ricalcola lo SCADA, quindi i valori giusti
sono quelli riletti a valle della procedura.

Nella modalità modifica l'utente potrà modificare:

- **Matrice**: mediante tasto dedicato, che apre un controllo (vedi `frmChangeDieCode`) che
  permette la selezione della nuova matrice verificandone l'esistenza (`EF.WRKCTRTABLE`) e lo
  stato (`EF.NPOWRKCTRSETUPTABLE.STATUSUSE`): si **blocca** su 2 (magazzino), 4 (eliminata) e 5
  (trasferita), si **avvisa** su 0 (test), si accetta 1 (disponibile). Il nuovo `DieID` viene
  propagato a **tutte** le billette del lotto, marcatori 0 e 2 compresi — il WinForms li lasciava
  indietro.
- **Causale di chiusura**: selezionabile da lista (vedi `frmChangeClosingReason`) tramite un campo
  di tipo combobox, senza controllo dedicato. Elenco filtrato su `IsActive` e ordinato per
  `Position` (senza il filtro su `IsActive_Master`, a differenza delle causali di fermo). Il
  cambio aggiorna `Batch.PressBatchClosingReasonID` **e** `ClosingReasonID` sul marcatore
  `TypeID = 2`.
- **Billette**: vedere "Modifica billette".
- **Incestamento\Rettifiche**: i dati di `Module_ModuleTrans` sono readonly; le rettifiche
  (`BatchBarQty`) possono avere Qta positiva o negativa, e le righe esistenti sono modificabili
  oltre che inseribili ed eliminabili. Il pannello delle rettifiche è visibile solo se
  `Company.SAW_AllowAdjustments` è vero, come nel WinForms.

#### Modifica billette

Le billette del lotto possono essere modificate liberamente, utilizzando la modalità "edit in
place". Sono editabili tutte le colonne mostrate **tranne le due leghe**, che derivano dalla
colata. Ogni modifica marca la billetta con `EditStatusID = 'M'`.

Ci sono dei tool che aiutano l'utente nella manutenzione (barra comandi):

- **Aggiungi billette**: sempre attivo (`frmNewBillet`)
- **Duplica billetta**: attivo su selezione singola
- **Rimuovi billette**: attivo su selezione multipla — anche un blocco di billette in una volta,
  mentre il WinForms ne cancellava una per volta
- **Rinumera billette**: agisce sulle **righe selezionate**, a partire da un numero
  (`frmRenumberBillets`). Con una sola riga selezionata funziona: nel WinForms non faceva nulla.
- **Modifica billette**: attivo su selezione multipla, modifica di alcuni dati di billetta
  - Codice colata, con controllo esistenza (`frmChangeCasting`); la lega viene derivata dalla
    colata
  - Lunghezza barra (`frmChangeLength`)
  - Lunghezza billetta (`frmChangeLength`)
  - Ordine di produzione, con controllo esistenza (`frmChangeProdId`)

Al salvataggio, come nel WinForms:

- `KgSheared` di ogni billetta viene calcolato come somma di `Billet1_Kg` e `Billet2_Kg`;
- i **marcatori** `TypeID` 0 e 2 vengono riallineati agli istanti della prima e dell'ultima
  billetta, se questi sono cambiati;
- le billette nuove nascono con `BatchBilletRawID = -1`, `SecCycle` derivato dalla durata e
  `EditStatusID = 'N'`.
