# Pubblicazione

Procedura della prima pubblicazione su IIS, con il contesto per rifarla e per capire cosa e'
andato storto quando qualcosa non funziona.

| | |
|---|---|
| Server | `itbsintra01.metra.local`, IIS |
| Forma | **applicazione** IIS di nome `MesDataManager`, sotto il sito esistente |
| Indirizzo | `https://itbsintra01.metra.local/MesDataManager` |
| Percorso virtuale | `/MesDataManager` — non e' un dettaglio, vedi sezione 1 |
| Identita' | account di servizio di dominio, es. `METRA\webapps` |
| Autenticazione | Microsoft Entra ID, permessi dai ruoli applicativi |
| Database | pre-esistente, questa applicazione **non** ne modifica lo schema |

Stato: prima stesura, settembre 2026. Le sezioni 0-5 sono state percorse una volta sola, quindi
vanno lette come istruzioni da verificare, non come procedura consolidata.

---

## 1. Perche' il percorso virtuale conta

Un'applicazione sotto `/MesDataManager` non e' un'applicazione alla radice con un prefisso
davanti: ogni indirizzo che il codice scrive con uno `/` iniziale finisce nella radice del
**server**, dove non c'e' niente. In una pagina Blazor l'effetto tipico e' una pagina bianca,
perche' il browser cerca `blazor.web.js` nel posto sbagliato e nessun gestore di eventi parte.

Il prefisso lo mette l'ASP.NET Core Module in `HttpContext.Request.PathBase`: l'applicazione non
deve chiamare `UsePathBase`, lo riceve gia' fatto. Quello che deve fare e' non ignorarlo. I punti
sistemati per questa pubblicazione:

| Dove | Cosa | Perche' |
|---|---|---|
| `Components/App.razor` | `<base href>` ricavato da `PathBase` | e' la base su cui il browser risolve script, fogli di stile e rotte |
| `Components/Layout/MainLayout.razor` | uscita e cambio lingua senza `/` iniziale | si risolvono sulla base dell'applicazione |
| `Components/Layout/MainLayout.razor` | marchio verso `Navigation.BaseUri` | il ritorno all'apertura resta dentro l'applicazione |
| `Program.cs` | endpoint `culture/set`: `PathBase` riaggiunto al rinvio | `LocalRedirect` scrive l'indirizzo cosi' com'e' |
| `Program.cs` | cookie di cultura con `Path` = percorso dell'applicazione | il nome del cookie e' quello predefinito, uguale a quello delle altre applicazioni dell'host |
| `Security/AuthenticationSetup.cs` | cookie di autenticazione rinominato | idem: `.AspNetCore.Cookies` e' il nome predefinito di **ogni** applicazione ASP.NET Core |

Nel documento HTML servito da un percorso virtuale la base deve risultare
`<base href="/MesDataManager/">`, con la barra finale: senza, il browser scarta l'ultimo segmento.
E' la prima cosa da guardare (`Ctrl+U` sulla pagina) quando l'applicazione appare bianca.

**Verificato in locale** aggiungendo temporaneamente un path base: base corretta, risorse servite
sotto il percorso virtuale, cambio lingua che rinvia a `/MesDataManager/archive/Reason` e cookie
di cultura con `path=/MesDataManager`. Resta da verificare sul server il giro completo verso
Entra ID, che in locale non e' riproducibile.

---

## 2. Prerequisiti sul server, una volta sola

1. **.NET 10 Hosting Bundle.** Installa runtime e ASP.NET Core Module V2. Dopo l'installazione
   serve `iisreset`, altrimenti IIS non vede il modulo. Verifica: `dotnet --list-runtimes` deve
   elencare `Microsoft.AspNetCore.App 10.*`.

2. **Feature WebSocket Protocol di IIS** (*Server Manager -> Add Roles and Features -> Web Server
   -> Application Development -> WebSocket Protocol*). Blazor Server tiene una connessione
   SignalR aperta per ogni sessione: senza WebSocket ricade sul long polling, che funziona ma
   trasforma ogni clic in una coppia di richieste HTTP.

3. **Account di servizio di dominio**: `METRA\webapps`, l'account con cui girano le
   applicazioni web di stabilimento. E' l'identita' con cui questa accede al database, e la
   scelta chiude la voce C2 del registro delle decisioni.

   **Conseguenza da tenere presente.** L'account e' condiviso con le altre applicazioni web:
   il permesso concesso qui sullo schema `MasterData` (sezione 4) vale per **tutto** quello che
   gira con quell'identita', non solo per questo modulo. Se in futuro serve isolare i permessi,
   il passo e' un account dedicato, non un permesso piu' fine.

