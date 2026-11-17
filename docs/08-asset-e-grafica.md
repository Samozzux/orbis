# 08 — Asset, catalogo e grafica

## 1. Fonti degli asset (solo gratuite e con licenza d'uso)

| Tipo | Fonti | Licenza |
|---|---|---|
| Texture PBR, HDRI | Poly Haven, ambientCG | CC0 |
| Modelli generici (oggetti, mobili, veicoli, natura) | Poly Haven, Kenney, Quaternius, Sketchfab (solo con licenza CC0/CC-BY indicata) | CC0 / CC-BY |
| Personaggi | MakeHuman (export CC0) + generatore modulare interno (corpo, viso, capelli, vestiti) | CC0 |
| Animazioni | Mixamo (uso consentito nei progetti) + animazioni procedurali (IK, locomozione) | termini Adobe |
| Facciate, tetti, arredo urbano, stadi, aeroporti | generati da script **Blender** (`tools/blender/`) | nostri |
| Landmark | Sketchfab CC0/CC-BY o modellati da script Blender | vedi `docs/02-mondo-reale.md` §5 |
| Suoni ambiente ed effetti | Freesound (solo CC0), Kenney | CC0 |
| Musica | brani con licenza CC0 o royalty-free verificata | da elencare in `CREDITS.md` |
| Font | Google Fonts con licenza OFL | OFL |

