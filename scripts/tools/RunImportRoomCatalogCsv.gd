@tool
extends EditorScript

## 在 Godot 脚本编辑器中运行，只回写房间目录。
func _run() -> void:
	ImportCsv.new().import_room_catalog_from_csv()
