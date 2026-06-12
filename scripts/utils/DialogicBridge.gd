extends Node

## C# 无法直接调用 Engine.set_meta，通过此桥接清理 Dialogic 跨场景持久化数据。
func clear_dialogic_persistent_info():
	Engine.set_meta("dialogic_persistent_style_info", {})
