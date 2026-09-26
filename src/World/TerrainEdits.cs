namespace Rebirth.World;

/// <summary>
/// Terrain whose procedural base is regenerated from its seed; only the changes (mined points)
/// need saving. Points are lattice coordinates flattened as x, y, z triples.
/// </summary>
public interface IEditableTerrain
{
	/// <summary>Stable id used to match saved edits to this terrain.</summary>
	string TerrainId { get; }
	(int[] Points, float[] Densities) ExportEdits();
	void ImportEdits(int[] points, float[] densities);
}
