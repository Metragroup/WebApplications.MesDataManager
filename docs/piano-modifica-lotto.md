# Piano di esecuzione — modifica dei lotti

Piano di attuazione di [`dettaglio-lotto.md`](dettaglio-lotto.md), cioe' della voce 1.3 di
[`roadmap.md`](roadmap.md) per la parte di **scrittura**: lock, modifica di testata e billette,
rettifiche, diagnostica, inserimento ed eliminazione del lotto.

Stato: **attuato il 9 settembre 2026**, tutte le nove fasi. Le decisioni della sezione 1 sono
state prese esplicitamente e non vanno ridiscusse: se una si rivela sbagliata si corregge
**qui**, con la data. La sezione 5 registra cosa ha insegnato la verifica sul database vero, e
dove ha cambiato il progetto.

---

## 1. Decisioni prese

Tutte confermate l'8 settembre 2026. Dove il comportamento del vecchio applicativo e' stato
letto nel codice, la fonte e' indicata fra parentesi.

### 1.1. Comandi al posto del menu contestuale

Niente tasto destro: **barra comandi sopra la griglia** con selezione multipla a caselle
(`MudDataGrid.MultiSelection`) e pulsanti che si abilitano in base al numero di righe
selezionate. Clic sulla riga = apri. Vale sia per *Dati di produzione* sia per la griglia delle
billette dentro la scheda.

Il tasto destro non e' raggiungibile da tastiera ne' da tablet, e con la barra la regola "attivo
su selezione singola" diventa visibile invece che scoperta al clic.

### 1.2. Le modifiche si accumulano in memoria, si salvano in una transazione

Modello (a): la scheda in modifica tiene lo stato delle modifiche **nel circuito**, e il
salvataggio le scrive tutte in **una transazione esplicita**. E' il modello del vecchio
applicativo (contesto EF unico di sessione, `SaveChanges` al "Salva") e l'unico che rende
possibile "Annulla modifiche".

Conseguenza: il modello di modifica e' un oggetto dell'applicazione, non entita' EF tracciate.
Le entita' si materializzano solo al salvataggio, dentro la transazione.

### 1.3. Lock

| Cosa | Decisione |
|---|---|
| `Lock_Usr` | l'**UPN** dell'utente (`nome.cognome@metra.it`). Nessuna gestione dei lock in formato vecchio (`utente\NOMEPC`) ne' di quelli orfani presenti sul database |
| Rilascio | alla **dismissione del circuito** Blazor, piu' una **scadenza di 60 minuti** da `Lock_Ts` |
| Sblocco forzato | `Administrator`; l'utente che perde il lock lo scopre al **primo salvataggio**, con un avviso esplicito |

La scadenza serve perche' nel web la sessione muore in silenzio: nel WinForms il lock orfano era
un caso, qui sarebbe la norma.

### 1.4. Precondizioni di modifica

Entrambe quelle del vecchio applicativo (`frmBatchDetail.btnEdit_Click`, `SetPermission`):

- `IsBatchProcessed = true` — altrimenti messaggio "elaborazione in corso" e nessun lock;
- `IsErpImported = false`;

piu' `CanEditProduction` e il lock libero (o proprio).

### 1.5. Perimetro

Dentro: tutto quello elencato in `dettaglio-lotto.md`, **piu'** le funzioni della voce 3 della
roadmap che il documento non citava — chiusura forzata pressa/sega
(`usp_Batch_PressClose` / `usp_Batch_SawClose`) e marcatura "da riconciliare" dalla scheda.

### 1.6. Billette

Si replica il comportamento del vecchio applicativo:

- editabili tutte le colonne mostrate **tranne le due leghe**, che derivano dalla colata
  (`SetEditableRow`); ogni modifica marca `EditStatusID = 'M'`;
- `KgSheared` lo **calcola l'applicazione** al salvataggio, come somma di `Billet1_Kg` e
  `Billet2_Kg` (`CalcBilletsKgSheared`);
