using System;
using Godot;
using Kuros.Actors;
using Kuros.Core;
using Kuros.Items.Effects;
using Kuros.Utils;

namespace Kuros.Items.World
{
	/// <summary>
	/// 重力手雷逻辑处理
	/// 负责：落地检测、黑洞生成、音效播放
	/// </summary>
	public partial class GravityGrenade : Node2D
	{
		[ExportGroup("BlackHole")]
		[Export] public string black_hole_scene_path { get; set; } = "res://shaders/black_hole.tscn";
		[Export] public NodePath RigidBodyEntityPath { get; set; } = new NodePath("..");
		
		[ExportGroup("Audio")]
		[Export] public string landing_sound_path { get; set; } = ""; // 着陆音效路径（可选）
		[Export(PropertyHint.Range, "0,1,0.1")] public float landing_sound_volume { get; set; } = 0.8f;
		
		private RigidBodyWorldItemEntity? _rigidBodyEntity;
		private double _flightCheckTimer = 0.0;
		private bool _hasLanded = false;
		private bool _throwArmed = false;
		private int _lowSpeedConfirmFrames = 0;
		private const float ThrowArmSpeedThreshold = 120f;
		private const float LandingSpeedThreshold = 25f;
		private const int LandingConfirmFrames = 3;
		
		public override void _Ready()
		{
			// 获取父节点的RigidBodyWorldItemEntity
			_rigidBodyEntity = GetNodeOrNull<RigidBodyWorldItemEntity>(RigidBodyEntityPath);
			if (_rigidBodyEntity == null)
			{
				GameLogger.Warn(nameof(GravityGrenade), $"未找到 RigidBodyWorldItemEntity: {RigidBodyEntityPath}");
			}
			
			GameLogger.Info(nameof(GravityGrenade), $"重力手雷已准备就绪: {Name}");
		}
		
		public override void _PhysicsProcess(double delta)
		{
			// 只在确认“已经投掷过”后才允许落地触发，避免未投掷时误触发黑洞。
			if (!_hasLanded && _rigidBodyEntity != null)
			{
				_flightCheckTimer += delta;
				
				// 检查速度是否已大幅降低（表示已落地）
				if (_flightCheckTimer >= 0.1) // 每0.1秒检查一次
				{
					_flightCheckTimer = 0.0;
					
					RigidBody2D? rigidBody = _rigidBodyEntity.GetNodeOrNull<RigidBody2D>("RigidBody2D");
					if (rigidBody != null)
					{
						float speed = rigidBody.LinearVelocity.Length();

						if (!_throwArmed && (speed > ThrowArmSpeedThreshold || _rigidBodyEntity.ThrowCooldownProgress > 0f))
						{
							_throwArmed = true;
							_lowSpeedConfirmFrames = 0;
						}

						if (!_throwArmed)
						{
							return;
						}

						if (speed < LandingSpeedThreshold)
						{
							_lowSpeedConfirmFrames++;
						}
						else
						{
							_lowSpeedConfirmFrames = 0;
						}

						if (_lowSpeedConfirmFrames >= LandingConfirmFrames)
						{
							OnGrenadeHit();
						}
					}
				}
			}
		}
		
		/// <summary>
		/// 手雷落地，生成黑洞
		/// </summary>
		private void OnGrenadeHit()
		{
			if (_hasLanded)
				return;
			
			_hasLanded = true;
			
			// 播放着陆音效
			PlayLandingSound();
			
			// 生成黑洞
			SpawnBlackHole();
			
			GameLogger.Info(nameof(GravityGrenade), $"重力手雷已落地，生成黑洞");
		}
		
		/// <summary>
		/// 生成黑洞场景
		/// </summary>
		private void SpawnBlackHole()
		{
			try
			{
				var blackHoleScene = GD.Load<PackedScene>(black_hole_scene_path);
				if (blackHoleScene == null)
				{
					GameLogger.Error(nameof(GravityGrenade), $"无法加载黑洞场景: {black_hole_scene_path}");
					return;
				}
				
				// 实例化黑洞
				var blackHole = blackHoleScene.Instantiate();
				var rigidBody = _rigidBodyEntity?.GetNodeOrNull<RigidBody2D>("RigidBody2D");
				Vector2 spawnPos = rigidBody?.GlobalPosition ?? GlobalPosition;
				Node? spawnParent = GetTree().CurrentScene ?? GetParent();
				
				// 获取场景的根节点并设置位置
				if (blackHole is Node2D blackHoleNode2D)
				{
					if (spawnParent == null)
					{
						GameLogger.Error(nameof(GravityGrenade), "无法确定黑洞挂载父节点，生成失败。");
						blackHoleNode2D.QueueFree();
						return;
					}

					spawnParent.AddChild(blackHoleNode2D);
					blackHoleNode2D.GlobalPosition = spawnPos;
					
					// 如果是 GravityGrenadeBlackHole，尝试传递来源 Actor。
					if (GodotObject.IsInstanceValid(blackHoleNode2D) && blackHoleNode2D is GravityGrenadeBlackHole gravityBlackHole)
					{
						if (HasMeta("SourceActor"))
						{
							gravityBlackHole.SetSourceActor(GetMeta("SourceActor").As<GameActor>());
						}
					}
					
					GameLogger.Info(nameof(GravityGrenade), $"黑洞已在位置 {spawnPos} 生成，父节点: {spawnParent.Name}");
				}
			}
			catch (Exception ex)
			{
				GameLogger.Error(nameof(GravityGrenade), $"生成黑洞时发生错误: {ex.Message}");
			}
		}
		
		/// <summary>
		/// 播放着陆音效
		/// </summary>
		private void PlayLandingSound()
		{
			if (string.IsNullOrEmpty(landing_sound_path))
				return; // 未配置音效路径，跳过
			
			try
			{
				var audioPlayer = new AudioStreamPlayer2D
				{
					GlobalPosition = GlobalPosition,
					Bus = "SFX"
				};
				
				var audioStream = GD.Load<AudioStream>(landing_sound_path);
				if (audioStream != null)
				{
					audioPlayer.Stream = audioStream;
					audioPlayer.VolumeDb = Mathf.LinearToDb(landing_sound_volume);
					GetParent()?.AddChild(audioPlayer);
					audioPlayer.Play();
					
					// 播放完成后自动删除
					audioPlayer.Finished += () => audioPlayer.QueueFree();
					GameLogger.Debug(nameof(GravityGrenade), "播放手雷落地音效");
				}
			}
			catch (Exception ex)
			{
				GameLogger.Warn(nameof(GravityGrenade), $"播放音效失败: {ex.Message}");
			}
		}
		
		/// <summary>
		/// 设置发射方Actor元数据（用于伤害计算）
		/// </summary>
		public void SetSourceActor(GameActor? actor)
		{
			if (actor == null)
			{
				RemoveMeta("SourceActor");
				return;
			}

			SetMeta("SourceActor", actor);
		}
	}
}
