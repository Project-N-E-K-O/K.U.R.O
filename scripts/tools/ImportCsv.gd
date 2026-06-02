@tool
extends RefCounted

class_name ImportCsv

# 从 data/*.csv 读取数据并回写对应的 .tres 资源文件
# 入口：RunImportCsv.gd 中调用 import_all()

const ITEMS_CSV_PATH  = "res://data/items.csv"
const SKILLS_CSV_PATH = "res://data/skills.csv"
const BUILDS_CSV_PATH = "res://data/builds.csv"
const LOOT_CSV_PATH   = "res://data/loot.csv"

const ITEMS_DIR  = "res://resources/items/"
const SKILLS_DIR = "res://resources/items/skills/"
const BUILDS_DIR = "res://resources/builds/"
const LOOT_DIR   = "res://resources/loot/"

var _log := CsvLogger.new()

# ── 主入口 ────────────────────────────────────────────────────────────────────
func import_all() -> void:
	_log.info("=== 开始批量导入 CSV → .tres ===")
	import_skills_from_csv()
	import_items_from_csv()
	import_builds_from_csv()
	import_loot_from_csv()
	_log.info("=== 全部导入完成 ===")

# ── SKILLS ────────────────────────────────────────────────────────────────────
func import_skills_from_csv() -> void:
	_log.info("--- [skills] ---")
	var rows = _read_csv(SKILLS_CSV_PATH)
	if rows.is_empty(): return
	var hm = _hmap(rows[0])
	var count = 0
	for i in range(1, rows.size()):
		var row = rows[i]
		var fname = _col(row, hm, "file")
		if fname == "": continue
		var path = "%s%s.tres" % [SKILLS_DIR, fname]
		var res = _load(path)
		if res == null: continue
		_s_str(res, "SkillId",          row, hm, "SkillId")
		_s_str(res, "DisplayName",      row, hm, "DisplayName")
		_s_str(res, "AnimationName",    row, hm, "AnimationName")
		_s_float(res, "DamageMultiplier", row, hm, "DamageMultiplier")
		_s_float(res, "CooldownSeconds",  row, hm, "CooldownSeconds")
		_s_bool(res,  "ShowHitboxDebug",  row, hm, "ShowHitboxDebug")
		_s_str(res, "Description",      row, hm, "Description")
		_s_str(res, "ActivationAction", row, hm, "ActivationAction")
		_s_float_neg1(res, "WarmupDuration",         row, hm)
		_s_float_neg1(res, "ActiveDuration",         row, hm)
		_s_float_neg1(res, "RecoveryDuration",       row, hm)
		_s_float_neg1(res, "AttackCooldownDuration",  row, hm)
		if _save(res, path): count += 1
	_log.info("  → 更新 %d 个" % count)

# ── ITEMS ─────────────────────────────────────────────────────────────────────
func import_items_from_csv() -> void:
	_log.info("--- [items] ---")
	var rows = _read_csv(ITEMS_CSV_PATH)
	if rows.is_empty(): return
	var hm = _hmap(rows[0])
	var count = 0
	for i in range(1, rows.size()):
		var row = rows[i]
		var fname = _col(row, hm, "file")
		if fname == "": continue
		var path = "%s%s.tres" % [ITEMS_DIR, fname]
		var res = _load(path)
		if res == null: continue
		_s_str(res,   "ItemId",      row, hm, "ItemId")
		_s_str(res,   "DisplayName", row, hm, "DisplayName")
		_s_str(res,   "Description", row, hm, "Description")
		_s_str(res,   "BuildClass",  row, hm, "BuildClass")
		_s_int(res,   "MaxStackSize", row, hm, "MaxStackSize")
		_s_bool(res,  "IsThrowable",  row, hm, "IsThrowable")
		_s_bool(res,  "IsFurniture",  row, hm, "IsFurniture")
		# attack_power 存于 AttributeEntries 子资源
		var atk = _col(row, hm, "attack_power")
		if atk != "": _set_attribute(res, "attack_power", float(atk))
		if _save(res, path): count += 1
	_log.info("  → 更新 %d 个" % count)

# ── BUILDS ────────────────────────────────────────────────────────────────────
func import_builds_from_csv() -> void:
	_log.info("--- [builds] ---")
	var rows = _read_csv(BUILDS_CSV_PATH)
	if rows.is_empty(): return
	var hm = _hmap(rows[0])
	var count = 0
	for i in range(1, rows.size()):
		var row = rows[i]
		var fname = _col(row, hm, "file")
		if fname == "": continue
		var path = "%s%s.tres" % [BUILDS_DIR, fname]
		var res = _load(path)
		if res == null: continue
		_s_str(res, "BuildId",        row, hm, "BuildId")
		_s_str(res, "BuildClass",     row, hm, "BuildClass")
		_s_str(res, "BuildName",      row, hm, "BuildName")
		_s_int(res, "Level",          row, hm, "Level")
		_s_int(res, "RequiredPoints", row, hm, "RequiredPoints")
		_s_str(res, "EffectId",       row, hm, "EffectId")
		_s_str(res, "EffectScript",   row, hm, "EffectScript")
		_s_str(res, "Description",    row, hm, "Description")
		if _save(res, path): count += 1
	_log.info("  → 更新 %d 个" % count)

