# Decisioni architetturali — modulo Anagrafiche

Documento di accompagnamento alla riscrittura. Serve a chi tocchera' questo codice fra qualche
mese, incluso chi lo ha scritto: registra **perche'** le cose stanno cosi', che e' l'unica parte
che il codice non racconta da solo.

Stato: prima stesura, settembre 2026. Da aggiornare quando una decisione cambia, non da
riscrivere: le decisioni superate restano, marcate come tali.

---

## 1. Contesto di partenza

L'applicazione precedente era un client WinForms su .NET Framework 4.7.2, con accesso ai dati
via Entity Framework 6 su modello EDMX (`ModelRDP.edmx`), architettura Model-View-Presenter e
un `RepositoryService` che esponeva `DbSet` filtrati per ciascuna anagrafica.

Il modulo anagrafiche era interamente concentrato in un unico user control (`UCArchives`) con
il presenter `ArchivePresenter`: un `TreeView` a due nodi per scegliere la tabella e una griglia
per il contenuto, con modifica in linea.

Le tabelle vivono nello schema SQL `MasterData` di un database MES pre-esistente, condiviso con
altri consumatori.

---

## 2. Blazor Server, non WebAssembly ne' Auto

**Decisione.** L'interfaccia e' Blazor Server con render mode interattivo lato server. I
componenti accedono ai servizi applicativi per dependency injection, in-process. Non esiste
un'API HTTP fra interfaccia e dati.

**Dove si dichiara, e perche' e' facile sbagliare.** In `Program.cs`
`AddInteractiveServerComponents()` e `AddInteractiveServerRenderMode()` rendono la modalita'
**disponibile**, non attiva: da .NET 8 i componenti devono chiederla con `@rendermode`, e la
dichiarazione sta su `<HeadOutlet>` e `<Routes>` in `Components/App.razor`.

Ometterla non produce ne' errori di compilazione ne' errori a runtime. L'applicazione risponde,
le pagine si disegnano e i dati si leggono — il rendering statico esegue `OnInitializedAsync` —
ma non esiste nessun circuito, quindi **nessun gestore di eventi viene mai eseguito**: niente
finestre di dialogo, niente paginazione, niente ricerca, niente interruttori. Sembra un guasto
dei singoli comandi, ed e' invece l'assenza di una riga.

Come riconoscerlo in un minuto: nell'HTML servito un componente interattivo lascia un commento
`<!--Blazor:{"type":"server",...}-->`. Se non c'e', la pagina e' statica.

**Perche'.** L'applicazione e' uno strumento interno di stabilimento: postazioni di reparto e
scrivanie sulla stessa rete del database. In questo scenario Server e' la scelta che porta meno
parti mobili — nessun layer HTTP da progettare, autenticare e versionare, latenza minima verso
SQL Server, un solo processo da distribuire.

**L'alternativa scartata, e il perche' conta.** WebAssembly eseguirebbe il codice nel browser e
richiederebbe quindi obbligatoriamente una vera API, perche' il browser non puo' parlare
direttamente con il database. Auto e' una via di mezzo tecnica — primo caricamento in modalita'
Server, poi passaggio a WebAssembly — ma **usa WASM per una parte dell'esecuzione, quindi
pretende comunque l'API dal primo giorno**.

Vale sottolinearlo perche' e' un fraintendimento facile: Auto **non e' un percorso graduale**
verso WASM da imboccare quando servira'. Non e' "Server con la possibilita' di crescere": e'
un'architettura diversa, con costi diversi, da scegliere in partenza.

**Cosa dara' spazio alla complessita' futura.** Non il render mode. Logiche di business anche
molto elaborate stanno benissimo in Blazor Server: si aggiungono servizi, regole, validazioni.
Il render mode non e' il fattore limitante.

**Cosa invece giustificherebbe rivedere questa scelta** (vedi il registro delle decisioni
aperte):

