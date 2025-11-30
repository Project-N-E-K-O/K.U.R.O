using System;

namespace Kuros.UI
{
	public interface IPlayerStatsSource
	{
		event Action<int, int, int> StatsUpdated;

		int CurrentHealth { get; }
		int MaxHealth { get; }
		int Score { get; }
	}
}

