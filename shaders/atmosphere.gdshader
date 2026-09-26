shader_type spatial;
render_mode unshaded, blend_add, cull_back, depth_draw_never, shadows_disabled;

// Thin atmosphere seen from space: a soft haze around the rim that fades towards the planet's face.
uniform vec3 tint : source_color = vec3(1.0, 0.72, 0.52); // warm dusty haze
uniform float strength = 0.7;
// The haze is a from-space effect: it fades out as the camera comes down to the surface.
uniform float fade_start = 1.6;   // camera distance in haze radii where fading begins
uniform float fade_end = 1.15;    // ... and where the haze is gone
uniform float haze_radius = 1.0;

void fragment() {
	float rim = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float camera_distance = length(CAMERA_POSITION_WORLD - NODE_POSITION_WORLD) / haze_radius;
	float fade = smoothstep(fade_end, fade_start, camera_distance);
	ALBEDO = tint * pow(rim, 3.0) * strength * fade;
}
