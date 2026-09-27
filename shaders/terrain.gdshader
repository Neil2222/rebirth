shader_type spatial;

// Soft cartoon rock. COLOR.rgb: material colour (linear). COLOR.a: faint self-light for ore patches.
// Big gentle colour mottling and a warm rim keep smooth surfaces from looking plain.
varying vec3 world_position;
varying vec3 world_normal;

// Healing planets: green spreads over the flatter ground as `bloom` (the planet's soil) rises.
uniform float bloom = 0.0;
uniform vec3 planet_center;
uniform vec3 grass_color : source_color = vec3(0.50, 0.66, 0.43);   // soft moss, not lime

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

float noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash13(i), hash13(i + vec3(1, 0, 0)), f.x), mix(hash13(i + vec3(0, 1, 0)), hash13(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash13(i + vec3(0, 0, 1)), hash13(i + vec3(1, 0, 1)), f.x), mix(hash13(i + vec3(0, 1, 1)), hash13(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = (MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz;
}

void fragment() {
	float mottle = noise3(world_position * 0.08) * 0.65 + noise3(world_position * 0.35) * 0.35;
	vec3 color = COLOR.rgb * (0.86 + 0.24 * mottle);
	if (bloom > 0.001) {
		vec3 up = world_position - planet_center;
		float up_length = length(up);
		up = up_length > 0.001 ? up / up_length : vec3(0.0, 1.0, 0.0);
		float normal_length = length(world_normal);
		vec3 n = normal_length > 0.0001 ? world_normal / normal_length : up;
		float level = smoothstep(0.55, 0.85, dot(n, up));
		float patches = noise3(world_position * 0.05) * 0.7 + noise3(world_position * 0.22) * 0.3;
		float grow = smoothstep(patches - 0.06, patches + 0.06, bloom * 1.1) * level;
		color = mix(color, grass_color * (0.78 + 0.26 * mottle), grow);
	}
	ALBEDO = color;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	RIM = 0.35;
	RIM_TINT = 0.6;
	EMISSION = color * COLOR.a;
}
