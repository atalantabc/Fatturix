# Fatturix
Programma per estrarre, ordinare e visualizzare fatture da ZIP in automatico.

# FattureViewer

FattureViewer è un'applicazione desktop moderna in C# (WPF) progettata per importare, visualizzare e organizzare facilmente le fatture elettroniche italiane (sia in formato XML puro che firmate digitalmente in formato P7M).

## 🚀 Caratteristiche Principali

- **Importazione massiva da ZIP**: Importa automaticamente archivi ZIP contenenti centinaia di fatture elettroniche con un solo clic.
- **Supporto P7M Nativo Avanzato**: Decodifica nativamente le fatture firmate in formato `.xml.p7m` tramite algoritmi intelligenti di recupero (estrazione binaria) a prova di errore, anche su file con firme corrotte.
- **Navigazione ad Albero (Tree View)**: L'interfaccia usa una struttura ad albero stile IDE per organizzare le tue fatture per **Anno > Mese > Nome Azienda**. L'anno e il mese correnti si espandono automaticamente all'avvio.
- **Anteprima HTML Tradizionale**: Visualizza la fattura elettronica in un formato classico e pulito, del tutto simile a quello di cortesia in PDF. Include tutti i dati di riepilogo IVA, spese, sconti, importo bollo, dettagli pagamento e IBAN.
- **Motore di Estrazione Configurabile**: Il comportamento di estrazione è personalizzabile. Attraverso una finestra dedicata è possibile modificare il file `config.txt` per impartire comandi automatizzati al programma, ad esempio per creare cartelle specifiche (es. `/Fatture_Mese_Anno`) ed estrarrvi le fatture importate.
- **Database Locale Veloce**: Utilizza SQLite per indicizzare e cercare le fatture importate in tempi ridottissimi.
- **Ricerca e Filtri**: Filtra istantaneamente le fatture per Anno, Mese e per nome dell'Azienda/Cedente.

## 🛠 Tecnologie Utilizzate

- **Linguaggio**: C# (versione 10.0+)
- **Framework**: .NET 6.0 Windows
- **UI**: Windows Presentation Foundation (WPF)
- **Database**: `sqlite-net-pcl`
- **Crittografia**: `System.Security.Cryptography.Pkcs` (v.6.0.4 specifica per massima stabilità su .NET 6)

## 💻 Come avviare l'applicazione

Usare apposito installer sulle release.

## Aggiornamenti

Ogni `FattureViewerInstaller-x.y.z.exe` funziona sia come prima installazione sia
come aggiornamento: se FattureViewer è già presente, sostituisce soltanto
l'eseguibile e conserva database, storage e impostazioni.

All'avvio l'app controlla in background le release pubblicate su GitHub. Una
release stabile più recente viene proposta all'utente e, dopo la conferma,
l'installer viene scaricato, verificato tramite SHA-256 quando GitHub fornisce
il digest, eseguito e l'app viene riavviata.

Per escludere una release dagli aggiornamenti automatici, terminare la
descrizione della release con `(N.U)`. Gli spazi e le righe vuote successive
vengono ignorati. La release resta comunque disponibile per il download
manuale.

## Fornitori, Clienti e sessione temporanea

- **Fornitori** mantiene il flusso completo con database `fatture.db`,
  estrazione, cartelle per periodo e archiviazione dello ZIP.
- **Clienti** usa il database separato `fattureClienti.db` nella stessa cartella
  principale e non copia, sposta o archivia file.
- Il controllo O.M.T. usa principalmente la partita IVA `IT04170380374` e
  richiede conferma da 6 fatture probabilmente importate nella sezione errata.
- L'opzione Admin **NON SALVATAGGIO IN SESSIONE** usa un database temporaneo:
  le fatture restano consultabili durante la sessione e vengono eliminate alla
  chiusura senza modificare database, ZIP o cartelle reali.
- La versione installata è consultabile dal menu con i tre puntini.

## ⚙️ Regole di Configurazione (config.txt)

Il programma è in grado di eseguire automatismi quando si importa un file ZIP. È possibile accedere all'editor di configurazione direttamente dall'interfaccia utente cliccando su **Configura Regole**.

Esempi di comandi supportati:
- `EXTRACT_DIR "C:\Cartella"` : Imposta la cartella in cui scompattare temporaneamente il file ZIP per leggere le fatture.
- `CREATE_DIR "C:\Cartella\{year}_{month}"` : Crea una cartella in modo dinamico in base all'anno e al mese correnti.
- `MOVE "*.xml" TO "C:\Cartella_Destinazione"` : Sposta tutti i file XML nella cartella indicata.

## 👥 Struttura del Progetto

- **MainWindow.xaml**: Interfaccia principale (Treeview e Anteprime).
- **ConfigWindow.xaml**: Editor integrato con formattazione a colori per il file `config.txt`.
- **ViewModels/**: Contiene i ViewModels (pattern MVVM), inclusi i nodi per l'albero (`TreeNodes.cs`).
- **Models/**: Contiene i modelli dati come `InvoiceData.cs` mappati sul database SQLite.
- **Services/**: 
  - `DatabaseService.cs`: Gestione SQLite.
  - `ExtractionEngine.cs`: Logica di estrazione ZIP e automazione file.
  - `InvoiceParser.cs`: Logica chirurgica di estrazione da XML e P7M.
  - `HtmlGenerator.cs`: Renderizzazione della grafica stile "fattura di cortesia".
