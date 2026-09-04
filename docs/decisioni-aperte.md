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

---

### C2 — Ospitalita' e credenziali di accesso al database — *chiusa il 3 settembre 2026*

**Com'era.** In sviluppo la stringa di connessione sta negli user secrets. Per l'esercizio si
ipotizzava l'autenticazione gestita (managed identity), ma dipendeva da dove sarebbe stata
ospitata l'applicazione, che non era deciso.

**Decisione.** Applicazione IIS di nome `MesDataManager` sotto il sito esistente di
`itbsintra01.metra.local`, quindi in `https://itbsintra01.metra.local/MesDataManager`.
L'application pool gira con un account di servizio di dominio e accede a SQL Server con
`Trusted_Connection`: la managed identity non esiste fuori da Azure, ma il risultato pratico e'
lo stesso — nessuna password nei file di configurazione e niente da ruotare.

Ne consegue che `appsettings.Production.json` non contiene segreti e sta sotto controllo di
versione: e' anche l'unico modo perche' sopravviva ai deploy, dato che la pubblicazione
sovrascrive i file sul server.

**Conseguenze sul codice.** Il percorso virtuale non era gestito: sei punti scrivevano indirizzi
a partire dalla radice del server. Corretti, e verificati in locale con un path base
temporaneo. Aggiunta la persistenza delle chiavi di Data Protection, che sotto IIS con le
impostazioni predefinite sarebbero effimere. Il dettaglio, con la procedura, e'
in `pubblicazione.md`.

**Cosa resta.** Un secondo server (o due istanze) richiederebbe chiavi protette da certificato
al posto di DPAPI e affinita' di sessione sul bilanciatore. Non serve oggi.

---

### A2 — Chi ha quale ruolo Entra ID — *chiusa il 3 settembre 2026*

**Com'era.** Tre ruoli dai nomi gerarchici — `Archive.Reader`, `Archive.Editor`,
`Archive.Administrator` — con `Editor` che inseriva e modificava e `Administrator` che in piu'
eliminava. Nel vecchio progetto l'`AuthService` restituiva sempre tutti i permessi, quindi non
c'era un precedente da rispettare.

Il difetto di quell'impianto era il nome: `Archive.` davanti a tutto, come se l'applicazione
fosse solo le anagrafiche. I moduli testata/righe della produzione arriveranno, e con
`Archive.Administrator` come ruolo del controllo completo non ci sarebbe stato posto per loro.

**Decisione.** Un ruolo globale, uno di sola consultazione, uno per ambito di scrittura:

| Ruolo | Legge | Scrive le anagrafiche | Scrive la produzione |
|---|---|---|---|
| `Administrator` | tutto | si' | si', quando esistera' |
| `Reader` | tutto | no | no |
| `Archive.Editor` | tutto | si' | no |
| `Production.Editor` | tutto | no | si', quando esistera' |

**La lettura non si divide per ambito, la scrittura si'.** Un `Reader` vede anagrafiche e
produzione; un editor scrive il suo ambito e legge il resto. Dividere anche la lettura avrebbe
significato quattro ruoli in piu' per proteggere dati che in stabilimento si guardano a vicenda.

**Solo chi ha un ruolo entra.** *Assignment required = Yes* sull'enterprise application: un
account del tenant non assegnato viene fermato da Entra ID (`AADSTS50105`) e non raggiunge
l'applicazione, nemmeno con l'indirizzo diretto. Ne consegue che **assegnare l'applicazione a
qualcuno significa sempre scegliergli un ruolo**: non esiste l'assegnazione di sola apertura, e
`Reader` e' il ruolo che serve a quello.

**Cosa cambia nel codice.** `UserPermissions.From` deriva i permessi di scrittura da
`Archive.Editor` oppure `Administrator`, e la **lettura da un ruolo qualsiasi fra i quattro**:
chi e' autenticato senza ruoli noti riceve `ArchiveException.Forbidden`, cioe' il messaggio di
accesso negato. Il controllo duplica cio' che Entra ID fa gia', e va tenuto: se un domani quella
impostazione venisse spostata per un altro motivo, la lettura non deve aprirsi a tutto il tenant
in silenzio. `Production.Editor` per ora concede solo la lettura; la scrittura nascera' con le
pagine di produzione.

**Cosa si perde, e va saputo.** La separazione fra "chi modifica" e "chi elimina" non esiste:
chi corregge una causale puo' anche cancellarla. Su queste tabelle l'eliminazione e' il gesto
rischioso (voce A5), quindi la rete rimasta e' quella dello schema e non quella dei permessi —
`PressFailureType` non e' eliminabile da nessuno, `Administrator` compreso. Se servisse
distinguere, la strada e' un ruolo in piu', non un ritocco alla mappatura.

**L'errore di assegnazione da aspettarsi.** `Production.Editor` a chi deve correggere le
anagrafiche: non produce un errore, produce una griglia in sola lettura. E' il prezzo di avere
l'ambito nel nome del ruolo, e vale la pena averlo pagato — l'alternativa era un ruolo di nome
`Archive.` che governa la produzione.

---

### C4 — Maiuscole nel percorso virtuale e redirect URI di Entra ID — *chiusa il 4 settembre 2026*

**Com'era.** Scoperto in esercizio: `https://itbsintra01.metra.local/MesDataManager` e
`https://itbsintra01.metra.local/mesdatamanager` sono entrambi indirizzi validi per IIS, che
instrada per nome senza distinguere le maiuscole. Il modulo ASP.NET Core pero' passa
all'applicazione il `PathBase` cosi' come e' stato digitato, non normalizzato secondo l'alias
configurato: l'applicazione generava quindi un `redirect_uri` diverso a seconda di come si era
raggiunta, e Entra ID confronta quella stringa lettera per lettera (`AADSTS50011`,
*"did not match because of case sensitivity"*). Chi aveva gia' una sessione valida non se ne
accorgeva — il cookie di autenticazione bastava, senza rifare il giro verso Entra ID — il che ha
reso il sintomo intermittente e legato in apparenza al browser, non alla configurazione.

**Decisione.** Non registrare piu' varianti in Entra ID (soluzione che si romperebbe alla prima
maiuscola non prevista) ne' vincolare la questione a un solo alias IIS: normalizzare il
`PathBase` a tutto minuscolo dentro l'applicazione, prima di qualunque altra pipeline. Un
accesso con maiuscole diventa un rinvio permanente (301) alla stessa pagina in minuscolo, e da
li' in poi l'applicazione vede sempre lo stesso `PathBase` indipendentemente da come e' stata
raggiunta. Il redirect URI e il front-channel logout URL in Entra ID restano quindi in
minuscolo, un'unica volta.

**Cosa cambia nel codice.** Un middleware in cima a `Program.cs`, prima dell'autenticazione:
confronta `PathBase` con la sua forma minuscola e rinvia se diverso. Verificato riproducendo il
comportamento del modulo ASP.NET Core con un middleware di prova (il `PathBase` reale segue la
capitalizzazione della richiesta, non quella dell'alias) — richiesta con maiuscole rinviata in
minuscolo con percorso e query stringa intatti, richiesta gia' in minuscolo servita senza
rinvii.

**Cosa resta.** Chi ha gia' un cookie di autenticazione scritto con un `PathBase` in
maiuscolo (da prima di questa correzione) lo perde alla prima richiesta successiva, perche' il
percorso del cookie non combacia piu': si ripresenta il login, una volta sola.
