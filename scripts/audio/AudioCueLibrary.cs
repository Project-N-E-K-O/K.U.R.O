using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.Audio
{
    /// <summary>
    /// 音频库：在 Inspector 中维护一组 Cue，运行时可按 Id 查询。
    /// </summary>
    [GlobalClass]
    public partial class AudioCueLibrary : Resource
    {
        [Export] public Godot.Collections.Array<AudioCue> Cues
        {
            get => _cues;
            set
            {
                _cues = value ?? new();
                _cache = null;
            }
        }

        private Godot.Collections.Array<AudioCue> _cues = new();
        private Dictionary<string, AudioCue>? _cache;

        public AudioCue? GetCue(string cueId)
        {
            if (string.IsNullOrWhiteSpace(cueId))
            {
                return null;
            }

            EnsureCache();
            return _cache != null && _cache.TryGetValue(cueId, out var cue) ? cue : null;
        }

        private void EnsureCache()
        {
            if (_cache != null) return;

            _cache = new Dictionary<string, AudioCue>(StringComparer.OrdinalIgnoreCase);
            foreach (var cue in _cues)
            {
                if (cue == null || string.IsNullOrWhiteSpace(cue.CueId) || _cache.ContainsKey(cue.CueId))
                {
                    continue;
                }

                _cache[cue.CueId] = cue;
            }
        }
    }
}

