// Procedural jagged sky-to-ground bolt; add blend for glow.
shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float flash : hint_range(0.0, 3.0) = 1.0;

void fragment() {
	vec2 uv = UV;
	// Jagged path toward center: multiple sine layers for "electric" motion
	float j = sin(uv.y * 55.0 + TIME * 45.0) * 0.1;
	j += sin(uv.y * 24.0 + TIME * 22.0) * 0.07;
	j += sin(uv.y * 88.0 - TIME * 30.0) * 0.04;
	float d = abs(uv.x - 0.5 + j);
	float core = 1.0 - smoothstep(0.0, 0.06, d);
	float glow = 1.0 - smoothstep(0.0, 0.2, d);
	// Slightly brighter at bottom (impact end)
	float foot = 1.0 - uv.y;
	glow += foot * 0.35;
	vec3 c = mix(vec3(0.15, 0.35, 0.95), vec3(0.92, 0.96, 1.0), core);
	COLOR = vec4(c, (core * 0.95 + glow * 0.45) * flash);
}
