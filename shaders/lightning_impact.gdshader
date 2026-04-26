// Ground impact bloom where the strike hits.
shader_type canvas_item;
render_mode unshaded, blend_add;

uniform float strength : hint_range(0.0, 2.0) = 1.0;

void fragment() {
	vec2 c = UV - vec2(0.5, 0.5);
	float r = length(c);
	// Elongate slightly to feel like a ground flash
	c.y *= 0.65;
	r = length(c);
	float a = (1.0 - smoothstep(0.12, 0.48, r)) * strength;
	vec3 col = mix(vec3(0.3, 0.55, 1.0), vec3(0.95, 0.95, 1.0), 1.0 - r * 1.2);
	COLOR = vec4(col, a);
}
