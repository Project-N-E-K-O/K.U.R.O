using System.Collections.Generic;
using Godot;
using Godot.Collections;
using Kuros.Core;

namespace Kuros.Controllers
{
    /// <summary>
    /// 分波次生成敌人管理器。
    ///
    /// 用法：
    ///   1. 将本节点添加到场景中。
    ///   2. 为每个波次创建一个 EnemySpawnManager 子节点（Wave1、Wave2 ...），
    ///      SpawnOnReady 和触发器请保持关闭，由本节点统一驱动。
    ///   3. （可选）在 TriggerArea 指定触发区域；留空则在 _Ready 时自动开始。
    ///
    /// 流程：
    ///   触发 → 第1波生成 → 等待本波全员死亡 → WaveClearDelay → 第2波生成 → …
    ///   → AllWavesCleared 信号
    /// </summary>
    [GlobalClass]
    public partial class WaveSpawnManager : Node2D
    {
        // ── 波次配置 ──────────────────────────────────────────────────────────
        /// <summary>
        /// 显式指定每个波次对应的 EnemySpawnManager 路径（按顺序）。
        /// 留空则自动按子节点顺序发现所有 EnemySpawnManager。
        /// </summary>
        [ExportCategory("波次配置")]
        [Export] public Array<NodePath> WavePaths { get; set; } = new();

        /// <summary>每波结束后到下一波开始前的延迟（秒）。</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float WaveClearDelay { get; set; } = 1.5f;

        /// <summary>轮询存活敌人的间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float PollInterval { get; set; } = 0.5f;

        // ── 触发器 ────────────────────────────────────────────────────────────
        [ExportCategory("触发器")]
        [Export] public Area2D? TriggerArea { get; set; }
        [Export] public string TriggerGroupName { get; set; } = "player";
        [Export] public bool TriggerOnce { get; set; } = true;
        [Export] public bool SpawnOnReady { get; set; } = false;

        // ── 联动 BattleArena ──────────────────────────────────────────────────
        [ExportCategory("联动 BattleArena")]
        /// <summary>
        /// 关联的 BattleArena 节点路径。
        /// 设置后，WaveSpawnManager 会在波次开始时锁定 BattleArena，
        /// 防止波次间隙（无敌人时）自动撤销空气墙，全部波次结束后解锁。
        /// </summary>
        [Export] public NodePath BattleArenaPath { get; set; } = new NodePath();

        // ── 调试 ──────────────────────────────────────────────────────────────
        [ExportCategory("调试")]
        [Export] public bool LogWaveEvents { get; set; } = true;

        // ── 信号 ──────────────────────────────────────────────────────────────
        [Signal] public delegate void WaveStartedEventHandler(int waveIndex);
        [Signal] public delegate void WaveClearedEventHandler(int waveIndex);
        [Signal] public delegate void AllWavesClearedEventHandler();

        // ── 内部状态 ──────────────────────────────────────────────────────────
        private readonly List<EnemySpawnManager> _waveManagers = new();

        /// <summary>当前波次产生的敌人节点列表。</summary>
        private readonly List<Node> _currentWaveEnemies = new();

        private bool _isRunning;
        private bool _hasTriggered;
        private Kuros.Managers.BattleArena? _battleArena;

        // ── 生命周期 ──────────────────────────────────────────────────────────
        public override void _Ready()
        {
            BuildWaveManagerList();

            if (!BattleArenaPath.IsEmpty)
                _battleArena = GetNodeOrNull<Kuros.Managers.BattleArena>(BattleArenaPath);

            if (TriggerArea != null)
                TriggerArea.BodyEntered += OnTriggerBodyEntered;

            if (SpawnOnReady)
                StartWaves();
        }

        public override void _ExitTree()
        {
            if (TriggerArea != null)
                TriggerArea.BodyEntered -= OnTriggerBodyEntered;
        }

        // ── 公共 API ──────────────────────────────────────────────────────────
        /// <summary>手动启动波次序列。</summary>
        public void StartWaves()
        {
            if (_isRunning) return;
            if (TriggerOnce && _hasTriggered) return;

            _hasTriggered = true;
            _ = RunWavesAsync();
        }

        /// <summary>重置已触发标志，允许再次触发（配合 TriggerOnce=true）。</summary>
        public void ResetTrigger() => _hasTriggered = false;