- serve un client esterno — app mobile, integrazione di terzi, secondo front-end;
- serve funzionamento offline o tollerante alla disconnessione su postazioni di reparto;
- il numero di sessioni simultanee rende oneroso tenere lo stato di ogni circuito sul server.

Nessuna delle tre e' un requisito oggi.

---

## 3. Separazione in layer, con le dipendenze verso l'interno

```
Web  →  Infrastructure  →  Application  →  Domain
```

`Domain` non dipende da nulla. `Application` dichiara contratti e non conosce EF Core.
`Infrastructure` implementa quei contratti con EF Core e SQL Server. `Web` conosce solo
`Application` per i contratti e `Infrastructure` per la registrazione dei servizi.

**Perche', dato che abbiamo appena detto che non serve un'API.** Perche' il costo di questa
separazione oggi e' quasi nullo — sono quattro progetti invece di uno — e il beneficio si
incassa il giorno in cui una delle tre condizioni del punto 2 si verifica. In quel caso basta
aggiungere un progetto `MesDataManager.Api` con Minimal API che richiama **gli stessi servizi
applicativi**, senza riscrivere logica.

La regola operativa che rende vero tutto questo: **i servizi applicativi non devono sapere che
esiste Blazor.** Se un servizio comincia a ricevere `NavigationManager`, `ISnackbar` o un tipo
di MudBlazor, la separazione e' rotta e la promessa del paragrafo precedente decade.

---

## 4. Riscrittura da zero, con recupero selettivo

**Decisione.** Il codice del modulo anagrafiche e' nuovo. Nessuna classe del vecchio progetto
e' stata portata.

**Perche'.** Il salto da .NET Framework 4.7.2 a .NET 10 non e' una trasposizione: DI, async
pervasivo, EF6 verso EF Core. Il codice non si sposta comunque in automatico. In piu' il vecchio
modulo aveva accumulato aggiustamenti legati ai limiti della griglia WinForms — validazioni
sparse fra eventi della griglia e click dei pulsanti, stato gestito a mano — che riprodurre in
Blazor avrebbe significato ereditare i problemi in una veste nuova.

**Cosa e' stato invece recuperato, e vale piu' del codice:**

| Cosa | Da dove |
|---|---|
| Schema fisico: tipi, lunghezze, nullabilita', chiavi, identity | `ModelRDP.edmx`, sezione SSDL |
| Schema SQL di appartenenza (`MasterData`) | `EntitySet` dell'EDMX |
| Le regole di business implicite (sezione 6) | `UCArchives`, `ArchivePresenter` |
| Etichette in italiano, inglese e francese | `Resources/Languages/Strings*.resx` |

Sulle traduzioni: 66 chiavi su 72 sono state riprese **testualmente** dai resx esistenti. Non e'
pigrizia — gli operatori e i capireparto conoscono quella terminologia, e cambiarla in una
riscrittura crea attrito senza dare nulla in cambio.

---

## 5. Database First, senza migration

**Decisione.** Il `MesDbContext` mappa a mano le colonne esistenti con `HasColumnName`,
`HasMaxLength`, `HasColumnType`. Il progetto **non genera e non applica migration**.

**Perche'.** Il database e' pre-esistente, condiviso con altri consumatori del MES e non e'
proprieta' di questa applicazione. Una migration generata da qui potrebbe proporre modifiche
allo schema che nessuno ha chiesto.

**Conseguenza pratica.** I nomi C# seguono le convenzioni .NET (`ModuleRepairReasonId`,
`IsActiveMaster`) mentre le colonne restano quelle reali (`ModuleRepairReasonID`,
`IsActive_Master`). La traduzione sta tutta nel `DbContext`. Se lo schema cambia, si aggiorna
lì e, dove serve, nel catalogo.

