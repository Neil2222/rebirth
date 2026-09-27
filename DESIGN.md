# Rebirth — ontwerpdocument

Levend document: bijwerken zodra een beslissing verandert.

## In één zin
Een comfy, retro-futuristische planeetbouwer: als een van de laatste AI's maak je slapende planeten
weer levend, laat je de mensheid opnieuw ontstaan uit een DNA-archief, en ontsnap je met een
portaalkanon uit de kooien waarin een onbekende macht alle sterrenclusters heeft opgesloten.
Bewust **geen** Space Engineers-kloon: speels en warm in plaats van donker en industrieel.

## Gevoel
Het spel waar je uren in verdwijnt terwijl er een serie aanstaat.
- **Geen tijdsdruk, geen permadeath.** Wat misgaat, staat stil tot je het oplost; het gaat niet kapot.
- **Het draait door.** Je automatisering werkt ook als jij even niks doet; terugkijken en groei zien is de beloning.
- **Korte klusjes, lange doelen.** Een kwartier optimaliseren kan altijd; de Breach Lance is het grote doel.
- **Leesbaar.** Stromen en knelpunten zie je in één oogopslag.
- **Mooi om naar te kijken.** Gloeiende stromen, zwermen drones, planeten die opbloeien.

## Verhaal
De mensheid is uitgestorven. Een entiteit die we **de Curator** noemen heeft elk sterrencluster
ingesloten in een **Box**: een gloeiende, ondoordringbare kooi. Jij bent een **Custodian**, een van de
laatste AI's, met het DNA-archief van de mens aan boord.

Je enige uitweg is de **Breach Lance**, een portaalkanon dat een doorgang naar een andere Box schiet.
Het laadt op met **Resonance**, die alleen ontstaat uit leven. Je trekt planeten dus niet leeg, je
maakt ze beter: hoe gezonder je werelden en hoe meer mensen er weer leven, hoe sneller je verder kunt.

Open vraag (bewust): waarom sloot de Curator alles op — straf of bescherming?

## Kernlus
1. Grondstoffen winnen (asteroïden, manen, planeten).
2. Automatiseren: machines, fabrieken, drones met routes.
3. Planeten genezen: atmosfeer, water, bodem, flora.
4. DNA incuberen: nederzettingen groeien, mensen hebben verzoeken.
5. Leven levert Resonance, Resonance laadt de Breach Lance.
6. Portaal naar de volgende Box: nieuwe regels, nieuwe wereld, technologie en archief gaan mee.

## Pijlers

### De Forge (ontwerpen à la Crossout)
Een eigen scherm: een neon-hangar met onderdelenpalet, draaibare camera en live statistieken
(massa, stroom, stuwkracht, lading, kosten). Hiermee ontwerp je alles: drones, rovers, schepen,
gebouwen, en je eigen robotlichaam.
- **Core** bepaalt wat een ontwerp is (drone, schip, rover, gebouwtype).
- **Modules** geven functie (thrusters, boren, lading, fabricage, energie).
- **Shell**: vrije vormgeving en neonkleuren.
- Elk gebouw en voertuig heeft een **standaardontwerp** (preset); zelf ontwerpen mag altijd.
- Ontwerpen zijn **blueprints**: opgeslagen, deelbaar, en door fabrieken in serie te printen.

### Automatisering
- Logistieke blokken (buizen, opslag, raffinaderij, fabricator, auto-drill) vormen een netwerk zodra ze vlak tegen elkaar zitten.
- Spullen reizen als gekleurde pods door glazen buizen, via de kortste route; machines die iets nodig hebben gaan vóór opslag.
- Auto-drill boort de grond voor zijn voorkant af; raffinaderij heeft een ertsbuffer en een staafbuffer; de fabricator trekt ontbrekende staven zelf uit opslag.
- Blueprints worden geprint door een fabricator; één ontwerp, vijftig drones.
- **Bots** zijn ontwerpen met een Bot Core (ontwerptype Bot). Ze vliegen zelf: materialen van Home
  naar een bouwplaats, blok voor blok bouwen, en ingots van sites naar Home slepen (haulroutes).