4. **Due cartelle fuori dalla cartella di pubblicazione**, perche' un deploy non le cancelli:

   | Cartella | Contenuto | Permessi per l'account di servizio |
   |---|---|---|
   | `C:\ProgramData\MesDataManager\keys` | chiavi che cifrano il cookie di autenticazione | Modifica |
   | `C:\ProgramData\MesDataManager\logs` | log applicativi, rotazione giornaliera, 14 giorni | Modifica |

5. **Uscita verso `login.microsoftonline.com:443` dal server**, non solo dalle postazioni. Il
   server scarica i metadati OpenID Connect e le chiavi di firma dei token: se la rete di
   stabilimento non lo consente, l'accesso fallisce con `IDX20803` e nessun messaggio piu'
   chiaro di quello. Se l'uscita passa da un proxy, va configurato per il processo.

---

## 3. Entra ID

`appsettings.json` contiene gia' `TenantId` e `ClientId` di una registrazione applicativa: il
primo passo e' verificare che sia quella giusta e completarla. Su quella registrazione:

**Authentication.**

- Redirect URI, tipo *Web*: `https://itbsintra01.metra.local/MesDataManager/signin-oidc`
- Front-channel logout URL: `https://itbsintra01.metra.local/MesDataManager/signout-callback-oidc`
- *Implicit grant and hybrid flows*: spuntare **ID tokens**. Serve perche' il flusso e' di sola
  autenticazione, senza client secret. Se manca, il primo accesso risponde `AADSTS700054`.

I due percorsi restano `/signin-oidc` e `/signout-callback-oidc` in configurazione: il prefisso
`/MesDataManager` lo aggiunge ASP.NET Core da solo. In Entra ID va invece registrato l'indirizzo
completo, prefisso compreso.

**App roles** (*App registration -> App roles*), con `Value` identico a queste stringhe, che il
codice confronta letteralmente:

| Value | Allowed member types | Cosa concede |
|---|---|---|
| `Administrator` | Users/Groups | tutto, in ogni ambito |
| `Reader` | Users/Groups | consultazione di tutti i dati, anagrafiche e produzione |
| `Archive.Editor` | Users/Groups | anagrafiche: inserimento, modifica ed eliminazione |
| `Production.Editor` | Users/Groups | dati di produzione: consultazione, e la scrittura quando le pagine esisteranno |

La lettura non si divide per ambito, la scrittura si': un `Reader` vede tutto, un editor scrive
il suo ambito. **Serve un ruolo anche solo per vedere i dati** — chi e' autenticato ma senza
ruoli riceve il messaggio di accesso negato.

Tre conseguenze da conoscere, tutte volute (voce A2 del registro, chiusa il 3 settembre 2026):

- **Chi modifica puo' anche eliminare.** Non c'e' un ruolo intermedio. Il freno
  sull'eliminazione e' per anagrafica e non per persona: `PressFailureType` non e' eliminabile
  da nessuno, `Administrator` compreso (voce A5).
- **`Production.Editor` sulle anagrafiche vale come `Reader`.** Assegnarlo a chi deve correggere
  una causale e' l'errore piu' probabile di questa configurazione, e non produce un messaggio:
  produce una griglia in sola lettura. Il ruolo giusto e' `Archive.Editor`.
- **Assegnare l'applicazione a qualcuno significa sempre scegliergli un ruolo.** Non esiste
  l'assegnazione "sola apertura": chi va aggiunto per consultare va aggiunto come `Reader`.

**Chi accede: solo chi ha un ruolo.** *Properties -> Assignment required = **Yes***. Un account
del tenant non assegnato viene fermato da Entra ID (`AADSTS50105`) e non raggiunge
l'applicazione, nemmeno con l'indirizzo diretto. Il requisito e' ripetuto nel codice — la
lettura pretende un ruolo noto — perche' la riservatezza dei dati non deve dipendere da un
interruttore nel portale, che un domani qualcuno potrebbe spostare per un altro motivo.

**Accesso e visibilita' sono due cose diverse**, e vanno regolate entrambe.

| | Chi la governa | Valore scelto |
|---|---|---|
| Chi puo' **entrare** | *Assignment required?* | `Yes`: solo gli utenti e i gruppi assegnati |
| Chi la **vede** in My Apps | *Visible to users?* | `No`: nessuno, nemmeno gli assegnati — l'applicazione si raggiunge per indirizzo |

My Apps elenca le applicazioni a cui si e' assegnati: portando *Visible to users?* a `Yes`
comparirebbe agli assegnati, e solo a loro. Non allarga l'accesso di nessuno, quindi e' una
comodita' da valutare a parte — un collegamento in piu' per chi lavora sull'applicazione.

**Assegnazione** (*Enterprise applications -> MesDataManager -> Users and groups*). Assegnare a
**gruppi** e non a persone: l'ingresso di un nuovo capoturno diventa un'aggiunta al gruppo, senza
toccare l'applicazione ne' il tenant. Vale soprattutto per `Reader`, che e' il ruolo destinato a
crescere di piu'.

