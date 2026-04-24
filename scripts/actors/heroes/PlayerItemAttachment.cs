using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Utils;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 负责把最近拾取的物品视觉挂在角色骨骼上，用于反馈当前持有物品。
    /// </summary>
    public partial class PlayerItemAttachment : Node
    {
        [Signal] public delegate void EquippedAttackAreaChangedEventHandler();

        [Export] public PlayerInventoryComponent? Inventory { get; set; }
        [Export] public NodePath AttachmentParentPath { get; set; } = new("SpineCharacter/Skeleton2D");
        [Export] public NodePath SpineSlotNodePath { get; set; } = new();
        [Export] public Godot.Collections.Array<NodePath> SpineBoneNodePaths { get; set; } = new();
        [Export] public Godot.Collections.Array<string> SpineBoneOrder { get; set; } = new() { "WQ1", "WQ2", "WP" };
        [Export] public Godot.Collections.Array<string> ShowHoldingItemStates { get; set; } = new() { "Idle", "Walk", "Run", "Hit" };
        [Export] public bool FlipBoneAttachmentWithFacing { get; set; } = false;
        [Export(PropertyHint.Range, "-1024,1024,1")] public Vector2 BoneIconOffset { get; set; } = Vector2.Zero;
        [Export] public bool RotateBoneOffsetWithBone { get; set; } = false;
        [Export(PropertyHint.Range, "-512,512,1")] public Vector2 IconOffset { get; set; } = new Vector2(32, -32);
        [Export(PropertyHint.Range, "-360,360,0.1")] public float IconRotationDegrees { get; set; } = 0f;
        [Export] public bool FlipWithFacing { get; set; } = true;
        [Export] public int ZIndex { get; set; } = 0;
        [Export] public string SpineSlotDefaultSelection { get; set; } = string.Empty;

        private Node2D? _attachmentParent;
        private Sprite2D? _iconSprite;
        private GameActor? _actor;
        private Node? _spineSlotNode;
        private Node? _SpineSlotIconContainer;
        private string? _previousSlotSelection;
        private readonly List<Node2D> _spineBoneNodes = new();
        private Node2D? _activeBoneNode;
        private bool _iconUsesBoneTracking;
        private bool _useSlotAnchorBinding;
        private Area2D? _equippedAttackArea;
        private Transform2D _equippedAttackAreaLocalTransform = Transform2D.Identity;
        private Shape2D? _equippedAttackShapeTemplate;
        private Transform2D _equippedAttackShapeTransform = Transform2D.Identity;
        private uint _equippedAttackCollisionMask = 1u;
        private const string SlotSelectProperty = "切换名";
        private const string SlotIconName = "HeldItemSlotIcon";

        public override void _Ready()
        {
            _actor = GetParent() as GameActor ?? GetOwner() as GameActor;
            Inventory ??= _actor?.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            if (Inventory == null)
            {
                GD.PushWarning($"{Name}: 未找到 PlayerInventoryComponent，无法显示物品附件。");
                return;
            }

            _attachmentParent = ResolveAttachmentParent();
            _spineSlotNode = ResolveSpineSlotNode();
            ResolveSpineBoneNodes();
            Inventory.ItemPicked += OnItemPicked;
            Inventory.ItemRemoved += OnItemRemoved;
            Inventory.ActiveBackpackSlotChanged += OnActiveSlotChanged;
            Inventory.QuickBarAssigned += OnQuickBarAssigned;
            Inventory.QuickBarSlotChanged += OnSelectedQuickBarSlotChanged;
            Inventory.WeaponEquipped += OnWeaponEquipped;
            Inventory.WeaponUnequipped += OnWeaponUnequipped;
            if (Inventory.Backpack != null)
            {
                Inventory.Backpack.InventoryChanged += OnInventoryChanged;
            }
            else
            {
                GD.PushWarning($"{Name}: PlayerInventoryComponent.Backpack 尚未初始化，无法订阅背包事件。");
            }

            // 订阅 QuickBar 事件（如果存在）
            SubscribeToQuickBar();

            UpdateAttachmentIcon();
        }

        /// <summary>
        /// 订阅 QuickBar 的事件
        /// </summary>
        public void SubscribeToQuickBar()
        {
            if (Inventory?.QuickBar != null)
            {
                // 先取消订阅，避免重复
                Inventory.QuickBar.InventoryChanged -= OnQuickBarChanged;
                Inventory.QuickBar.SlotChanged -= OnQuickBarSlotChanged;
                
                // 订阅
                Inventory.QuickBar.InventoryChanged += OnQuickBarChanged;
                Inventory.QuickBar.SlotChanged += OnQuickBarSlotChanged;
            }
        }

        private void OnQuickBarChanged()
        {
            UpdateAttachmentIcon();
        }

        private void OnQuickBarAssigned()
        {
            SubscribeToQuickBar();
            UpdateAttachmentIcon();
        }

        private void OnSelectedQuickBarSlotChanged(int _)
        {
            UpdateAttachmentIcon();
        }

        private void OnQuickBarSlotChanged(int slotIndex, string itemId, int quantity)
        {
            // 如果变化的是当前选中的快捷栏槽位，更新图标
            if (Inventory != null && slotIndex == Inventory.SelectedQuickBarSlot)
            {
                UpdateAttachmentIcon();
            }
        }

        public override void _ExitTree()
        {
            if (Inventory != null)
            {
                Inventory.ItemPicked -= OnItemPicked;
                Inventory.ItemRemoved -= OnItemRemoved;
                Inventory.ActiveBackpackSlotChanged -= OnActiveSlotChanged;
                Inventory.QuickBarAssigned -= OnQuickBarAssigned;
                Inventory.QuickBarSlotChanged -= OnSelectedQuickBarSlotChanged;
                Inventory.WeaponEquipped -= OnWeaponEquipped;
                Inventory.WeaponUnequipped -= OnWeaponUnequipped;
                if (Inventory.Backpack != null)
                {
                    Inventory.Backpack.InventoryChanged -= OnInventoryChanged;
                }
                // 取消订阅 QuickBar 事件
                if (Inventory.QuickBar != null)
                {
                    Inventory.QuickBar.InventoryChanged -= OnQuickBarChanged;
                    Inventory.QuickBar.SlotChanged -= OnQuickBarSlotChanged;
                }
            }
            base._ExitTree();
        }

        private void OnItemPicked(ItemDefinition item)
        {
            UpdateAttachmentIcon();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            MaintainSlotAnchorBinding();
            UpdateBoneAttachmentTransform();
            UpdateEquippedAttackAreaTransform();
            UpdateAttachmentIcon(); // 每帧检查状态，及时更新图标显示
        }

        private void ShowItemIcon(Texture2D? texture)
        {
            if (_spineSlotNode != null)
            {
                ShowOnSpineSlot(texture);
                return;
            }

            if (TryShowOnSpineBone(texture))
            {
                return;
            }

            if (_attachmentParent == null)
            {
                return;
            }

            if (texture == null)
            {
                _iconSprite?.QueueFree();
                _iconSprite = null;
                _activeBoneNode = null;
                _iconUsesBoneTracking = false;
                return;
            }

            _iconSprite ??= CreateIconSprite("HeldItemIcon");
            AttachIconTo(_attachmentParent, texture);
        }

        private void OnItemRemoved(string itemId)
        {
            UpdateAttachmentIcon();
        }

        private void OnActiveSlotChanged(int _)
        {
            UpdateAttachmentIcon();
        }

        private void OnInventoryChanged()
        {
            UpdateAttachmentIcon();
        }

        private void OnWeaponEquipped(ItemDefinition _)
        {
            UpdateAttachmentIcon();
        }

        private void OnWeaponUnequipped()
        {
            UpdateAttachmentIcon();
        }

        private void UpdateAttachmentIcon()
        {
            // Combat weapon source: special weapon slot > quick bar > backpack.
            ItemDefinition? activeItem = Inventory?.GetActiveCombatWeaponDefinition();

            // Hitbox 始终根据当前持握物品更新（不受视觉状态影响）
            // 这样武器的攻击区始终有效，不会被清空而回退到角色自身AttackArea
            UpdateEquippedAttackArea(activeItem);

            // 获取当前状态名
            string? currentState = _actor?.StateMachine?.CurrentState?.Name;

            // 只控制视觉显示（是否显示图标），与Hitbox独立
            // 对于可投掷物(投掷类)：在IdleHolding、RunHolding状态显示
            // 对于不可投掷物(武器类)：在Idle、Run、Walk、Hit状态显示
            if (!ShouldShowHoldingItem(currentState))
            {
                ShowItemIcon(null);  // 隐藏视觉，但Hitbox保留
                return;
            }

            ShowItemIcon(activeItem?.Icon);  // 显示视觉
        }

        public Area2D? GetEquippedAttackArea()
        {
            if (_equippedAttackArea == null || !GodotObject.IsInstanceValid(_equippedAttackArea) || !_equippedAttackArea.IsInsideTree())
            {
                return null;
            }

            return _equippedAttackArea;
        }

        public bool TryGetEquippedAttackAreaTemplate(out Shape2D? shape, out Transform2D transform, out uint collisionMask)
        {
            if (_equippedAttackShapeTemplate == null)
            {
                shape = null;
                transform = Transform2D.Identity;
                collisionMask = 1u;
                return false;
            }

            shape = _equippedAttackShapeTemplate.Duplicate() as Shape2D;
            transform = _equippedAttackShapeTransform;
            collisionMask = _equippedAttackCollisionMask;
            return shape != null;
        }

        public bool TryGetAttackAnchorGlobalPosition(out Vector2 globalPosition)
        {
            var anchorNode = ResolveCurrentSlotAnchorNode() ?? ResolveActiveBoneNode();
            if (anchorNode == null || !IsInstanceValid(anchorNode))
            {
                globalPosition = Vector2.Zero;
                return false;
            }

            var anchorTransform = anchorNode.GetGlobalTransform();
            globalPosition = RotateBoneOffsetWithBone
                ? anchorTransform * BoneIconOffset
                : anchorNode.GlobalPosition + BoneIconOffset;
            return true;
        }

        private void ShowOnSpineSlot(Texture2D? texture)
        {
            if (_spineSlotNode == null)
            {
                return;
            }

            if (texture == null)
            {
                ClearSpineSlot();
                return;
            }

            _iconSprite ??= CreateIconSprite(SlotIconName);

            var slotAnchorNode = (_SpineSlotIconContainer as Node2D) ?? (_spineSlotNode as Node2D);
            if (slotAnchorNode != null)
            {
                _useSlotAnchorBinding = true;
                _activeBoneNode = slotAnchorNode;
                _iconUsesBoneTracking = false;
                AttachIconTo(slotAnchorNode, texture);
                ApplySlotAnchorLocalTransform();

                return;
            }
            else
            {
                _useSlotAnchorBinding = false;
                AttachIconTo(_SpineSlotIconContainer ?? _spineSlotNode, texture);
            }

            if (_spineSlotNode.HasMethod("set_slot") && texture is AtlasTexture atlasTexture)
            {
                _spineSlotNode.Call("set_slot", atlasTexture.Region, atlasTexture.Atlas);
                return;
            }

            if (_spineSlotNode.HasMethod("set_slot"))
            {
                _spineSlotNode.Call("set_slot");
                return;
            }

            if (_spineSlotNode.HasMeta(SlotSelectProperty))
            {
                // fallback if property stored as metadata
                _previousSlotSelection ??= _spineSlotNode.GetMeta(SlotSelectProperty).AsString();
            }

            if (_previousSlotSelection == null)
            {
                var value = _spineSlotNode.Get(SlotSelectProperty);
                _previousSlotSelection = value.VariantType == Variant.Type.String ? (string)value : SpineSlotDefaultSelection;
            }

            _spineSlotNode.Set(SlotSelectProperty, _iconSprite.Name);
        }

        private bool TryShowOnSpineBone(Texture2D? texture)
        {
            var boneNode = ResolveActiveBoneNode();
            if (boneNode == null)
            {
                return false;
            }

            if (texture == null)
            {
                if (_iconSprite != null)
                {
                    _iconSprite.Visible = false;
                }
                _activeBoneNode = null;
                _iconUsesBoneTracking = false;
                return true;
            }

            _iconSprite ??= CreateIconSprite(SlotIconName);
            _activeBoneNode = boneNode;
            _iconUsesBoneTracking = true;
            AttachIconTo(_attachmentParent ?? boneNode, texture, isBoneAnchor: true);
            UpdateBoneAttachmentTransform(force: true);
            return true;
        }

        private void ResolveSpineBoneNodes()
        {
            _spineBoneNodes.Clear();

            foreach (var path in SpineBoneNodePaths)
            {
                if (path == null || path.IsEmpty)
                {
                    continue;
                }

                var node = GetNodeOrNull<Node2D>(path) ??
                           _actor?.GetNodeOrNull<Node2D>(path) ??
                           _attachmentParent?.GetNodeOrNull<Node2D>(path);
                if (node != null)
                {
                    _spineBoneNodes.Add(node);
                }
            }

            if (_spineBoneNodes.Count == 0 && _attachmentParent != null)
            {
                foreach (var boneName in SpineBoneOrder)
                {
                    if (string.IsNullOrWhiteSpace(boneName)) continue;
                    var candidate = _attachmentParent.FindChild(boneName, recursive: true, owned: false) as Node2D;
                    if (candidate != null)
                    {
                        _spineBoneNodes.Add(candidate);
                        break;
                    }
                }
            }
        }

        private Node2D? ResolveActiveBoneNode()
        {
            if (_spineBoneNodes.Count == 0)
            {
                ResolveSpineBoneNodes();
            }

            foreach (var node in _spineBoneNodes)
            {
                if (IsInstanceValid(node))
                {
                    return node;
                }
            }

            return null;
        }

        private void ClearSpineSlot()
        {
            if (_spineSlotNode == null)
            {
                return;
            }

            if (_previousSlotSelection != null)
            {
                _spineSlotNode.Set(SlotSelectProperty, _previousSlotSelection);
            }

            if (_iconSprite != null)
            {
                _iconSprite.Visible = false;
            }

            _activeBoneNode = null;
            _iconUsesBoneTracking = false;
            _useSlotAnchorBinding = false;
        }

        private void UpdateEquippedAttackArea(ItemDefinition? item)
        {
            if (item == null)
            {
                ClearEquippedAttackArea();
                return;
            }

            string scenePath = item.ResolveWorldScenePath();
            if (string.IsNullOrWhiteSpace(scenePath))
            {
                ClearEquippedAttackArea();
                return;
            }

            var scene = ResourceLoader.Load<PackedScene>(scenePath, string.Empty, ResourceLoader.CacheMode.Ignore);
            if (scene == null)
            {
                ClearEquippedAttackArea();
                GD.PushWarning($"{Name}: 无法加载物品场景 {scenePath}，无法生成武器 AttackArea。");
                return;
            }

            var instance = scene.Instantiate();
            if (instance == null)
            {
                ClearEquippedAttackArea();
                return;
            }

            var sourceArea = instance.GetNodeOrNull<Area2D>("AttackArea")
                ?? instance.FindChild("AttackArea", recursive: true, owned: false) as Area2D;
            if (sourceArea == null)
            {
                instance.QueueFree();
                ClearEquippedAttackArea();
                return;
            }

            var duplicatedArea = sourceArea.Duplicate() as Area2D;
            instance.QueueFree();
            if (duplicatedArea == null)
            {
                ClearEquippedAttackArea();
                return;
            }

            LogSourceAttackArea(item, scenePath, duplicatedArea);

            ClearEquippedAttackArea();
            _equippedAttackArea = duplicatedArea;
            _equippedAttackArea.Name = "AttackArea";
            ConfigureEquippedAttackArea(_equippedAttackArea);
            _equippedAttackAreaLocalTransform = _equippedAttackArea.Transform;
            CaptureEquippedAttackAreaTemplate(_equippedAttackArea);
            AttachEquippedAttackAreaToIcon();
            EmitSignal(SignalName.EquippedAttackAreaChanged);
        }

        private void LogSourceAttackArea(ItemDefinition item, string scenePath, Area2D sourceArea)
        {
            CollisionShape2D? collisionShape = sourceArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null)
            {
                foreach (Node child in sourceArea.GetChildren())
                {
                    if (child is CollisionShape2D shape)
                    {
                        collisionShape = shape;
                        break;
                    }
                }
            }

            string shapeType = collisionShape?.Shape?.GetType().Name ?? "None";
            string shapeInfo = string.Empty;
            if (collisionShape?.Shape is RectangleShape2D rect)
            {
                shapeInfo = $"rectSize={rect.Size}";
            }
            else if (collisionShape?.Shape is CapsuleShape2D capsule)
            {
                shapeInfo = $"capsule(r={capsule.Radius}, h={capsule.Height})";
            }
            else if (collisionShape?.Shape is CircleShape2D circle)
            {
                shapeInfo = $"circle(r={circle.Radius})";
            }

            // GameLogger.Info(nameof(PlayerItemAttachment),
            //     $"EquipAttackArea item={item.ItemId}, scene={scenePath}, areaTransform={sourceArea.Transform}, shapeTransform={collisionShape?.Transform}, shape={shapeType} {shapeInfo}");
        }

        private void CaptureEquippedAttackAreaTemplate(Area2D area)
        {
            _equippedAttackShapeTemplate = null;
            _equippedAttackShapeTransform = Transform2D.Identity;
            _equippedAttackCollisionMask = area.CollisionMask != 0 ? area.CollisionMask : 1u;

            var collisionShape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null)
            {
                foreach (Node child in area.GetChildren())
                {
                    if (child is CollisionShape2D shape)
                    {
                        collisionShape = shape;
                        break;
                    }
                }
            }

            if (collisionShape?.Shape == null)
            {
                return;
            }

            _equippedAttackShapeTemplate = collisionShape.Shape.Duplicate() as Shape2D;
            _equippedAttackShapeTransform = area.Transform * collisionShape.Transform;
        }

        private void ConfigureEquippedAttackArea(Area2D area)
        {
            NormalizeAttackAreaGeometry(area);

            area.TopLevel = true;
            area.Monitoring = true;
            area.Monitorable = false;
            area.CollisionLayer = 0;

            if (_actor is SamplePlayer player && player.AttackArea != null)
            {
                area.CollisionMask = player.AttackArea.CollisionMask;
            }

            foreach (Node child in area.GetChildren())
            {
                if (child is CollisionShape2D shape)
                {
                    shape.Disabled = false;
                }
            }
        }

        private static void NormalizeAttackAreaGeometry(Area2D area)
        {
            var collisionShape = area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null)
            {
                foreach (Node child in area.GetChildren())
                {
                    if (child is CollisionShape2D shape)
                    {
                        collisionShape = shape;
                        break;
                    }
                }
            }

            if (collisionShape?.Shape == null)
            {
                area.Scale = Vector2.One;
                return;
            }

            Vector2 areaScale = area.Scale;
            Vector2 shapeScale = collisionShape.Scale;
            Vector2 bakedScale = new Vector2(
                Mathf.Abs(areaScale.X * shapeScale.X),
                Mathf.Abs(areaScale.Y * shapeScale.Y));

            collisionShape.Shape = DuplicateShapeWithScale(collisionShape.Shape, bakedScale);
            collisionShape.Position = new Vector2(
                collisionShape.Position.X * areaScale.X,
                collisionShape.Position.Y * areaScale.Y);
            collisionShape.Scale = Vector2.One;
            area.Scale = Vector2.One;
        }

        private static Shape2D DuplicateShapeWithScale(Shape2D shape, Vector2 scale)
        {
            var duplicated = shape.Duplicate() as Shape2D;
            if (duplicated == null)
            {
                return shape;
            }

            if (duplicated is RectangleShape2D rect)
            {
                rect.Size = new Vector2(rect.Size.X * scale.X, rect.Size.Y * scale.Y);
                return rect;
            }

            if (duplicated is CircleShape2D circle)
            {
                circle.Radius *= Mathf.Max(scale.X, scale.Y);
                return circle;
            }

            if (duplicated is CapsuleShape2D capsule)
            {
                capsule.Radius *= scale.X;
                capsule.Height *= scale.Y;
                return capsule;
            }

            return duplicated;
        }

        private void AttachEquippedAttackAreaToIcon()
        {
            if (_equippedAttackArea == null)
            {
                return;
            }

            Node? targetParent = (Node?)_iconSprite ?? _attachmentParent ?? _actor;
            if (targetParent == null)
            {
                GD.PushWarning($"{Name}: 无法找到 AttackArea 的挂载父节点，武器攻击区域将无法生效。");
                return;
            }

            if (_equippedAttackArea.GetParent() != targetParent)
            {
                if (_equippedAttackArea.GetParent() != null)
                {
                    _equippedAttackArea.Reparent(targetParent);
                }
                else
                {
                    targetParent.AddChild(_equippedAttackArea);
                }
            }

            UpdateEquippedAttackAreaTransform();
        }

        private void ClearEquippedAttackArea()
        {
            if (_equippedAttackArea != null && GodotObject.IsInstanceValid(_equippedAttackArea))
            {
                _equippedAttackArea.QueueFree();
            }

            _equippedAttackArea = null;
            _equippedAttackAreaLocalTransform = Transform2D.Identity;
            _equippedAttackShapeTemplate = null;
            _equippedAttackShapeTransform = Transform2D.Identity;
            _equippedAttackCollisionMask = 1u;
            EmitSignal(SignalName.EquippedAttackAreaChanged);
        }

        private void UpdateEquippedAttackAreaTransform()
        {
            if (_equippedAttackArea == null || !GodotObject.IsInstanceValid(_equippedAttackArea))
            {
                return;
            }

            if (_iconSprite != null && GodotObject.IsInstanceValid(_iconSprite))
            {
                Transform2D iconNoScale = RemoveScaleFromTransform(_iconSprite.GlobalTransform);
                _equippedAttackArea.GlobalTransform = iconNoScale * _equippedAttackAreaLocalTransform;
            }
            else
            {
                // Fallback: follow attachment parent or actor position
                var anchor = (_attachmentParent as Node2D) ?? (_actor as Node2D);
                if (anchor != null)
                {
                    _equippedAttackArea.GlobalTransform = anchor.GlobalTransform * _equippedAttackAreaLocalTransform;
                }
            }
        }

        private static Transform2D RemoveScaleFromTransform(Transform2D transform)
        {
            Vector2 x = transform.X;
            Vector2 y = transform.Y;

            float xLen = x.Length();
            float yLen = y.Length();

            Vector2 xUnit = xLen > 0.0001f ? x / xLen : Vector2.Right;
            Vector2 yUnit = yLen > 0.0001f ? y / yLen : Vector2.Down;

            return new Transform2D(xUnit, yUnit, transform.Origin);
        }

        private float GetIconRotationRadians()
        {
            return Mathf.DegToRad(IconRotationDegrees);
        }

        private Sprite2D CreateIconSprite(string name)
        {
            var sprite = new Sprite2D
            {
                Name = name,
                TopLevel = false
            };
            return sprite;
        }

        private void AttachIconTo(Node parent, Texture2D texture, bool isBoneAnchor = false)
        {
            if (_iconSprite == null)
            {
                return;
            }

            if (_iconSprite.GetParent() != parent)
            {
                parent.AddChild(_iconSprite);
            }

            _iconSprite.Visible = true;
            _iconSprite.Texture = texture;
            _iconSprite.ZIndex = ZIndex;
            _iconSprite.ZAsRelative = true;
            AttachEquippedAttackAreaToIcon();

            if (isBoneAnchor)
            {
                UpdateBoneAttachmentTransform(force: true);
            }
            else
            {
                _iconUsesBoneTracking = false;
                _activeBoneNode = null;
                _iconSprite.Position = IconOffset;
                _iconSprite.Rotation = GetIconRotationRadians();
                ApplyFacingFlip(applyForBone: false);
            }
        }

        private void UpdateBoneAttachmentTransform(bool force = false)
        {
            if (!_iconUsesBoneTracking || _iconSprite == null || _activeBoneNode == null)
            {
                return;
            }

            if (!_iconSprite.Visible && !force)
            {
                return;
            }

            var boneTransform = _activeBoneNode.GetGlobalTransform();
            _iconSprite.GlobalPosition = RotateBoneOffsetWithBone
                ? boneTransform * BoneIconOffset
                : _activeBoneNode.GlobalPosition + BoneIconOffset;
            _iconSprite.GlobalRotation = _activeBoneNode.GlobalRotation + GetIconRotationRadians();

            var scale = _iconSprite.Scale;
            float absX = MathF.Abs(scale.X);
            float absY = MathF.Abs(scale.Y);

            if (_actor != null && FlipBoneAttachmentWithFacing)
            {
                scale.X = absX * (_actor.FacingRight ? 1f : -1f);  // 修复：应该是1f，不是2f
            }
            else
            {
                scale.X = absX;
            }

            scale.Y = absY;
            _iconSprite.Scale = scale;
        }

        private void MaintainSlotAnchorBinding()
        {
            if (!_useSlotAnchorBinding || _iconSprite == null || !_iconSprite.Visible)
            {
                return;
            }

            var slotAnchorNode = ResolveCurrentSlotAnchorNode();
            if (slotAnchorNode == null)
            {
                return;
            }

            _activeBoneNode = slotAnchorNode;

            if (_iconSprite.GetParent() != slotAnchorNode)
            {
                slotAnchorNode.AddChild(_iconSprite);
            }

            ApplySlotAnchorLocalTransform();
        }

        private Node2D? ResolveCurrentSlotAnchorNode()
        {
            var slotAnchorNode = (_SpineSlotIconContainer as Node2D) ?? (_spineSlotNode as Node2D);
            if (slotAnchorNode != null && IsInstanceValid(slotAnchorNode))
            {
                return slotAnchorNode;
            }

            _spineSlotNode = ResolveSpineSlotNode();
            slotAnchorNode = (_SpineSlotIconContainer as Node2D) ?? (_spineSlotNode as Node2D);
            if (slotAnchorNode != null && IsInstanceValid(slotAnchorNode))
            {
                return slotAnchorNode;
            }

            return ResolveActiveBoneNode();
        }

        private void ApplySlotAnchorLocalTransform()
        {
            if (_iconSprite == null)
            {
                return;
            }

            _iconSprite.Position = BoneIconOffset;
            _iconSprite.Rotation = GetIconRotationRadians();

            var scale = _iconSprite.Scale;
            float absX = MathF.Abs(scale.X);
            float absY = MathF.Abs(scale.Y);
            if (_actor != null && FlipBoneAttachmentWithFacing)
            {
                scale.X = absX * (_actor.FacingRight ? 1f : -1f);
            }
            else
            {
                scale.X = absX;
            }
            scale.Y = absY;
            _iconSprite.Scale = scale;
        }

        private void ApplyFacingFlip(bool applyForBone)
        {
            if (_iconSprite == null || _actor == null)
            {
                return;
            }

            bool shouldFlip = applyForBone ? FlipBoneAttachmentWithFacing : FlipWithFacing;
            var targetScaleX = shouldFlip && _actor != null
                ? (_actor.FacingRight ? 1f : -1f)
                : 1f;

            var scale = _iconSprite.Scale;
            scale.X = MathF.Abs(scale.X) * targetScaleX;
            _iconSprite.Scale = scale;
        }

        private Node2D? ResolveAttachmentParent()
        {
            if (_actor == null)
            {
                return null;
            }

            if (AttachmentParentPath.GetNameCount() == 0)
            {
                return _actor;
            }

            return _actor.GetNodeOrNull<Node2D>(AttachmentParentPath);
        }

        private Node? ResolveSpineSlotNode()
        {
            if (SpineSlotNodePath.IsEmpty)
            {
                return null;
            }

            return GetNodeOrNull(SpineSlotNodePath) ?? _actor?.GetNodeOrNull(SpineSlotNodePath);
        }

        /// <summary>
        /// 判断当前状态/动画名是否需要显示持握物品
        /// </summary>
        public bool ShouldShowHoldingItem(string? stateOrAnimationName)
        {
            if (string.IsNullOrWhiteSpace(stateOrAnimationName))
            {
                return false;
            }

            foreach (var state in ShowHoldingItemStates)
            {
                if (string.Equals(state, stateOrAnimationName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

