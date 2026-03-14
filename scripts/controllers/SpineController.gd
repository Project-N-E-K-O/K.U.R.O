extends SpineSprite

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
