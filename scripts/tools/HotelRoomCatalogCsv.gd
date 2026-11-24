@tool
extends RefCounted

const CATALOG_PATH := "res://resources/stages/hotel_room_catalog.tres"
const CSV_PATH := "res://data/hotel_room_catalog.csv"
const COLUMNS := ["ScenePath", "FloorTheme", "RoomType", "RouteTags",
	"ElevatorPreviewName", "ElevatorPreviewIcon"]
const EMBEDDED_ICON := "@embedded"


static func export_rows() -> Array:
	var catalog := _load_catalog(CATALOG_PATH)
	if catalog == null:
		return []
	var rooms := _index_rooms(catalog)
	if rooms.is_empty():
		return []
	var types := _room_types(rooms.values()[0])
	var rows: Array = [COLUMNS]
	for path in rooms:
		var room: Resource = rooms[path]
		var type_name: Variant = types.find_key(room.get("RoomType"))
		if type_name == null:
			_error("%s 的 RoomType 无法导出" % path)
			return []
		var tags: PackedStringArray = room.get("RouteTags")
		for tag in tags:
			if "|" in tag:
				_error("%s 的路线标签含有分隔符 |" % path)
				return []
		var icon: Texture2D = room.get("ElevatorPreviewIcon")
		rows.append([path, room.get("FloorTheme"), type_name, "|".join(tags),
			room.get("ElevatorPreviewName"), _icon_path(icon)])
	return rows


static func import_rooms(csv_path: String = CSV_PATH, catalog_path: String = CATALOG_PATH) -> bool:
	var file := FileAccess.open(csv_path, FileAccess.READ)
	if file == null:
		return _error("无法读取 %s，请先导出房间表" % csv_path)
	var headers := file.get_csv_line()
	var columns := {}
	for index in headers.size():
		var header := headers[index].trim_prefix("\ufeff").strip_edges()
		if columns.has(header):
			return _error("CSV 表头重复：%s" % header)
		columns[header] = index
	for column in COLUMNS:
		if not columns.has(column):
			return _error("CSV 缺少列：%s" % column)

	var catalog := _load_catalog(catalog_path)
	if catalog == null:
		return false
	var rooms := _index_rooms(catalog)
	if rooms.is_empty():
		return false
	var types := _room_types(rooms.values()[0])
	if types.is_empty():
		return false
	var updates: Array[Dictionary] = []
	var seen := {}
	var row_number := 1
	while file.get_position() < file.get_length():
		var row := file.get_csv_line()
		row_number += 1
		if row.size() == 1 and row[0].strip_edges().is_empty():
			continue
		if row.size() != headers.size():
			return _error("第 %d 行列数与表头不符" % row_number)
		var path := _resource_path(row[columns["ScenePath"]])
		if not rooms.has(path):
			return _error("第 %d 行场景无效或未在目录登记：%s" % [row_number, row[columns["ScenePath"]]])
		if seen.has(path):
			return _error("第 %d 行重复引用房间：%s" % [row_number, path])
		seen[path] = true
		var theme := row[columns["FloorTheme"]].strip_edges()
		var type_name := row[columns["RoomType"]].strip_edges()
		if theme.is_empty() or not types.has(type_name):
			return _error("第 %d 行楼层主题为空或房间类型无效：%s" % [row_number, type_name])
		var tags := PackedStringArray()
		for tag in row[columns["RouteTags"]].split("|", false):
			var trimmed := tag.strip_edges()
			if not trimmed.is_empty():
				tags.append(trimmed)
		var room: Resource = rooms[path]
		var icon: Texture2D = null
		var icon_path := row[columns["ElevatorPreviewIcon"]].strip_edges()
		if icon_path == EMBEDDED_ICON:
			icon = room.get("ElevatorPreviewIcon")
			if _icon_path(icon) != EMBEDDED_ICON:
				return _error("第 %d 行没有可保留的内嵌图标" % row_number)
		elif not icon_path.is_empty():
			var resolved_icon := _resource_path(icon_path)
			if ResourceLoader.exists(resolved_icon, "Texture2D"):
				icon = ResourceLoader.load(resolved_icon, "Texture2D") as Texture2D
			if icon == null:
				return _error("第 %d 行图标无法加载为 Texture2D：%s" % [row_number, icon_path])
		var fields := {
			"FloorTheme": theme, "RoomType": types[type_name], "RouteTags": tags,
			"ElevatorPreviewName": row[columns["ElevatorPreviewName"]], "ElevatorPreviewIcon": icon,
		}
		for field in fields:
			if room.get(field) != fields[field]:
				updates.append({"room": room, "fields": fields})
				break

	if updates.is_empty():
		print("[HotelRoomCatalogCsv] 房间目录没有变动")
		return true
	for update in updates:
		for field in update.fields:
			update.room.set(field, update.fields[field])
	if not _save_catalog(catalog, catalog_path):
		return false
	print("[HotelRoomCatalogCsv] 已更新 %d 个房间" % updates.size())
	return true


