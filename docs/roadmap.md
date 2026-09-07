# Roadmap

Cosa manca per arrivare, in questa riscrittura, alle funzionalita' del vecchio applicativo
WinForms (`S:\Applications\MesDataManager`). Il modulo anagrafiche (`docs/architettura.md`) copre
oggi solo le tabelle di lookup — sedici anagrafiche in `MasterData` — non i dati transazionali di
produzione: batch, fermate, ordini, trasferimenti modulo, importazioni bilancia. Questo documento
elenca cosa resta, da dove si riprende nel vecchio codice e cosa cambia rispetto all'approccio
generico usato per le anagrafiche.

Stato: prima stesura, settembre 2026. Da aggiornare quando una voce viene chiusa, spuntandola
piuttosto che cancellandola — la corrispondenza col vecchio modulo resta utile anche a lavoro
fatto.

---

## Perche' queste pagine non sono un altro catalogo di metadati

Il modulo anagrafiche funziona perche' sedici tabelle sono strutturalmente simili: chiave,
qualche campo, poche relazioni. `docs/architettura.md`, sezione 7, lo dice esplicitamente: quel
disegno "smettera' di essere quello giusto" sui moduli testata/righe, dove la logica non e' "quali
campi ha questa tabella" ma "cosa significa chiudere un lotto" — transizioni di stato, validazioni
che attraversano piu' entita'. E' esattamente il caso di tutto cio' che segue: servizi applicativi
scritti a mano, pagine dedicate, non un'estensione del catalogo di `ArchiveCatalog.cs`.

Conseguenza pratica: **le entita' transazionali non erano in `MesDataManager.Domain`** e vanno
scritte una alla volta, con l'anagrafica gia' presente accanto. Con i fermi macchina (1.1) sono
arrivate `BatchDowntime` e `PressDowntimeType`; restano da scrivere `Batch`, `BatchBillet`,
`ProductionPlan`, `LogScaleImport` e le entita' di trasferimento modulo, con lo stesso approccio
database-first a mappatura manuale usato finora (sezione 5 di `architettura.md`): colonne reali
invariate, nomi C# secondo convenzione.

Il ruolo `Production.Editor` scrive dalla pagina dei fermi in avanti, tramite un permesso
separato da quello delle anagrafiche (`architettura.md`, sezione 8). Sulle pagine ancora da
scrivere vale come `Reader`, semplicemente perche' non c'e' altro a cui applicarsi.

**Un avvertimento sui volumi**, gia' registrato in `architettura.md` sezione 11: la griglia delle
anagrafiche carica tutte le righe, ed e' accettabile perche' la piu' popolata ha 249 righe. Le
tabelle di produzione non hanno questa proprieta' — `BatchDowntime` da sola ne ha 4,59 milioni.
Ogni pagina sotto ha bisogno di paginazione e filtri lato server fin dalla prima versione, non
come ottimizzazione successiva.

**Cosa non si riporta.** Il vecchio `VersionHelper`, che applicava script SQL incrementali
all'avvio, e' stato deliberatamente lasciato indietro (`architettura.md`, sezione 10): lo schema
delle tabelle sotto va verificato allineato prima di scrivere il servizio che le legge, con lo
stesso avvertimento gia' valso per le anagrafiche (`docs/pubblicazione.md`, sezione 4).

---

## 1. Pagine produzione

### 1.1. Fermi — fatto

Registrazione delle fermate macchina con causale, consultabili per pressa, periodo e tipo.

| | |
|---|---|
| Nel vecchio progetto | `ucDowntimes` / `DowntimesPresenter`, form `FrmDowntime` |
| Dov'e' ora | pagina `produzione/fermi` (`Components/Pages/MachineDowntime.razor`), servizio `MachineDowntimeService` |
| Entita' aggiunte | `BatchDowntime` (schema `Press`, 4,59M righe) e `PressDowntimeType` (schema `MasterData`) |
| Causali | `PressDowntimeReason`, gia' fra le anagrafiche: il filtro elenca solo le attive con master attivo, la griglia mostra la descrizione anche delle causali storiche disattivate |
| Permessi | scrivono `Production.Editor` e `Administrator`; `Archive.Editor` qui consulta soltanto |

**`PressDowntimeType` non e' un'anagrafica gestibile.** Non ha ne' `Position` ne' `IsActive`: due
righe fisse (`Microfermo` = 0, `Macrofermo` = 1, verificate sul database di test) che nessuno
amministra dall'applicazione, esattamente come nel vecchio progetto. Resta un lookup di sola
lettura per il filtro Tipo, e per questo non compare nel catalogo delle anagrafiche.

Tre comportamenti che il vecchio applicativo non aveva:

