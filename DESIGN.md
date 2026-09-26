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
- Drones krijgen simpele routes/opdrachten ("haal ijs bij maan B, lever bij verwerker op A").

### De Nexus (regisseursoverzicht)
- Bouw een **Uplink** op een planeet en die wordt deel van je Nexus.
- Kaart van Boxes → stelsels → planeten met stromen, knelpunten, gezondheid, Lance-lading.
- Op afstand minmaxen: productiedoelen, routes, blueprint-toewijzingen.
- Planeten zonder Uplink blijven "donker": daar moet je zelf heen.
- Vloeiende overgang tussen third person en de Nexus.

### Planeten genezen
- Slapende planeet (stoffig, kaal) → atmosfeerverwerkers, ijsleveringen, bodem en zaad.
- Het terrein kleurt letterlijk mee naarmate de planeet gezonder wordt.
- Incubators met menselijk DNA; nederzettingen verschijnen als lichtjes en groeien.
- Mensen doen **verzoeken**: gesprekken, cadeaus, bouwopdrachten, een band opbouwen.

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
- Forge voor ontwerpen, Nexus voor overzicht.

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
| C2 | Drones met routes tussen planeten en stations | |
| D | Nexus-overzicht en Uplink | |
| E | Planeten genezen, meekleurend terrein, DNA-incubatie, nederzettingen | |
| F | Breach Lance, Box-grens, nieuwe Box genereren | |
| G | Dreiging en minigames: circuitpuzzel, gesprekken, cadeaus | |

## Techniek
- Godot 4.7 (.NET) + C#, Jolt-physics.
- Voxelterrein met Surface Nets; planeten streamen chunks rond de camera met een far mesh erachter.
- Blueprints en saves als JSON in `user://`.