---

## 4. Database

L'applicazione **non crea e non aggiorna lo schema**: si aspetta le tabelle dello schema
`MasterData` gia' allineate. Chi applica gli script di versione e' la voce A3 del registro delle
decisioni, ancora aperta: prima di pubblicare va verificato che il database di esercizio sia
allineato, altrimenti il disallineamento si scopre al primo salvataggio.

Sul database di esercizio:

```sql
CREATE LOGIN [METRA\webapps] FROM WINDOWS;
GO
USE [COMPILARE-database];
CREATE USER [METRA\webapps] FOR LOGIN [METRA\webapps];
GO
-- Tutte le tabelle usate stanno in questo schema, lookup compresi: il permesso si concede
-- una volta sullo schema invece che tabella per tabella.
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::MasterData TO [METRA\webapps];
```

Niente `db_owner` e niente `db_datareader`/`db_datawriter`: il primo consentirebbe anche il DDL,
i secondi darebbero accesso a tutto il database, storico dei fermi compreso — 4,5 milioni di
righe che questa applicazione non ha motivo di leggere.

---

## 5. Configurazione

`appsettings.Production.json` contiene le tre impostazioni di esercizio: stringa di connessione,
cartella delle chiavi e percorso dei log. Va compilato **in repository**, non a mano sul server:
il file fa parte della pubblicazione, quindi ogni deploy sovrascrive quello che c'e' sulla
macchina.

Non contiene segreti — la connessione usa l'identita' dell'application pool e Entra ID non
richiede client secret — quindi puo' stare sotto controllo di versione. E' il vantaggio pratico
dell'account di servizio: niente da custodire, niente da ruotare, niente user secrets in
esercizio.

`ASPNETCORE_ENVIRONMENT` non va impostata: assente vale `Production`, che e' quello che serve. La
modalita' di autenticazione di sviluppo, se arrivasse qui per un file dimenticato, **impedisce
l'avvio** invece di far partire l'applicazione senza controlli.

---

## 6. Pubblicazione e IIS

**Sulla macchina di sviluppo:**

```powershell
dotnet publish src\MesDataManager.Web -c Release -o C:\pubblicazioni\MesDataManager
```

**Application pool** (*IIS Manager -> Application Pools -> Add*): nome `MesDataManager`.

| Impostazione | Valore | Perche' |
|---|---|---|
| .NET CLR version | **No Managed Code** | il processo e' .NET 10 fuori da IIS, IIS fa solo da fronte |
| Managed pipeline mode | Integrated | |
| Identity | `METRA\webapps` | e' l'identita' che accede a SQL Server |
| Load User Profile | **True** | senza profilo caricato DPAPI non puo' cifrare le chiavi |
| Idle Time-out | 0 | l'arresto per inattivita' chiude le sessioni Blazor aperte |
| Regular Time Interval (recycling) | 0 | il riciclo periodico fa lo stesso, a orari imprevedibili |
| Start Mode | AlwaysRunning | la prima richiesta dopo un riavvio non aspetta l'avvio |

**Applicazione** (*Sites -> il sito -> Add Application*): alias `MesDataManager`, application pool
`MesDataManager`, percorso fisico `C:\inetpub\MesDataManager` — **fuori** da `wwwroot`, altrimenti
il sito padre servirebbe quei file anche come contenuto statico, `appsettings` compresi.

Sulla cartella dell'applicazione l'account di servizio ha bisogno di *Lettura ed esecuzione*; le
due cartelle in `ProgramData` sono quelle in cui serve *Modifica* (sezione 2).

**Copia dei file.** Con l'hosting in-process il processo tiene aperte le proprie DLL, quindi
sostituirle a caldo non funziona:

1. copiare un file vuoto `app_offline.htm` nella cartella dell'applicazione — IIS arresta il
   processo e serve quel file a chi si collega;
2. sostituire il contenuto della cartella con la nuova pubblicazione;
3. eliminare `app_offline.htm`.

E' anche la procedura di rientro: conservare la cartella precedente rende il ritorno alla
versione buona una copia di file, non una ricompilazione.

---

## 7. Prova di accensione

In ordine, perche' ogni passo dimostra una cosa diversa:

