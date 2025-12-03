using System.Collections.Generic;
using Godot;

namespace Kuros.Audio
{
    /// <summary>
    /// 统一的音频播放入口：负责管理 BGM、全局音效与 2D 音效播放器池。
    /// 建议作为 Autoload 场景挂载，以便全局访问。
    /// </summary>
    public partial class AudioManager : Node
    {
        private const string DefaultBus = "Master";

        [Export] public AudioCueLibrary? CueLibrary { get; set; }
        [Export(PropertyHint.Range, "1,64,1")] public int OneShotPlayerCount { get; set; } = 12;

        private readonly Queue<AudioStreamPlayer> _freeGlobalPlayers = new();
        private readonly Queue<AudioStreamPlayer2D> _free2DPlayers = new();
        private AudioStreamPlayer? _bgmPlayer;

        public override void _Ready()
        {
            base._Ready();
            InitializePlayers();
        }

        #region Public API

        public void PlaySfx(string cueId, Vector2? position = null)
        {
            var cue = CueLibrary?.GetCue(cueId);
            if (cue == null)
            {
                GD.PushWarning($"[AudioManager] 未找到 Cue: {cueId}");
                return;
            }

            PlayCue(cue, position);
        }

        public void PlayCue(AudioCue cue, Vector2? position = null)
        {
            if (cue?.Stream == null)
            {
                return;
            }

            if (position.HasValue)
            {
                var player = Get2DPlayer();
                ConfigurePlayer(player, cue);
                player.GlobalPosition = position.Value;
                player.Play();
            }
            else
            {
                var player = GetGlobalPlayer();
                ConfigurePlayer(player, cue);
                player.Play();
            }
        }

        public void PlayBgm(string cueId)
        {
            var cue = CueLibrary?.GetCue(cueId);
            if (cue == null)
            {
                GD.PushWarning($"[AudioManager] 未找到 BGM Cue: {cueId}");
                return;
            }

            PlayBgm(cue);
        }

        public void PlayBgm(AudioCue cue)
        {
            if (cue?.Stream == null)
            {
                return;
            }

            _bgmPlayer ??= CreateBgmPlayer();
            var streamInstance = PrepareStreamForBgm(cue);
            if (streamInstance == null)
            {
                return;
            }

            ConfigurePlayer(_bgmPlayer, cue, streamInstance);
            _bgmPlayer.Play();
        }

        public void StopBgm(float fadeSeconds = 0f)
        {
            if (_bgmPlayer == null) return;

            if (fadeSeconds <= 0f)
            {
                _bgmPlayer.Stop();
                return;
            }

            var tween = CreateTween();
            tween.TweenProperty(_bgmPlayer, "volume_db", -80f, fadeSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.Out);
            tween.TweenCallback(Callable.From(() =>
            {
                _bgmPlayer.Stop();
                _bgmPlayer.VolumeDb = 0f;
            }));
        }

        #endregion

        #region Internal helpers

        private void InitializePlayers()
        {
            for (int i = 0; i < OneShotPlayerCount; i++)
            {
                _freeGlobalPlayers.Enqueue(CreateOneShotPlayer());
                _free2DPlayers.Enqueue(CreateOneShotPlayer2D());
            }
        }

        private AudioStreamPlayer CreateOneShotPlayer()
        {
            var player = new AudioStreamPlayer
            {
                Bus = DefaultBus,
                Autoplay = false
            };
            player.Finished += () => OnPlayerFinished(player);
            AddChild(player);
            return player;
        }

        private AudioStreamPlayer2D CreateOneShotPlayer2D()
        {
            var player = new AudioStreamPlayer2D
            {
                Bus = DefaultBus,
                Autoplay = false
            };
            player.Finished += () => OnPlayerFinished(player);
            AddChild(player);
            return player;
        }

        private AudioStreamPlayer CreateBgmPlayer()
        {
            var player = new AudioStreamPlayer
            {
                Bus = DefaultBus,
                Autoplay = false
            };
            AddChild(player);
            return player;
        }

        private AudioStreamPlayer GetGlobalPlayer()
        {
            return _freeGlobalPlayers.Count > 0 ? _freeGlobalPlayers.Dequeue() : CreateOneShotPlayer();
        }

        private AudioStreamPlayer2D Get2DPlayer()
        {
            return _free2DPlayers.Count > 0 ? _free2DPlayers.Dequeue() : CreateOneShotPlayer2D();
        }

        private void OnPlayerFinished(AudioStreamPlayer player)
        {
            player.Stream = null;
            _freeGlobalPlayers.Enqueue(player);
        }

        private void OnPlayerFinished(AudioStreamPlayer2D player)
        {
            player.Stream = null;
            _free2DPlayers.Enqueue(player);
        }

        private static void ConfigurePlayer(AudioStreamPlayer player, AudioCue cue, AudioStream? streamOverride = null)
        {
            var stream = streamOverride ?? cue.Stream;
            if (stream == null)
            {
                player.Stream = null;
                return;
            }

            player.Stream = stream;
            player.Bus = string.IsNullOrWhiteSpace(cue.Bus) ? DefaultBus : cue.Bus;
            player.VolumeDb = cue.VolumeDb;
            player.PitchScale = cue.SamplePitchScale();
        }

        private static void ConfigurePlayer(AudioStreamPlayer2D player, AudioCue cue, AudioStream? streamOverride = null)
        {
            var stream = streamOverride ?? cue.Stream;
            if (stream == null)
            {
                player.Stream = null;
                return;
            }

            player.Stream = stream;
            player.Bus = string.IsNullOrWhiteSpace(cue.Bus) ? DefaultBus : cue.Bus;
            player.VolumeDb = cue.VolumeDb;
            player.PitchScale = cue.SamplePitchScale();
        }

        private static AudioStream? PrepareStreamForBgm(AudioCue cue)
        {
            if (cue.Stream == null)
            {
                return null;
            }

            var duplicated = cue.Stream.Duplicate() as AudioStream ?? cue.Stream;
            ApplyLoopSetting(duplicated, cue.Loop);
            return duplicated;
        }

        private static void ApplyLoopSetting(AudioStream stream, bool loop)
        {
            switch (stream)
            {
                case AudioStreamWav wav:
                    wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;
                    break;
                case AudioStreamOggVorbis ogg:
                    ogg.Loop = loop;
                    break;
                case AudioStreamMP3 mp3:
                    mp3.Loop = loop;
                    break;
                case AudioStreamSample sample:
                    sample.LoopMode = loop ? AudioStreamSample.LoopModeEnum.Forward : AudioStreamSample.LoopModeEnum.Disabled;
                    break;
                default:
                    break;
            }
        }

        #endregion
    }
}

