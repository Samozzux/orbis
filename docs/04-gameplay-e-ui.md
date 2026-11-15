# 04 — Gameplay e interfaccia

## 1. Flusso iniziale
1. Avvio → schermata di accesso (o accesso automatico con Face ID/passkey se già configurato; "Gioca offline" sempre possibile).
2. Menu principale: **Continua**, **Nuova partita**, **Visita amici**, **Impostazioni**, **Crediti**.
3. Nuova partita:
   - scegli il **luogo** sul globo (ricerca per nome; Chieti preselezionata);
   - scegli il **tipo**: città reale / da zero / storica / scenario / creativa;
   - scegli il **ruolo**: proprietario / cittadino (cambiabile poi);
   - **configurazione del mondo** (tutte le opzioni di `docs/00-visione.md` §14 con valori predefiniti);
   - se cittadino: **creazione del personaggio** (aspetto, nome, età, tratti, famiglia iniziale, animale).
4. Breve tutorial facoltativo e contestuale (si può saltare).

## 2. Camera e controlli

### Viste
- **3D** (predefinita): camera orbitale.
- **2D**: mappa dall'alto (ortografica) con stile cartografico, utile per pianificare; tutte le azioni disponibili anche in 2D.
- **Prima persona**: a piedi o su un veicolo.
- **Drone/libera** e **modalità foto**.

### Comandi
| Azione | Touch | Mouse / tastiera |
|---|---|---|
| Spostarsi | trascina con un dito | trascina col tasto sinistro / WASD / frecce |
| Zoom | pizzico | rotella |
| Inclinare la visuale (alzare/abbassare) | due dita trascinate in verticale | tasto destro trascinato in verticale / Q-E |
| Ruotare | due dita che ruotano | tasto destro trascinato in orizzontale / Z-X |
| Selezionare | tocco | clic sinistro |
| Menu contestuale | tocco su elemento | clic sinistro (o destro per menu rapido) |
| Passare 2D/3D | pulsante | pulsante / tasto M |
| Prima persona: muoversi | joystick virtuale + trascinamento per guardare | WASD + mouse (puntatore bloccato) |
| Annulla / ripeti | pulsanti nell'HUD | Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z |
| Pausa / velocità | pulsanti | Spazio, 1–4 |

- Lo zoom va senza interruzioni dal globo alla strada.
- Inerzia e animazioni morbide; doppio tocco per centrare.

## 3. Selezione e menu contestuale
Tocco su un edificio/oggetto/cittadino/veicolo → evidenziazione + pannello con le azioni disponibili (dipendono da ruolo, possesso e tipo):

- **Entra** (interni in prima persona o vista "casa delle bambole")
- **Dettagli**: nome reale, indirizzo, tipo, livello, proprietario, residenti/lavoratori, valore, rendita, felicità, statistiche
- **Potenzia** (livello successivo, costo, tempo, effetti)
- **Varianti** (stile, colori, materiali; anteprima dal vivo)
- **Sposta**, **Ruota**, **Demolisci**
- **Arreda / Modifica interni**
- **Compra / Vendi / Affitta**
- Azioni specifiche: *Supermercato* → vedi incassi e scorte; *Stadio* → calendario, compra biglietto, guarda la partita; *Scuola* → studenti; *Casa di un cittadino* → la famiglia; ecc.
- Su un **cittadino**: segui, parla (standard/IA), vedi profilo (solo dati di gioco), interazioni.

