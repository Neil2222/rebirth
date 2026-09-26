shader_type spatial;

// Soft cartoon rock. COLOR.rgb: material colour (linear). COLOR.a: faint self-light for ore patches.
// Big gentle colour mottling and a warm rim keep smooth surfaces from looking plain.
varying vec3 world_position;

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
}

void fragment() {
	float mottle = noise3(world_position * 0.08) * 0.65 + noise3(world_position * 0.35) * 0.35;
	vec3 color = COLOR.rgb * (0.86 + 0.24 * mottle);
	ALBEDO = color;
	ROUGHNESS = 0.9;
	SPECULAR = 0.2;
	RIM = 0.35;
	RIM_TINT = 0.6;
	EMISSION = color * COLOR.a;
}