- **Periodo massimo per tipo**: 7 giorni di calendario per Macrofermo, 1 per Microfermo, estremi
  inclusi (`dal == al` vale un giorno). Serve perche' senza limite l'interrogazione gira su 4,59
  milioni di righe. Il limite si abbina alla **descrizione** del tipo e non al suo id, che dipende
  dai dati del database e non e' una costante deducibile dal codice.
- **Paginazione lato server** (`MudDataGrid.ServerData`): in un solo giorno i microfermi di tutte
  le presse sono centinaia — 486 il 15 aprile 2024 su `MES40_RDP_TEST` — quindi il modello delle
  anagrafiche, che carica tutte le righe e pagina nel browser, qui non si applica.
- **Sovrapposizione dei fermi**: il vecchio controllo verificava solo se un fermo esistente
  conteneva l'inizio o la fine del nuovo, e non vedeva il caso di un fermo nuovo che ne inghiotte
  uno esistente. Ora il confronto fra intervalli e' completo.

Resta fuori, deliberatamente: la verifica del periodo contro i lotti
(`IsPressBatchDowntimePeriodValid`), che nel vecchio applicativo era gia' commentata al momento
del salvataggio, e l'ordinamento per colonna, che con la paginazione lato server richiederebbe un
`ORDER BY` sulle descrizioni che arrivano da un join, senza un indice a sostenerlo. L'ordine e'
fisso: fermi piu' recenti in cima.

### 1.2. Dati di produzione — fatto

Elenco dei lotti **chiusi** a pressa e a sega, con i filtri della riconciliazione ERP.