- al salvataggio i **marcatori** `TypeID` 0 e 2 vengono riallineati agli istanti della prima e
  ultima billetta, se questi sono cambiati (`CheckBillets`);
- **rinumerazione** sulle sole righe selezionate, a partire da un numero
  (`frmRenumberBillet`), corretto il difetto per cui con una sola riga selezionata non faceva
  nulla;
- **eliminazione anche di un blocco** di billette (il vecchio ne cancellava una per volta);
- le billette nuove nascono con `BatchBilletRawID = -1`, `SecCycle` derivato dalla durata,
  `EditStatusID = 'N'` (`CreateBatchBillet`).

### 1.7. Rettifiche (`Press.BatchBarQty`)

Pannello **condizionato** a `Company.SAW_AllowAdjustments` come nel vecchio
(`frmBatchDetail.LoadData`, azienda = la prima attiva). Le righe esistenti sono **modificabili**,
oltre a inseribili ed eliminabili. `Qty` ammette segno negativo. `CreatedTs` alla creazione.

### 1.8. Matrice

Esistenza su `EF.WRKCTRTABLE` **e stato** da `EF.NPOWRKCTRSETUPTABLE.STATUSUSE`: si **blocca** su
2 (magazzino), 4 (eliminata) e 5 (trasferita), si **avvisa** su 0 (test), si accetta 1
(disponibile). Nel vecchio lo stato lo guardava solo la diagnostica, non il cambio matrice.

Il nuovo `DieID` (`codice` oppure `codice/numero`) si propaga a **tutte** le billette del lotto,
**marcatori 0 e 2 compresi** — il vecchio li lasciava indietro (`ChangeDie` passa da `GetBillets`,
che esclude i tipi 0 e 2).

### 1.9. Causale di chiusura

Elenco filtrato su `IsActive`, ordinato per `Position`, **senza** il filtro su `IsActive_Master`
(`FrmChangeClosingReason`: e' diverso dalle causali di fermo, di proposito). Il cambio aggiorna
`Batch.PressBatchClosingReasonID` **e** `ClosingReasonID` sul marcatore `TypeID = 2`.

### 1.10. Diagnostica

- Servizio raggiungibile, indirizzo **non e' un segreto**: va in `appsettings*.json`
  (`http://192.168.3.8:8000/api/v1/batches/`, timeout 30s, parametro
  `ignoreManualAddedBillets=false` quando si chiama dalla scheda).
- **Nessun ripiego locale**: se il servizio non risponde, messaggio di errore. Le ~340 righe di
  regole del vecchio presenter non si riportano.
- Le **billette mancanti** restituite dal servizio si inseriscono/aggiornano come nel vecchio
  (`UpsertMissingBillets`): chiave `BatchID` + `BilletNo`, si ignora la riga che esiste con
  `EditStatusID <> 'A'`, altrimenti si scrive con `EditStatusID = 'A'`.
- Le billette automatiche sono **riconoscibili in griglia** (da `EditStatusID = 'A'`), e il
  dettaglio diagnostico di ciascuna — `decision`, `confidenceScore`, `reasonCodes`, il vuoto
  rilevato — si legge nella scheda Diagnostica, dalla risposta conservata in `UsrDiagJson`
  (1.11). Il campo tiene l'**ultima** risposta: rieseguendo la diagnostica dopo aver corretto il
  lotto, il motivo delle billette inserite prima non c'e' piu' (voce B4 di
  `decisioni-aperte.md`).
