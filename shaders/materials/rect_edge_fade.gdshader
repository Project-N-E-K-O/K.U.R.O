shader_type canvas_item;
render_mode unshaded;

// Each width is a fraction of the rectangle's width or height. Zero disables that edge.
uniform float fade_left : hint_range(0.0, 1.0, 0.01) = 0.0;
uniform float fade_right : hint_range(0.0, 1.0, 0.01) = 0.25;
uniform float fade_top : hint_range(0.0, 1.0, 0.01) = 0.0;
uniform float fade_bottom : hint_range(0.0, 1.0, 0.01) = 0.0;

float edge_alpha(float distance_to_edge, float fade_width) {
    if (fade_width <= 0.0) {
        return 1.0;
    }
    return smoothstep(0.0, fade_width, distance_to_edge);
}

void fragment() {
    float horizontal_alpha = edge_alpha(UV.x, fade_left)
        * edge_alpha(1.0 - UV.x, fade_right);
    float vertical_alpha = edge_alpha(UV.y, fade_top)
        * edge_alpha(1.0 - UV.y, fade_bottom);
    COLOR.a *= horizontal_alpha * vertical_alpha;
}