**Attenzione ricorrente.** Le chiavi di risorsa delle etichette seguono i nomi C#, non quelli
delle colonne: il resx importato dal WinForms usava i secondi (`OprID`, `CompanyID`) e la
rinomina va fatta su entrambi i lati. Il localizzatore distingue maiuscole e minuscole e in
caso di mancata corrispondenza non segnala niente — mostra la chiave grezza. Un test del
catalogo verifica ora che ogni etichetta dichiarata esista nei tre resx.

### Nomi delle chiavi di risorsa

Un prefisso raggruppa le chiavi per tipo di testo, cosi' dal nome si capisce a cosa serve una
voce senza cercarla nel codice. `ResourceKeys` e' l'unico punto che conosce i prefissi.

| Gruppo | Contenuto | Come si ricava |
|---|---|---|
| `Archive.` | Nome dell'anagrafica: menu e titolo di pagina | Da `ArchiveDescriptor.Key` |
| `Field.` | Etichetta di colonna | Dal nome della proprieta' C#, o da `ArchiveField.LabelKey` |
| `Nav.` | Navigazione e gruppi del menu | Letterale |
| `Action.` | Pulsanti e comandi | Letterale |
| `Msg.` | Esiti, conferme, suggerimenti, titoli dei dialog | Letterale o `ResourceKeys.Message` |
| `Error.` | Errori e validazione | Dalle factory di `ArchiveException` |
| `App.` | Titolo dell'applicazione e selettore di lingua | Letterale |

Nomi dell'anagrafica ed etichette dei campi si ricavano per convenzione: dichiararli a mano
significherebbe tenere allineate due liste. Prima dei prefissi la convenzione costringeva a
distinguere le collisioni a mano — `PressArchive` per la tabella perche' `Press` sarebbe finita
addosso al campo — e il suffisso finiva su meta' delle tabelle e non sull'altra meta'.

I messaggi si costruiscono solo dalle factory statiche di `ArchiveException`: sono l'elenco
completo di cio' che l'applicazione puo' mostrare, e un test le percorre per riflessione
verificando che ogni chiave sia tradotta nelle tre lingue. Prima meta' delle chiavi era una
stringa inline nel servizio e nessun test poteva vederle: `Error.RecordNotFound` e
`Error.ArchiveNotFound` erano citate dal codice ma assenti da tutti e tre i resx.

**I trigger vanno dichiarati, o la scrittura non parte.** `Press.BatchDowntime` ha un trigger su
INSERT/UPDATE/DELETE (`TR_Press_BatchDowntime`, che alimenta lo storico in
`History._BatchDowntime`). Da EF Core 7 il salvataggio rilegge i valori generati dal database con
una clausola `OUTPUT`, e SQL Server la rifiuta su una tabella con trigger: senza
`t.HasTrigger("...")` nella mappatura **ogni** inserimento, modifica ed eliminazione falliva con
`DbUpdateException`, mentre le letture funzionavano regolarmente.

E' un guasto scomodo per due motivi. Il primo: si manifesta solo sulla scrittura, quindi una
pagina di sola consultazione lo attraversa senza accorgersene. Il secondo: **i test su SQLite non
lo vedono**, perche' SQLite non ha trigger — il caso e' stato scoperto provando a salvare sul
database vero. Per questo esiste un test che guarda il **modello** invece del database
(`Il_trigger_di_BatchDowntime_e_dichiarato_nel_modello`): e' l'unico modo di far fallire in
locale una regressione su questa riga.

Fra le tabelle usate oggi il trigger e' solo su `BatchDowntime`, ed e' la ragione per cui la
scrittura sulle anagrafiche non aveva mai dato problemi. Verificato che le tabelle delle prossime
voci di roadmap (`Batch`, `BatchBillet`, `ProductionPlan`, `LogScaleImport`, i trasferimenti
modulo) non ne hanno; se un domani ne comparisse uno, il sintomo e' quello descritto qui.