static func _load_catalog(path: String) -> Resource:
	var catalog := ResourceLoader.load(path, "", ResourceLoader.CACHE_MODE_IGNORE)
	if catalog == null or catalog.get("Rooms") == null:
		_error("无法读取房间目录：%s" % path)
		return null
	return catalog


static func _index_rooms(catalog: Resource) -> Dictionary:
	var indexed := {}
	for room in catalog.get("Rooms"):
		var path := _resource_path(room.get("ScenePath")) if room != null else ""
		if path.is_empty() or not path.get_extension() in ["tscn", "scn"] \
				or not ResourceLoader.exists(path, "PackedScene"):
			_error("目录包含无效房间场景：%s" % path)
			return {}
		if indexed.has(path):
			_error("目录中有重复房间：%s" % path)
			return {}
		indexed[path] = room
	if indexed.is_empty():
		_error("房间目录为空")
	return indexed


static func _resource_path(value: String) -> String:
	var path := value.strip_edges()
	if path.begins_with("uid://"):
		var uid := ResourceUID.text_to_id(path)
		path = ResourceUID.get_id_path(uid) if ResourceUID.has_id(uid) else ""
	path = path.simplify_path()
	return path if path.begins_with("res://") else ""


static func _icon_path(icon: Texture2D) -> String:
	if icon == null:
		return ""
	var path := icon.resource_path
	return path if path.begins_with("res://") and not "::" in path else EMBEDDED_ICON


static func _room_types(room: Resource) -> Dictionary:
	# 读取 C# 导出的枚举提示，避免 CSV 维护第二套类型定义。
	for property in room.get_property_list():
		if property.name == "RoomType" and property.hint == PROPERTY_HINT_ENUM:
			var types := {}
			var value := 0
			var hint: String = property.hint_string
			for entry in hint.split(","):
				var parts := entry.split(":", false, 1)
				if parts.size() == 2:
					value = int(parts[1])
				types[parts[0]] = value
				value += 1
			return types
	_error("无法读取 RoomType 枚举")
	return {}


static func _save_catalog(catalog: Resource, path: String) -> bool:
	var temporary := "%s.csv-import-%d.tres" % [path.get_basename(), Time.get_ticks_usec()]
	var error := ResourceSaver.save(catalog, temporary)
	var uid := ResourceLoader.get_resource_uid(path)
	if error == OK and uid != ResourceUID.INVALID_ID:
		error = ResourceSaver.set_uid(temporary, uid)
	if error == OK:
		error = DirAccess.rename_absolute(temporary, path)
	if error == OK:
		return true
	if FileAccess.file_exists(temporary):
		DirAccess.remove_absolute(temporary)
	return _error("保存房间目录失败：%s (%s)" % [path, error_string(error)])


static func _error(message: String) -> bool:
	push_error("[HotelRoomCatalogCsv] " + message)
	return false
