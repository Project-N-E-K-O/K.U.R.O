using Godot;
using System.Collections.Generic;

namespace Kuros.Data
{
	/// <summary>
	/// 对话条目数据
	/// </summary>
	[GlobalClass]
	public partial class DialogueEntry : Resource
	{
		[ExportGroup("对话内容")]
		[Export] public string SpeakerName { get; set; } = "NPC";
		[Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
		[Export] public Texture2D? SpeakerPortrait { get; set; }
		
		[ExportGroup("选项")]
		// 使用非泛型 Array 避免导出时的序列化问题
		[Export] public Godot.Collections.Array Choices { get; set; } = new();
		
		[ExportGroup("行为")]
		[Export] public string OnDialogueEndAction { get; set; } = ""; // 对话结束时的行为标识
		[Export] public bool AutoAdvance { get; set; } = false; // 是否自动推进到下一句
		[Export(PropertyHint.Range, "0,10,0.1")] public float AutoAdvanceDelay { get; set; } = 2.0f;
		[Export] public int NextEntryIndex { get; set; } = -2; // -2表示继续下一个（默认），-1表示结束，>=0表示跳转
		
		/// <summary>
		/// 获取选项（类型安全的访问方法）
		/// </summary>
		public DialogueChoice? GetChoice(int index)
		{
			if (index < 0 || index >= Choices.Count)
				return null;
			return Choices[index].As<DialogueChoice>();
		}
		
		/// <summary>
		/// 添加选项
		/// </summary>
		public void AddChoice(DialogueChoice choice)
		{
			Choices.Add(choice);
		}
	}
	
	/// <summary>
	/// 对话选项
	/// </summary>
	[GlobalClass]
	public partial class DialogueChoice : Resource
	{
		[Export] public string Text { get; set; } = "选择";
		[Export] public int NextEntryIndex { get; set; } = -1; // -1表示结束对话
		[Export] public string OnSelectedAction { get; set; } = ""; // 选择此选项时的行为标识
	}
	
	/// <summary>
	/// 对话数据资源，包含完整的对话树
	/// </summary>
	[GlobalClass]
	public partial class DialogueData : Resource
	{
		[ExportGroup("对话信息")]
		[Export] public string DialogueId { get; set; } = "";
		[Export] public string DialogueName { get; set; } = "对话";
		
		[ExportGroup("对话条目")]
		// 使用非泛型 Array 避免导出时的序列化问题
		[Export] public Godot.Collections.Array Entries { get; set; } = new();
		
		[ExportGroup("默认设置")]
		[Export] public int StartEntryIndex { get; set; } = 0;
		[Export] public bool CanSkip { get; set; } = true; // 是否可以跳过对话
		
		/// <summary>
		/// 获取对话条目
		/// </summary>
		public DialogueEntry? GetEntry(int index)
		{
			if (index < 0 || index >= Entries.Count)
				return null;
			return Entries[index].As<DialogueEntry>();
		}
		
		/// <summary>
		/// 获取起始对话条目
		/// </summary>
		public DialogueEntry? GetStartEntry()
		{
			return GetEntry(StartEntryIndex);
		}
		
		/// <summary>
		/// 添加对话条目
		/// </summary>
		public void AddEntry(DialogueEntry entry)
		{
			Entries.Add(entry);
		}
	}
}

