using Godot;

namespace Kuros.Controllers
{
	/// <summary>
	/// 挂在 <b>SpineBoneNode 的子节点</b>上的锚点（炮口 marker、出膛点、判定点等）：补回 SpineBoneNode 丢掉的**骨链反射**。
	///
	/// 背景：这套骨架的 d_* 姿态是"手绘的镜像孪生"，镜像机制是骨骼数据里 `bone2.scale.y = -1`
	/// （整条骨链相对 bone2 的 X 轴做反射）。而 spine-godot 的 SpineBoneNode 只把骨骼的**位置与旋转**搬到节点上，
	/// 它的基是纯旋转（行列式恒正）→ 反射被丢掉了：**与骨链垂直的那一维方向反了**（本骨架里是骨骼局部 Y）。
	/// 子节点照原偏移摆，就会落到镜像的错误一侧——表现就是"转身进 d_attack 后，炮口 marker 还留在原来的位置"。
	///
	/// 本脚本每帧读骨骼的**世界矩阵**（get_a/get_b/get_c/get_d，行列式 &lt; 0 = 骨链被反射），
	/// 反射时把自己局部位置的 Y 取负。数学上：骨骼真实变换 = 节点变换 ∘ diag(1,-1)，
	/// 所以骨骼坐标系里的 (x, y) 在节点坐标系里就是 (x, -y) —— 两种朝向下锚点都落在骨骼坐标系的同一点。
	///
	/// 用法：把锚点拖成 SpineBoneNode 的子节点，在编辑器（setup 姿态）里对着美术摆好即可；X 永不改动，只在镜像姿态翻 Y。
	/// **不要再给它套 <see cref="ShadowFollower"/>**——那是"相对基线的位移增量"跟随，既不含旋转也不含镜像，
	/// 姿态一大（镜像姿态的位移上千像素）就会跑飞。
	///
	/// 继承 <see cref="Marker2D"/> 而不是 Node2D：攻击模板是用 `GetNodeOrNull&lt;Marker2D&gt;` 找锚点的，
	/// 而 C# 类型查找比对的是**托管实例类型**——脚本基类不是 Marker2D 的话，调用方拿到的就是 null，
	/// 特效会静默回退到敌人原点生成。
	/// </summary>
	public partial class SpineBoneAnchor : Marker2D
	{
		[Export] public bool EnableDebugLogs { get; set; } = false;

		private Vector2 _basePosition;
		private Node2D? _boneNode;
		private GodotObject? _bone;   // SpineBone（RefCounted，解析一次缓存）
		private bool _reflected;

		/// <summary>当前姿态下骨链是否被反射（d_* 姿态为 true；turn 在片中切换）。</summary>
		public bool IsReflected => _reflected;

		public override void _Ready()
		{
			_basePosition = Position;
			_boneNode = GetParent() as Node2D;
			if (_boneNode == null || _boneNode.GetParent() == null)
				GD.PushWarning($"{Name}: 必须挂在 SpineBoneNode 下（父 = 骨骼节点，祖父 = SpineSprite），否则不会跟随");
		}

		public override void _PhysicsProcess(double delta)
		{
			if (_boneNode == null || !GodotObject.IsInstanceValid(_boneNode)) return;

			if (_bone == null || !GodotObject.IsInstanceValid(_bone)) ResolveBone();
			if (_bone == null) return;

			float a = _bone.Call("get_a").AsSingle();
			float b = _bone.Call("get_b").AsSingle();
			float c = _bone.Call("get_c").AsSingle();
			float d = _bone.Call("get_d").AsSingle();
			_reflected = a * d - b * c < 0f;

			var wanted = new Vector2(_basePosition.X, _reflected ? -_basePosition.Y : _basePosition.Y);
			if (Position == wanted) return;

			Position = wanted;
			if (EnableDebugLogs)
			{
				string state = _reflected ? "镜像 → 局部 Y 取负" : "未镜像 → 恢复原始偏移";
				GD.Print($"{Name}: {state}（local={wanted}）");
			}
		}

		/// <summary>解析 SpineBone：父节点（SpineBoneNode）拿 bone_name，祖父（SpineSprite）拿 skeleton。
		/// 解析失败只警告一次（骨骼导入/改名时会走到这里）。</summary>
		private void ResolveBone()
		{
			var spine = _boneNode?.GetParent();
			string boneName = _boneNode?.Get("bone_name").AsString() ?? string.Empty;
			if (spine == null || string.IsNullOrEmpty(boneName))
			{
				GD.PushWarning($"{Name}: 解析不到骨骼（父={_boneNode?.Name}，bone_name='{boneName}'）");
				_bone = null;
				return;
			}

			var skeleton = spine.Call("get_skeleton");
			var bone = skeleton.VariantType == Variant.Type.Nil
				? default
				: skeleton.AsGodotObject()?.Call("find_bone", boneName) ?? default;
			if (bone.VariantType == Variant.Type.Nil)
			{
				GD.PushWarning($"{Name}: 骨骼 '{boneName}' 不存在");
				_bone = null;
				return;
			}

			_bone = bone.AsGodotObject();
		}
	}
}