**Un contesto per operazione, non per sessione.** `MesDbContext` si ottiene da
`IDbContextFactory` e viene smaltito a fine operazione. In Blazor Server un servizio con ambito
vive quanto il circuito, cioè quanto l'intera sessione dell'utente: un `DbContext` registrato
così sarebbe condiviso fra tutti i componenti della pagina — e due operazioni sovrapposte lo
fanno cadere — oltre a tenere aperta la connessione per tutto il tempo.

Per la stessa ragione **il tracciamento delle entità resta attivo**: le letture chiedono
`AsNoTracking()` per conto proprio, ma la modifica funziona caricando l'entità tracciata e
mutandone le proprietà. Disattivare il tracciamento a livello di contesto rende `Find`
restituzione di un'entità distaccata, e `SaveChanges` non scrive nulla **senza segnalare
errori**: è il tipo di guasto che nessuna prova manuale nota subito, perché la UI conferma il
salvataggio.

---

## 6. Le regole di business recuperate dal WinForms

Sono la parte piu' preziosa dell'analisi del vecchio codice, perche' non erano scritte da
nessuna parte: stavano solo nell'implementazione, accumulate nel tempo.

| Regola | Dov'era | Dov'e' ora |
|---|---|---|
| Sulle tabelle allineate dall'ERP si modificano solo `Position` e `IsActive` | `SetEditableGrid()` | `ArchiveDescriptor.IsWritable` |
| `IsActive` si puo' alzare **solo se `IsActive_Master` e' true** | `GrdMasterData_RowEnter()` | `ArchiveService.EnforceMasterActivationRule` |
| Quelle tabelle non ammettono inserimenti ne' eliminazioni | menu contestuale su `IsMasterTable` | `ArchiveEditPolicy.MasterControlled` |
| Filtro "mostra voci non attive" | `chkShowAll` + `GetDbSet*(showInactive)` | `ArchiveQuery.IncludeInactive` |
| Colonne larghe per descrizioni, note, e-mail | `AdjustGridColumn()` | `ArchiveField.IsWide` |

**Uno spostamento deliberato.** La regola su `IsActive_Master` era nella griglia, cioe' nella
UI. Ora e' nel servizio. Motivo: una regola che vive nella UI vale solo per chi passa da quella
UI. Nel servizio vale per qualunque chiamante — inclusa la futura API, e inclusi gli script di
manutenzione se un giorno passeranno da qui.

**Due scoperte dall'analisi**, entrambe conservate come comportamento:

1. `PressFailureType` compare fra gli "Archivi generali" ma **non era** fra le master table del
   vecchio codice, perche' non ha ne' `Position` ne' `IsActive`. Il raggruppamento nel menu e le
   regole di modifica sono quindi due concetti distinti, e nel nuovo modello sono modellati
   separatamente: `ArchiveGroup` e `ArchiveEditPolicy`.

   Su questa tabella l'eliminazione e' stata poi **disabilitata** (`PreventDelete`), per un
   motivo che non riguarda ne' il gruppo ne' la policy ma lo schema: e' referenziata dallo
   storico dei fermi macchina senza vincolo di chiave esterna. Ed e' la ragione per cui il
   divieto e' un flag a se' e non un quarto valore dell'enumerazione — la policy dice **come
   si governa** l'anagrafica, il flag dice **cosa non regge** a database. Il giorno in cui il
   vincolo esiste si toglie una riga dal catalogo. Vedi il registro, voce A5.
2. `Press`, `Oven` e `HeatThreatment` avevano codice di lettura nel `RepositoryService`, un caso
   nello `switch` del presenter e le etichette tradotte in tutte e tre le lingue, ma erano state
   rimosse dai due elenchi che popolavano il `TreeView`: erano **codice morto irraggiungibile**.
   Le anagrafiche effettivamente usabili erano tredici, non sedici. Vedi il registro delle
   decisioni aperte.

---

## 7. CRUD generico guidato dai metadati

