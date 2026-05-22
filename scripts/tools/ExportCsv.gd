@tool
extends SceneTree

## ExportCsv.gd — 资源数据导出工具
## 将 resources/ 目录下的 .tres 文件导出为 data/*.csv
##
## 运行方式（由 run_export.ps1 调用）:
##   godot --headless --quit -s res://scripts/tools/ExportCsv.gd

# ── 源目录 ─────────────────────────────────────────────────────────────────────
const ITEMS_DIR  := "res://resources/items"
const SKILLS_DIR := "res://resources/items/skills"
const BUILDS_DIR := "res://resources/builds"
const LOOT_DIR   := "res://resources/loot"

# ── 输出路径 ───────────────────────────────────────────────────────────────────
const OUT_ITEMS  := "res://data/items.csv"
const OUT_BUILDS := "res://data/builds.csv"
const OUT_SKILLS := "res://data/skills.csv"
const OUT_LOOT   := "res://data/loot.csv"

# ══════════════════════════════════════════════════════════════════════════════
#  入口
# ══════════════════════════════════════════════════════════════════════════════

func _initialize() -> void:
	_run()
	quit()

func _run() -> void:
	print("[ExportCsv] Starting export...")
	_export_items()
	_export_builds()
	_export_skills()
	_export_loot()
	print("[ExportCsv] All exports completed.")
# ══════════════════════════════════════════════════════════════════════════════
#  ITEMS  (resources/items/*.tres  →  ItemDefinition)
# ══════════════════════════════════════════════════════════════════════════════

func _export_items() -> void:
	var files := _list_tres(ITEMS_DIR)
	if files.is_empty():
		push_warning("[ExportCsv] No .tres found in " + ITEMS_DIR)
		return

	var headers := [
		"file", "ItemId", "DisplayName", "Description", "Category", "Tags",
		"MaxStackSize", "BuildClass", "LevelCount", "IsThrowable", "IsFurniture",
		"attack_power", "MaxDurability", "DamagePerHit", "SkillRefs"
	]
	var rows: Array = [headers]

	for fpath_v in files:
		var fpath: String = str(fpath_v)
		var p := _parse_tres(fpath)
		if p.is_empty():
			continue
		var r: Dictionary   = p["resource"]
		var sub: Dictionary = p["sub_resources"]
		var ext: Dictionary = p["ext_resources"]

		# 从 AttributeEntries 中取 attack_power
		var atk: String = ""
		for sid in sub:
			if _str(str(sub[sid].get("AttributeId", ""))) == "attack_power":
				atk = str(sub[sid].get("Value", ""))
				break

		# DurabilityConfig
		var max_dur: String = ""
		var dmg_per: String = ""
		var dur_raw: String = str(r.get("DurabilityConfig", ""))
		if dur_raw.begins_with("SubResource"):
			var did: String = _subres_id(dur_raw)
			if did in sub:
				max_dur = str(sub[did].get("MaxDurability", ""))
				dmg_per = str(sub[did].get("DamagePerHit", ""))

		# WeaponSkillResources → 收集引用的技能文件名
		var skill_refs: String = ""
		var skill_raw: String = str(r.get("WeaponSkillResources", ""))
		if not skill_raw.is_empty():
			var ids := _extres_ids(skill_raw)
			var names: Array = []
			for id in ids:
				if id in ext:
					names.append(str(ext[id]["path"]).get_file().get_basename())
			skill_refs = "|".join(names)

		rows.append([
			fpath.get_file().get_basename(),
			_str(str(r.get("ItemId", ""))),
			_str(str(r.get("DisplayName", ""))),
			_str(str(r.get("Description", ""))),
			_str(str(r.get("Category", ""))),
			_arr_str(str(r.get("Tags", ""))),
			str(r.get("MaxStackSize", "1")),
			_str(str(r.get("BuildClass", ""))),
			str(r.get("LevelCount", "1")),
			str(r.get("IsThrowable", "false")),
			str(r.get("IsFurniture", "false")),
			atk, max_dur, dmg_per, skill_refs
		])

	_write_csv(OUT_ITEMS, rows)
	print("[ExportCsv] items.csv -> %d rows" % (rows.size() - 1))

