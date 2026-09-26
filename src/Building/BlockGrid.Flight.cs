using System.Collections.Generic;
using Godot;

namespace Driftworks.Building;

/// <summary>Pilot input, expressed in the controlling cockpit's frame (-Z forward, +Y up).</summary>
public struct ShipControls
{
	/// <summary>Desired thrust direction per axis, each in -1..1.</summary>
	public Vector3 Move;
	/// <summary>Desired angular velocity (rad/s) around cockpit X (pitch), Y (yaw), Z (roll).</summary>
	public Vector3 Rotate;
	public bool Dampeners;
}

// Flight model in the spirit of Space Engineers: thrust is applied through the center of mass
// (no thruster torque), dampeners cancel drift on every axis without input, and gyroscopes
// drive the angular velocity towards the pilot's request (zero when there is none).
public partial class BlockGrid
{
	public const float MaxSpeed = 100f;

	// Thrust capacity per grid-local direction, indexed by DirectionIndex.
	private readonly float[] _thrustCapacity = new float[6];
	private readonly float[] _throttle = new float[6];
	private readonly List<(Vector3I Cell, int Direction)> _thrusters = new();
	private float _gyroTorque;

	/// <summary>Controls applied every physics tick. Kept after the pilot leaves, so dampeners keep holding the ship.</summary>
	public ShipControls Controls { get; set; } = new() { Dampeners = true };

	/// <summary>Orientation (grid-local) of the cockpit the controls are relative to.</summary>
	public Basis ControlFrame { get; set; } = Basis.Identity;

	public float ThrustCapacity(Vector3I direction) => _thrustCapacity[DirectionIndex(direction)];
	public float GyroTorque => _gyroTorque;

	private static int DirectionIndex(Vector3I d) =>
		d.X != 0 ? (d.X > 0 ? 0 : 1) : d.Y != 0 ? (d.Y > 0 ? 2 : 3) : (d.Z > 0 ? 4 : 5);

	public static Vector3I DominantAxis(Vector3 v)
	{
		Vector3 a = v.Abs();
		if (a.X >= a.Y && a.X >= a.Z)
			return new Vector3I(Mathf.Sign(v.X), 0, 0);
		if (a.Y >= a.Z)
			return new Vector3I(0, Mathf.Sign(v.Y), 0);
		return new Vector3I(0, 0, Mathf.Sign(v.Z));
	}

	private void RebuildFlightCapabilities()
	{
		System.Array.Clear(_thrustCapacity);
		_thrusters.Clear();
		_gyroTorque = 0f;
		foreach (var (cell, block) in _blocks)
		{
			if (block.Definition.Thrust > 0f)
			{
				int dir = DirectionIndex(DominantAxis(block.Orientation * Vector3.Forward));
				_thrustCapacity[dir] += block.Definition.Thrust;
				_thrusters.Add((cell, dir));
			}
			_gyroTorque += block.Definition.Torque;
		}
	}

	public override void _IntegrateForces(PhysicsDirectBodyState3D state)
	{
		System.Array.Clear(_throttle);
		if (Freeze)
			return;

		float dt = state.Step;
		Basis toWorld = state.Transform.Basis;
		Basis toLocal = toWorld.Transposed();

		// Linear: per grid axis, use thrusters facing that way.
		Vector3 move = ControlFrame * Controls.Move;
		Vector3 localVelocity = toLocal * state.LinearVelocity;
		Vector3 force = Vector3.Zero;
		for (int axis = 0; axis < 3; axis++)
		{
			float positive = _thrustCapacity[axis * 2];
			float negative = _thrustCapacity[axis * 2 + 1];
			float f = 0f;
			if (Mathf.Abs(move[axis]) > 0.01f)
				f = move[axis] * (move[axis] > 0f ? positive : negative);
			else if (Controls.Dampeners)
				f = Mathf.Clamp(-localVelocity[axis] * Mass / dt, -negative, positive);
			force[axis] = f;
			if (f > 0f && positive > 0f)
				_throttle[axis * 2] = f / positive;
			else if (f < 0f && negative > 0f)
				_throttle[axis * 2 + 1] = -f / negative;
		}
		state.LinearVelocity = (state.LinearVelocity + toWorld * force * (dt / Mass)).LimitLength(MaxSpeed);

		// Angular: gyros apply at most _gyroTorque to reach the requested angular velocity.
		if (_gyroTorque > 0f)
		{
			Vector3 target = toWorld * (ControlFrame * Controls.Rotate);
			Basis inverseInertia = state.InverseInertiaTensor;
			Vector3 torque = (inverseInertia.Inverse() * (target - state.AngularVelocity) / dt).LimitLength(_gyroTorque);
			state.AngularVelocity += inverseInertia * torque * dt;
		}
	}

	public override void _Process(double delta)
	{
		foreach (var (cell, dir) in _thrusters)
		{
			if (_decorations.TryGetValue(cell, out var decoration) && decoration.GetNodeOrNull<Node3D>(BlockVisuals.FlameName) is { } flame)
			{
				float target = Mathf.Max(_throttle[dir] * 3f, 0.001f);
				flame.Scale = new Vector3(1, 1, Mathf.Lerp(flame.Scale.Z, target, 0.3f));
			}
		}
	}
}
