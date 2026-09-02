# MES Data Manager — modulo Anagrafiche

Riscrittura del modulo anagrafiche di MES Data Manager: da WinForms su .NET Framework 4.7.2
a Blazor Server su .NET 10, con backend a layer e autenticazione Microsoft Entra ID.

## Struttura

```
MesDataManager.sln
├── Directory.Build.props          impostazioni comuni (net10.0, nullable, analyzer)
├── Directory.Packages.props       versioni dei pacchetti, centralizzate
└── src/
    ├── MesDataManager.Domain          entità e regole intrinseche, nessuna dipendenza
    ├── MesDataManager.Application     contratti, descrittori, permessi
    ├── MesDataManager.Infrastructure  EF Core, catalogo anagrafiche, lookup
    └── MesDataManager.Web             Blazor Server + MudBlazor, Entra ID
```

Le dipendenze puntano verso l'interno: `Web → Infrastructure → Application → Domain`.
`Application` non conosce EF Core e `Domain` non conosce nulla. Il giorno in cui servirà
un'API HTTP (client mobile, integrazioni, passaggio a WebAssembly) basta aggiungere un
progetto che richiami gli stessi servizi.

## Come funziona il CRUD generico

Le sedici anagrafiche condividono **una sola pagina** (`Components/Pages/Archive.razor`) e
un solo servizio. Ciò che cambia da una tabella all'altra sta tutto in
`Infrastructure/Archives/ArchiveCatalog.cs`, che dichiara per ciascuna:

- entità e nome della tabella;
- nodo del menu in cui compare;
- cosa si può fare (`ArchiveEditPolicy`);
- l'elenco dei campi con tipo, obbligatorietà, lunghezza massima, editabilità.

Da quei metadati la UI genera colonne, form, validazione e voci di menu. **Aggiungere una
colonna o una tabella significa modificare solo il catalogo**, non scrivere una pagina.

### Regole di business recuperate dall'applicazione WinForms

Il codice è nuovo, ma queste regole erano nell'app precedente e sono state mantenute:

| Regola | Dov'era | Dov'è ora |
|---|---|---|
| Sulle tabelle allineate dall'ERP si modificano solo `Position` e `IsActive` | `ucArchives.SetEditableGrid()` | `ArchiveDescriptor.IsWritable` |
| `IsActive` si può alzare solo se `IsActive_Master` è true | `ucArchives.GrdMasterData_RowEnter()` | `ArchiveService.EnforceMasterActivationRule` |
| Le tabelle allineate dall'ERP non ammettono inserimenti né eliminazioni | menu contestuale condizionato da `IsMasterTable` | `ArchiveEditPolicy.MasterControlled` |
| Filtro "mostra voci non attive" | `chkShowAll` + `GetDbSet*(showInactive)` | `ArchiveQuery.IncludeInactive` |
| Colonne larghe per descrizioni, note ed e-mail | `ucArchives.AdjustGridColumn()` | `ArchiveField.IsWide` |
| Etichette in italiano, inglese e francese | `Strings*.resx` | `Resources/Strings*.resx`, stesse traduzioni |

## Punti da verificare prima di procedere

1. **Versioni dei pacchetti.** In `Directory.Packages.props` sono indicate versioni di
   partenza. Alla prima apertura conviene aggiornarle da "Gestisci pacchetti NuGet",
   in particolare MudBlazor e Microsoft.Identity.Web, che evolvono spesso.

2. **`Press`, `Oven` e `HeatThreatment`.** Nell'app WinForms avevano già il codice di
   lettura e le etichette tradotte, ma erano state rimosse dall'albero di navigazione:
   risultavano quindi irraggiungibili. Qui sono esposte **in sola lettura**. Da decidere se
   aprirle alla modifica (`EditPolicy = ArchiveEditPolicy.Full`) o togliere il descrittore.
   Le anagrafiche effettivamente raggiungibili nell'app precedente erano tredici.

3. **`PressFailureType`.** Compare fra gli "Archivi generali" ma non ha né `Position` né
   `IsActive`, e infatti nel vecchio codice non rientrava fra le "master table". È stata
   mappata come tabella a gestione piena, coerentemente con il comportamento precedente.

4. **Entità `Company`.** Mappata solo sulle tre colonne che servono al lookup. La tabella
   reale ne ha molte altre, alcune NOT NULL: va bene in lettura, ma non tentare inserimenti
   su `Company` da questo contesto.

5. **Permessi.** L'`AuthService` precedente restituiva sempre tutti i permessi (`TODO` nel
   codice). Ora derivano dai ruoli Entra ID; serve decidere chi ha quale ruolo.

## Configurazione

### 1. Registrazione applicativa su Entra ID

Serve una app registration nel tenant, con:

- **Redirect URI** (tipo Web): `https://localhost:7194/signin-oidc` per lo sviluppo, più
  l'URL di produzione;
- **Front-channel logout URL**: `https://localhost:7194/signout-callback-oidc`;
- **App roles** (`Users/Groups` come allowed member type):
  `Archive.Reader`, `Archive.Editor`, `Archive.Administrator`.

Poi compilare `AzureAd` in `appsettings.json` con `TenantId`, `ClientId` e `Domain`.

Il flusso usato è OpenID Connect con solo autenticazione: non servono client secret né
permessi Graph, quindi non c'è nessun segreto da custodire.

### 2. Stringa di connessione

Non va in `appsettings.json`. In sviluppo:

```powershell
cd src\MesDataManager.Web
dotnet user-secrets set "ConnectionStrings:MesDatabase" "Server=...;Database=...;Trusted_Connection=True;TrustServerCertificate=True"
```

In produzione conviene l'autenticazione gestita (managed identity) invece di
utente e password, così non c'è nulla da ruotare.

### 3. Avvio

```powershell
dotnet restore
dotnet build
dotnet run --project src\MesDataManager.Web
```

## Nota sulle migration

Il database è pre-esistente e resta di proprietà del MES: **questa solution non genera e non
applica migration**. Il `MesDbContext` mappa a mano le colonne esistenti nello schema
`MasterData`. Se lo schema cambia, va aggiornata la configurazione in `MesDbContext` e,
dove serve, il catalogo.

Il meccanismo di versionamento del vecchio `VersionHelper` (script SQL incrementali applicati
all'avvio) **non è stato riportato**: eseguire DDL all'avvio di un'applicazione web con più
istanze non è sicuro. Gli script vanno gestiti dal processo di deploy del database.

## Documentazione

- [`Docs/architettura.md`](Docs/architettura.md) — perché le cose stanno così: scelta del
  render mode, separazione in layer, CRUD guidato dai metadati, regole recuperate dal
  WinForms, cosa è stato lasciato indietro e cosa non è stato verificato.
- [`Docs/decisioni-aperte.md`](Docs/decisioni-aperte.md) — cosa resta da decidere, con il
  contesto per deciderlo e cosa succede se non si decide.

## Prossimi passi suggeriti

1. Compilare, connettersi a un database di sviluppo e verificare la mappatura tabella per
   tabella (è il punto in cui emergono le differenze fra EDMX e schema reale).
2. Decidere la sorte di `Press`, `Oven` e `HeatThreatment`.
3. Assegnare i ruoli Entra ID e provare i tre livelli di permesso.
4. Solo dopo: affrontare i moduli testata/righe (batch, billette, fermate), che sono la
   parte con logica di dominio vera.
