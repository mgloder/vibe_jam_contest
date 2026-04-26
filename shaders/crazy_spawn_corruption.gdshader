// Blood “spring” on the ground: gushes from the civilian’s cell center, pools ~5 fine cells across; mix blend reads as a stain, not a glow.
shader_type canvas_item;
render_mode unshaded, blend_mix;

uniform float progress : hint_range(0.0, 1.0) = 0.0;

// Cheap hash for splatter
float h21(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment() {
	// Faux ground: wide shallow puddle, origin = civilian’s cell center; stretched Y reads like a board tile seen flat
	vec2 p = (UV - vec2(0.5)) * 2.0;
	p.x *= 1.05;
	p.y *= 0.48;
	float t = progress;
	// Welling from center, spread over time
	float gush_r = 0.12 + t * 0.88;
	float d = length(p);
	// Puddle: soft ink + noise edge
	float n = h21(floor(p * 18.0 + t * 2.0)) * 0.5 + h21(floor(p * 31.0)) * 0.3;
	float pool = (1.0 - smoothstep(0.0, gush_r, d * (0.9 + n * 0.15)));
	pool *= (0.88 + 0.12 * n);
	// “Spring” arcs: short radial jets at early time
	float ang = atan(p.y, p.x);
	float rays = 0.5 + 0.5 * sin(ang * 7.0 + t * 10.0);
	float jet = (1.0 - smoothstep(0.0, 0.35, d)) * rays * (1.0 - t) * 0.45;
	// Drips toward bottom (gravity) — subtle streaks
	float drop = 0.0;
	float gy = p.y;
	if (gy > 0.0) {
		drop = (1.0 - smoothstep(0.0, 0.55, abs(p.x) + p.y * 0.2)) * step(0.15, p.y) * 0.35 * (1.0 - t);
	}
	float body = clamp(pool * 0.9 + jet + drop, 0.0, 1.0);
	// Color: capillary bright → clotted deep
	vec3 cap = mix(vec3(0.5, 0.06, 0.08), vec3(0.85, 0.12, 0.1), 0.3 + 0.4 * (1.0 - d / max(gush_r, 0.01)));
	vec3 deep = mix(vec3(0.1, 0.0, 0.01), vec3(0.35, 0.02, 0.04), 0.6);
	vec3 col = mix(deep, cap, body) * 0.95;
	float alpha = body * 0.78 * (1.0 - 0.5 * t);
	// Darken edge so it sits “on” the ground
	alpha *= (0.55 + 0.45 * pool);
	COLOR = vec4(col, clamp(alpha, 0.0, 0.9));
}
