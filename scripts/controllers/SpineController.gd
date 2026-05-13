extends SpineSprite

# 2026/3/18新增
# 定義一個信號，讓父節點或其他腳本可以輕鬆監聽
signal hit_received(hit_step: int, animation_name: String)

# 2026/5/12新增：特效事件信號
signal effect_triggered(animation_name: String, event_time: float)

var _current_animation_name: String = ""
var _hit_sequence: int = 0

func _ready() -> void:
	# 在初始化時連結 Spine 原生的事件信號
	self.animation_event.connect(_on_animation_event)

## 處理 Spine 事件
func _on_animation_event(_sprite: SpineSprite, _anim_state: SpineAnimationState, track_entry: SpineTrackEntry, event: SpineEvent):
	var event_name = event.get_data().get_event_name()
	var anim_name = track_entry.get_animation().get_name()
	
	if event_name == "hit":
		var raw_hit_step = event.get_int_value()

		if anim_name != _current_animation_name:
			_current_animation_name = anim_name
			_hit_sequence = 0

		_hit_sequence += 1

		# Spine 事件的 int 值不会自动递增；若未在 Spine 中手动填写，则回退为脚本内自增序号
		var hit_step = raw_hit_step if raw_hit_step != 0 else _hit_sequence
		
		# 發出我們自定義的信號
		hit_received.emit(hit_step, anim_name)

		# 打印調試資訊
		print("[Spine Event] 觸發 hit: ", anim_name, " 原始值: ", raw_hit_step, " 自增段數: ", _hit_sequence, " 輸出段數: ", hit_step)
	
	elif event_name == "effect":
		# 處理特效事件
		var event_time = track_entry.get_track_time()
		effect_triggered.emit(anim_name, event_time)
		## print("[Spine Event] 觸發 effect: ", anim_name, " 時間: ", event_time)

## 播放动画
## anim: 动画名称
## loop: 是否循环播放
## mix_duration: 动画混合时长（默认 0.1 秒）
## time_scale: 时间缩放/播放速度（默认 1.0）
func play(anim: String, loop := true, mix_duration := 0.1, time_scale := 1.0):
	var state = get_animation_state()
	if not state:
		return null

	_current_animation_name = anim
	_hit_sequence = 0
	
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