# ── LOOT ──────────────────────────────────────────────────────────────────────
# CSV 结构：每行对应一个 LootDropTable 中的一条 Entry
# table_file 相同的行属于同一张表（表级字段取首行）
func import_loot_from_csv() -> void:
	_log.info("--- [loot] ---")
	var rows = _read_csv(LOOT_CSV_PATH)
	if rows.is_empty(): return
	var hm = _hmap(rows[0])

	# 按 table_file 分组
	var groups: Dictionary = {}
	for i in range(1, rows.size()):
		var row = rows[i]
		var tname = _col(row, hm, "table_file")
		if tname == "": continue
		if not groups.has(tname):
			groups[tname] = []
		groups[tname].append(row)

	var count = 0
	for tname in groups:
		var path = "%s%s.tres" % [LOOT_DIR, tname]
		var table = _load(path)
		if table == null: continue

		# 表级字段（用首行）
		var first = groups[tname][0]
		_s_int(table,   "MaxDrops",       first, hm, "MaxDrops")
		_s_float(table, "ScatterRadius",  first, hm, "ScatterRadius")
		_s_float(table, "DefaultImpulse", first, hm, "DefaultImpulse")
		_s_float(table, "GlobalDropChance", first, hm, "GlobalDropChance")
		var sel = _col(first, hm, "SelectionMode")
		if sel != "": table.set("SelectionMode", int(sel))

		# 条目级字段（按 entry_index，1-based）
		var entries = table.get("Entries")
		if entries == null or entries.size() == 0:
			_log.warn("%s 的 Entries 为空" % tname)
		else:
			for row in groups[tname]:
				var idx_str = _col(row, hm, "entry_index")
				if idx_str == "": continue
				var idx = int(idx_str) - 1  # 转为 0-based
				if idx < 0 or idx >= entries.size():
					_log.warn("%s entry_index %d 越界（共 %d 条）" % [tname, idx + 1, entries.size()])
					continue
				var entry = entries[idx]
				_s_float(entry, "DropChance",          row, hm, "DropChance")
				_s_int(entry,   "MaxStacks",           row, hm, "MaxStacks")
				_s_float(entry, "ImpulseStrength",     row, hm, "ImpulseStrength")
				_s_float(entry, "ImpulseSpreadDegrees", row, hm, "ImpulseSpreadDegrees")

		if _save(table, path): count += 1
	_log.info("  → 更新 %d 个表" % count)

# ── 工具函数 ──────────────────────────────────────────────────────────────────
func _read_csv(path: String) -> Array:
	var f = FileAccess.open(path, FileAccess.READ)
	if f == null:
		_log.error("无法打开：%s" % path)
		return []
	var rows: Array = []
	while not f.eof_reached():
		var line = f.get_csv_line(",")
		if line.size() > 0 and line[0].strip_edges() != "":
			rows.append(line)
	return rows

func _hmap(header_row: Array) -> Dictionary:
	var m = {}
	for i in range(header_row.size()):
		m[header_row[i].strip_edges()] = i
	return m

func _col(row: Array, hm: Dictionary, key: String) -> String:
	var idx = hm.get(key, -1)
	if idx < 0 or idx >= row.size(): return ""
	return row[idx].strip_edges()

func _load(path: String) -> Resource:
	if not ResourceLoader.exists(path):
		_log.warn("找不到：%s" % path)
		return null
	return ResourceLoader.load(path)

func _save(res: Resource, path: String) -> bool:
	var err = ResourceSaver.save(res, path)
	if err != OK:
		_log.error("保存失败 %s（错误码 %d）" % [path, err])
		return false
	_log.info("已更新：%s" % path.get_file())
	return true

func _s_str(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v != "": res.set(prop, v)

func _s_float(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v != "": res.set(prop, float(v))

func _s_int(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v != "": res.set(prop, int(v))

func _s_bool(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col).to_lower()
	if v != "": res.set(prop, v == "true" or v == "1" or v == "yes")

func _s_float_neg1(res: Resource, prop: String, row: Array, hm: Dictionary) -> void:
	var v = _col(row, hm, prop)
	if v != "":
		var f = float(v)
		res.set(prop, f if f >= 0.0 else -1.0)

# 更新 AttributeEntries 中指定 AttributeId 的 Value
func _set_attribute(res: Resource, attr_id: String, value: float) -> void:
	var entries = res.get("AttributeEntries")
	if entries == null: return
	for entry in entries:
		if entry.get("AttributeId") == attr_id:
			entry.set("Value", value)
			return

class CsvLogger:
	func info(msg: String) -> void:
		print("[ImportCsv] %s" % msg)
	func warn(msg: String) -> void:
		push_warning("[ImportCsv] %s" % msg)
	func error(msg: String) -> void:
		push_error("[ImportCsv] %s" % msg)
