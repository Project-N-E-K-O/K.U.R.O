@tool
extends StyleBox

@export var fill := Color(0.98, 0.96, 0.88)
@export var ink := Color(0.055, 0.05, 0.06)
@export_range(0.0, 10.0, 0.5) var stroke_width := 3.0
@export_range(0.0, 5.0, 0.1) var wobble := 1.1
@export_range(0.0, 8.0, 0.5) var inset := 0.0
@export var shadow := false

func _draw(canvas_item: RID, rect: Rect2) -> void:
	var padding := stroke_width * 0.5 + wobble + inset + 1.0
	if shadow:
		padding += 2.0
	var shape := rect.grow(-padding)
	if shape.size.x < 2.0 or shape.size.y < 2.0:
		return

	var radius := minf(9.0, minf(shape.size.x, shape.size.y) * 0.23)
	var corners: Array[Vector2] = [
		Vector2(shape.position.x + radius, shape.position.y + radius),
		Vector2(shape.end.x - radius, shape.position.y + radius),
		Vector2(shape.end.x - radius, shape.end.y - radius),
		Vector2(shape.position.x + radius, shape.end.y - radius),
	]
	var normals: Array[Vector2] = [Vector2.UP, Vector2.RIGHT, Vector2.DOWN, Vector2.LEFT]
	var outline: Array[Vector2] = []
	var amplitude := minf(wobble, minf(shape.size.x, shape.size.y) * 0.08)
	for side in range(4):
		var next := (side + 1) % 4
		var direction := normals[side]
		var edge_start := corners[side] + direction * radius
		var edge_end := corners[next] + direction * radius
		var segments := clampi(ceili(edge_start.distance_to(edge_end) / 21.0), 3, 72)
		for segment in range(segments):
			var t := float(segment) / segments
			var wave := sin(t * segments * 1.21 + side * 2.6) + 0.4 * sin(t * segments * 2.73 + side)
			outline.append(edge_start.lerp(edge_end, t) + direction * wave * amplitude * sin(PI * t))
		for segment in range(4):
			var angle := direction.angle() + float(segment) * PI / 8.0
			outline.append(corners[next] + Vector2.from_angle(angle) * radius)

	var polygon := PackedVector2Array(outline)
	if shadow:
		var offset := Transform2D(0.0, Vector2(3.0, 4.0))
		RenderingServer.canvas_item_add_polygon(canvas_item, offset * polygon, PackedColorArray([Color(0.18, 0.25, 0.30, 0.23)]))
	if fill.a > 0.0:
		RenderingServer.canvas_item_add_polygon(canvas_item, polygon, PackedColorArray([fill]))
	if stroke_width > 0.0:
		polygon.append(outline[0])
		RenderingServer.canvas_item_add_polyline(canvas_item, polygon, PackedColorArray([ink]), stroke_width, true)