**Decisione.** Le anagrafiche condividono una sola pagina (`Components/Pages/Archive.razor`) e
un solo servizio. Quel che distingue una tabella dall'altra e' dichiarato in
`Infrastructure/Archives/ArchiveCatalog.cs`: entita', gruppo nel menu, policy di modifica,
elenco dei campi con tipo, obbligatorieta', lunghezza massima, editabilita'.

Da quei metadati si generano colonne della griglia, campi del form, validazione e voci di menu.

**Perche'.** Sedici tabelle strutturalmente simili — chiave, qualche campo, relazioni
occasionali. Scrivere sedici pagine avrebbe prodotto sedici posti in cui correggere lo stesso
bug. Aggiungere una colonna oggi e' una riga nel catalogo.

**Come le righe viaggiano.** `ArchiveRow` porta i valori in un dizionario indicizzato per nome
di campo, invece di un tipo generico per tabella. Cosi' la griglia e il form sono scritti una
volta sola, e le entita' EF Core non arrivano alla UI. Il prezzo e' la perdita del controllo di
tipo a compile time nel passaggio UI-servizio: la riconduzione al tipo esatto della colonna
avviene in `FieldValueConverter`, lato infrastruttura.

**Dove questo approccio smettera' di essere quello giusto.** Sui moduli testata/righe. Lì la
logica non e' "quali campi ha questa tabella" ma "cosa significa chiudere un lotto", con
validazioni che coinvolgono piu' entita' e transizioni di stato. Un catalogo di metadati non
esprime quel tipo di regole, e forzarlo produrrebbe un motore di configurazione illeggibile.
Quei moduli vogliono servizi applicativi scritti a mano e pagine dedicate.

Detto altrimenti: il generico e' la risposta giusta per il CRUD ripetitivo, non un principio da
estendere a tutta l'applicazione.

---

## 7-bis. La produzione in scrittura: modifiche in sospeso e blocco su riga

**Decisione.** L'area produzione non estende il catalogo dei metadati (vedi la sezione 7): sono
servizi applicativi scritti a mano e pagine dedicate. Due scelte di impianto la distinguono dalle
anagrafiche, e valgono per i moduli testata/righe che arriveranno.

**Le modifiche in sospeso non sono entita' tracciate.** Un lotto in modifica vive in un oggetto
dell'applicazione (`BatchEditModel`) che tiene la fotografia di partenza accanto ai valori
correnti; le entita' EF si materializzano solo al salvataggio, dentro una transazione sola. Il
vecchio applicativo otteneva lo stesso risultato con un contesto EF unico di sessione, che in una
applicazione web sarebbe condiviso male e terrebbe aperta una connessione per l'intera modifica.

Conseguenza utile: "c'e' qualcosa da salvare" si ottiene **confrontando** i valori con quelli di
partenza, non alzando un flag che qualcuno deve ricordarsi di alzare a ogni cella toccata — che
nel WinForms era il compito di un gestore della griglia.

**Il blocco e' pessimistico e sta a database.** `Batch.IsLock`/`Lock_Usr`/`Lock_Ts`, come prima,
con tre differenze imposte dal web: si prende con **una sola istruzione condizionata** (leggere e
poi scrivere lascia la finestra in cui due utenti passano entrambi il controllo), si rilascia alla
dismissione del circuito, e **scade dopo 60 minuti** — perche' nel web la sessione muore in
silenzio e il blocco orfano sarebbe la norma. La scadenza vale per gli altri: chi possiede il
blocco continua a salvare anche oltre l'ora, se nessuno gliel'ha portato via.

