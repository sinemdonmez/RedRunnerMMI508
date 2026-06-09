using System;
using System.Collections.Generic;
using UnityEngine;

namespace RedRunner.Analytics
{
	/// <summary>
	/// The thing that ended a run. <see cref="None"/> means the run is still
	/// in progress / no cause was reported yet.
	/// </summary>
	public enum ObstacleType
	{
		None,
		Saw,
		Mace,
		Spike,
		Water,
		Fall
	}

	/// <summary>
	/// One life: from <see cref="AnalyticsManager.RunStart"/> (StartGame) to
	/// <see cref="AnalyticsManager.RunEnd"/> (death).
	/// </summary>
	[Serializable]
	public struct RunRecord
	{
		public int RunIndex;
		public float DurationSeconds;
		public float Distance;
		public int Jumps;
		public int Coins;
		public float AverageSpeed;
		public float MaxSpeed;
		public ObstacleType DeathCause;
	}

	/// <summary>
	/// Lightweight, editor-playtest analytics collector.
	///
	/// This lives in the runtime assembly only so gameplay scripts can push data
	/// into it via a handful of one-line hooks. It performs no file IO and has no
	/// scene presence; the editor window (AnalyticsWindow) drives test start/stop
	/// and writes the CSVs. In a real build nothing ever calls StartTest, so the
	/// collector simply stays idle.
	/// </summary>
	public static class AnalyticsManager
	{
		public delegate void RunRecordedHandler(RunRecord record);
		public delegate void StateChangedHandler();

		/// <summary>Raised after a completed run is appended to <see cref="Runs"/>.</summary>
		public static event RunRecordedHandler OnRunRecorded;
		/// <summary>Raised when a test is started or ended.</summary>
		public static event StateChangedHandler OnStateChanged;

		// ----- Test context -----
		private static readonly List<RunRecord> m_Runs = new List<RunRecord>();
		public static IReadOnlyList<RunRecord> Runs { get { return m_Runs; } }
		public static bool TestActive { get; private set; }
		public static string TestId { get; private set; }
		public static string Tester { get; private set; }
		public static string Notes { get; private set; }
		public static DateTime StartedUtc { get; private set; }

		// ----- Current run accumulators -----
		private static bool m_RunActive;
		private static float m_RunTime;
		private static int m_Jumps;
		private static int m_CoinAtRunStart;
		private static float m_SpeedSum;
		private static int m_SpeedSamples;
		private static float m_MaxSpeed;
		private static ObstacleType m_DeathCause;

		// ----- Live getters for the editor window -----
		public static bool IsRunActive { get { return m_RunActive; } }
		public static float CurrentRunTime { get { return m_RunTime; } }
		public static int CurrentJumps { get { return m_Jumps; } }
		public static float CurrentMaxSpeed { get { return m_MaxSpeed; } }

		#region Test lifecycle (called by the editor window)

		public static void StartTest(string testId, string tester, string notes)
		{
			TestId = testId;
			Tester = tester;
			Notes = notes;
			StartedUtc = DateTime.UtcNow;
			m_Runs.Clear();
			TestActive = true;
			if (OnStateChanged != null)
			{
				OnStateChanged();
			}
		}

		public static void EndTest()
		{
			TestActive = false;
			if (OnStateChanged != null)
			{
				OnStateChanged();
			}
		}

		#endregion

		#region Run lifecycle (called by gameplay hooks)

		/// <summary>Hook: GameManager.StartGame() — a new life begins.</summary>
		public static void RunStart()
		{
			m_RunActive = true;
			m_RunTime = 0f;
			m_Jumps = 0;
			m_SpeedSum = 0f;
			m_SpeedSamples = 0;
			m_MaxSpeed = 0f;
			m_DeathCause = ObstacleType.None;
			m_CoinAtRunStart = CurrentCoin();
		}

		/// <summary>
		/// Hook: GameManager.DeathCrt() — the life ended. <paramref name="distance"/>
		/// is the final score (max X reached). The record is only stored while a
		/// test is active.
		/// </summary>
		public static void RunEnd(float distance)
		{
			if (!m_RunActive)
			{
				return;
			}
			m_RunActive = false;

			if (!TestActive)
			{
				return;
			}

			RunRecord record = new RunRecord
			{
				RunIndex = m_Runs.Count + 1,
				DurationSeconds = m_RunTime,
				Distance = distance,
				Jumps = m_Jumps,
				Coins = Mathf.Max(0, CurrentCoin() - m_CoinAtRunStart),
				AverageSpeed = m_SpeedSamples > 0 ? m_SpeedSum / m_SpeedSamples : 0f,
				MaxSpeed = m_MaxSpeed,
				DeathCause = m_DeathCause
			};
			m_Runs.Add(record);
			if (OnRunRecorded != null)
			{
				OnRunRecorded(record);
			}
		}

		/// <summary>
		/// Hook: RedCharacter.Update() — called once per frame while the game is
		/// running (the caller early-returns when paused/not started, so paused
		/// frames are naturally excluded from the run timer). <paramref name="speedX"/>
		/// is the absolute horizontal speed.
		/// </summary>
		public static void Tick(float speedX)
		{
			if (!m_RunActive)
			{
				return;
			}
			m_RunTime += Time.unscaledDeltaTime;
			float abs = Mathf.Abs(speedX);
			m_SpeedSum += abs;
			m_SpeedSamples++;
			if (abs > m_MaxSpeed)
			{
				m_MaxSpeed = abs;
			}
		}

		/// <summary>Hook: RedCharacter.Jump() — a successful (grounded) jump.</summary>
		public static void ReportJump()
		{
			if (m_RunActive)
			{
				m_Jumps++;
			}
		}

		/// <summary>
		/// Hook: enemy Kill() methods and the fall-death branch. First reported
		/// cause for the run wins.
		/// </summary>
		public static void ReportDeathCause(ObstacleType cause)
		{
			if (m_RunActive && m_DeathCause == ObstacleType.None)
			{
				m_DeathCause = cause;
			}
		}

		#endregion

		private static int CurrentCoin()
		{
			if (GameManager.Singleton != null && GameManager.Singleton.m_Coin != null)
			{
				return GameManager.Singleton.m_Coin.Value;
			}
			return 0;
		}
	}
}
