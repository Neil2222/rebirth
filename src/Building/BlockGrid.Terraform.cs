using System.Collections.Generic;
using Godot;

namespace Rebirth.Building;

/// <summary>What a terraformer adds to its planet.</summary>
public enum Vital { Air, Water, Soil }

// Terraformers eat what the logistics network brings them (stone, ice) and turn it into air, water
// and soil for the planet they stand on. The grid only counts what was made; the colony hands it to
// the planet, which knows how much it takes to come alive.
public partial class BlockGrid
{
	public const float AirStonePerSecond = 10f;
	public const float WaterIcePerSecond = 10f;
	public const float SoilStonePerSecond = 5f;

	/// <summary>Set by the colony: the planet's air and seas are far enough along for seeds to take.</summary>
	public bool GardensCanGrow { get; set; }

	/// <summary>Set by the colony: false when this station doesn't stand on a planet (nothing to heal).</summary>
	public bool OnPlanet { get; set; } = true;

	private readonly Dictionary<Vital, float> _made = new();
	private float _terraformDraw;

	/// <summary>Kilograms turned into each vital since the last call; the colony passes them to the planet.</summary>
	public Dictionary<Vital, float> TakeTerraformed()
	{
		var made = new Dictionary<Vital, float>(_made);
		_made.Clear();
		return made;
	}

	private static (string Item, float PerSecond, Vital Vital)? TerraformRecipe(BlockKind kind) => kind switch
	{
		BlockKind.AirProcessor => ("stone", AirStonePerSecond, Vital.Air),
		BlockKind.Hydrator => ("ice", WaterIcePerSecond, Vital.Water),
		BlockKind.SeedGarden => ("stone", SoilStonePerSecond, Vital.Soil),
		_ => null,
	};

	private void UpdateTerraformers(float dt)
	{
		float draw = 0f;
		foreach (var (cell, block) in _blocks)
		{
			if (TerraformRecipe(block.Definition.Kind) is not { } recipe)
				continue;
			var input = _state[cell].Input!;
			if (!OnPlanet)
			{
				_machineStatus[cell] = "Needs to stand on a planet";
				continue;
			}
			if (recipe.Vital == Vital.Soil && !GardensCanGrow)
			{
				_machineStatus[cell] = "Waiting: air and water must reach 30%";
				continue;
			}
			float have = input.Get(recipe.Item);
			if (have <= 0f)
			{
				_machineStatus[cell] = $"Waiting for {Items.ItemCatalog.DisplayName(recipe.Item).ToLowerInvariant()}";
				continue;
			}
			float amount = Mathf.Min(have, recipe.PerSecond * PowerSatisfaction * dt);
			input.TryRemove(recipe.Item, amount);
			_made[recipe.Vital] = _made.GetValueOrDefault(recipe.Vital) + amount;
			draw += block.Definition.PowerDraw;
			_machineStatus[cell] = recipe.Vital switch
			{
				Vital.Air => "Making air",
				Vital.Water => "Filling the seas",
				_ => "Growing soil",
			} + (PowerSatisfaction < 0.999f ? " (low power)" : "");
		}
		_terraformDraw = draw;
	}

	/// <summary>Terraformer demand for the logistics network: fill the input with its raw material.</summary>
	private static string? TerraformInput(BlockKind kind) => TerraformRecipe(kind)?.Item;
}
