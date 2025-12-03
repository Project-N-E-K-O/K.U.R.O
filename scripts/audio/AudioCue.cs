using Godot;

namespace Kuros.Audio
{
    /// <summary>
    /// 描述一段可复用的音频素材及其基础播放参数。
    /// </summary>
    [GlobalClass]
    public partial class AudioCue : Resource
    {
        [Export] public string CueId { get; set; } = string.Empty;
        [Export] public AudioStream? Stream { get; set; }
        [Export(PropertyHint.Range, "-80,6,0.1")] public float VolumeDb { get; set; } = 0f;
        [Export(PropertyHint.Range, "0.25,3,0.01")] public float PitchScale { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,1,0.01")] public float PitchJitter { get; set; } = 0f;
        [Export] public string Bus { get; set; } = "Master";
        [Export] public bool Loop { get; set; } = false;

        public float SamplePitchScale()
        {
            if (PitchJitter <= 0f)
            {
                return PitchScale;
            }

            float jitter = (float)GD.RandRange(-PitchJitter, PitchJitter);
            return Mathf.Max(0.01f, PitchScale + jitter);
        }
    }
}

