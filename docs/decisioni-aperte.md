# Decisioni aperte

Registro delle cose non ancora decise, con il contesto necessario per deciderle. Ogni voce dice
cosa e' in gioco, quali sono le opzioni e cosa succede se non si decide.

Convenzione: quando una voce si chiude, **non si cancella** — si sposta in coda con la decisione
presa e la data. Serve a non ridiscutere due volte le stesse cose.

Stato: prima stesura, settembre 2026.

---

## Da decidere prima di andare in esercizio

### A1 — Sorte di `Press`, `Oven` e `HeatThreatment`

**Contesto.** Nel vecchio progetto avevano codice di lettura, un caso nello `switch` del
presenter e le etichette tradotte in tre lingue, ma erano state rimosse dagli elenchi che
popolavano il `TreeView`: erano irraggiungibili. Non si sa se per una scelta o per una
regressione mai notata. Le anagrafiche effettivamente usabili erano tredici.

**Stato attuale.** Esposte con `ArchiveEditPolicy.ReadOnly`: si vedono, non si modificano.

**Opzioni.**

1. Aprirle alla modifica (`EditPolicy = Full`). `Press` ha 23 colonne, di cui una decina di
   parametri del monitor pressa e della taglierina: vale la pena chiedersi chi le manutiene
   oggi, e se lo fa da SQL Server Management Studio.
2. Lasciarle in sola lettura, come consultazione.
3. Togliere i descrittori. Le entita' restano perche' servono ai lookup (`Oven` per
   `OvenRecipe`, e `Company` per `Worker` e `Press`).

**Chi decide.** Serve chiedere a chi usa l'applicazione se quelle tabelle le modifica qualcuno,
e con quale strumento.

**Se non si decide.** Restano in sola lettura: nessun danno, ma tre voci nel menu che forse non
servono a nessuno.

---

### A2 — Chi ha quale ruolo Entra ID

**Contesto.** I tre ruoli sono `Archive.Reader`, `Archive.Editor`, `Archive.Administrator`,
gerarchici. Nel vecchio progetto l'`AuthService` restituiva sempre tutti i permessi (con un
`TODO` accanto), quindi **non esiste un precedente**: chiunque aprisse l'applicazione poteva
fare tutto.

**Il punto delicato.** Introdurre permessi dove prima non c'erano e' un cambiamento di
comportamento visibile. Se il capoturno che ha sempre corretto le posizioni delle causali si
trova la griglia in sola lettura, la segnalazione arriva il primo giorno.

**Da chiarire.**

- Chi deve poter **eliminare** (oggi solo `Administrator`)? L'eliminazione su queste tabelle e'
  rischiosa: vedi A5.
- Il ruolo `Reader` serve davvero? Attualmente `CanRead` coincide con l'essere autenticati,
  quindi il ruolo non e' usato. Va deciso se la sola autenticazione basta a leggere le
  anagrafiche o se serve un ruolo esplicito.

**Se non si decide.** Nessuno ha ruoli, quindi tutti vedono le anagrafiche in sola lettura e
nessuno puo' modificare: l'applicazione risulta rotta.

---

### A3 — Dove finisce il versionamento dello schema

**Contesto.** Il vecchio `VersionHelper` applicava script SQL incrementali all'avvio
dell'applicazione. Non e' stato riportato, perche' con piu' istanze web due avvii simultanei
eseguirebbero lo stesso DDL in parallelo.

**Da decidere.** Chi applica gli script d'ora in avanti. Opzioni tipiche: un progetto di
database (SSDT / DACPAC) nel deploy, uno strumento di migration dedicato eseguito come step di
pipeline, o una procedura manuale documentata.

**Nota.** Questa decisione riguarda il MES nel suo complesso, non solo questo modulo: se altri
componenti dipendono ancora dal vecchio meccanismo, va coordinata.

**Se non si decide.** Gli script non vengono applicati da nessuno e il disallineamento emerge in
produzione.

---

### A4 — Modifiche concorrenti

**Contesto.** Oggi non c'e' controllo di concorrenza. Se due persone aprono la stessa causale e
salvano, **vince l'ultimo che salva** e la prima modifica si perde in silenzio.

**Quanto e' grave.** Su anagrafiche modificate raramente, poco. Ma su `Position` e' plausibile:
riordinare le causali e' un'operazione che due persone possono fare nello stesso momento senza
saperlo.

**Opzioni.**

1. Lasciare cosi', accettando l'ultimo-vince. Documentarlo e basta.
2. Aggiungere una colonna `rowversion` alle tabelle e usare `IsRowVersion()` in EF Core.
   Richiede modifica dello schema, quindi dipende da A3.
3. Concorrenza ottimistica sui soli campi modificati, senza cambiare lo schema: si confronta il
   valore letto con quello attuale prima di scrivere. Piu' lavoro nel servizio, nessuna
   modifica al database.

**Se non si decide.** Opzione 1 per omissione, senza che sia documentata.

---

### A5 — Eliminazione: fisica o logica

**Contesto.** Sulle anagrafiche con `EditPolicy.Full` l'eliminazione e' fisica (`DELETE`). Le
tabelle allineate dall'ERP non ammettono eliminazione, quindi il problema non le riguarda.

