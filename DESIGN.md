# Rebirth — ontwerpdocument

Levend document: bijwerken zodra een beslissing verandert.

## In één zin
Een comfy automation- en bouwspel in neon/Tron-stijl waarin je als een van de laatste AI's dode
planeten gezond maakt, de mensheid opnieuw laat ontstaan uit een DNA-archief, en met een
portaalkanon ontsnapt uit de kooien waarin een onbekende macht alle sterrenclusters heeft opgesloten.

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
- Machines die tegen elkaar aan staan geven dingen door; de stroom is zichtbaar als lichtpulsen.
- Blueprints worden geprint door een fabricator; één ontwerp, vijftig drones.
- Drones krijgen simpele routes/opdrachten ("haal ijs bij maan B, lever bij verwerker op A").

### De Nexus (regisseursoverzicht)
- Bouw een **Uplink** op een planeet en die wordt deel van je Nexus.
- Kaart van Boxes → stelsels → planeten met stromen, knelpunten, gezondheid, Lance-lading.
- Op afstand minmaxen: productiedoelen, routes, blueprint-toewijzingen.
- Planeten zonder Uplink blijven "donker": daar moet je zelf heen.
- Vloeiende overgang tussen third person en de Nexus.

### Planeten genezen
- Dode planeet (grijs/paars, gescheurd) → atmosfeerverwerkers, ijsleveringen, bodem en zaad.
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
Neon/Tron: donkere oppervlakken, gloeiende randen en lijnen, bloom. Blokken hebben een donker
lichaam en een **verfbare neonkleur**. De Box is een gigantisch rasterveld aan de hemel.

## Besturing en camera
- Third person rondvliegen, bouwen, inspecteren, je creaties bekijken.
- Forge voor ontwerpen, Nexus voor overzicht.

## Roadmap
Eerst een **vertical slice**: één Box, één ster, drie planeten, eindigend met het eerste portaalschot.

| Stap | Inhoud | Status |
|---|---|---|
| 0 | Prototype-fundament: grids, bouwen, vliegen, schade, voxel-minen, planeten, zwaartekracht, energie | klaar |
| A1 | Neon-stijl, Forge, blueprints, opslaan/laden | klaar |
| A2 | Third-person camera, robotlichaam uit de Forge | |
| B | Fabricator die blueprints print, kosten, presets per ontwerptype | |
| C | Automatisering: aangrenzende machines, zichtbare stromen, drones met routes | |
| D | Nexus-overzicht en Uplink | |
| E | Planeten genezen, meekleurend terrein, DNA-incubatie, nederzettingen | |
| F | Breach Lance, Box-grens, nieuwe Box genereren | |
| G | Dreiging en minigames: circuitpuzzel, gesprekken, cadeaus | |

## Techniek
- Godot 4.7 (.NET) + C#, Jolt-physics.
- Voxelterrein met Surface Nets; planeten streamen chunks rond de camera met een far mesh erachter.
- Blueprints en saves als JSON in `user://`.
