shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;

// The Lance's ball of gathered light: a white-hot heart fading to soft blue at the rim, with slow
// swirls moving over it. `brightness` above 1 makes it bloom.
uniform float brightness = 1.0;

void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float swirl = 0.5 + 0.5 * sin(TIME * 1.7 + UV.x * 37.0 + sin(UV.y * 12.0 + TIME) * 3.0);
	vec3 heart = vec3(1.0, 1.0, 0.98);
	vec3 rim = vec3(0.55, 0.75, 1.0);
	ALBEDO = mix(rim, heart, pow(facing, 1.2)) * (0.85 + 0.25 * swirl) * brightness;
}