## 4. Modalità costruzione
- Catalogo a schede per categoria con ricerca, filtri (livello sbloccato, costo, stile), preferiti.
- Posizionamento con griglia allineata alle strade reali (snap ai lotti/strade), anteprima "fantasma" verde/rossa, rotazione, info costi ed effetti (felicità, valore immobiliare nell'area evidenziati con mappa di calore).
- **Strade**: disegno a curve/rettilinei, incroci automatici, ponti/gallerie, tipi (vicolo → autostrada).
- **Zone** opzionali (residenziale/commerciale/industriale) che crescono da sole, oltre al posizionamento manuale.
- **Piani di progetto**: più elementi in stato "pianificato" senza spesa; si confermano tutti o uno alla volta.
- **Terraforming**: alza/abbassa/spiana, acqua, spiagge.
- **Editor modulare di edifici**: componi piani, tetti, facciate, balconi; salva come progetto personale; condividi con gli amici.
- Ogni operazione è un comando annullabile.

## 5. Potenziamenti e varianti
- Ogni elemento del catalogo ha livelli 1→5: aumentano capienza/rendita/servizio e cambiano aspetto (es. casa: ristrutturazione → piano in più → giardino → piscina → villa).
- Le varianti sono cosmetiche o con piccoli effetti (es. pannelli solari).
- Requisiti di sblocco: livello del giocatore, popolazione, servizi presenti.

## 6. Interni
- Generati alla prima apertura e poi **salvati** (restano uguali finché non li cambi):
  - planimetria dalla sagoma e dal numero di piani/appartamenti (suddivisione in stanze per tipo: ingresso, cucina, soggiorno, camere, bagni);
  - arredo scelto da regole: tipo di stanza × reddito × età × gusti × animali × numero di persone; oggetti di vita vissuta (piatti, vestiti, giochi dei bambini, foto);
  - contenuti legati alla simulazione: frigo/dispensa = inventario della famiglia; scrivania di chi studia; cuccia dell'animale.
- Edifici non residenziali: generatori dedicati per **supermercato** (corsie, scaffali con prodotti, casse, magazzino), **bar/ristorante**, **negozi** per tipo, **uffici**, **scuole**, **ospedale**, **stazione**, **aeroporto**, **stadio/palazzetto** (spogliatoi, tribune), **chiesa**, **museo**, **palestra**, **fabbrica**.
- Vista interni: prima persona oppure vista dall'alto senza tetto ("casa delle bambole") per arredare comodamente.

## 7. Modalità cittadino (stile The Sims)
- HUD con bisogni, umore, soldi, ora; coda delle azioni.
- **Azioni** sugli oggetti (mangiare, dormire, cucinare, fare la doccia, guardare la TV, allenarsi…) e sulle persone.
- **Dialoghi**:
  - *Standard*: menu di interazioni sociali (chiacchiera, scherza, complimento, consiglio, flirt, litiga, chiedi un favore, invita…) con esito basato su relazione e tratti; frasi da modelli testuali.
  - *Avanzati (IA)*: conversazione libera scrivendo (o con dettatura del sistema); l'NPC risponde coerente con personalità, ricordi e situazione; l'IA restituisce anche un **effetto strutturato** (variazione relazione, umore, eventuale impegno: appuntamento, offerta di lavoro…). Disponibile solo dove c'è l'IA locale.
- Famiglia, figli, animali (cani, gatti, uccelli, pesci… con bisogni e comportamenti).
- **Carriere** con livelli, competenze, orari; possibilità di lavorare in prima persona in alcuni mestieri (mini-attività semplici, facoltative).
- **Attività propria**: apri un negozio/ristorante/palestra; decidi prezzi, personale, menu, orari, arredo; vedi clienti e conti.
- **Immobiliare**: cerca casa, visita, compra/affitta, mutuo, aste.
- **Telefono nel gioco** (app elencate in `docs/00-visione.md` §7): si apre con un pulsante; su tablet/desktop è un pannello laterale.
- **Diario di vita**: momenti importanti con foto automatica.

## 8. Progressione
- **XP e livelli** separati per ruolo (sindaco e cittadino) + livello globale del giocatore.
- **Missioni** continue (brevi, guidano verso nuove meccaniche), **obiettivi** a lungo termine, **sfide settimanali** (generate), **scenari** (partite con obiettivo e condizioni di vittoria), **achievement**.
- Ricompense: monete, sblocchi di edifici/varianti/landmark posizionabili, oggetti per la casa, cosmetici.
- Dati di missioni/scenari in `data/scenarios/` e modificabili dall'admin.

## 9. Media
- **Notizie**: articoli generati dagli eventi della simulazione (IA o modelli).
- **Social "Piazza"**: post dei cittadini su eventi, sindaco, sport, prezzi; il giocatore può pubblicare e rispondere.
- **Radio**: stazioni (musica libera, "La mia musica", talk) con interventi del DJ.

## 10. Musica
- On/off generale; volumi separati (musica, effetti, ambiente, voce).
- Playlist per contesto: menu, città (giorno/notte), casa, auto/radio, stadio, eventi.
- Fonti: libreria libera inclusa (CC0/royalty-free, crediti in `CREDITS.md`) + "La mia musica" (`docs/01-architettura.md` §10).
- Scelta dei singoli brani e dell'ordine per ogni playlist.

## 11. Modalità foto e time-lapse
Camera libera, profondità di campo, ora del giorno e meteo regolabili, filtri, nascondi interfaccia; salva nella galleria del dispositivo (iOS) o scarica (web). Time-lapse della crescita costruito dagli snapshot giornalieri.

## 12. Visita degli amici
Lista amici; "Visita" scarica un'istantanea della loro città (sola lettura): puoi girare, entrare negli edifici, guardare le loro squadre, lasciare un "mi piace"/commento, copiare un progetto di edificio condiviso.

## 13. Menu sviluppatore (solo account admin)
Aggiungi monete, salta il tempo, attiva eventi/disastri, mostra statistiche di performance, ricarica content pack, ispeziona un cittadino/edificio, posiziona qualsiasi oggetto ignorando i requisiti.

## 14. Stile dell'interfaccia
- Moderna, pulita, pannelli in vetro sfocato ("liquid glass") coerenti con iOS 26, icone chiare, animazioni brevi.
- Leggibile su telefono in orizzontale; più dense su tablet/desktop.
- Tutti i testi dalle tabelle Localization (italiano).
- Accessibilità: dimensione testo regolabile, contrasto alto opzionale, sottotitoli per voci.
