extends SpineSprite

# 2026/3/18新增
# 定義一個信號，讓父節點或其他腳本可以輕鬆監聽
signal hit_received(hit_step: int, animation_name: String)

# 2026/5/12新增：特效事件信號
signal effect_triggered(animation_name: String, event_time: float)

var _current_animation_name: String = ""
var _hit_sequence: int = 0

# 描边精灵（OutlineSpineSprite*，主精灵的视觉镜像）不处理动画事件：
# 主精灵 + 4 描边共 5 个 SpineController 实例，若不跳过会导致 hit 事件被处理 5 次
# （5 倍日志噪音、各自 _hit_sequence 从 0 计数、重复 emit 信号）
var _is_outline: bool = false

func _ready() -> void:
	# 在初始化時連結 Spine 原生的事件信號
	self.animation_event.connect(_on_animation_event)
	_is_outline = name.begins_with("OutlineSpineSprite")

## 處理 Spine 事件
func _on_animation_event(_sprite: SpineSprite, _anim_state: SpineAnimationState, track_entry: SpineTrackEntry, event: SpineEvent):
	if _is_outline:
		return

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

		# 調試資訊：默認注釋不輸出（高频攻击（如长按连段）下同步写 stdout 会造成卡顿）。
		# 需要檢查 hit 事件時取消下方注释：
		## print("[Spine Event] 觸發 hit: ", anim_name, " 原始值: ", raw_hit_step, " 自增段數: ", _hit_sequence, " 輸出段數: ", hit_step)
	
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

## 从指定时间点播放动画（跳帧）
## start_time: 动画起始时间（秒），播放后立即跳到此时间点继续
func play_from(anim: String, start_time: float, loop := true, mix_duration := 0.1, time_scale := 1.0):
	var entry = play(anim, loop, mix_duration, time_scale)
	if entry and start_time > 0.0:
		if entry.has_method("set_track_time"):
			entry.set_track_time(start_time)
		else:
			entry.track_time = start_time
	return entry

## 当前动画的播放时间（秒）——供切换动画时保持时间轴位置（hit 帧不因切换丢失）
func get_track_time() -> float:
	var state = get_animation_state()
	if not state:
		return 0.0
	var entry = state.get_current(0)
	if entry:
		if entry.has_method("get_track_time"):
			return entry.get_track_time()
		return entry.track_time
	return 0.0

## 动态修改当前正在播放动画的时间缩放，无需重启动画。
func change_time_scale(time_scale: float):
	var state = get_animation_state()
	if not state:
		return
	var entry = state.get_current(0)
	if entry:
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
		else:
			entry.time_scale = time_scale

## 获取当前的 AnimationState
func get_state():
	return get_animation_state()
