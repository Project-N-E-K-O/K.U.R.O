extends Node

# 这是一个 GDScript 桥接脚本，用于帮助 C# 操作 Spine 节点
# 因为 C# 绑定可能缺失，我们在 GDScript 中进行操作

var _flash_tokens: Dictionary = {}
var _flash_restore_colors: Dictionary = {}

func find_spine_node(root: Node) -> Node:
	if root.has_node("SpineCharacter"):
		return root.get_node("SpineCharacter")
	if root.has_node("SpineSprite"):
		return root.get_node("SpineSprite")
	# 尝试递归查找
	var found = root.find_child("SpineSprite", true, false)
	if found: return found
	return root.find_child("SpineCharacter", true, false)

func find_spine_nodes(root: Node) -> Array:
	var nodes: Array = []
	var primary = find_spine_node(root)
	if primary:
		nodes.append(primary)

	for candidate in root.find_children("OutlineSpineSprite*", "", true, false):
		if candidate and candidate != primary and candidate.has_method("get_animation_state"):
			nodes.append(candidate)

	return nodes

func flip_facing(root: Node, face_right: bool, default_face_left: bool) -> void:
	var sign_val = 1.0 if face_right else -1.0
	if default_face_left:
		sign_val *= -1.0

	for sprite in find_spine_nodes(root):
		sprite.scale.x = abs(sprite.scale.x) * sign_val

func flash_damage(root: Node, color: Color, restore_color = null, duration: float = 0.1) -> void:
	var sprite = find_spine_node(root)
	if not sprite:
		return

	var key = sprite.get_instance_id()
	var token = int(_flash_tokens.get(key, 0)) + 1
	_flash_tokens[key] = token

	var base_restore_color: Color
	if restore_color is Color:
		base_restore_color = restore_color
	elif _flash_restore_colors.has(key):
		base_restore_color = _flash_restore_colors[key]
	else:
		base_restore_color = sprite.modulate

	_flash_restore_colors[key] = base_restore_color

	var flash_color = color
	flash_color.a = base_restore_color.a
	sprite.modulate = flash_color
	
	var tween = create_tween()
	tween.tween_interval(maxf(duration, 0.0))
	tween.tween_callback(func(): 
		if not is_instance_valid(sprite):
			_flash_tokens.erase(key)
			_flash_restore_colors.erase(key)
			return
		if int(_flash_tokens.get(key, 0)) != token:
			return
		sprite.modulate = _flash_restore_colors.get(key, base_restore_color)
		_flash_tokens.erase(key)
		_flash_restore_colors.erase(key)
	)

# 动画控制相关
func play_animation(root: Node, anim_name: String, loop: bool, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprites = find_spine_nodes(root)
	if sprites.is_empty():
		print("[SpineWrapper] ERROR: 無法找到 SpineSprite 節點在: ", root)
		return false

	var played_any := false
	for sprite in sprites:
		var state = sprite.get_animation_state()
		if not state:
			continue
	
		var entry = state.set_animation(anim_name, loop)
		if entry:
			if entry.has_method("set_mix_duration"):
				entry.set_mix_duration(mix_duration)
			if entry.has_method("set_time_scale"):
				entry.set_time_scale(time_scale)
			played_any = true
	
	if played_any:
		return true

	print("[SpineWrapper] ERROR: set_animation() 返回 nil，動畫可能不存在或其他錯誤")
	var skeleton = sprites[0].get_skeleton()
	if skeleton and skeleton.has_method("get_animations"):
		var animations = skeleton.get_animations()
		print("[SpineWrapper] 可用的動畫列表: ", animations)
	return false

func add_animation(root: Node, anim_name: String, loop: bool, delay: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var played_any := false
	for sprite in find_spine_nodes(root):
		var state = sprite.get_animation_state()
		if not state:
			continue
	
		var entry = state.add_animation(anim_name, loop, delay)
		if entry:
			if entry.has_method("set_mix_duration"):
				entry.set_mix_duration(mix_duration)
			if entry.has_method("set_time_scale"):
				entry.set_time_scale(time_scale)
			if entry.has_method("set_event_threshold"):
				entry.set_event_threshold(1.0)
			played_any = true
	return played_any

func set_empty_animation(root: Node, track_index: int, mix_duration: float) -> bool:
	var cleared_any := false
	for sprite in find_spine_nodes(root):
		var state = sprite.get_animation_state()
		if not state:
			continue
	
		state.set_empty_animation(track_index, mix_duration)
		cleared_any = true
	return cleared_any

func play_partial_loop_animation(root: Node, anim_name: String, loop_start: float, loop_end: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	if loop_end <= loop_start:
		return false

	var played_any := false
	for sprite in find_spine_nodes(root):
		var state = sprite.get_animation_state()
		if not state:
			continue

		var entry = state.set_animation(anim_name, false)
		if not entry:
			continue

		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
		played_any = true

	return played_any

func play_partial_once_animation(root: Node, anim_name: String, part_start: float, part_end: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	if part_end <= part_start:
		return false

	var played_any := false
	for sprite in find_spine_nodes(root):
		var state = sprite.get_animation_state()
		if not state:
			continue

		var entry = state.set_animation(anim_name, false)
		if not entry:
			continue

		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
		if entry.has_method("set_track_time"):
			entry.set_track_time(part_start)
		if entry.has_method("set_track_last"):
			entry.set_track_last(part_start)
		if entry.has_method("set_track_end"):
			entry.set_track_end(part_end)
		played_any = true

	return played_any

func update_partial_loop_animation(root: Node, track_index: int, loop_start: float, loop_end: float) -> bool:
	if loop_end <= loop_start:
		return false

	var updated_any := false
	for sprite in find_spine_nodes(root):
		var state = sprite.get_animation_state()
		if not state or not state.has_method("get_current"):
			continue

		var entry = state.get_current(track_index)
		if not entry or not entry.has_method("get_track_time"):
			continue

		var track_time := float(entry.get_track_time())
		if track_time < loop_end:
			updated_any = true
			continue

		var loop_len = maxf(loop_end - loop_start, 0.0001)
		var wrapped_time = loop_start + fmod(maxf(track_time - loop_start, 0.0), loop_len)
		if not entry.has_method("set_track_time"):
			continue

		entry.set_track_time(wrapped_time)
		if entry.has_method("set_track_last"):
			entry.set_track_last(wrapped_time)
		updated_any = true

	return updated_any