- **I campi del servizio non si toccano mai** (corretto l'11 settembre 2026). Fino a quella data
  l'applicazione compilava `FirstDiagnostics*` "se vuoti", come rete di sicurezza; ora i due
  gruppi sono `SvcDiag*` e `UsrDiag*` e l'applicazione scrive solo i secondi — nemmeno quando i
  primi sono vuoti, perche' quel vuoto e' un buco nel calcolo automatico del servizio e va
  lasciato visibile. La rete di sicurezza nascondeva il problema invece di segnalarlo.
- Sulle presse con `Press.HasMes = false` l'esito della diagnostica decide `IsPressClosed` e
  `IsSawClosed` (`= status == "OK"`), ma **al salvataggio del lotto**, non a ogni esecuzione della
  diagnostica come faceva il vecchio (`SetBatchDiagnosticsStatus`).
- La esegue **chiunque abbia scrittura produzione**, non un `Reader` — la diagnostica scrive su
  `Batch`, e nel vecchio applicativo il ruolo di sola lettura non esisteva. Se il lotto e' in
  modifica, solo l'utente che lo sta modificando.

### 1.11. Due gruppi di quattro campi: servizio e utente

Rinominati e ampliati l'11 settembre 2026 su `MES40_RDP` e `MES40_RDP_TEST`:

| Prima | Adesso | Tipo |
|---|---|---|
| `FirstDiagnosticsStatus` | `SvcDiagStatus` | `char(3)` |
| `FirstDiagnosticsTs` | `SvcDiagTs` | `datetime` |
| `FirstDiagnosticsMsg` | `SvcDiagJson` | `nvarchar(max)` |
| `DiagnosticsStatus` | `UsrDiagStatus` | `char(3)` |
| `DiagnosticsTs` | `UsrDiagTs` | `datetime` |
| `DiagnosticsMsg` | `UsrDiagJson` | `nvarchar(max)` |
| — | `SvcDiagMsg`, `UsrDiagMsg` | `nvarchar(2000)` |

**Messaggio e risposta si conservano separati.** Il referto leggibile non si costruisce piu' dal
JSON: arriva dal servizio, nel campo `diagnostics.message` — che il servizio ha cominciato a
restituire insieme alla rinomina.

Perche' le due colonne JSON non hanno una lunghezza fissa: una risposta reale senza billette
mancanti misura **4.253 caratteri**, e cresce di ~884 byte per billetta mancante e di ~100 byte
per errore o avviso, con gli errori contati **per billetta** (media 18 billette per lotto su
`MES40_RDP`, massimo 494). Un lotto malfatto supera gli 8.000 caratteri.

Conseguenze da rispettare nel codice:

- le colonne JSON si mappano **senza** `HasMaxLength` e senza dichiarare il tipo: una stringa
  senza lunghezza massima e' gia' `nvarchar(max)` per EF. **Non** `HasColumnType("nvarchar(max)")`
  — arriverebbe alla lettera anche a SQLite, dove `max` non e' sintassi valida, e farebbe cadere
  l'intera suite di test (vedi 5.4);
- gli istanti sono `datetime` e non `datetime2` come il resto della tabella: va dichiarato, o EF
  invia parametri con una precisione che la colonna non ha;
- la risposta si salva **come arriva**, senza riserializzarla: ne' reindentata ne' minificata;
- il referto si **tronca** ai 2.000 caratteri della colonna prima di scriverlo: un lotto con molti
  errori ha un referto lungo quanto l'elenco degli errori, e sarebbe il salvataggio a fallire
  proprio sul lotto messo peggio. Il testo intero resta nel JSON accanto;
- le colonne JSON **non entrano nelle query di elenco** — si leggono solo in `GetDetailAsync`. Un
  valore fino a 8.000 byte resta in riga, oltre va su pagine LOB;
- "lo stato della diagnostica" e' una **lettura di tre**, nell'ordine
  `Diagnostics* -> UsrDiag* -> SvcDiag*`: prima il vecchio applicativo finche' e' in servizio, poi
  l'utente, poi il servizio. Vale per l'elenco, per la marcatura e per l'effetto sulle presse
  senza MES, e coincide con la `COALESCE` delle funzioni (vedi sotto).

Nota di metodo, che e' costata un errore: `sys.columns.max_length` e' in **byte**, quindi per
`nvarchar` va diviso per due.

**Le colonne vecchie restano, e hanno la precedenza.** `Diagnostics*` non sono state eliminate:
il vecchio applicativo e' ancora in servizio e continua a scriverle, su 273.899 lotti con un
esito vero. Le funzioni `EF.ufn_BatchByLength` e `EF.ufn_BatchByLengthShift` sono state allineate
l'11 settembre 2026 a `COALESCE(B.DiagnosticsStatus, B.UsrDiagStatus, B.SvcDiagStatus)`, e questa
applicazione legge nello stesso ordine — verificato sui dati veri: sui 29 lotti di una giornata,
tabella e funzione danno lo stesso esito su tutti.

Ne discende che **su un lotto gia' diagnosticato dal vecchio applicativo una riesecuzione fatta
da qui non cambia cio' che gli elenchi mostrano**. Si vede nella scheda, che tiene i tre
produttori affiancati. Quando il vecchio applicativo uscira' di servizio, va tolto il primo dei
tre: qui, nell'entita' `Batch` e nelle due funzioni.

Restano da guardare, quando quel momento arriva: `BI.vBatch`, `History.Batch`,
`MetraPQ.DatiLottoPerTurnoLunghezza` e `Press.ufn_Batch_Data`, che citano ancora le colonne
vecchie.

**Se lo spazio diventasse un problema** la leva e' una pulizia periodica delle risposte piu'
vecchie di sei mesi, tenendo esito, data e referto. Non e' deciso e non serve oggi.

### 1.12. Ordini di produzione

Sorgente `EF.NPOPRODUCTIONTAG` con la regola a due livelli — `distinct` su
`BatchBilletProdOrders` per `BatchID`, e solo se vuoto `BatchProdOrders`
(`GetBatchProductionTags`). E' una **correzione**: la vecchia scheda usava la vista
`MetraPQ.Cartellini` e i soli `BatchProdOrders` (`GetTags` + `GetOrdersForBatch`).

Colonne: `PRODID`, `CUSTNAME`, **`SALESALLOYID` e `PRODALLOYID` entrambe** (possono differire),
`SALESHEATTREATMENT`.

### 1.13. Incestamento

`EF.Module_ModuleTrans` da sola non basta: "Oper. n.", "Operazione" e "Centro di lavoro" vengono
dall'**ultimo passo lavorato** di `Module_ModuleTransRoute` (`IsProcessed = true`, ordinato per
`OprNumPriority`) e "Qta scarto" dalla **somma** di `Module_ModuleTransScrap.Qty`
(`GetModuleTransDtos`). "Numero cesta" e' `ModuleID`. Tutto in sola lettura.

