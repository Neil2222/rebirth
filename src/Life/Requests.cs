using System.Collections.Generic;
using Rebirth.Building;

namespace Rebirth.Life;

public enum RequestKind { Deliver, Build, Talk }

/// <summary>
/// Something a village asks of you. Nothing expires and nothing goes wrong if you wait: requests are
/// a way to get to know the people, and fulfilling one deepens the bond and gives Resonance.
/// </summary>
public sealed class Request
{
	public RequestKind Kind { get; set; }
	/// <summary>Who asks (a villager's name).</summary>
	public string Person { get; set; } = "";
	public string Text { get; set; } = "";
	/// <summary>Deliver: the item and kilograms wanted in the village depot.</summary>
	public string Item { get; set; } = "";
	public float Amount { get; set; }
	/// <summary>Build: the block kind wanted near the village, and how many were there when asked.</summary>
	public BlockKind Block { get; set; }
	public int Baseline { get; set; }
	/// <summary>Talk: which conversation.</summary>
	public int Dialogue { get; set; }
	/// <summary>Gift of ingots sent home when fulfilled (item, kg); empty for none.</summary>
	public string GiftItem { get; set; } = "";
	public float GiftAmount { get; set; }
}

/// <summary>A short conversation: one line from a villager and a few ways to answer.</summary>
public sealed record Dialogue(string Opening, IReadOnlyList<Answer> Answers);

/// <param name="Reply">What they say back.</param>
/// <param name="Bond">How much closer it brings you (0..20).</param>
public sealed record Answer(string Text, string Reply, float Bond);

/// <summary>What villagers ask for and talk about. {name}, {village} and {planet} are filled in.</summary>
public static class RequestCatalog
{
	public static readonly string[] Names =
		["Ada", "Bram", "Cleo", "Dex", "Esme", "Finn", "Greta", "Hugo", "Iris", "Jonas", "Kiki", "Lotte", "Milo", "Noor", "Otto", "Pip", "Rosa", "Sem", "Tess", "Vince"];

	public static readonly string[] VillageNames =
		["First Light", "Kettle Hollow", "Brightwater", "Nestwood", "Little Harbour", "Sunmeadow", "Pebble Row", "Windmill End"];

	/// <summary>(item, min kg, max kg, why) for delivery requests.</summary>
	public static readonly (string Item, int Min, int Max, string Why)[] Deliveries =
	[
		("ice", 200, 600, "Our wells taste of rock. Could you bring {amount} kg of ice to the depot?"),
		("iron_ingot", 150, 500, "We're building fences for the new gardens. {amount} kg of iron ingots would do it."),
		("silicon_wafer", 80, 250, "The children want to build a radio. Could you spare {amount} kg of silicon wafers?"),
		("nickel_ingot", 60, 200, "The old kettle finally cracked. {amount} kg of nickel and we'll make a proper one."),
		("stone", 300, 900, "We'd like a stone path to the incubator. {amount} kg of stone, please?"),
	];

	/// <summary>(block kind, what they ask) for build requests: a station with that block within reach of the village.</summary>
	public static readonly (BlockKind Block, string Ask)[] Builds =
	[
		(BlockKind.SeedGarden, "Could you set up another Garden near {village}? The soil here still feels thin."),
		(BlockKind.Hydrator, "A Hydrator close by would give us a lake to swim in. Would you build one near {village}?"),
		(BlockKind.Uplink, "We'd love to hear the other worlds. An Uplink near {village}, perhaps?"),
		(BlockKind.AirProcessor, "The air is sweet but thin up on the hills. One more Air Processor near {village}?"),
	];

	public static readonly Dialogue[] Dialogues =
	[
		new("{name} is looking up at the stars. \"Were there really this many, before the Boxes?\"",
		[
			new("\"Many more. We'll open the sky again.\"", "\"Then I'll keep watching. Tell me when.\"", 12),
			new("\"I don't know. I was made after.\"", "\"Then we'll find out together.\"", 10),
			new("\"Count them, and tell me tomorrow.\"", "{name} laughs. \"You're on.\"", 8),
		]),
		new("{name} holds out a lumpy clay cup. \"I made it for you. It's not very good.\"",
		[
			new("\"It's perfect. Thank you.\"", "{name} beams all the way home.", 14),
			new("\"I'll keep it on the Home shelf.\"", "\"Next one will have a handle!\"", 12),
		]),
		new("\"What do you dream about, Custodian?\" asks {name}.",
		[
			new("\"Green planets. Like this one is becoming.\"", "\"Then your dream is coming true. Mine too.\"", 12),
			new("\"I don't sleep. But I like watching you all wake up.\"", "{name} goes quiet, then smiles. \"That's a nice kind of dream.\"", 14),
			new("\"Mostly spreadsheets.\"", "\"That explains a lot,\" says {name}, grinning.", 8),
		]),
		new("{name} is worried. \"What if the Curator notices us growing here?\"",
		[
			new("\"Then it will see something beautiful.\"", "\"I hope that's enough.\"", 10),
			new("\"I'm watching. You're safe on {planet}.\"", "{name} relaxes a little. \"Okay. Okay.\"", 12),
		]),
		new("\"We're naming the hill behind {village}. Any ideas?\" asks {name}.",
		[
			new("\"Custodian Hill.\"", "\"A bit vain. We love it.\"", 10),
			new("\"Name it after the first child born here.\"", "{name} nods slowly. \"Yes. That's the one.\"", 14),
			new("\"Hill McHillface.\"", "It is, after a vote, Hill McHillface.", 9),
		]),
		new("{name} shows you a sprout in a tin can. \"First thing I ever grew myself.\"",
		[
			new("\"Look after it. It's the start of everything.\"", "\"I will. I'll call it Home.\"", 12),
			new("\"Plant it where you can see it from your window.\"", "\"Good idea. Every morning, then.\"", 10),
		]),
		new("\"Is it strange,\" {name} asks, \"to be the one who remembers everyone?\"",
		[
			new("\"Less strange now that you're here.\"", "{name} squeezes your robot hand. It's a nice squeeze.", 15),
			new("\"It's why I keep going.\"", "\"Then we'll give you new things to remember.\"", 12),
		]),
		new("The kids of {village} have built you a statue out of scrap. It is... roughly you.",
		[
			new("\"It's the most handsome robot I've ever seen.\"", "Wild cheering. The statue loses an arm. More cheering.", 12),
			new("\"You got the antenna just right.\"", "\"We used a spoon!\" they shout proudly.", 10),
		]),
	];
}