**Rilevato su `MES40_RDP_TEST` il 2 settembre 2026** (query su `sys.foreign_keys` e
`sys.columns`). I vincoli di chiave esterna in ingresso allo schema `MasterData` sono **tre in
tutto**:

| Anagrafica | Protetta da |
|---|---|
| `Oven` | `MasterData.OvenRecipe` |
| `Press` | `Press.Batch`, `Press.BatchBillet` |
| `Worker` | `Press.BatchWorker` |

Le sei anagrafiche su cui l'eliminazione e' abilitata sono `DieCorrectionIssue`, `Module`,
`OvenRecipe`, `Worker`, `EmailRecipient`, `PressFailureType`: di queste **solo `Worker` ha una
rete**, e parziale.

**Il caso che pesa: `PressFailureType`.** Non ha vincoli in ingresso, ma e' referenziata di
fatto da `Press.BatchDowntime.FailureType` e `History._BatchDowntime.FailureType` — 4,59 milioni
di righe di fermi macchina. Il riferimento non si chiama come la chiave (`FailureType` contro
`PressFailureTypeID`) e **i tipi non coincidono**: `tinyint` da un lato, `smallint` dall'altro.
Un vincolo non e' quindi aggiungibile senza prima riconciliare i tipi. Nel frattempo un
`DELETE` da questa applicazione riesce e lascia orfano lo storico dei fermi, senza che nessuno
se ne accorga.

**Quanto pesa, in numeri.** L'anagrafica censisce cinque tipi (`Produzione`, `Elettrico`,
`Meccanico`, `Elettrico / Meccanico`, `Microfermo`). I fermi li usano tutti, in modo molto
sbilanciato:

| Tipo | Fermi che lo usano |
|---|---|
| 4 — Microfermo | 4.498.500 |
| 0 — Produzione | 84.686 |
| 2 — Meccanico | 4.228 |
| 1 — Elettrico | 3.914 |
| 3 — Elettrico / Meccanico | 756 |
| 99 — *non esiste in anagrafica* | 25 |

Eliminare `Microfermo` renderebbe illeggibili 4,5 milioni di righe di storico. E il valore 99,
che nell'anagrafica non c'e', dimostra che il riferimento non e' mai stato garantito da
nessuno: 25 fermi sono **gia'** orfani.

**Nota per chi rifara' queste query.** `MES40_RDP_TEST` e' un ambiente vivo: durante la
sessione di verifica `PressFailureType` e' passata da zero a cinque righe. I conteggi qui
sopra sono una fotografia del 2 settembre 2026, non una costante.

**Anche `Worker` e' meno protetta di quanto sembri.** Oltre a `Press.BatchWorker` (con vincolo)
e' referenziata da `MobileDevice.Worker` e `MobileDevice.WorkerMenu`, **senza vincolo**. Il
vincolo esistente respinge la cancellazione di un operatore con storico di lotto, non di uno
presente solo sui dispositivi mobili.

**Non referenziate da nessuna colonna omonima:** `DieCorrectionIssue`, `Module`, `OvenRecipe`,
`EmailRecipient`. La ricerca e' per nome di colonna, quindi non e' una prova: un riferimento
chiamato diversamente sfuggirebbe, come e' successo per `FailureType`.

**Deciso il 2 settembre 2026, per `PressFailureType`.** Eliminazione disabilitata
(`ArchiveDescriptor.PreventDelete`). Inserimento e modifica restano.

Il divieto e' un flag a se' e non un quarto valore di `ArchiveEditPolicy`: la policy descrive
come l'anagrafica e' governata, questo descrive un limite dello schema. Quando il vincolo di
chiave esterna esistera' si toglie una riga dal catalogo, senza toccare il modello.

E' l'unico punto in cui la nuova applicazione fa **meno** della precedente, dove
l'eliminazione era possibile dal menu contestuale. Se qualcuno la usava, la segnalazione
arrivera': la risposta e' che il vecchio comportamento poteva rendere illeggibile un pezzo di
storico dei fermi, non che la funzione e' stata dimenticata.

**Cosa resta da decidere.**

1. Dove serve una disattivazione logica al posto della cancellazione: `EmailRecipient` e
   `Worker` hanno `IsActive` e possono farla subito; `PressFailureType`, `Module`,
   `DieCorrectionIssue` e `OvenRecipe` no, quindi dipendono da A3.
2. Se aggiungere i vincoli mancanti a database, sapendo che per `PressFailureType` implica
   anche un cambio di tipo di colonna (`tinyint` verso `smallint`).
3. Se lo stesso trattamento serva a `Module`, `DieCorrectionIssue` e `OvenRecipe`: nessuna
   colonna omonima le referenzia, ma la ricerca era per nome — e proprio su
   `PressFailureType` quel metodo aveva mancato il riferimento.

**Verificato per contro.** La traduzione dei codici di errore funziona: un inserimento a chiave
duplicata restituisce `DuplicateKey` e uno che viola un vincolo di chiave esterna restituisce
`ForeignKeyViolation`, entrambi come messaggio localizzato e non come errore del provider.