# ══════════════════════════════════════════════════════════════════════════════
#  BUILDS  (resources/builds/*.tres  →  BuildLevelEffectEntry)
# ══════════════════════════════════════════════════════════════════════════════

func _export_builds() -> void:
	var files := _list_tres(BUILDS_DIR)
	if files.is_empty():
		push_warning("[ExportCsv] No .tres found in " + BUILDS_DIR)
		return

	var headers := [
		"file", "BuildId", "BuildClass", "BuildName", "Level", "RequiredPoints",
		"EffectId", "EffectScript", "Description"
	]
	var rows: Array = [headers]

	for fpath_v in files:
		var fpath: String = str(fpath_v)
		var p := _parse_tres(fpath)
		if p.is_empty():
			continue
		var r: Dictionary = p["resource"]
		rows.append([
			fpath.get_file().get_basename(),
			_str(str(r.get("BuildId", ""))),
			_str(str(r.get("BuildClass", ""))),
			_str(str(r.get("BuildName", ""))),
			str(r.get("Level", "1")),
			str(r.get("RequiredPoints", "1")),
			_str(str(r.get("EffectId", ""))),
			_str(str(r.get("EffectScript", ""))),
			_str(str(r.get("Description", "")))
		])

	_write_csv(OUT_BUILDS, rows)
	print("[ExportCsv] builds.csv -> %d rows" % (rows.size() - 1))

# ══════════════════════════════════════════════════════════════════════════════
#  SKILLS  (resources/items/skills/*.tres  →  WeaponSkillDefinition)
# ══════════════════════════════════════════════════════════════════════════════

func _export_skills() -> void:
	var files := _list_tres(SKILLS_DIR)
	if files.is_empty():
		push_warning("[ExportCsv] No .tres found in " + SKILLS_DIR)
		return

	var headers := [
		"file", "SkillId", "DisplayName", "AnimationName",
		"DamageMultiplier", "CooldownSeconds", "ShowHitboxDebug",
		"Description", "ActivationAction"
	]
	var rows: Array = [headers]

	for fpath_v in files:
		var fpath: String = str(fpath_v)
		var p := _parse_tres(fpath)
		if p.is_empty():
			continue
		var r: Dictionary = p["resource"]
		rows.append([
			fpath.get_file().get_basename(),
			_str(str(r.get("SkillId", ""))),
			_str(str(r.get("DisplayName", ""))),
			_str(str(r.get("AnimationName", ""))),
			str(r.get("DamageMultiplier", "1.0")),
			str(r.get("CooldownSeconds", "0.5")),
			str(r.get("ShowHitboxDebug", "true")),
			_str(str(r.get("Description", ""))),
			_str(str(r.get("ActivationAction", "")))
		])

	_write_csv(OUT_SKILLS, rows)
	print("[ExportCsv] skills.csv -> %d rows" % (rows.size() - 1))

# ══════════════════════════════════════════════════════════════════════════════
#  LOOT  (resources/loot/*.tres  →  LootDropTable，每个掉落条目一行)
# ══════════════════════════════════════════════════════════════════════════════

