# Editing dati
La visualizzazione dei dati di tabella in formato grid dovrebbe mostrare un sottoinsieme delle colonne per rendere la form più compatta.
Integrare la stuttura ArchiveDescriptor.Fields aggiungendo la proprietà ShowOnGrid (bool), usa sempre true come valore di default: poi farò tuning

# Revisione menu

## Logo
Integrare nella pagina home il logo @prompts\METRA_logo_orizzontale.svg

## ArchiveGroup
Rivedere le descrizioni associate all'enumerato ArchiveGroup
* MasterData -> la descrizione diventa "Anagraficge di gruppo"
* Plant	-> la descrizione diventa "Anagrafiche di impianto"

## Menu di navigazione
Nella pagina home sostituire il componente MudGrid con MudNavMemu con sezioni collassaibili
* Non usare icone
* Allineare in alto sotto il logo Metra
* Sezione "Archivi": contiene
	* MasterData: contiene tutte le tabelle ArchiveGroup.MasterData, stato iniziale collassato
	* Plant: contiene tutte le tabelle ArchiveGroup.Plant, stato iniziale collassato
* Sezione "Produzione": le pagine non esistono ancora, prepara le voci di menu
	* Fermi
	* Dati produzione
	* Lotti in corso
	* Storico ceste
	* Storico carico