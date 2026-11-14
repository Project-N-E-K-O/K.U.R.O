shader_type canvas_item;

uniform float softness = 0.5;
uniform float alpha = 0.4;

varying float canvas_alpha;

void vertex() {
    // 节点及父级透明度；保持原有程序化阴影形状，不额外采样贴图 Alpha。
    canvas_alpha = COLOR.a;
}

void fragment() {

    vec2 uv = UV * 2.0 - 1.0;

    float dist = length(uv);

    float shadow = smoothstep(1.0, softness, dist);

    COLOR = vec4(0.0, 0.0, 0.0, shadow * alpha * canvas_alpha);
}