**Le procedure lunghe del MES non si chiamano affatto: si mette il lotto nella loro coda.**
`usp_Batch_Elab` impiega ~35 secondi per lotto (misurato), e sul MES gira gia' un lavoro
pianificato che ogni cinque minuti la esegue su tutti i lotti con `IsPressClosed = 1` e
`IsBatchProcessed = 0`. Il salvataggio si limita quindi a rimettere quel flag a zero, dentro la
transazione, e finisce in mezzo secondo; il lotto resta non modificabile finche' il MES non ha
finito, che e' la stessa precondizione di sempre. Deciso l'11 settembre 2026, al posto della
chiamata sul momento che faceva durare quaranta secondi ogni salvataggio. E' anche il motivo per
cui i valori derivati — pesi, conteggi, tempi di ciclo — non si calcolano qui: appartengono alla
procedura, e fino al suo passaggio restano quelli di prima.

---

## 8. Entra ID, con ruoli applicativi e non gruppi

**Decisione.** Autenticazione OpenID Connect verso Microsoft Entra ID. I permessi derivano dai
**ruoli dell'app registration** (claim `roles`): `Administrator`, `Reader`, `Archive.Editor`,
`Production.Editor`.

**Ambiti, non una gerarchia.** `Archive.Editor` gestisce le anagrafiche, `Production.Editor` i
dati di produzione — dalla pagina dei fermi macchina in avanti — `Administrator` vale su tutto e
`Reader` solo consulta. I ruoli non si sommano in scala: l'ambito sta nel nome perche' questa
applicazione non e' solo le anagrafiche.

**La lettura non si divide per ambito, la scrittura si'.** Un `Reader` vede tutto; un editor
scrive il suo ambito e legge il resto.

**Due permessi di scrittura, non uno.** `UserPermissions` tiene separati `CanInsert`/`CanUpdate`/
`CanDelete`, che valgono solo per le anagrafiche, e `CanEditProduction`, che vale per i dati di
produzione. Un solo gruppo di flag sarebbe stato piu' breve e sbagliato: le pagine delle
anagrafiche leggono quei tre, quindi concedere la scrittura a `Production.Editor` alzandoli
avrebbe aperto anche le anagrafiche a chi registra i fermi. La separazione degli ambiti vale solo
se esiste anche nei permessi che ne derivano.

**Serve un ruolo anche solo per leggere.** Chi e' autenticato ma non ha nessuno dei quattro
ruoli riceve accesso negato. Entra ID lo fermerebbe prima — l'applicazione richiede
l'assegnazione — ma il controllo e' ripetuto nel codice di proposito: la riservatezza dei dati
non deve poggiare su un'impostazione del portale, che qualcuno potra' spostare in futuro per
un motivo che non ha niente a che vedere con questi dati.

Deciso il 3 settembre 2026: registro delle decisioni, voce A2.

**Perche' i ruoli e non i gruppi di dominio.** I gruppi appartengono alla struttura
organizzativa del tenant, che cambia per motivi che non hanno nulla a che vedere con questa
applicazione. I ruoli appartengono all'applicazione. Il collegamento fra i due lo fa
l'amministratore del tenant, dove va fatto.

**Perche' solo autenticazione, senza permessi Graph.** Non serve leggere nulla dal tenant, e
questo elimina il client secret: non c'e' nessun segreto da custodire e ruotare.

**Cosa non c'era prima.** L'`AuthService` del vecchio progetto restituiva sempre tutti i
permessi, con un `TODO` accanto. I permessi sono quindi codice nuovo e non ancora provato in
esercizio: la modalita' di sviluppo (sezione 9) serve anche a verificarli.

**Da dove arriva l'identita'.** Da `AuthenticationStateProvider`, non da `HttpContext`. In
rendering interattivo `HttpContext` esiste soltanto durante il prerendering: appena il circuito
e' stabilito diventa nullo, e un contesto utente costruito su `IHttpContextAccessor`
risulterebbe non autenticato da quel momento in poi — negando ogni permesso, compresa la
lettura, subito dopo il primo disegno della pagina.