| Prova | Attesa | Cosa dimostra |
|---|---|---|
| Aprire `https://itbsintra01.metra.local/MesDataManager` | rinvio a Microsoft, ritorno all'applicazione | uscita di rete, redirect URI, certificato |
| La pagina si disegna e il menu risponde al clic | interfaccia viva | base dei percorsi e WebSocket |
| Aprire un'anagrafica | griglia popolata | connessione al database e permessi SQL |
| Cambiare lingua | resta sotto `/MesDataManager` | endpoint di cultura e cookie |
| Salvare una modifica | modifica scritta | permessi di scrittura, con l'utente in `Archive.Editor` |
| Provare con e senza `Archive.Editor` | senza: nessun pulsante di scrittura, sola consultazione; con: inserimento, modifica ed eliminazione, tranne su `PressFailureType` | i permessi, che in esercizio non erano mai stati provati |
| Aprire l'indirizzo con un account non assegnato | Entra ID rifiuta (`AADSTS50105`), l'applicazione non si apre | `Assignment required = Yes` e' effettivo: l'indirizzo diretto non basta |
| Aprire con un utente in `Reader` | consultazione, nessun pulsante di scrittura | i due livelli sono distinti davvero |
| **Riavviare l'application pool e ricaricare** | si resta collegati | le chiavi sono persistite: e' la prova che cerca il problema piu' insidioso |
| Guardare `C:\ProgramData\MesDataManager\logs` | file del giorno con le operazioni | permessi di scrittura e tracciabilita' |

L'ultima riga della penultima prova e' quella che conta: se le chiavi non fossero persistite,
tutto il resto funzionerebbe comunque, e il problema si manifesterebbe giorni dopo, a caso.

---

## 8. Se qualcosa non va

| Sintomo | Causa quasi certa |
|---|---|
| Pagina bianca, `404` su `blazor.web.js` | base dei percorsi: guardare `<base href>` nel sorgente della pagina |
| `AADSTS50011` (redirect URI mismatch) | il redirect URI in Entra ID non ha il prefisso `/MesDataManager`, o e' `http` |
| `AADSTS700054` | manca la spunta *ID tokens* nella registrazione |
| `AADSTS50105` | utente non assegnato all'applicazione: e' il comportamento voluto. Se riguarda qualcuno che deve entrare, va assegnato — come `Reader` se deve solo consultare |
| `IDX20803: Unable to obtain configuration` | il server non raggiunge `login.microsoftonline.com` |
| `Correlation failed` dopo un riciclo del pool | chiavi non persistite: `DataProtection:KeyPath`, permessi sulla cartella, *Load User Profile* |
| Si torna al login a ogni pagina | cookie di un'altra applicazione dell'host con lo stesso nome (per questo e' rinominato) |
| `HTTP 500.19` | hosting bundle assente o `web.config` non leggibile |
| `HTTP 500.30` | il processo non parte: abilitare `stdoutLogEnabled` in `web.config`, o guardare il registro eventi. Cause tipiche: stringa di connessione, cartelle non scrivibili |
| Tutti in sola lettura | assegnato `Reader` o `Production.Editor` al posto di `Archive.Editor`, o ruolo dato a un gruppo di cui l'utente non fa parte |
| Accesso negato a chi e' assegnato | il ruolo assegnato non e' uno dei quattro: il `Value` in Entra ID deve coincidere alla lettera |
| L'aspetto e' diverso da quello atteso | i font arrivano da Google Fonts e la rete non esce: voce C1 del registro |

---

## 9. Cose da sapere prima che qualcuno le chieda

**La revoca di un ruolo non e' immediata.** I ruoli stanno nel cookie di autenticazione: togliere
qualcuno dal gruppo `Archive.Editor` ha effetto al suo accesso successivo, non subito. Se
dovesse servire l'effetto immediato, la strada e' un `RevalidatingServerAuthenticationStateProvider`.

**HSTS riguarda tutto l'host.** `app.UseHsts()` e' attivo fuori dallo sviluppo e l'intestazione
vale per `itbsintra01.metra.local`, non per `/MesDataManager`: dopo la prima visita i browser
rifiutano `http` verso **qualunque** applicazione di quell'host. Se sul server convive
un'applicazione ancora in `http`, va tolto.

**Le chiavi sono legate a questa macchina e a questo account.** DPAPI cifra con l'account che
scrive: copiare la cartella su un secondo server non basterebbe, servirebbe passare a un
certificato condiviso. Vale anche per il caso di due istanze in bilanciamento, che oggi non c'e'.

**Una sola istanza.** Blazor Server tiene lo stato della sessione nel processo: con due istanze
servirebbe l'affinita' di sessione sul bilanciatore, oltre alle chiavi condivise.

**Modifiche concorrenti: vince l'ultimo che salva** (voce A4, aperta). Su `Position` e'
plausibile, e la perdita e' silenziosa.

**Lo schema non lo aggiorna nessuno automaticamente** (voce A3, aperta).

---

## Sequenza minima, per la seconda volta

1. `dotnet publish src\MesDataManager.Web -c Release -o C:\pubblicazioni\MesDataManager`
2. `app_offline.htm` nella cartella sul server
3. copia dei file, conservando la cartella precedente
4. rimozione di `app_offline.htm`
5. apertura dell'applicazione, un'anagrafica, un salvataggio
