using Godot;
using Kuros.Core;

public partial class FollowingPickupProperty : PickupProperty
{
    [ExportGroup("Bone Binding Overrides")]
    [Export]
    public Vector2 BoneLocalOffset
    {
        get => AttachedLocalOffset;
        set => AttachedLocalOffset = value;
    }

    [Export]
    public NodePath BoneNodePath
    {
        get => AttachmentPointPath;
        set => AttachmentPointPath = value;
    }
}