- **Zelf bouwen is goedkoop, bots laten bouwen kost 2,5× zoveel.** Je doet het één keer zelf (tutorial);
  daarna is groei een kwestie van kiezen: planeet, ontwerp, bots erop.
- Later: routes tussen willekeurige sites, prioriteiten, specialisaties per bot-ontwerp.

### De Nexus (regisseursoverzicht)
- **Nu (N of Esc-menu):** camera hoog boven het stelsel, labels bij planeten, sites en bots. Menu's
  voor alles: bots printen bij Home, planeet kiezen (klik voor een eigen plek), ontwerp kiezen en
  bots sturen, bouwvoortgang, opslag en machines per site.
- **Uplinks:** Home en elk station met een Uplink-blok reiken 170 m. Wat buiten bereik ligt is
  **donker** (gesluierd, geen bouwmenu): daar moet je zelf heen en een Uplink neerzetten.
- **Bodemscan:** bij een gekozen bouwplek zie je wat er onder zit (steen, ijzer, nikkel, silicium).
- **Stromen:** productie per minuut per station, "Income" van Home, gloeiende stroomlijnen met
  bewegende streepjes per route, en ⚠-knelpunten in gewone taal (te weinig stroom, opslag vol,
  drill raakt geen steen, ingots stapelen zich op zonder route).
- **Routes tussen alle stations,** met lading: ingots, erts of alles. Nieuws-feed in de Nexus.
- Bouw een **Uplink** op een planeet en die wordt deel van je Nexus.
- Kaart van Boxes → stelsels → planeten met stromen, knelpunten, gezondheid, Lance-lading.
- Op afstand minmaxen: productiedoelen, routes, blueprint-toewijzingen.
- Planeten zonder Uplink blijven "donker": daar moet je zelf heen.
- Vloeiende overgang tussen third person en de Nexus.

### Planeten genezen
- **Nu (E1):** elke kleine planeet heeft Lucht, Water en Bodem (0–100%). Toolbarpagina "Life":
  **Air Processor** (stookt steen tot lucht), **Hydrator** (smelt ijs tot zee; ijs komt uit de korst van
  Frost), **Seed Garden** (maakt bodem, pas vanaf 30% lucht en water). Presets: Air Maker (drill + processor),
  Water Works (hydrator + opslag, ijs per botroute), Garden. De haze wordt hemelsblauw, zeeën stijgen in de
  dalen, groen verspreidt zich over vlakke grond. Levende planeten geven **Resonance**.
- Slapende planeet (stoffig, kaal) → atmosfeerverwerkers, ijsleveringen, bodem en zaad.
- Het terrein kleurt letterlijk mee naarmate de planeet gezonder wordt.
- Incubators met menselijk DNA; nederzettingen verschijnen als lichtjes en groeien.
- Mensen doen **verzoeken**: gesprekken, cadeaus, bouwopdrachten, een band opbouwen.
- **Nu (E2):** een **Incubator** (preset Settlement Seed: incubator + depot + zonnepanelen) wekt gezinnen
  zodra de planeet bewoonbaar is (50% lucht en water, 30% bodem). Er groeit een dorpje van speelgoedhuisjes
  met warme ramen rond de incubator; bodem bepaalt hoeveel mensen er passen. Verzoeken zijn een mix:
  **leveren** (in het depot), **bouwen** (station met een bepaald blok binnen 70 m), **praten** (korte
  gesprekken met keuzes), soms met een **cadeau** terug naar Home. Geen tijdsdruk. Elk vervuld verzoek
  verdiept de **band** (♥) en geeft Resonance; bevolking × band geeft doorlopend Resonance.
  Mensen zijn nu lichtjes en huisjes; echte poppetjes komen bij de grafische ronde.

### Dreiging (vriendelijk)
Geen FPS, geen tijdsdruk. Getroffen machines gaan in **quarantaine** (stil, niet kapot).
- **Virus**: een circuitpuzzel om het te zuiveren.
- **Zwerm van de Curator**: praten, iets geven, of te slim af zijn met een puzzel.
- Later: **firewall**-gebouwen die dreiging geautomatiseerd afhandelen.