        // ── 内部逻辑 ──────────────────────────────────────────────────────────
        private async System.Threading.Tasks.Task RunWavesAsync()
        {
            _isRunning = true;
            int total = _waveManagers.Count;

            // 在整个波次期间锁定 BattleArena，防止波次间隙撤销空气墙
            _battleArena?.SetForceLock(true);
            if (LogWaveEvents && _battleArena != null)
                GD.Print($"[{Name}] BattleArena 已锁定，波次进行中不会撤销空气墙");

            for (int i = 0; i < total; i++)
            {
                var mgr = _waveManagers[i];
                _currentWaveEnemies.Clear();

                // 订阅本波信号，收集即将生成的敌人引用
                mgr.EnemySpawned += OnEnemySpawned;

                if (LogWaveEvents)
                    GD.Print($"[{Name}] ▶ Wave {i + 1}/{total} 开始");

                EmitSignal(SignalName.WaveStarted, i);
                mgr.StartSpawnSequence();

                // 等待本波全部敌人生成完毕
                await ToSignal(mgr, EnemySpawnManager.SignalName.SpawnCompleted);
                mgr.EnemySpawned -= OnEnemySpawned;

                if (LogWaveEvents)
                    GD.Print($"[{Name}] Wave {i + 1} 生成完毕，共追踪 {_currentWaveEnemies.Count} 个敌人，等待全员消灭…");

                // 轮询直到本波所有敌人死亡/被释放
                while (HasLivingEnemies())
                {
                    var pollTimer = GetTree().CreateTimer(PollInterval);
                    await ToSignal(pollTimer, SceneTreeTimer.SignalName.Timeout);
                }

                if (LogWaveEvents)
                    GD.Print($"[{Name}] ✔ Wave {i + 1}/{total} 已消灭");

                EmitSignal(SignalName.WaveCleared, i);

                // 最后一波结束后不需要等待延迟
                if (i < total - 1 && WaveClearDelay > 0f)
                {
                    var delay = GetTree().CreateTimer(WaveClearDelay);
                    await ToSignal(delay, SceneTreeTimer.SignalName.Timeout);
                }
            }

            _isRunning = false;

            // 所有波次结束，解锁 BattleArena（若无敌人则自动停用）
            _battleArena?.SetForceLock(false);
            if (LogWaveEvents && _battleArena != null)
                GD.Print($"[{Name}] BattleArena 已解锁");

            EmitSignal(SignalName.AllWavesCleared);

            if (LogWaveEvents)
                GD.Print($"[{Name}] ★ 所有波次已完成！");
        }

        /// <summary>判断当前波次是否仍有存活敌人。</summary>
        private bool HasLivingEnemies()
        {
            foreach (var enemy in _currentWaveEnemies)
            {
                if (!GodotObject.IsInstanceValid(enemy)) continue;

                // 优先使用 GameActor.IsDead
                if (enemy is GameActor actor)
                {
                    if (!actor.IsDead) return true;
                    continue;
                }

                // 对于非 GameActor 节点，节点仍存在即视为"存活"
                return true;
            }

            return false;
        }

        private void OnEnemySpawned(Node enemy, int index)
        {
            _currentWaveEnemies.Add(enemy);
        }

        private void OnTriggerBodyEntered(Node2D body)
        {
            if (!string.IsNullOrWhiteSpace(TriggerGroupName) && !body.IsInGroup(TriggerGroupName))
                return;
            StartWaves();
        }

        /// <summary>构建波次管理器列表。</summary>
        private void BuildWaveManagerList()
        {
            _waveManagers.Clear();

            if (WavePaths.Count > 0)
            {
                // 使用显式路径
                foreach (var path in WavePaths)
                {
                    var mgr = GetNodeOrNull<EnemySpawnManager>(path);
                    if (mgr != null)
                        _waveManagers.Add(mgr);
                    else
                        GD.PushWarning($"[{Name}] WavePaths 中路径 '{path}' 未找到或类型不是 EnemySpawnManager");
                }
            }
            else
            {
                // 自动发现子节点中的 EnemySpawnManager（按子节点顺序即为波次顺序）
                foreach (var child in GetChildren())
                {
                    if (child is EnemySpawnManager mgr)
                        _waveManagers.Add(mgr);
                }
            }

            if (LogWaveEvents)
                GD.Print($"[{Name}] 已加载 {_waveManagers.Count} 个波次管理器");
        }
    }
}
