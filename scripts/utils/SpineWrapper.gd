extends Node

# 这是一个 GDScript 桥接脚本，用于帮助 C# 操作 Spine 节点
# 因为 C# 绑定可能缺失，我们在 GDScript 中进行操作

func find_spine_node(root: Node) -> Node:
	if root.has_node("SpineCharacter"):
		return root.get_node("SpineCharacter")
	if root.has_node("SpineSprite"):
		return root.get_node("SpineSprite")
	# 尝试递归查找
	var found = root.find_child("SpineSprite", true, false)
	if found: return found
	return root.find_child("SpineCharacter", true, false)

func flip_facing(root: Node, face_right: bool, default_face_left: bool) -> void:
	var sprite = find_spine_node(root)
	if sprite:
		var sign_val = 1.0 if face_right else -1.0
		if default_face_left: sign_val *= -1.0
		sprite.scale.x = abs(sprite.scale.x) * sign_val

func flash_damage(root: Node, color: Color) -> void:
	var sprite = find_spine_node(root)
	if sprite:
		var original_modulate = sprite.modulate
		sprite.modulate = color
		
		var tween = create_tween()
		tween.tween_interval(0.1)
		tween.tween_callback(func(): 
			if is_instance_valid(sprite):
				sprite.modulate = original_modulate
		)

# 动画控制相关
func play_animation(root: Node, anim_name: String, loop: bool, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite: return false
	
	var state = sprite.get_animation_state()
	if not state: return false
	
	var entry = state.set_animation(anim_name, loop)
	if entry:
		# Use setter methods instead of direct property assignment
		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
	return true

func add_animation(root: Node, anim_name: String, loop: bool, delay: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite: return false
	
	var state = sprite.get_animation_state()
	if not state: return false
	
	var entry = state.add_animation(anim_name, loop, delay)
	if entry:
		# Use setter methods instead of direct property assignment
		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
	return true

func set_empty_animation(root: Node, track_index: int, mix_duration: float) -> bool:
	var sprite = find_spine_node(root)
	if not sprite: return false
	
	var state = sprite.get_animation_state()
	if not state: return false
	
	state.set_empty_animation(track_index, mix_duration)
	return true
