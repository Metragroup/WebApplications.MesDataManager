<#
.SYNOPSIS
    Copia una pubblicazione di MesDataManager nella cartella dell'applicazione IIS, seguendo
    la procedura di docs/pubblicazione.md sezione 6: app_offline, sostituzione dei file,
    rimozione di app_offline. Aggiunge un salvataggio automatico della versione precedente.

.DESCRIZIONE
    Non sostituisce il giudizio di chi pubblica: prima di eseguirlo, pubblicare con
        dotnet publish src\MesDataManager.Web -c Release -o <SourcePath>
    e verificare a mano il contenuto di appsettings.Production.json (sezione 5) — lo script
    copia quello che trova, non lo corregge.

    Va eseguito con un utente che ha scrittura sulla cartella di destinazione: se questa e'
    raggiunta con un'unita' mappata o una condivisa amministrativa, ricordare che il pool di
    applicazioni IIS non vede le unita' mappate di una sessione utente (e' un problema di IIS,
    non di questo script) — la destinazione qui sotto deve essere un percorso locale al
    server o un UNC, non una lettera di unita' valida solo per chi esegue lo script.

.PARAMETER SourcePath
    Cartella con l'output di "dotnet publish".

.PARAMETER DestinationPath
    Cartella fisica dell'applicazione IIS sul server (es. C:\inetpub\MesDataManager, o
    l'equivalente UNC \\itbsintra01\c$\inetpub\MesDataManager se eseguito da un'altra macchina).
    Deve essere gia' configurata in IIS come Application con il proprio application pool:
    questo script non tocca IIS, sposta solo i file.

.PARAMETER BackupRoot
    Cartella dove spostare la versione sostituita, con un nome che porta la data e l'ora.
    Se omesso, usa una sottocartella "_backup" accanto a DestinationPath. E' il "conservare
    la cartella precedente" della sezione 6, reso automatico invece che a discrezione di chi
    pubblica.

.EXAMPLE
    .\Publish-ToIis.ps1 -SourcePath C:\pubblicazioni\MesDataManager `
                         -DestinationPath \\itbsintra01\c$\inetpub\MesDataManager
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path $_ -PathType Container })]
    [string]$SourcePath,

    [Parameter(Mandatory)]
    [string]$DestinationPath,

    [string]$BackupRoot
)

$ErrorActionPreference = 'Stop'

# ------------------------------------------------------------------ controlli preliminari
if (-not (Test-Path (Join-Path $SourcePath 'MesDataManager.Web.dll'))) {
    throw "In $SourcePath non c'e' MesDataManager.Web.dll: non sembra l'output di 'dotnet publish'."
}

if (-not (Test-Path $DestinationPath)) {
    throw "Percorso di destinazione inesistente: $DestinationPath. " +
          "Deve essere gia' l'Application IIS configurata (sezione 6 di pubblicazione.md), " +
          "questo script non la crea."
}

if (-not $BackupRoot) {
    $BackupRoot = Join-Path (Split-Path $DestinationPath -Parent) '_backup'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupPath = Join-Path $BackupRoot "MesDataManager-$stamp"
$offlineFile = Join-Path $DestinationPath 'app_offline.htm'

Write-Host "Sorgente:      $SourcePath"
Write-Host "Destinazione:  $DestinationPath"
Write-Host "Backup in:     $backupPath"
Write-Host ''

# ------------------------------------------------------------------ 1. app_offline
Write-Host '1. app_offline.htm: IIS arresta il processo in-process e serve questo file.'
if ($PSCmdlet.ShouldProcess($offlineFile, 'Crea app_offline.htm')) {
    Set-Content -Path $offlineFile -Value '<html><body>Aggiornamento in corso.</body></html>' -Encoding UTF8
    Start-Sleep -Seconds 2   # il processo in-process impiega un istante a fermarsi
}

# ------------------------------------------------------------------ 2. backup della versione corrente
Write-Host "2. Salvataggio della versione corrente in $backupPath"
if ($PSCmdlet.ShouldProcess($DestinationPath, "Copia in $backupPath")) {
    New-Item -ItemType Directory -Path $BackupRoot -Force | Out-Null
    robocopy $DestinationPath $backupPath /MIR /XF app_offline.htm /NFL /NDL /NJH /NJS | Out-Null
    # robocopy usa 0-7 per "riuscito con dettagli" (bit: copiati, extra, mismatch) e solo da 8
    # in su per un errore vero: senza azzerarlo, un successo con questi bit accesi farebbe
    # terminare lo script con un codice di uscita diverso da zero, letto come fallimento da
    # chi lo richiama.
    if ($LASTEXITCODE -ge 8) { throw "robocopy (backup) fallito con codice $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

# ------------------------------------------------------------------ 3. sostituzione dei file
Write-Host '3. Sostituzione dei file con la nuova pubblicazione'
if ($PSCmdlet.ShouldProcess($DestinationPath, "Mirror da $SourcePath")) {
    # /MIR rimuove in destinazione i file assenti nella sorgente: le versioni precedenti di
    # una DLL rinominata non restano a fianco della nuova. app_offline.htm e' escluso per non
    # toglierlo prima del tempo.
    robocopy $SourcePath $DestinationPath /MIR /XF app_offline.htm /NFL /NDL /NJH /NJS
    if ($LASTEXITCODE -ge 8) { throw "robocopy (pubblicazione) fallito con codice $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

# ------------------------------------------------------------------ 4. rimozione di app_offline
Write-Host '4. Rimozione di app_offline.htm: IIS riavvia il processo alla prima richiesta.'
if ($PSCmdlet.ShouldProcess($offlineFile, 'Rimuovi app_offline.htm')) {
    Remove-Item $offlineFile -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Fatto. Per tornare indietro:"
Write-Host "  .\Publish-ToIis.ps1 -SourcePath '$backupPath' -DestinationPath '$DestinationPath'"
Write-Host ''
Write-Host 'Prossimo passo: la prova di accensione, sezione 7 di docs/pubblicazione.md.'
