@tool
extends RefCounted

class_name ImportCsv

# 从 data/*.csv 读取数据并回写对应的 .tres 资源文件
# 入口：RunImportCsv.gd 中调用 import_all()

const ITEMS_CSV_PATH  = "res://data/items.csv"
const SKILLS_CSV_PATH = "res://data/skills.csv"
const LOOT_CSV_PATH   = "res://data/loot.csv"
const CHARACTERS_CSV_PATH = "res://data/characters.csv"

const ITEMS_DIR  = "res://resources/items/"
const SKILLS_DIR = "res://resources/items/skills/"
const LOOT_DIR   = "res://resources/loot/"
const CHARACTERS_DIR = "res://scenes/actors/characters/"

var _log := CsvLogger.new()

# ── 主入口 ────────────────────────────────────────────────────────────────────
func import_all() -> void:
	_log.info("=== 开始批量导入 CSV → .tres ===")
	import_skills_from_csv()
	import_items_from_csv()
	import_loot_from_csv()
	import_characters_from_csv()
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
		# 按列名映射，缺失列/空值跳过；新增字段与 ExportCsv 的技能列同步。
		_s_str(res, "SkillId",          row, hm, "SkillId")
		_s_str(res, "DisplayName",      row, hm, "DisplayName")
		_s_int(res, "SkillType",        row, hm, "SkillType")
		_s_str(res, "AnimationName",    row, hm, "AnimationName")
		_s_float(res, "DamageMultiplier", row, hm, "DamageMultiplier")
		_s_float_neg1(res, "DashDamageMultiplier",       row, hm)
		_s_float(res, "CooldownSeconds",  row, hm, "CooldownSeconds")
		_s_bool(res,  "ShowHitboxDebug",  row, hm, "ShowHitboxDebug")
		_s_str(res, "Description",      row, hm, "Description")
		_s_str(res, "ActivationAction", row, hm, "ActivationAction")
		_s_bool(res, "AllowHoldContinuousAttack", row, hm, "AllowHoldContinuousAttack")
		_s_float_neg1(res, "WarmupDuration",         row, hm)
		_s_float_neg1(res, "ActiveDuration",         row, hm)
		_s_float_neg1(res, "RecoveryDuration",       row, hm)
		_s_float(res, "WarmupAnimationSpeed",    row, hm, "WarmupAnimationSpeed")
		_s_float(res, "ActiveAnimationSpeed",    row, hm, "ActiveAnimationSpeed")
		_s_float(res, "RecoveryAnimationSpeed",  row, hm, "RecoveryAnimationSpeed")
		_s_str(res, "DashAnimationName",         row, hm, "DashAnimationName")
		_s_float_neg1(res, "DashWarmupDuration",        row, hm)
		_s_float_neg1(res, "DashActiveDuration",        row, hm)
		_s_float_neg1(res, "DashRecoveryDuration",      row, hm)
		_s_float_neg1(res, "DashWarmupAnimationSpeed",  row, hm)
		_s_float_neg1(res, "DashActiveAnimationSpeed",  row, hm)
		_s_float_neg1(res, "DashRecoveryAnimationSpeed", row, hm)
		_s_int(res,   "DashAttackSpeedSource",       row, hm, "DashAttackSpeedSource")
		_s_float_neg1(res, "DashAttackFixedSpeed",       row, hm)
		_s_float_neg1(res, "DashAttackSpeedMultiplier",  row, hm)
		_s_int(res,   "DashAttackDecayWindow",       row, hm, "DashAttackDecayWindow")
		_s_int(res,   "AttackFallImmunities", row, hm, "AttackFallImmunities")
		_s_int(res,   "DashAttackFallImmunities", row, hm, "DashAttackFallImmunities")
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
		_s_str(res,   "Category",    row, hm, "Category")
		_s_str(res,   "DisplayName", row, hm, "DisplayName")
		_s_str(res,   "Description", row, hm, "Description")
		_s_str_array(res, "Tags", row, hm)
		# _s_str(res,   "BuildClass",  row, hm, "BuildClass")  # build 重做后已失效
		_s_int(res,   "MaxStackSize", row, hm, "MaxStackSize")
		_s_bool(res,  "IsThrowable",  row, hm, "IsThrowable")
		_s_bool(res,  "IsThrowWeapon", row, hm, "IsThrowWeapon")
		_s_bool(res,  "PreventDropDuringCooldown", row, hm, "PreventDropDuringCooldown")
		_s_bool(res,  "SpawnEffectOnThrow", row, hm, "SpawnEffectOnThrow")
		_s_vec2(res,  "ThrowStartOffset", row, hm, "ThrowStartOffset")
		_s_float(res, "ThrowParabolicDuration",      row, hm, "ThrowParabolicDuration")
		_s_float(res, "ThrowParabolicPeakHeight",    row, hm, "ThrowParabolicPeakHeight")
		_s_float(res, "ThrowHorizontalDistance",     row, hm, "ThrowHorizontalDistance")
		_s_float(res, "ThrowParabolicLandingYOffset", row, hm, "ThrowParabolicLandingYOffset")
		_s_float(res, "ThrowWeaponCooldown", row, hm, "ThrowWeaponCooldown")
		_s_float(res, "UnpickedLifetime", row, hm, "UnpickedLifetime")
		_s_int(res,   "ThrowTier", row, hm, "ThrowTier")
		# attack_power 存于 AttributeEntries 子资源
		var atk = _col(row, hm, "attack_power")
		if atk != "": _set_attribute(res, "attack_power", float(atk))
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