Ne consegue che `IUserContext` e' **asincrono**: lo stato di autenticazione si ottiene con
un'attesa. I componenti ne prendono un'istantanea in `OnInitializedAsync` e il markup legge
quella; il servizio ricontrolla comunque a ogni operazione, quindi i permessi in pagina
servono solo a non mostrare pulsanti che darebbero un rifiuto.

---

## 9. Modalita' di autenticazione per lo sviluppo locale

**Decisione.** `Authentication:Mode` sceglie fra `EntraId` e `Development`. In modalita'
`Development` un handler autentica ogni richiesta con un utente fittizio, con ruoli
configurabili.

**Perche'.** Permette di lavorare sul modulo prima che esista la registrazione applicativa, e di
verificare i tre livelli di permesso cambiando un array in configurazione invece di creare tre
utenti sul tenant.

**Le due protezioni, e perche' non sono opzionali.** Quella modalita' non e' un login
alternativo: e' un cortocircuito completo dell'autenticazione.

1. Se `Mode = Development` in un ambiente diverso da `Development`, **l'applicazione non parte**
   e dice perche'. Un'applicazione che rifiuta di avviarsi e' molto meglio di una che va in
   esercizio senza controlli per un `appsettings` dimenticato.
2. Se `Mode = EntraId` con `TenantId` o `ClientId` non compilati, l'errore e' esplicito. Il
   messaggio nativo di OpenID Connect in quel caso (`IDX20807`) non dice quale impostazione
   manca, e fa perdere tempo.

---

## 10. Cosa e' stato deliberatamente lasciato indietro

**Il versionamento dello schema all'avvio.** Il vecchio `VersionHelper` applicava script SQL
incrementali all'avvio dell'applicazione. Non e' stato riportato: eseguire DDL all'avvio di
un'applicazione web con piu' istanze non e' sicuro — due istanze che partono insieme
eseguirebbero lo stesso script in parallelo. Gli script vanno gestiti dal processo di deploy del
database. Vedi il registro delle decisioni aperte.

**La modifica in linea nella griglia.** Il vecchio modulo si modificava direttamente nelle celle.
Il nuovo usa una finestra di dialogo generata dai metadati. Motivo: il form dichiara
esplicitamente cosa e' scrivibile e cosa no, e puo' **spiegare perche'** un campo e' bloccato —
cosa che una cella grigia non fa. Su tabelle dove la meta' dei campi arriva dall'ERP, questo
conta.

Ne consegue una divisione di ruoli fra i due: **la griglia mostra i campi principali
(`ShowInGrid`), la scheda tutti.** Su `Worker` restano fuori dalla griglia i dieci flag di
mansione, su `Press` e `Oven` i parametri di monitor e taglierina: metterli in griglia
renderebbe illeggibile l'elenco, ma da qualche parte devono potersi leggere.

Per questo la scheda si apre **anche in sola consultazione**, con un comando dedicato, e non
solo quando si ha il permesso di modificare: se dipendesse dai permessi di scrittura, i campi
fuori dalla griglia sarebbero invisibili a chi ha il ruolo `Reader` — e nelle anagrafiche in
sola lettura, come `Press`, non ci sarebbe **nessun** modo di vederli. In consultazione la
scheda mostra anche la chiave generata, che altrove non compare mai.

**I permessi permissivi.** Vedi sezione 8.

---

## 11. Cosa e' verificato e cosa no

Onesta' sullo stato di questa consegna, perche' incide su come leggerla.

**Verificato (2 settembre 2026).**

