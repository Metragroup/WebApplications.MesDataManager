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

**Il rischio.** `DieCorrectionIssue`, `Module`, `OvenRecipe`, `EmailRecipient` sono
verosimilmente referenziate dai dati di produzione. Un `DELETE` su una voce usata viene
respinto dal vincolo di chiave esterna — e il servizio lo traduce in un messaggio comprensibile
— ma solo **se il vincolo esiste a database**. Dove non c'e', l'eliminazione riesce e lascia
riferimenti orfani nello storico.

**Da verificare.** Quali di quelle quattro tabelle hanno vincoli di chiave esterna in ingresso.
E' una query su `sys.foreign_keys`, va fatta sul database reale.

**Opzioni.** Se i vincoli mancano: disattivazione logica invece di eliminazione dove la tabella
ha `IsActive` (`EmailRecipient` lo ha), o aggiunta dei vincoli mancanti (dipende da A3).

**Se non si decide.** Un'eliminazione sbagliata puo' rendere illeggibile un pezzo di storico di
produzione, ed e' il tipo di danno che si scopre mesi dopo.

---

### A6 — Versioni dei pacchetti

**Contesto.** `Directory.Packages.props` contiene versioni di partenza non verificate,
soprattutto per MudBlazor e Microsoft.Identity.Web.

**Azione.** Aggiornare da NuGet e fissare le versioni. Con la gestione centralizzata sono tutte
in un file.

**Se non si decide.** Ripristino con versioni vecchie o mancanti, ed eventuali errori di API
attribuiti al codice invece che alla versione.

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

### B2 — Test automatici

**Contesto.** Non esiste un progetto di test.

**Dove renderebbero piu' servizio.** Non sulla UI generica, ma sulle regole del servizio: la
regola su `IsActive_Master`, la matrice di `IsWritable` per le tre policy, la conversione dei
tipi in `FieldValueConverter`, la traduzione degli errori SQL. Sono tutte testabili senza
database, con un contesto in memoria o con doppi.

**Perche' prima dei moduli testata/righe.** Lì la logica di dominio e' vera e i test diventano
necessari, non opzionali. Conviene che l'infrastruttura di test esista gia'.

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

*(Nessuna ancora. Quando una voce sopra si chiude, va spostata qui con la decisione e la data.)*
