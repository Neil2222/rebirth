namespace Rebirth.Core;

/// <summary>
/// A string hash that is the same on every run. .NET randomises <c>string.GetHashCode</c> per process,
/// so seeding looks from it made houses, swarms and icons change every time the game started.
/// </summary>
public static class StableHash
{
	/// <summary>32-bit FNV-1a of the string's characters.</summary>
	public static int Of(string text)
	{
		uint hash = 2166136261;
		foreach (char c in text)
		{
			hash ^= c;
			hash *= 16777619;
		}
		return unchecked((int)hash);
	}
}
