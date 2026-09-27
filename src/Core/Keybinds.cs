using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;

namespace Rebirth.Core;

/// <summary>
/// Every input action with its default key, the player's own choices on top (user://settings.json,
/// shared by all save slots), and the key names shown in hints ("{use}" → "F").
/// </summary>
public static class Keybinds
{
	private const string SettingsPath = "user://settings.json";

	/// <summary>Actions the player may rebind, in the order shown, with a friendly name.</summary>
	public static readonly (string Action, string Name)[] Rebindable =
	[
		("move_forward", "Forward"), ("move_back", "Back"), ("move_left", "Left"), ("move_right", "Right"),
		("move_up", "Up / jump"), ("move_down", "Down"), ("roll_left", "Roll left"), ("roll_right", "Roll right"),
		("sprint", "Sprint"), ("toggle_jetpack", "Jetpack on/off"), ("toggle_dampeners", "Dampeners on/off"),
		("toggle_light", "Light on/off"), ("use", "Use / interact"), ("primary_action", "Place / drill"),
		("secondary_action", "Remove block"), ("rotate_block_yaw", "Turn block"), ("rotate_block_pitch", "Tip block"),
		("cycle_shape", "Next block shape"),
		("toolbar_page", "Next hotbar page"), ("toolbar_page_back", "Previous hotbar page"),
		("open_inventory", "Inventory"), ("open_nexus", "Nexus"), ("open_forge", "Forge"),
		("toggle_view", "First / third person"), ("free_look", "Look around (hold)"),
		("toggle_grid_static", "Station / ship toggle"), ("toggle_creative", "Creative mode"),
		("quick_save", "Save"), ("quick_load", "Reload save"), ("release_mouse", "Menu / back"),
		("slot_1", "Hotbar 1"), ("slot_2", "Hotbar 2"), ("slot_3", "Hotbar 3"), ("slot_4", "Hotbar 4"), ("slot_5", "Hotbar 5"),
		("slot_6", "Hotbar 6"), ("slot_7", "Hotbar 7"), ("slot_8", "Hotbar 8"), ("slot_9", "Hotbar 9"), ("slot_0", "Hotbar 0"),
	];

	private static readonly Dictionary<string, InputEvent> Defaults = new();