### 1.14. Nuovo lotto

Chiave `PressID + yyMMddHHmmss` (`GenerateBatchNumber`) — due lotti nello stesso secondo sulla
stessa pressa danno chiave duplicata, tradotta nel messaggio localizzato che c'e' gia'.
`IsPressClosed` e `IsSawClosed` a `true`, `EditStatusID = 'N'`, marcatori 0 e 2, N billette con
tempo e kg divisi in parti uguali, poi `usp_Batch_Elab` (`FrmNewBatch`, `CreateBatch`).

Due difetti del vecchio si correggono: la matrice **si valida** come al punto 1.8 (il vecchio
controllava solo che il campo non fosse vuoto) e il controllo di **sovrapposizione periodo** si
scrive completo, come gia' fatto per i fermi macchina — `IsBatchPeriodValid` non vede il lotto
nuovo che ne inghiotte uno esistente.

### 1.15. Eliminazione del lotto

Cascata: `BatchProdOrders`, `BatchBilletProdOrders`, `BatchBillet`, **`Press.BatchWorker`** (che
il vecchio non cancellava e l'EDMX non modellava), `Batch`. Piu' il messaggio asincrono verso
l'ERP (`ERP.usp_SendAsyncMessage`, classe `NPOPackingManager`, metodo `processBatchDeletion`).

Conferme: la prima riporta numero lotto e codice matrice; la **seconda** compare se
`EditStatusID <> 'N'`, cioe' se il lotto viene dalla produzione e non e' stato inserito a mano.
Un lotto **gia' riconciliato** (`IsErpImported`) **non si elimina** — il vecchio lo permetteva.

### 1.16. Segna / annulla da riconciliare

**Non** si rilancia la diagnostica: nel 99% dei casi i lotti l'hanno gia'. Si marcano i lotti
selezionati che hanno una diagnostica **non in errore**, si **saltano** quelli senza diagnostica
o in `ERR`, quelli gia' importati e quelli bloccati, e si mostra all'operatore il riepilogo di
cosa e' stato saltato e perche'. Annullare la marcatura non ha limitazioni.

Rispetto al vecchio (`ucBatches`, caso `SetElaboration`) cade l'esecuzione automatica della
diagnostica su ogni riga selezionata, che su una selezione ampia significava altrettante chiamate
HTTP.

### 1.17. Concorrenza e tracciabilita'

Nessuna concorrenza ottimistica sulle billette: il lock pessimistico e' la garanzia, e dopo il
salvataggio la scheda si **ricarica** dai valori a valle di `usp_Batch_Elab`. La tracciabilita'
resta quella dei log Serilog (voci A4 e B1 di `decisioni-aperte.md`, invariate).

---

## 2. Cosa manca a database e nel modello

Verificato su `MES40_RDP_TEST` l'8 settembre 2026.

**Presente e utilizzabile:** `Press.BatchBarQty`, `Press.BatchProdOrders`,
`Press.BatchBilletProdOrders`, `Press.BatchWorker`, `EF.Module_ModuleTrans`,
`EF.NPOPRODUCTIONTAG`, `EF.WRKCTRTABLE`, `EF.NPOWRKCTRSETUPTABLE`, `MetraPQ.Casting`,
`Press.usp_Batch_Elab`, `Press.usp_Batch_PressClose`, `Press.usp_Batch_SawClose`,
`Press.usp_LogScaleImportUpdateByBatchID` (portata su test l'8 settembre 2026),
`ERP.usp_SendAsyncMessage`, le colonne di diagnostica, `Press.HasMes`,
`Company.SAW_AllowAdjustments`.

**Nessun trigger** su `Press.Batch`, `Press.BatchBillet`, `Press.BatchBarQty`: a differenza di
`BatchDowntime` non serve dichiararne alcuno nel modello (il perche' conta: vedi il commento in
`MesDbContext`).

**Da aggiungere in `Domain` / `MesDbContext`:** `BatchBarQty`, `ModuleTrans`,
`ModuleTransRoute`, `ModuleTransScrap`, `ProductionTag` (`NPOPRODUCTIONTAG`), `Die`
(`WRKCTRTABLE`), `DieSetup` (`NPOWRKCTRSETUPTABLE`), `Casting`, `BatchProdOrder`,
`BatchBilletProdOrder`, `BatchWorker`.

**Da estendere:** `Batch` (i due gruppi di campi di diagnostica, `DieStatusID`),
`BatchBillet` (`StartTsNextBill`, `SecExtrusion`, `ClosingReasonID` — obbligatorie in
inserimento), `Company` (`SAW_AllowAdjustments`), `Press` (`HasMes`, `BilletMeterWeight` se
serve).

**Da correggere:** la mappatura dei campi di diagnostica, rifatta l'11 settembre 2026 con i nomi
nuovi e i tipi giusti — `nvarchar(max)` per le risposte, `nvarchar(2000)` per i referti,
`datetime` per gli istanti (vedi 1.11).

---

## 3. Fasi

Ogni fase e' rilasciabile e si verifica sul database vero prima della successiva. Per le
scritture la verifica e' reale ma dentro una transazione annullata (connessione e transazione
esterne a EF, la factory dei contesti agganciata alla stessa transazione).

### Fase 1 — completare la scheda in sola lettura

Entita' e viste nuove della sezione 2; scheda riorganizzata a **schede** come il vecchio
(Billette, Incestamento, Fermi, Ordini di produzione, Diagnostica); colonne mancanti sui fermi
(codice fermo, numero, descrizione, tipo); rettifiche e transazioni cesta; ordini di produzione
con la regola a due livelli; diagnostica del servizio e dell'utente mostrate accanto.

Nessuna scrittura. Si consegna valore da subito e si costruisce tutta la mappatura che le fasi
successive richiedono.

File: `Domain/Entities/*`, `Infrastructure/Persistence/MesDbContext.cs`,
`Application/Production/BatchModels.cs`, `Infrastructure/Production/BatchService.cs`,
`Components/Pages/ProductionBatchDetail.razor`, risorse nelle tre lingue.

### Fase 2 — lock e modalita' modifica

`IBatchService`: `BeginEditAsync`, `CancelEditAsync`, `ForceUnlockAsync`. Precondizioni 1.4,
messaggio con `Lock_Usr` e `Lock_Ts` per chi trova il lotto occupato, scadenza a 60 minuti,
rilascio alla dismissione del circuito, sblocco forzato per `Administrator` con avviso al
proprietario al primo salvataggio.

Sulla pagina: interruttore lettura/modifica, comandi *Modifica* / *Salva* / *Annulla*, avviso
all'abbandono con modifiche pendenti.

Ancora nessun campo modificabile: si verifica il solo ciclo di vita del lock, che e' la parte
dove i difetti costano piu' caro.

Nuove factory in `ProductionException`: lotto bloccato da altri, elaborazione in corso, lotto
gia' riconciliato, lock perduto.

### Fase 3 — testata

Cambio matrice (validazione 1.8, propagazione a tutte le billette compresi i marcatori) e causale
di chiusura (combobox, elenco 1.9, aggiornamento del marcatore 2). Prime modifiche che entrano
nel modello in memoria della fase 2 ed escono dalla transazione della fase 6.

### Fase 4 — billette

La fase piu' grossa. Edit in place con le colonne di 1.6, `EditStatusID` automatico, validazione
colata con derivazione della lega e validazione ordine di produzione; barra comandi con aggiungi,
duplica, rimuovi (blocco), rinumera (righe selezionate), modifica multipla di colata, lunghezza
barra, lunghezza billetta e ordine di produzione; riallineamento dei marcatori; calcolo di
`KgSheared`.

### Fase 5 — rettifiche

`BatchBarQty`: inserimento, modifica, eliminazione, quantita' con segno, pannello condizionato a
`SAW_AllowAdjustments`.

### Fase 6 — salvataggio

Una transazione esplicita per billette eliminate, modificate e aggiunte, rettifiche e testata;
poi `usp_Batch_Elab`, poi `usp_LogScaleImportUpdateByBatchID`, poi l'effetto `HasMes` di 1.10,
poi sblocco e **ricarica** della scheda.

I valori di riepilogo li ricalcola lo SCADA: dopo il salvataggio i valori giusti sono quelli
riletti a valle della procedura, mai quelli in memoria.

### Fase 7 — diagnostica

`IBatchDiagnosticsService` con `HttpClient` tipizzato e indirizzo in configurazione; mappatura
della risposta a `OK` / `ATT` / `ERR` (`ERR` dallo stato, `ATT` se ci sono avvisi); **la risposta
si conserva verbatim** in `UsrDiagJson` e il referto del servizio in `UsrDiagMsg`, separati
(1.11); i campi `SvcDiag*` non si toccano mai; upsert delle billette
mancanti; marcatore visivo e riquadro di dettaglio; ripiego a testo per i messaggi storici che
JSON non sono; messaggio d'errore quando il servizio non risponde; regola su chi puo' eseguirla.

### Fase 8 — comandi di *Dati di produzione*

Selezione multipla e barra comandi: segna / annulla da riconciliare con la regola 1.16, apri
lotto, **nuovo lotto** come procedura guidata a due passi (testata, billette) secondo 1.14,
**elimina lotto** secondo 1.15. Piu' la chiusura forzata pressa/sega (1.5).

### Fase 9 — chiusura

Test di servizio sulle regole nuove (lock e scadenza, riallineamento dei marcatori,
rinumerazione, validazioni, sovrapposizione periodo, cascata di eliminazione, regola di
marcatura); verifica completa sul database di test; aggiornamento di `roadmap.md` (spuntare 1.3),
`architettura.md` (l'area produzione in scrittura) e `decisioni-aperte.md`; chiavi di
localizzazione nelle tre lingue, con il test che le percorre per riflessione.

### Dipendenze

```
1 → 2 → 3, 4, 5 → 6 → 7 → 8 → 9
```

La fase 7 potrebbe precedere la 6, ma la segue perche' l'effetto `HasMes` si applica al
salvataggio del lotto (1.10). La 8 dipende dalla 2 per l'apertura in modifica e dalla 7 solo per
leggere lo stato della diagnostica, non per eseguirla.

---

## 4. Rischi

**Lo stato in memoria della fase 4.** Tenere billette, aggiunte ed eliminazioni nel circuito per
tutta la durata della modifica e' la parte in cui i difetti si annidano: un riferimento sbagliato
fra riga di griglia e modello, e si salva la billetta sbagliata. Mitigazione: modello di modifica
esplicito con identita' propria per le righe nuove (che non hanno ancora una chiave), e test di
servizio sul rientro completo — apri, modifica, salva, rileggi.

**Il lock nel web.** Circuito disconnesso, scheda riaperta in un'altra finestra, sblocco forzato
a modifiche in corso: tre casi che il WinForms non aveva. La fase 2 li isola di proposito prima
che ci sia qualcosa da perdere.

**`usp_Batch_Elab` e i valori derivati.** Pesi, conteggi e tempi di ciclo non vanno calcolati qui
ne' scritti a mano (l'unica eccezione decisa e' `KgSheared` di billetta, 1.6). Un valore
ricalcolato in C# che poi la procedura sovrascrive produce una schermata che mente fino al
ricaricamento.

**Il messaggio all'ERP nell'eliminazione** e' l'unica azione irreversibile verso l'esterno di
tutto il modulo: va inviata **dentro** la transazione dell'eliminazione, non prima, e la
procedura va provata su test sapendo che l'ERP di test riceve davvero il messaggio.

---

## 5. Cosa ha insegnato la verifica

Le fasi sono state attuate il 9 settembre 2026 e verificate su `MES40_RDP_TEST` con una sonda
usa-e-getta fuori dal repository: legge davvero, e per le scritture apre una connessione e una
transazione **esterne** a EF, aggancia i contesti a quella transazione e alla fine la annulla. La
prova e' reale, il database resta invariato.

Quattro cose hanno cambiato il progetto rispetto a come era scritto qui l'8 settembre.

### 5.1. `usp_Batch_Elab` costa ~35 secondi, e non puo' stare nella transazione

Misurata tre volte, su lotti diversi e a cache calda: 34,2s, 36,8s, 36,7s. Il piano prevedeva di
chiamarla **dentro** la transazione per avere atomicita'. Non e' sostenibile: terrebbe i lock di
scrittura su `Press.Batch` e `Press.BatchBillet` per quaranta secondi, e su quelle tabelle la
raccolta dati del MES scrive di continuo.

Sta quindi **dopo il commit**, come nel vecchio applicativo, e con un **timeout esplicito di tre
minuti** — il predefinito dell'applicazione e' 30 secondi, quindi senza quello il ricalcolo
sarebbe fallito **sempre**, e nessun test su SQLite lo avrebbe rivelato. `SaveAsync` restituisce
percio' `BatchSaveResult`, che distingue "salvato" da "salvato ma non ricalcolato": nel secondo
caso i dati dell'operatore sono a database e i valori di riepilogo sono ancora quelli di prima, e
va detto invece che nascosto.

Il salvataggio di un lotto dura di conseguenza **circa quaranta secondi**, misurati: 37,9s su un
lotto da 13 billette. La scheda lo dichiara prima di cominciare.

### 5.2. Lo stato d'uso delle matrici: l'avviso sarebbe stato rumore

Sui lotti dei sei mesi precedenti: 215 su matrici disponibili, **94 su matrici senza riga di
stato d'uso**, 37 su matrici di prova, 1 su una matrice eliminata. Un avviso sullo stato mancante
avrebbe riguardato un quarto delle assegnazioni, e il rumore si impara a ignorare — anche quando
dice qualcosa. Lo stato mancante passa quindi in silenzio, come nella diagnosi del vecchio
applicativo; l'avviso resta per le matrici di prova.

Verificati tutti e cinque gli stati su matrici vere (32.693 in stato 0, 22.071 in 1, 17 in 2,
22.837 in 4, 598 in 5), piu' il caso della matrice inesistente e quello senza riga di stato.

### 5.3. Una risposta di diagnostica reale non stava nella colonna precedente

Il servizio ha risposto per un lotto vero con **4.158 caratteri** — 31 controlli, un avviso,
nessuna billetta mancante; con il campo `message` aggiunto l'11 settembre, 4.253. Il campo prima
della modifica del 9 settembre teneva 2.000 caratteri: la risposta non ci stava nemmeno nel caso
piu' semplice. Con `nvarchar(max)` si conserva intera, e si rilegge da database ancora
interpretabile (verificato).

### 5.4. Due difetti trovati dal codice, non dai test

- `HasColumnType("varchar(max)")` arriva **alla lettera** anche a SQLite, dove `max` non e'
  sintassi valida: `EnsureCreated` fallisce e cadono tutti i 313 test. Il tipo si dichiara
  lasciando la stringa senza lunghezza massima, che su SQL Server diventa `nvarchar(max)` e su
  SQLite `TEXT`.
- `EnsureCreated` **non crea le viste**, quindi la scheda del lotto non era verificabile nei
  test. Le tabelle corrispondenti si generano dal modello EF stesso
  (`tests/Support/ViewTables.cs`): ogni vista mappata in futuro riceve la sua tabella di prova
  senza che nessuno debba ricordarsene. E poiche' EF **rifiuta** di salvare un'entita' mappata a
  una vista — proprieta' voluta, sotto test — i dati di partenza delle viste si scrivono con SQL,
  come li scriverebbe il MES.

### 5.5. Cosa e' stato verificato sul database vero

| Fase | Verificato |
|---|---|
| 1 | scheda completa su un lotto vero: rettifiche, incestamento, ordini di produzione, `FirstDiagnostics*`; `DieCode` con spazi di riempimento ricomposto correttamente |
| 2 | presa del blocco, rientro nel proprio, contesa respinta col nome di chi lo tiene, sblocco forzato, rilascio, precondizioni su lotti reali |
| 3 | tutti e cinque gli stati d'uso delle matrici, la matrice inesistente, quella senza stato, il codice senza numero |
| 4-6 | salvataggio completo in 37,9s: causale, lunghezza barra, allungamento dell'ultima billetta, billetta nuova, rettifica; `usp_Batch_Elab` ha ricalcolato (13 → 14 billette, kg aggiornati), marcatori riallineati, blocco rilasciato |
| 7 | servizio interrogato davvero (HTTP 200, esito ATT), risposta conservata e rileggibile, `FirstDiagnostics*` non sovrascritta alla seconda esecuzione, rifiuto su lotto bloccato da altri |
| 8 | creazione (24,6s col ricalcolo, 3 billette piu' 2 marcatori), sovrapposizione respinta col nome del lotto in conflitto, marcatura saltata senza diagnostica e riuscita con esito OK, chiusura forzata, eliminazione completa col messaggio ERP nel formato dei 294 messaggi gia' in coda |
| 9 | 480 test verdi; prova di accensione dell'applicazione con le tre pagine di produzione servite (200) e la scheda che mostra le cinque schede |

**Cosa resta fuori dalla verifica.** L'interfaccia in un browser: nessun test di componente (voce
B2 di `decisioni-aperte.md`, invariata) e la prova di accensione esercita solo il primo
rendering, non il circuito interattivo. Modifica, salvataggio e comandi vanno provati a mano da
browser prima dell'esercizio — i percorsi di scrittura sono verificati sotto, al livello del
servizio, ma non attraverso i controlli che li richiamano.