func _export_loot() -> void:
	var files := _list_tres(LOOT_DIR)
	if files.is_empty():
		push_warning("[ExportCsv] No .tres found in " + LOOT_DIR)
		return

	var headers := [
		"table_file", "SelectionMode", "GlobalDropChance", "MaxDrops",
		"ScatterRadius", "DefaultImpulse",
		"entry_index", "item_file", "DropChance", "MaxStacks",
		"ImpulseStrength", "ImpulseSpreadDegrees"
	]
	var rows: Array = [headers]

	for fpath_v in files:
		var fpath: String = str(fpath_v)
		var p := _parse_tres(fpath)
		if p.is_empty():
			continue
		var r: Dictionary   = p["resource"]
		var sub: Dictionary = p["sub_resources"]
		var ext: Dictionary = p["ext_resources"]

		var tname: String         = fpath.get_file().get_basename()
		var sel_mode: String      = str(r.get("SelectionMode", "1"))
		var global_chance: String = str(r.get("GlobalDropChance", "1.0"))
		var max_drops: String     = str(r.get("MaxDrops", "0"))
		var scatter: String       = str(r.get("ScatterRadius", "24.0"))
		var impulse: String       = str(r.get("DefaultImpulse", "0.0"))

		var entries_raw: String = str(r.get("Entries", ""))
		var entry_ids := _subres_ids(entries_raw)

		if entry_ids.is_empty():
			rows.append([tname, sel_mode, global_chance, max_drops, scatter, impulse,
						 "", "", "", "", "", ""])
			continue

		for i in entry_ids.size():
			var eid: String  = str(entry_ids[i])
			var entry: Dictionary = sub.get(eid, {}) as Dictionary

			var item_ref: String = str(entry.get("Item", ""))
			var item_file: String = ""
			if item_ref.begins_with("ExtResource"):
				var rid: String = _extres_id(item_ref)
				if rid in ext:
					item_file = str(ext[rid]["path"]).get_file().get_basename()

			rows.append([
				tname, sel_mode, global_chance, max_drops, scatter, impulse,
				str(i + 1), item_file,
				str(entry.get("DropChance", "")),
				str(entry.get("MaxStacks", "1")),
				str(entry.get("ImpulseStrength", "")),
				str(entry.get("ImpulseSpreadDegrees", ""))
			])

	_write_csv(OUT_LOOT, rows)
	print("[ExportCsv] loot.csv -> %d rows" % (rows.size() - 1))

# ══════════════════════════════════════════════════════════════════════════════
#  .tres 文本解析器
# ══════════════════════════════════════════════════════════════════════════════

## 解析一个 .tres 文件，返回：
##   result["script_class"]   : String
##   result["resource"]       : Dictionary { key -> value_string }
##   result["sub_resources"]  : Dictionary { id -> {key -> value_string} }
##   result["ext_resources"]  : Dictionary { id -> {path, type} }
func _parse_tres(path: String) -> Dictionary:
	var f := FileAccess.open(path, FileAccess.READ)
	if not f:
		push_error("[ExportCsv] Cannot read: " + path)
		return {}

	var result := {
		"script_class": "",
		"resource":      {},
		"sub_resources": {},
		"ext_resources": {}
	}

	var lines   := f.get_as_text().split("\n")
	f.close()

	var section := ""   # "res" | "sub" | ""
	var sub_id  := ""
	var cur_key := ""
	var cur_val := ""

	for raw_line in lines:
		var s := raw_line.strip_edges()

		# ── 区段标头 ──────────────────────────────────────────────────────────
		if s.begins_with("[") and s.ends_with("]"):
			if cur_key != "":
				_kv_store(result, section, sub_id, cur_key, cur_val)
			cur_key = ""; cur_val = ""

			var hdr := s.substr(1, s.length() - 2)

			if hdr.begins_with("gd_resource"):
				result["script_class"] = _hattr(hdr, "script_class")
				section = ""

			elif hdr.begins_with("ext_resource"):
				var eid := _hattr(hdr, "id")
				result["ext_resources"][eid] = {
					"path": _hattr(hdr, "path"),
					"type": _hattr(hdr, "type")
				}
				section = ""

			elif hdr.begins_with("sub_resource"):
				sub_id = _hattr(hdr, "id")
				result["sub_resources"][sub_id] = {}
				section = "sub"

			elif hdr == "resource":
				section = "res"
				sub_id = ""

			else:
				section = ""
			continue

		if section == "" or s.is_empty():
			continue

		# ── 键值对 ─────────────────────────────────────────────────────────────
		var eq := s.find(" = ")
		if eq >= 0:
			if cur_key != "":
				_kv_store(result, section, sub_id, cur_key, cur_val)
			cur_key = s.left(eq).strip_edges()
			cur_val = s.substr(eq + 3)
		elif cur_key != "":
			cur_val += "\n" + s   # 多行值续行

	if cur_key != "":
		_kv_store(result, section, sub_id, cur_key, cur_val)

	return result

func _kv_store(result: Dictionary, section: String, sid: String,
		key: String, val: String) -> void:
	match section:
		"res":
			result["resource"][key] = val.strip_edges()
		"sub":
			if sid in result["sub_resources"]:
				result["sub_resources"][sid][key] = val.strip_edges()