- **La build.** La solution era stata scritta senza poter essere compilata (nessun SDK .NET
  nell'ambiente di generazione) e in effetti **non compilava**: un tag `ChildContent` non chiuso
  in `MainLayout.razor`. Ora compila senza avvisi con .NET SDK 10.0.400.
- **L'avvio.** L'applicazione parte, la home e la pagina di un'anagrafica rispondono, le
  etichette escono tradotte. Con la stringa di connessione mancante non parte affatto, e dice
  quale impostazione manca.
- **Le regole del servizio.** 190 test in `tests/MesDataManager.Tests`, su SQLite in memoria:
  matrice dei permessi, regola su `IsActive_Master`, validazione, ordinamento e ricerca,
  coerenza del catalogo, mappatura dei ruoli.
- **Le versioni dei pacchetti.** Aggiornate e fissate; alla data non risultavano aggiornamenti
  disponibili per i pacchetti effettivamente referenziati.

- **La mappatura sul database reale.** Verificata su `MES40_RDP_TEST` (SQL Server 2022): una
  lettura per ciascuna delle sedici anagrafiche, tutte riuscite. Nessuna colonna mancante o
  incompatibile, schema `MasterData` compreso — l'EDMX era allineato. Alcune tabelle erano vuote
  al momento della prova (`DieCorrectionIssue`, `EmailRecipient`, `HeatThreatment`, `Oven`,
  `OvenRecipe`), il che non intacca la verifica: SQL Server risolve i nomi di colonna anche su
  una tabella senza righe.

  Attenzione: `MES40_RDP_TEST` e' un ambiente **vivo**, non una fotografia. Durante la sessione
  di verifica una tabella e' passata da zero a cinque righe senza che fossimo noi a scriverla.
  Chi rifara' le prove non trovera' gli stessi conteggi.
- **I percorsi di scrittura.** Inserimento, modifica ed eliminazione provati sul database di
  test e poi annullati. Verificata anche la traduzione dei codici di errore: chiave duplicata
  (2627) e violazione di chiave esterna (547) arrivano alla UI come messaggi localizzati.

**Verificato (9 settembre 2026), area produzione in scrittura.**

- **Il modulo lotti**, tutte le nove fasi di `piano-modifica-lotto.md`: blocco, modifica di
  testata e billette, rettifiche, salvataggio in transazione, diagnostica, creazione,
  eliminazione e chiusura forzata. Le scritture provate su `MES40_RDP_TEST` dentro una
  transazione poi annullata; il servizio di diagnostica interrogato davvero.
- **480 test** in `tests/MesDataManager.Tests` (erano 190). Fra i nuovi, quelli che verificano
  la mappatura **come la vede SQL Server** senza connettersi: il tipo delle colonne di
  diagnostica e lo schema `Press` delle tabelle di lotto — la classe di errore piu' costosa di
  questo progetto e' proprio quella che SQLite non riproduce.
- **Le misure che hanno cambiato il progetto** sono in `piano-modifica-lotto.md`, sezione 5:
  `usp_Batch_Elab` a ~35 secondi per lotto, la dimensione reale di una risposta di diagnostica,
  la distribuzione degli stati d'uso delle matrici.

**Non verificato.**

- **L'interfaccia della modifica del lotto in un browser.** La prova di accensione serve le
  pagine e la scheda si disegna, ma esercita solo il primo rendering: modifica in linea delle
  billette, comandi della barra e salvataggio passano da un circuito interattivo che nessun test
  attraversa (voce B2 delle decisioni aperte, invariata). Da provare a mano prima dell'esercizio.

- **Il percorso interattivo dell'autenticazione.** L'identita' letta da
  `AuthenticationStateProvider` e' verificata nel rendering lato server; il comportamento a
  circuito stabilito va confermato da browser, con i ruoli veri del tenant.
- **La concorrenza.** Vedi il registro delle decisioni aperte, voce A4: vince l'ultimo che
  salva, e non c'e' modo di accorgersene.
- **Il comportamento su volumi di produzione.** Le anagrafiche sono piccole (la piu' popolata
  del database di test ha 249 righe) e la griglia le carica tutte; le tabelle di produzione a
  cui fanno riferimento no — `Press.BatchDowntime` ha 4,59 milioni di righe. Riguarda i moduli
  testata/righe, non questo.