Regole:
- Ogni asset esterno registrato in `data/assets/registry.json` (fonte, autore, licenza, URL, data) e, se richiesto dalla licenza, in `CREDITS.md`.
- **Nessun marchio reale** su veicoli, prodotti, insegne commerciali generiche (modelli e nomi inventati). I nomi reali sono ammessi per luoghi pubblici e POI presi da OSM (es. il nome di un bar reale sull'insegna) e per lo sport (con il data pack dedicato).
- Asset pesanti non nel repo git: generati/scaricati dal workflow `assets.yml` e distribuiti come asset pack (Addressables).

## 2. Catalogo ("deve esserci tutto")
Tutto è dati (`data/catalog/*.json` → content pack "catalog"). Obiettivi di quantità per la **versione 1.0** (crescono nel tempo con l'admin):

| Categoria | Esempi | Obiettivo elementi base (ognuno con varianti) |
|---|---|---|
| Residenziale | case, villette, condomini, ville, grattacieli | 60 |
| Commerciale | negozi per tipo, supermercati, centri commerciali, bar, ristoranti, hotel | 80 |
| Uffici e industria | uffici, fabbriche per settore, magazzini, porti | 50 |
| Energia | pale eoliche (singole e parchi), fotovoltaico a terra e sui tetti, centrali idroelettriche, termoelettriche (gas, carbone), a biomasse, geotermiche, **nucleari**, tralicci e linee elettriche, sottostazioni | 30 |
| Agricoltura e campagna | campi per coltura (grano, mais, girasoli, ortaggi…), **vigneti**, uliveti, frutteti, serre, fattorie, casali, stalle, silos, cantine, frantoi | 50 |
| Servizi pubblici | municipio, scuole, università, ospedali, polizia, pompieri, poste, biblioteche | 40 |
| Sport | campi, stadi, palazzetti, piscine, circuiti, piste, palestre | 50 |
| Cultura e svago | musei, teatri, cinema, parchi divertimento, zoo, discoteche | 40 |
| Trasporti | stazioni, fermate, metro, aeroporti, eliporti, porti, parcheggi | 40 |
| Natura | alberi (per specie e clima), piante, fiori, prati, rocce, acqua | 150 |
| Decorazioni e arredo urbano | fontane, statue, panchine, lampioni, fioriere | 120 |
| Mobili e oggetti per interni | cucina, soggiorno, camera, bagno, ufficio, negozio, elettronica | 800 |
| Veicoli | auto (per segmento), moto, bici, bus, tram, metro, treni, camion, emergenza, aerei, elicotteri, barche | 150 |
| Cibi e prodotti | alimenti per scaffali/frigo/menu (con prezzo, valore nutritivo di gioco, scadenza) | 400 |
| Animali | domestici, da fattoria, selvatici, zoo | 80 |

Ogni elemento ha una **sagoma esatta** (impronta a terra + altezza) e si piazza su griglia dentro lotti delimitati (`docs/04` §4): nello schema `footprint` è l'impronta in metri (eventualmente un poligono `shape` in coordinate locali), e il modello non deve uscirne.

### Schema di un elemento (esempio)
```json
{
  "key": "house_villetta_abruzzese",
  "category": "residential",
  "name": "Villetta abruzzese",
  "footprint": { "w": 12, "d": 10 },
  "unlock": { "playerLevel": 3 },
  "levels": [
    { "level": 1, "cost": 25000, "buildHours": 6, "capacity": 4, "rentPerMonth": 450, "maintenancePerMonth": 40, "model": "villetta_l1" },
    { "level": 2, "cost": 18000, "buildHours": 8, "capacity": 5, "rentPerMonth": 580, "model": "villetta_l2" }
  ],
  "variants": [
    { "key": "intonaco_ocra", "materials": { "facade": "plaster_ochre", "roof": "coppi_red" } },
    { "key": "pietra", "materials": { "facade": "stone_majella", "roof": "coppi_old" }, "costMultiplier": 1.15 }
  ],
  "effects": { "happinessRadius": 30, "happiness": 1, "landValue": 2 },
  "interiorGenerator": "house_small",
  "styles": ["realistic", "stylized"]
}
```
Gli schemi JSON formali di ogni tipo stanno in `data/schemas/` e sono usati sia dal gioco (validazione al caricamento) sia dal pannello admin (form automatici).

## 3. Stili grafici

| Aspetto | Realistico | Classico | Pixel |
|---|---|---|---|
| Materiali | PBR completi (albedo, normal, roughness, AO) | stilizzati, colori saturi, poche texture | stilizzati |
| Luce | ciclo giorno/notte fisico, cielo procedurale, ombre in cascata | luce morbida, ombre semplici | come Classico |
| Post-processing | tonemapping ACES, bloom, AO, nebbia atmosferica, color grading | bloom leggero, contorni leggeri | render a bassa risoluzione + palette + contorni + dithering |
| Vegetazione | alberi dettagliati con vento | alberi low-poly | come Classico |
| Costo | alto | medio | basso |

- **Edifici reali "da gioco"** (decisione di Samuele, 17/11/2026): nello stile Classico ogni edificio OSM mantiene sagoma, altezza, piani e tetto reali, ma è ricostruito con i dettagli tipici del suo uso (dal tipo di edificio e dai POI che contiene): bar/ristoranti (tende a righe, insegna, dehors con tavolini e ombrelloni), negozi (tende, insegne, vetrine), farmacie (croce verde), benzinai (pensilina e pompe), alberghi, scuole e ospedali (insegne, bandiere), industrie (impianti sul tetto, container), case (balconi, fioriere, persiane colorate, comignoli), tetti piani con parapetto e terrazze. Gli **addobbi** dipendono dal **livello** dell'edificio (1→5): ogni potenziamento ne aggiunge o cambia, sia per gli elementi del catalogo sia per gli edifici reali. Gli addobbi sono geometria procedurale nei tile vicini (LOD pieno), con densità ridotta sui profili di qualità bassi.
- Il cambio di stile avviene a runtime senza riavvio (cambio di set di materiali + profilo URP + volume di post-processing).
- Nel prototipo (M1–M10) si sviluppa prima lo stile **Classico**; Realistico e Pixel completi in M11, ma l'architettura dei materiali a due set va predisposta da M1.

## 4. Profili di qualità
`Auto` (predefinito: sceglie in base al dispositivo e alle prestazioni misurate nei primi minuti) / Basso / Medio / Alto / Ultra.
Parametri: distanza di visualizzazione, densità di agenti L0, ombre, risoluzione di rendering (scala dinamica), qualità delle texture, densità di vegetazione, effetti.
Su Android: "Auto" in base a GPU/RAM, fino a "Ultra" sui top di gamma. Su web: profilo massimo "Alto" con WebGPU, "Medio" con WebGL 2; su telefoni via browser "Basso".

## 5. Personaggi
- Generatore modulare: corporatura, altezza, età (bambini → anziani), tratti del viso, carnagione, capelli, barba, vestiti (per stagione, lavoro, reddito, occasione), accessori.
- Ogni cittadino ha parametri di aspetto salvati (non mesh): l'aspetto si ricostruisce identico.
- LOD: personaggio completo vicino, versione semplificata a media distanza, impostor/billboard lontano; animazioni con GPU skinning/instancing per le folle.

## 6. Prestazioni degli asset
- Budget triangoli indicativi: edificio procedurale LOD0 ≤ 5k, mobile ≤ 2k, personaggio LOD0 ≤ 15k, veicolo ≤ 10k, landmark ≤ 150k (con LOD).
- Atlas di texture per i kit di facciate; compressione ASTC (iOS e Android) e formati adatti al web.
- Addressables raggruppati per regione/stile/categoria per scaricare solo il necessario, con build separate per iOS, Android e Web.