# ── CHARACTERS ────────────────────────────────────────────────────────────────────
func import_characters_from_csv() -> void:
	_log.info("--- [characters] ---")
	var rows = _read_csv(CHARACTERS_CSV_PATH)
	if rows.is_empty(): return
	var hm = _hmap(rows[0])
	var count = 0
	for i in range(1, rows.size()):
		var row = rows[i]
		var fname = _col(row, hm, "file")
		if fname == "": continue
		var path = "%s%s.tscn" % [CHARACTERS_DIR, fname]
		var content = _read_text(path)
		if content == "": continue
		content = _set_tscn_root_prop(content, "Speed", _float_literal(_col(row, hm, "Speed")))
		content = _set_tscn_root_prop(content, "AttackDamage", _float_literal(_col(row, hm, "AttackDamage")))
		content = _set_tscn_root_prop(content, "AttackCooldown", _float_literal(_col(row, hm, "AttackCooldown")))
		content = _set_tscn_root_prop(content, "MaxHealth", _col(row, hm, "MaxHealth"))
		content = _set_tscn_root_prop(content, "AiDescription", _col(row, hm, "AiDescription"), true)
		if _write_text(path, content): count += 1
	_log.info("  → 更新 %d 个" % count)

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
	res.set(prop, v)

## 回写 `|` 分隔的字符串数组（对应导出 _arr_str 的 "a|b|c" 格式，如 Tags）
func _s_str_array(res: Resource, prop: String, row: Array, hm: Dictionary) -> void:
	var raw := _col(row, hm, prop)
	if raw == "": return
	var out: Array = []
	for t in raw.split("|", false):
		out.append(t.strip_edges())
	res.set(prop, out)

func _s_float(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v != "": res.set(prop, float(v))

func _s_int(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v != "": res.set(prop, int(v))

func _s_bool(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col).to_lower()
	if v != "": res.set(prop, v == "true" or v == "1" or v == "yes")


func _s_vec2(res: Resource, prop: String, row: Array, hm: Dictionary, col: String) -> void:
	var v = _col(row, hm, col)
	if v == "": return
	var parts = v.split("|")
	if parts.size() != 2: return
	res.set(prop, Vector2(float(parts[0]), float(parts[1])))
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

# ── 文本文件工具（用于 .tscn 场景文件） ────────────────────────────────────────
func _read_text(path: String) -> String:
	var f = FileAccess.open(path, FileAccess.READ)
	if f == null:
		_log.warn("无法打开：%s" % path)
		return ""
	var content = f.get_as_text()
	f.close()
	return content

func _write_text(path: String, content: String) -> bool:
	var f = FileAccess.open(path, FileAccess.WRITE)
	if f == null:
		_log.error("无法写入：%s" % path)
		return false
	f.store_string(content)
	f.close()
	_log.info("已更新：%s" % path.get_file())
	return true

## 数值属性强制写成 float 字面量（400 → 400.0）：
## C# [Export] float 属性（Speed/AttackDamage/AttackCooldown）若在 .tscn 中存为 int 字面量，
## 运行时加载会隐式转换所以游戏里数值正常，但编辑器编译后重载场景的类型检查更严格，
## int 值会被拒并回落 C# 默认值（100/1/0）——Inspector 显示默认值、保存时还会把原行丢掉。
func _float_literal(v: String) -> String:
	if v == "": return ""
	return v if v.contains(".") else v + ".0"

## 在 .tscn 文本中设置根节点的属性值。
## quote = true 时按字符串格式写入（tscn 字符串必须带双引号，内部引号/反斜杠转义）——
## 用于 AiDescription 等字符串属性；数值属性保持裸写。
func _set_tscn_root_prop(content: String, prop: String, value: String, quote: bool = false) -> String:
	if value == "":
		return content

	var final_value: String = value
	if quote:
		# 只转义双引号；反斜杠保留原样——tscn 的转义序列（如 \n）经 CSV 往返后语义不变
		final_value = "\"" + value.replace("\"", "\\\"") + "\""

	var lines: Array = content.split("\n")
	var in_root: bool = false
	var prop_found: bool = false
	var new_lines: Array = []

	for i in range(lines.size()):
		var s: String = lines[i]
		if s.begins_with("[") and s.ends_with("]"):
			in_root = s.begins_with("[node ") and not "parent=" in s
			new_lines.append(lines[i])
			continue
		if in_root and not prop_found:
			var eq: int = s.find(" = ")
			if eq >= 0:
				var key: String = s.left(eq).strip_edges()
				if key == prop:
					var indent: String = ""
					for c in lines[i]:
						if c == "\t":
							indent += "\t"
						else:
							break
					new_lines.append(indent + prop + " = " + final_value)
					prop_found = true
					continue
		new_lines.append(lines[i])

	if not prop_found:
		var out: Array = []
		in_root = false
		for i in range(new_lines.size()):
			var s: String = new_lines[i]
			if s.begins_with("[") and s.ends_with("]"):
				if in_root:
					out.append(prop + " = " + final_value)
				in_root = s.begins_with("[node ") and not "parent=" in s
			out.append(new_lines[i])
		return "\n".join(out)

	return "\n".join(new_lines)
class CsvLogger:
	func info(msg: String) -> void:
		print("[ImportCsv] %s" % msg)
	func warn(msg: String) -> void:
		push_warning("[ImportCsv] %s" % msg)
	func error(msg: String) -> void:
		push_error("[ImportCsv] %s" % msg)
