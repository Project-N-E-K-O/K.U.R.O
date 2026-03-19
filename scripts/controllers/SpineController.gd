extends SpineSprite

# 2026/3/18新增
# 定義一個信號，讓父節點或其他腳本可以輕鬆監聽
signal hit_received(hit_step: int, animation_name: String)

func _ready() -> void:
	# 在初始化時連結 Spine 原生的事件信號
	self.animation_event.connect(_on_animation_event)

## 處理 Spine 事件
func _on_animation_event(_sprite: SpineSprite, _anim_state: SpineAnimationState, track_entry: SpineTrackEntry, event: SpineEvent):
	if event.get_data().get_event_name() == "hit":
		var hit_step = event.get_int_value()
		var anim_name = track_entry.get_animation().get_name()
		
		# 發出我們自定義的信號
		hit_received.emit(hit_step, anim_name)

		# 打印調試資訊
		print("[Spine Event] 觸發 hit: ", anim_name, " 段數: ", hit_step)

## 播放动画
## anim: 动画名称
## loop: 是否循环播放
## mix_duration: 动画混合时长（默认 0.1 秒）
## time_scale: 时间缩放/播放速度（默认 1.0）
func play(anim: String, loop := true, mix_duration := 0.1, time_scale := 1.0):
	var state = get_animation_state()
	if not state:
		return null
	
	var entry = state.set_animation(anim, loop)
	if entry:
		# 设置混合时长
		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		else:
			entry.mix_duration = mix_duration
		
		# 设置时间缩放
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
		else:
			entry.time_scale = time_scale
	
	return entry

## 获取当前的 AnimationState
func get_state():
	return get_animation_state()