| | |
|---|---|
| Nel vecchio progetto | `ucBatches` / `BatchesPresenter` (il titolo della pagina e' "Dati Produzione") |
| Dov'e' ora | pagina `produzione/dati` (`Components/Pages/ProductionData.razor`), `BatchService.GetProductionPageAsync` |
| Filtri | pressa, periodo, tipo (produzione/campionatura), stato di riconciliazione, lotto e matrice per frammento, piu' la casella "dettaglio lunghezza" |
| Riuso dalla 1.3 | entita' `Batch`, servizio e scheda di dettaglio: la riga cliccata apre la stessa scheda |

**"Dettaglio lunghezza" non aggiunge colonne: cambia la sorgente.** Senza, una riga per lotto letta
da `Press.Batch`; con, una riga per ogni combinazione di lunghezza barra e turno, letta dalla
funzione tabellare `EF.ufn_BatchByLengthShift(@startTs, @stopTs)` — dove il periodo e' un
**parametro** e non una condizione. Sui dati veri della settimana dell'8 aprile 2024, 242 lotti
diventano 341 righe. La funzione e' mappata in EF come funzione componibile, quindi filtri,
ordinamento e paginazione finiscono nella stessa query invece che in memoria.

Le quattro colonne del dettaglio — lunghezza barra, turno, data turno, lega/trattamento —
compaiono solo in quella modalita', e seguono la modalita' **con cui la pagina e' stata caricata**,
non la casella: cambiando la spunta senza premere "Applica" le colonne non devono cambiare sotto
righe che appartengono all'altra sorgente.

Come per i macrofermi, il **periodo non puo' superare una settimana**: senza limite la query
attraversa 240 mila lotti, e altrettante righe scomposte per turno. La regola vale su entrambe le
sorgenti.

Rispetto al vecchio applicativo: la ricerca per lotto e matrice passa **parametri** invece di
concatenare i valori nel testo SQL (nel legacy e' SQL injection a tutti gli effetti), e la
paginazione e' lato server. Non sono stati riportati il filtro "nascondi annullati" — che nel
vecchio codice arriva fino alla query e **non viene usato** — e il pulsante di stampa, mai visibile.

All'apertura la pagina propone gli **ultimi sette giorni**, cioe' la finestra piu' ampia
consentita, e lo stato "Non riconciliate" come nel vecchio applicativo. La scelta del dettaglio
lunghezza **resta memorizzata nel browser** (archivio locale protetto) e viene ripresa alla visita
successiva: e' una preferenza di chi guarda, non un dato, quindi se l'archivio non e' leggibile —
finestra anonima, chiavi di protezione cambiate — si riparte dal valore predefinito senza
segnalare nulla. Finche' la preferenza non e' nota la griglia non si disegna: caricarla per poi
rifarla mostrerebbe per un istante le colonne dell'altra sorgente e costerebbe
un'interrogazione buttata.

Restano fuori, perche' sono scritture o dipendono dalla diagnostica: la marcatura "da riconciliare"
sulle righe selezionate e la **diagnostica massiva** sull'intero elenco. Vedi 1.3, punto 3.

### 1.3. Lotti in corso — elenco e dettaglio fatti, scrittura da fare

Elenco dei lotti non ancora conclusi e scheda del singolo lotto, in sola consultazione.

| | |
|---|---|
| Nel vecchio progetto | `ucPendingBatches` (elenco) e `frmBatchDetail` (scheda), con `BatchesPresenter` |
| Dov'e' ora | pagine `produzione/lotti` e `produzione/lotti/{lotto}` (`Components/Pages/ProductionBatches.razor`, `ProductionBatchDetail.razor`), servizio `BatchService` |
| Entita' aggiunte | `Batch` e `BatchBillet` (schema `Press`), con la chiave esterna fra le due che **manca dall'EDMX del vecchio progetto** ma esiste a database |
| Nella scheda | testata, billette vere del lotto e macrofermi che si sovrappongono alla sua finestra (`IMachineDowntimeService.GetForBatchAsync`) |

**`BatchBillet.TypeID` non e' un dettaglio tecnico**: `0` apre il lotto, `1` e' una billetta vera,
`2` lo chiude. Le righe 0 e 2 portano gli stessi istanti della testata e **non** sono billette:
ogni conteggio o somma deve filtrare `TypeID = 1`, come faceva anche il vecchio applicativo.

Due condizioni del filtro sono regole di business riprese dal vecchio codice: il lotto deve avere
almeno una billetta vera (esclude i lotti fantasma) e non deve essere gia' importato in ERP. Due
comportamenti sono invece correzioni dichiarate: **paginazione vera** al posto del `SELECT TOP(10)`
cablato, e ordine per **data di inizio** invece che per `BatchID`, che cominciando con la sigla
della pressa raggruppava per pressa prima che per data. Niente filtro di periodo, come nel vecchio
applicativo: un lotto aperto da mesi e' l'anomalia da vedere.

**Sui dati di test l'elenco e' vuoto, e non e' un difetto**: dei 331 lotti con una chiusura
mancante nessuno ha `IsErpImported = 0` — in tutto il database solo due lotti non sono importati,
ed entrambi sono chiusi. Il vecchio applicativo non mostrerebbe nulla neanche lui. La scheda invece
e' stata verificata su lotti veri, e ha fatto emergere che `DieCode` contiene spazi di riempimento
su alcune righe mentre `DieId` e' pulito: per questo la matrice si legge da `DieId`, come faceva
la vecchia scheda.

Restano da fare, in ordine di dipendenza:

1. **Lock del lotto e modifica delle billette.** Il lock del vecchio applicativo e' pessimistico,
   persistito su riga e **senza scadenza**: un blocco orfano resta tale a tempo indeterminato, e
   basta premere "Modifica" su un lotto gia' riconciliato per bloccarlo per sempre. Da rifare, non
   da ricopiare. Sul database di test si vedono ancora lotti con `IsLock = 1`.
2. **Le operazioni di correzione**: ordine di produzione, colata, lunghezze, rinumerazione,
   matrice, causale di chiusura, nuova e duplica billetta. Nel vecchio applicativo scrivono tutte
   in differita, sul contesto EF unico di sessione, e la `SaveChanges` avviene al salvataggio della
   scheda: qui serve una transazione esplicita.
3. **Chiusura forzata pressa/sega** (stored procedure `usp_Batch_PressClose`/`usp_Batch_SawClose`),
   **marcatura "da riconciliare"** (che nel vecchio codice richiede il superamento della
   diagnostica) ed **eliminazione** (cascata manuale su piu' tabelle, compresa `Press.BatchWorker`
   che l'EDMX non modella, piu' un messaggio asincrono verso l'ERP).
4. **Diagnostica**: servizio HTTP esterno con ripiego su ~340 righe di regole locali. Attenzione:
   sulle presse senza MES la diagnostica **decide anche** `IsPressClosed`/`IsSawClosed`.

**I valori di riepilogo li ricalcola lo SCADA, non questa applicazione.** `usp_Batch_Elab`
ricalcola i valori di riepilogo di lotto e billette e **appartiene alle procedure di raccolta dati
del sistema SCADA**: la sua logica non va replicata qui. La regola per la tranche di scrittura e'
quindi semplice — l'applicazione scrive solo cio' che l'operatore ha modificato e poi **richiama
la procedura dopo il salvataggio dei dati di lotto**, come faceva il vecchio applicativo al
termine del salvataggio della scheda.

Ne discende che pesi, conteggi e tempi di ciclo sono **derivati**: non vanno calcolati in C# ne'
scritti a mano, e dopo un salvataggio i valori giusti sono quelli che si rileggono a valle della
procedura, non quelli in memoria.

### 1.4. Storico ceste

Storico dei trasferimenti dei moduli (ceste) fra reparti, con causali di scarto e rilavorazione.

| | |
|---|---|
| Nel vecchio progetto | `ucModuleTrans`, form `frmModuleTrans`, `FrmModuleQtyHistory` |
| Entita' coinvolte | `Module`, `Module_ModuleTrans`, `Module_ModuleTransRoute`, `Module_ModuleTransScrap`, funzione `ufn_ModuleQtyHistory_Result`; causali gia' migrate come anagrafiche: `ModuleRepairReason`, `ModuleScrapReason`, `ModuleTransRouteReason` |
| Cosa manca | entita' di trasferimento in `Domain`/`Infrastructure` (`Module` esiste gia' come anagrafica, le tabelle di transazione no), servizio di consultazione storica, pagina Blazor |
| Nota | modulo senza presenter dedicato nel vecchio progetto (tabella della sezione 4 di `technical-design-document.md`): la logica sta nello `UserCtrl` stesso, va estratta in un servizio applicativo qui, non solo tradotta |

### 1.5. Storico carico

Storico delle importazioni dei log di pesatura da bilancia.

| | |
|---|---|
| Nel vecchio progetto | `ucLogScaleImport` / `LogScaleImportsPresenter` |
| Entita' coinvolte | `LogScaleImport` |
| Cosa manca | entita' `LogScaleImport` in `Domain`/`Infrastructure`, servizio di consultazione (e capire se l'importazione stessa resta un processo esterno o va rifatta qui — nel vecchio progetto non e' chiaro dal solo codice GUI), pagina Blazor |

---


## 2. Homepage con indicatori — fatta

Sostituisce l'elenco delle voci di menu con un pannello di sintesi, diviso in sezioni verticali
(`Components/Pages/Home.razor`, servizio `HomeIndicatorService`).

**Attivita' in corso** — una targa per pressa con due numeri **cliccabili**: i lotti in corso
aprono `produzione/lotti` filtrato su quella pressa, quelli da riconciliare aprono
`produzione/dati` con lo stato corrispondente. Un numero senza la strada per arrivare al dettaglio
serve a poco, e cosi' la home resta anche una via d'ingresso — era la ragione per cui prima
mostrava il menu. Il conteggio degli aperti usa **le stesse condizioni della pagina "Lotti in
corso"** (almeno una billetta vera, lotto non ancora importato in ERP): un indicatore che contasse
righe che la pagina non mostra darebbe un numero che, cliccato, apre un elenco piu' corto.

**Macrofermi** e **Microfermi** sono due sezioni pari all'attivita' in corso, non sottosezioni di
un raggruppamento: si leggono allo stesso modo, una scheda per pressa con **tutti** i turni della
giornata e, per ciascuno, quanti fermi e quanto sono durati. I turni senza fermi mostrano zero:
un turno assente lascerebbe il dubbio che il dato manchi. Separati perche' i macrofermi si contano
a decine e i microfermi a centinaia: sul 15 aprile 2024, MP6 nel turno T1 ha 2 macrofermi per 6
minuti e 169 microfermi per un'ora e ventidue.

**Come si attribuisce un fermo a un turno.** `Press.BatchDowntime` non porta ne' turno ne' lotto:
solo pressa e istanti. Le finestre dei turni arrivano dalla funzione dell'impianto
`Press.ufn_GetShifts` — non da una regola inventata qui, e non dalle billette: i fermi capitano
spesso **fra** una billetta e l'altra (cambio billetta, cambio matrice) e ricavare le finestre
dalla produzione li perderebbe. Della funzione si usano le finestre **estese** e non quelle di
calendario: le prime combaciano fra loro, quindi ogni istante appartiene a un turno e uno solo,
mentre le seconde lasciano scoperti gli intervalli fra un turno e il successivo. Il calendario
`MasterData.DateShift` e' vuoto sul database di test e la funzione ripiega su tre turni di otto
ore, dichiarandolo con `IsFromCalendar`.

La giornata di produzione non e' quella del calendario: il turno di notte scavalca la mezzanotte e
appartiene al giorno in cui e' cominciato, quindi prima delle sei del mattino il pannello mostra
ancora la giornata di ieri.

I turni stanno dietro `IShiftCalendar` perche' la funzione di SQL Server non esiste su SQLite:
l'aggregazione — attribuzione al turno, conteggi, somme, istante di confine, durate incoerenti —
e' cosi' verificabile nei test con un calendario finto.

**Altri indicatori da definire** — candidati una volta scritta la 1.4: conteggio fermate aperte
per pressa, ultimo carico bilancia importato. Da confermare con chi usa l'applicazione prima di
implementare, non da anticipare qui.
