shader_type spatial;

// Chunky painted toy blocks: rounded edges, a soft groove where blocks meet, and a little
// paint grain so surfaces feel made rather than rendered.
//   COLOR.rgb  paint (linear)        UV   0..1 across each face (tangent = +U, binormal = +V)
//   UV2.x      block health 0..1     UV2.y  per-block random, so grain differs per block
uniform float bevel = 0.22;      // fraction of a face that curls over the edge
uniform float groove = 0.02;     // width of the dark seam between neighbouring blocks

float hash(vec2 p) {
	return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
		mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

void fragment() {
	vec2 d = min(UV, 1.0 - UV);
	vec2 side = sign(UV - 0.5);

	// Rounded edges: flat most of the way, then the normal curls outwards towards each edge.
	vec2 curl = 1.0 - smoothstep(0.0, bevel, d);
	curl *= curl;
	NORMAL_MAP = normalize(vec3(side * curl * 2.2, 1.0)) * 0.5 + 0.5;
	// A soft sheen where the edge rounds over, like light catching moulded plastic.
	float sheen = max(curl.x, curl.y);

	float seam = 1.0 - smoothstep(0.0, groove, min(d.x, d.y));
	float grain = 0.5 * value_noise(UV * 40.0 + UV2.y * 91.0) + 0.5 * value_noise(UV * 9.0 + UV2.y * 13.0);
	vec3 paint = COLOR.rgb * (0.93 + 0.1 * grain);

	// Damage shows as sooty scorch patches that spread as health drops.
	float health = UV2.x;
	float soot = smoothstep(health + 0.15, health - 0.05, value_noise(UV * 5.0 + UV2.y * 7.0));

	paint = mix(paint, paint * 1.18 + 0.03, sheen * 0.35);
	ALBEDO = mix(paint, paint * 0.22, soot * 0.85) * (1.0 - 0.5 * seam);
	ROUGHNESS = mix(0.45, 0.85, soot);
	SPECULAR = 0.45;
}
