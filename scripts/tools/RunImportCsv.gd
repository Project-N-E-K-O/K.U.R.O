@tool
extends EditorScript

# 编辑器脚本：从 data/*.csv 批量导入回 .tres 资源
# 在 Godot 编辑器中右键此脚本 → Run

func _run() -> void:
	var importer = load("res://scripts/tools/ImportCsv.gd").new()
	importer.import_all()