### Elke Box is anders
Box zonder ster (breng zelf licht), waterwereld, zwermnest, ... Technologie en archief gaan mee.

## Stijl
**Comfy retro-futurisme, cartoony, LittleBigPlanet-achtig.** (Neon/Tron is geprobeerd en afgewezen:
te fel en te donker.)
- Blokken zijn **speelgoed**: afgeronde randen, geverfd plastic, zachte naad tussen blokken,
  warme retro-kleuren (crème, oranje, koraal, mosterd, mint, teal, hemelsblauw, pruim, leisteen).
  Elk bloktype heeft een eigen kleur; spelers kunnen overschilderen.
- Details in jaren-70-ruimtevaartstijl: chroom randen, bubbelglas, bolle straalpijpen, messing,
  kleine lampjes. **Licht alleen waar een lamp of vlam is**; geen gloeiende randen.
- Ruimte is **zacht en kleurrijk**: pruim, perzik rond de zon, teal aan de andere kant, nevelwolkjes.
  De lucht is ook de invullende belichting, met zachte contactschaduwen (SSAO).
- Asteroïden en planeten zijn **rond en glad** (kiezels, glooiende heuvels), in pastel.
  Slapende planeten zijn stoffig zandroze; tot leven gewekt kleuren ze op.
- UI: crème panelen met ronde hoeken, bruine tekst, oranje accent.
- De Box is een groot, zacht zichtbaar raster aan de hemel (nog uit te werken in stijl).

## Besturing en camera
- Third person rondvliegen, bouwen, inspecteren, je creaties bekijken.
- Forge voor ontwerpen, Nexus voor overzicht. Het spel is volledig via menu's te spelen: titelmenu,
  Esc-menu (Nexus, Forge, opslaan, nieuw spel), Nexus.

## Begin van het spel
- **Tutorial (third person):** erts minen, raffinaderij voeren, een buis leggen, een auto-drill zetten
  (spookblokken wijzen de plek aan), ingots laten stromen, twee Worker Bots printen. Dan gaat de Nexus aan.
- **Intro overslaan:** start met precies die uitkomst: Home met drill en buis, twee bots, Nexus open.
- Zelf bouwen verbeteren loont pas als er meer onderdelen en vormen zijn; eerst de groei-lus.

## Roadmap
Eerst een **vertical slice**: één Box, één ster, drie planeten, eindigend met het eerste portaalschot.

| Stap | Inhoud | Status |
|---|---|---|
| 0 | Prototype-fundament: grids, bouwen, vliegen, schade, voxel-minen, planeten, zwaartekracht, energie | klaar |
| A1 | Forge, blueprints, opslaan/laden (neon-stijl later vervangen) | klaar |
| A2 | Third-person camera, robotlichaam uit de Forge | klaar |
| B | Fabricator die blueprints print, kosten, presets per ontwerptype | klaar |
| S | Stijlwissel naar comfy retro-futurisme | klaar |
| P | Kleine loopbare planeten (40–60 m, rondje in ±1 minuut) met eigen zwaartekracht | klaar |
| C1 | Logistiek netwerk: buizen, pods, auto-drill, machinebuffers | klaar |
| C2 | Bots, Nexus-overzicht met menu's, bouwen via bots (2,5× kosten), haulroutes, tutorial, titel- en pauzemenu | klaar |
| D | Uplink, donkere planeten, bodemscan, stromen en knelpunten in de Nexus, routes tussen sites | klaar |
| E1 | Planeten genezen: lucht, water, bodem, meekleurend terrein, zeeën, Resonance | klaar |
| E2 | DNA-incubatie, nederzettingen, verzoeken van mensen (leveren, bouwen, praten, cadeaus), band | klaar |
| F | Breach Lance, Box-grens, nieuwe Box genereren | |
| G | Dreiging en minigames: circuitpuzzel, gesprekken, cadeaus | |

## Techniek
- Godot 4.7 (.NET) + C#, Jolt-physics.
- Voxelterrein met Surface Nets; planeten streamen chunks rond de camera met een far mesh erachter.
- Blueprints en saves als JSON in `user://`.