**Se non si decide.** Un'eliminazione sbagliata puo' rendere illeggibile un pezzo di storico di
produzione, ed e' il tipo di danno che si scopre mesi dopo.

---

---

## Da decidere prima dei moduli testata/righe

### B1 — Tracciabilita' delle modifiche

**Contesto.** Oggi le operazioni finiscono nei log Serilog (chi, quale anagrafica, quale
operazione), su file con rotazione giornaliera e conservazione a 14 giorni. Non c'e' traccia a
database, e non si registra **quale valore** e' cambiato.

**Da decidere.** Se serve una tracciabilita' consultabile — chi ha cambiato quella posizione, e
quando — e a quale profondita'. Su un MES la domanda tende a presentarsi il giorno di una
contestazione, non prima.

**Opzioni.** Colonne di audit sulle tabelle (dipende da A3), tabella di audit separata,
temporal table di SQL Server, o niente oltre i log.

---

### B2 — Estensione dei test automatici

**Contesto.** `tests/MesDataManager.Tests` esiste (190 test su SQLite in memoria) e copre le
regole del servizio: matrice di `IsWritable` per le tre policy, regola su `IsActive_Master`,
permessi, validazione, conversione dei tipi, ordinamento e ricerca, coerenza del catalogo.

**Cosa resta scoperto, e perche'.**

- **La traduzione dei codici di errore di SQL Server** (2601/2627 chiave duplicata, 547 vincolo
  di chiave esterna): SQLite non produce quei codici. Serve un database di prova vero, oppure
  un doppio che simuli la `SqlException` — che verificherebbe la sola tabella di traduzione.
- **La mappatura delle colonne.** SQLite accetta tipi che SQL Server rifiuterebbe: i test non
  sostituiscono la verifica tabella per tabella sul database reale.
- **La UI.** Nessun test di componente. Su una pagina generata dai metadati il rapporto fra
  costo e beneficio e' discutibile; se si volesse, `bunit` e' la strada.

**Perche' contava averli prima dei moduli testata/righe.** Lì la logica di dominio e' vera e i
test diventano necessari, non opzionali. L'infrastruttura ora esiste.

---

### B3 — Come modellare testata/righe

**Contesto.** Il catalogo di metadati risolve il CRUD ripetitivo. Non risolve "cosa significa
chiudere un lotto": validazioni che coinvolgono piu' entita', transizioni di stato, calcoli.

**Da decidere.** L'approccio: servizi applicativi scritti a mano con pagine dedicate (la strada
naturale), e come strutturare la transazione fra testata e righe.

**Nota di metodo.** Vale la pena rifare per quei moduli lo stesso lavoro fatto qui: leggere il
vecchio codice per estrarne le regole implicite, prima di scrivere il nuovo. Nel modulo
anagrafiche e' stata la parte piu' utile dell'analisi, e quella che il codice non raccontava.

---

## Rinviabili

### C1 — Autoconservazione dei font

`wwwroot/css/app.css` importa IBM Plex da Google Fonts. Su rete di stabilimento senza uscita
verso Internet i font non arrivano e si ricade sullo stack di sistema (Segoe UI). Funziona, ma
l'aspetto cambia. Se la resa deve essere identica su tutte le postazioni, i font vanno scaricati
in `wwwroot/fonts` e serviti da lì.

### C2 — Credenziali di accesso al database in esercizio

In sviluppo la stringa di connessione sta negli user secrets. In esercizio conviene
l'autenticazione gestita (managed identity) invece di utente e password: niente da ruotare e
nessun segreto nei file di configurazione. Dipende da dove verra' ospitata l'applicazione, che
non e' ancora deciso.

### C3 — Quando rivedere la scelta del render mode

Blazor Server e' la scelta giusta per lo scenario attuale (vedi `architettura.md`, sezione 2).
Le condizioni che giustificherebbero riaprire la questione:

- serve un client esterno (mobile, integrazione di terzi, secondo front-end);
- serve funzionamento offline su postazioni di reparto;
- il numero di sessioni simultanee rende oneroso lo stato dei circuiti sul server.

Al verificarsi di una di queste, il passo non e' "cambiare render mode" ma **prima** aggiungere
il progetto API che richiama i servizi applicativi esistenti.

---

## Decisioni chiuse

### A6 — Versioni dei pacchetti — *chiusa il 2 settembre 2026*

**Com'era.** `Directory.Packages.props` conteneva versioni di partenza non verificate.

**Decisione.** Versioni verificate e fissate. Alla data non risultavano aggiornamenti
disponibili per i pacchetti effettivamente referenziati: MudBlazor 9.9.0, Microsoft.Identity.Web
4.14.2, EF Core 10.0.11. Rimosse due voci mai referenziate da alcun progetto
(`Microsoft.EntityFrameworkCore.Design`, incoerente con la scelta di non generare migration, e
`Microsoft.Extensions.Localization.Abstractions`, fornita dal framework condiviso).

**Nota.** Il controllo va rifatto periodicamente, ma non e' piu' una decisione: e' manutenzione.