	/// <summary>Registers the defaults, then applies the player's saved choices.</summary>
	public static void Setup()
	{
		Default("move_forward", Key.W);
		Default("move_back", Key.S);
		Default("move_left", Key.A);
		Default("move_right", Key.D);
		Default("move_up", Key.Space);
		Default("move_down", Key.C);
		Default("roll_left", Key.Q);
		Default("roll_right", Key.E);
		Default("toggle_dampeners", Key.Z);
		Default("toggle_jetpack", Key.X);
		Default("release_mouse", Key.Escape);
		Default("use", Key.F);
		Default("toggle_grid_static", Key.K);
		Default("rotate_block_yaw", Key.R);
		Default("rotate_block_pitch", Key.T);
		Default("cycle_shape", Key.G);
		Default("toggle_creative", Key.F2);
		Default("toggle_light", Key.L);
		Default("sprint", Key.Shift);
		Default("open_forge", Key.B);
		Default("open_nexus", Key.N);
		Default("open_inventory", Key.Tab);
		Default("toggle_view", Key.V);
		Default("toolbar_page", new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown });
		Default("toolbar_page_back", new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp });
		Default("free_look", Key.Alt);
		Default("quick_save", Key.F5);
		Default("quick_load", Key.F9);
		for (int slot = 0; slot <= 9; slot++)
			Default($"slot_{slot}", Key.Key0 + slot);
		Default("primary_action", new InputEventMouseButton { ButtonIndex = MouseButton.Left });
		Default("secondary_action", new InputEventMouseButton { ButtonIndex = MouseButton.Right });

		foreach (var (action, ev) in Defaults)
			Set(action, ev);
		Load();
	}

	private static void Default(string action, Key key) => Default(action, new InputEventKey { PhysicalKeycode = key });

	private static void Default(string action, InputEvent ev)
	{
		Defaults[action] = ev;
		if (!InputMap.HasAction(action))
			InputMap.AddAction(action);
	}

	public static InputEvent? Current(string action) =>
		InputMap.HasAction(action) ? InputMap.ActionGetEvents(action).FirstOrDefault() : null;

	private static void Set(string action, InputEvent ev)
	{
		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, ev);
	}

	/// <summary>
	/// Binds <paramref name="action"/> to <paramref name="ev"/>. An action that already used that key gets
	/// this action's old key instead, so nothing is left without a key. Returns that other action, if any.
	/// </summary>
	public static string? Rebind(string action, InputEvent ev)
	{
		var old = Current(action);
		string? swapped = null;
		foreach (var (other, _) in Rebindable)
		{
			if (other == action || Current(other) is not { } theirs || !Same(theirs, ev))
				continue;
			if (old is not null)
				Set(other, old);
			swapped = other;
		}
		Set(action, ev);
		Save();
		return swapped;
	}

	public static void ResetAll()
	{
		foreach (var (action, ev) in Defaults)
			Set(action, ev);
		Save();
	}

	private static bool Same(InputEvent a, InputEvent b) => Describe(a) == Describe(b);

	// ------------------------------------------------------------ names

	/// <summary>How a key reads in hints: "F", "Tab", "LMB", "Wheel down".</summary>
	public static string Label(string action) => Current(action) is { } ev ? NameOf(ev) : "?";

	public static string NameOf(InputEvent ev) => ev switch
	{
		InputEventKey key => OS.GetKeycodeString(key.PhysicalKeycode != Key.None ? DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode) : key.Keycode),
		InputEventMouseButton { ButtonIndex: MouseButton.Left } => "LMB",
		InputEventMouseButton { ButtonIndex: MouseButton.Right } => "RMB",
		InputEventMouseButton { ButtonIndex: MouseButton.Middle } => "MMB",
		InputEventMouseButton { ButtonIndex: MouseButton.WheelUp } => "Wheel up",
		InputEventMouseButton { ButtonIndex: MouseButton.WheelDown } => "Wheel down",
		InputEventMouseButton mouse => $"Mouse {(int)mouse.ButtonIndex}",
		_ => "?",
	};

	/// <summary>Replaces {action} tokens in <paramref name="text"/> with the keys currently bound.</summary>
	public static string Fill(string text) =>
		Regex.Replace(text, @"\{([a-z_0-9]+)\}", m => InputMap.HasAction(m.Groups[1].Value) ? Label(m.Groups[1].Value) : m.Value);

	// ------------------------------------------------------------ storage

	private static string Describe(InputEvent ev) => ev switch
	{
		InputEventKey key => $"key:{(int)(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode)}",
		InputEventMouseButton mouse => $"mouse:{(int)mouse.ButtonIndex}",
		_ => "",
	};

	private static InputEvent? Parse(string text)
	{
		var parts = text.Split(':');
		if (parts.Length != 2 || !int.TryParse(parts[1], out int code))
			return null;
		return parts[0] switch
		{
			"key" => new InputEventKey { PhysicalKeycode = (Key)code },
			"mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)code },
			_ => null,
		};
	}

	private static void Save()
	{
		var changed = Rebindable.Where(r => Current(r.Action) is { } ev && Defaults.TryGetValue(r.Action, out var d) && !Same(ev, d))
			.ToDictionary(r => r.Action, r => Describe(Current(r.Action)!));
		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
		file?.StoreString(JsonSerializer.Serialize(new Dictionary<string, object> { ["bindings"] = changed }));
	}

	private static void Load()
	{
		if (!FileAccess.FileExists(SettingsPath))
			return;
		try
		{
			using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(SettingsPath));
			if (!doc.RootElement.TryGetProperty("bindings", out var bindings))
				return;
			foreach (var entry in bindings.EnumerateObject())
				if (InputMap.HasAction(entry.Name) && Parse(entry.Value.GetString() ?? "") is { } ev)
					Set(entry.Name, ev);
		}
		catch (JsonException e)
		{
			GD.PushWarning($"Ignoring unreadable settings: {e.Message}");
		}
	}
}