## 从区段标头中提取属性值，如 id="xxx" → "xxx"
## 搜索时加前缀空格，避免 uid="..." 被当作 id="..." 误匹配
func _hattr(header: String, attr: String) -> String:
	var search := " " + attr + "=\""
	var pos := header.find(search)
	if pos < 0:
		return ""
	pos += search.length()
	var end := header.find("\"", pos)
	return header.substr(pos, end - pos) if end >= 0 else header.substr(pos)

# ══════════════════════════════════════════════════════════════════════════════
#  值提取工具
# ══════════════════════════════════════════════════════════════════════════════

## 提取 "some text" → some text
func _str(raw: String) -> String:
	var s := raw.strip_edges()
	if s.length() >= 2 and s.begins_with("\"") and s.ends_with("\""):
		return s.substr(1, s.length() - 2)
	return s

## 提取 Array[String](["a","b"]) → "a|b"
func _arr_str(raw: String) -> String:
	var start := raw.find("([")
	var end   := raw.rfind("])")
	if start < 0 or end < 0:
		return ""
	var inner := raw.substr(start + 2, end - start - 2)
	var re := RegEx.new()
	re.compile('"([^"]*)"')
	var out: Array = []
	for m in re.search_all(inner):
		out.append(m.get_string(1))
	return "|".join(out)

## 提取 SubResource("id") → id（取第一个）
func _subres_id(raw: String) -> String:
	var re := RegEx.new()
	re.compile('SubResource\\("([^"]+)"\\)')
	var m := re.search(raw)
	return m.get_string(1) if m else ""

## 提取所有 SubResource("id")
func _subres_ids(raw: String) -> Array:
	var re := RegEx.new()
	re.compile('SubResource\\("([^"]+)"\\)')
	var out: Array = []
	for m in re.search_all(raw):
		out.append(m.get_string(1))
	return out

## 提取 ExtResource("id") → id（取第一个）
func _extres_id(raw: String) -> String:
	var re := RegEx.new()
	re.compile('ExtResource\\("([^"]+)"\\)')
	var m := re.search(raw)
	return m.get_string(1) if m else ""

## 提取所有 ExtResource("id")
func _extres_ids(raw: String) -> Array:
	var re := RegEx.new()
	re.compile('ExtResource\\("([^"]+)"\\)')
	var out: Array = []
	for m in re.search_all(raw):
		out.append(m.get_string(1))
	return out

# ══════════════════════════════════════════════════════════════════════════════
#  文件工具
# ══════════════════════════════════════════════════════════════════════════════

## 列出目录中所有 .tres 文件（非递归）
func _list_tres(dir_path: String) -> Array:
	var files: Array = []
	var dir := DirAccess.open(dir_path)
	if not dir:
		push_error("[ExportCsv] Cannot open dir: " + dir_path)
		return files
	dir.list_dir_begin()
	var name := dir.get_next()
	while name != "":
		if not dir.current_is_dir() and name.ends_with(".tres"):
			files.append(dir_path + "/" + name)
		name = dir.get_next()
	dir.list_dir_end()
	return files

## 将 rows 数组写入 CSV 文件（res:// 路径）
func _write_csv(res_path: String, rows: Array) -> void:
	var abs_path := ProjectSettings.globalize_path(res_path)
	var dir_path := abs_path.get_base_dir()
	if not DirAccess.dir_exists_absolute(dir_path):
		DirAccess.make_dir_recursive_absolute(dir_path)

	var content := ""
	for row in rows:
		var cells: Array = []
		for cell in row:
			cells.append(_csv_escape(str(cell)))
		content += ",".join(cells) + "\n"

	var f := FileAccess.open(abs_path, FileAccess.WRITE)
	if not f:
		push_error("[ExportCsv] Cannot write: " + abs_path)
		return
	f.store_string(content)
	f.close()

## CSV 转义：含逗号、引号、换行时加双引号包裹
func _csv_escape(s: String) -> String:
	if s.contains(",") or s.contains("\"") or s.contains("\n") or s.contains("\r"):
		return "\"" + s.replace("\"", "\"\"") + "\""
	return s
