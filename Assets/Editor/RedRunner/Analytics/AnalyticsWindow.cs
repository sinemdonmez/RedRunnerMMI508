using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

using RedRunner.Analytics;

namespace RedRunner.EditorTools
{
	/// <summary>
	/// Simple offline playtest analytics window.
	///
	/// Workflow: enter Play mode, type a tester name, press "Start Test", let the
	/// tester play any number of runs, then press "Complete Test". Each run is
	/// appended to Analytics/playtest_runs.csv as it ends; a single aggregate row
	/// is appended to Analytics/playtest_sessions.csv when the test completes.
	///
	/// A test must be conducted within one continuous Play session (Unity clears
	/// runtime state when Play mode stops).
	/// </summary>
	public class AnalyticsWindow : EditorWindow
	{
		private const string RUNS_FILE = "playtest_runs.csv";
		private const string SESSIONS_FILE = "playtest_sessions.csv";

		private const string RUNS_HEADER =
			"timestamp_utc,test_id,tester,run_index,duration_s,distance,jumps,coins,avg_speed,max_speed,death_cause";
		private const string SESSIONS_HEADER =
			"test_id,tester,started_utc,completed_utc,runs,total_duration_s,avg_duration_s,best_distance,avg_distance,total_jumps,avg_jumps,total_coins,avg_speed,deaths_saw,deaths_mace,deaths_spike,deaths_water,deaths_fall,notes";

		private string m_Tester = "tester";
		private string m_Notes = "";
		private Vector2 m_Scroll;

		[MenuItem("Tools/RedRunner/Playtest Analytics")]
		public static void Open()
		{
			AnalyticsWindow window = GetWindow<AnalyticsWindow>(false, "Playtest Analytics");
			window.minSize = new Vector2(360f, 420f);
			window.Show();
		}

		private void OnEnable()
		{
			AnalyticsManager.OnRunRecorded += HandleRunRecorded;
			AnalyticsManager.OnStateChanged += Repaint;
		}

		private void OnDisable()
		{
			AnalyticsManager.OnRunRecorded -= HandleRunRecorded;
			AnalyticsManager.OnStateChanged -= Repaint;
		}

		private void HandleRunRecorded(RunRecord record)
		{
			AppendRunRow(record);
			Repaint();
		}

		private void OnInspectorUpdate()
		{
			// Keeps the live run timer/stats ticking while playing.
			if (Application.isPlaying)
			{
				Repaint();
			}
		}

		private void OnGUI()
		{
			m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);

			EditorGUILayout.LabelField("Offline Playtest Analytics", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"Data is collected only while playing in the Editor. Start a test, have the tester play, then complete it. CSVs are written to the project-root /Analytics folder.",
				MessageType.Info);

			EditorGUILayout.Space();

			bool active = AnalyticsManager.TestActive;

			using (new EditorGUI.DisabledScope(active))
			{
				m_Tester = EditorGUILayout.TextField("Tester", m_Tester);
				EditorGUILayout.LabelField("Notes");
				m_Notes = EditorGUILayout.TextArea(m_Notes, GUILayout.Height(40f));
			}

			EditorGUILayout.Space();

			if (!active)
			{
				using (new EditorGUI.DisabledScope(!Application.isPlaying || string.IsNullOrEmpty(m_Tester.Trim())))
				{
					if (GUILayout.Button("Start Test", GUILayout.Height(30f)))
					{
						StartTest();
					}
				}
				if (!Application.isPlaying)
				{
					EditorGUILayout.HelpBox("Enter Play mode to start a test.", MessageType.Warning);
				}
			}
			else
			{
				DrawActiveTest();
				EditorGUILayout.Space();
				if (GUILayout.Button("Complete Test", GUILayout.Height(30f)))
				{
					CompleteTest();
				}
			}

			EditorGUILayout.Space();
			DrawFooter();

			EditorGUILayout.EndScrollView();
		}

		private void DrawActiveTest()
		{
			EditorGUILayout.LabelField("Active test: " + AnalyticsManager.TestId, EditorStyles.boldLabel);

			int runs = AnalyticsManager.Runs.Count;
			float totalDuration = 0f, bestDistance = 0f;
			int totalJumps = 0;
			for (int i = 0; i < runs; i++)
			{
				RunRecord r = AnalyticsManager.Runs[i];
				totalDuration += r.DurationSeconds;
				totalJumps += r.Jumps;
				if (r.Distance > bestDistance)
				{
					bestDistance = r.Distance;
				}
			}

			EditorGUILayout.LabelField("Completed runs", runs.ToString());
			EditorGUILayout.LabelField("Avg run time", runs > 0 ? F(totalDuration / runs) + " s" : "-");
			EditorGUILayout.LabelField("Best distance", runs > 0 ? F(bestDistance) : "-");
			EditorGUILayout.LabelField("Total jumps", totalJumps.ToString());

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Current run", EditorStyles.miniBoldLabel);
			if (AnalyticsManager.IsRunActive)
			{
				EditorGUILayout.LabelField("  Time", F(AnalyticsManager.CurrentRunTime) + " s");
				EditorGUILayout.LabelField("  Jumps", AnalyticsManager.CurrentJumps.ToString());
				EditorGUILayout.LabelField("  Max speed", F(AnalyticsManager.CurrentMaxSpeed));
			}
			else
			{
				EditorGUILayout.LabelField("  (waiting for next run)");
			}
		}

		private void DrawFooter()
		{
			EditorGUILayout.LabelField("Output folder", EditorStyles.miniBoldLabel);
			EditorGUILayout.SelectableLabel(AnalyticsDirectory(), EditorStyles.textField, GUILayout.Height(16f));
			if (GUILayout.Button("Reveal in Finder/Explorer"))
			{
				string dir = AnalyticsDirectory();
				Directory.CreateDirectory(dir);
				EditorUtility.RevealInFinder(dir);
			}
		}

		#region Test lifecycle

		private void StartTest()
		{
			string tester = m_Tester.Trim();
			string testId = string.Format("{0}_{1}", Sanitize(tester), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
			AnalyticsManager.StartTest(testId, tester, m_Notes);
		}

		private void CompleteTest()
		{
			AppendSessionRow();
			AnalyticsManager.EndTest();
		}

		#endregion

		#region CSV writing

		private void AppendRunRow(RunRecord r)
		{
			string row = string.Join(",", new string[]
			{
				DateTime.UtcNow.ToString("o"),
				Csv(AnalyticsManager.TestId),
				Csv(AnalyticsManager.Tester),
				r.RunIndex.ToString(CultureInfo.InvariantCulture),
				F(r.DurationSeconds),
				F(r.Distance),
				r.Jumps.ToString(CultureInfo.InvariantCulture),
				r.Coins.ToString(CultureInfo.InvariantCulture),
				F(r.AverageSpeed),
				F(r.MaxSpeed),
				r.DeathCause.ToString()
			});
			Append(RUNS_FILE, RUNS_HEADER, row);
		}

		private void AppendSessionRow()
		{
			int runs = AnalyticsManager.Runs.Count;
			float totalDuration = 0f, bestDistance = 0f, totalDistance = 0f, speedSum = 0f;
			int totalJumps = 0, totalCoins = 0;
			int dSaw = 0, dMace = 0, dSpike = 0, dWater = 0, dFall = 0;

			for (int i = 0; i < runs; i++)
			{
				RunRecord r = AnalyticsManager.Runs[i];
				totalDuration += r.DurationSeconds;
				totalDistance += r.Distance;
				totalJumps += r.Jumps;
				totalCoins += r.Coins;
				speedSum += r.AverageSpeed;
				if (r.Distance > bestDistance)
				{
					bestDistance = r.Distance;
				}
				switch (r.DeathCause)
				{
					case ObstacleType.Saw: dSaw++; break;
					case ObstacleType.Mace: dMace++; break;
					case ObstacleType.Spike: dSpike++; break;
					case ObstacleType.Water: dWater++; break;
					case ObstacleType.Fall: dFall++; break;
				}
			}

			float div = runs > 0 ? runs : 1;
			string row = string.Join(",", new string[]
			{
				Csv(AnalyticsManager.TestId),
				Csv(AnalyticsManager.Tester),
				AnalyticsManager.StartedUtc.ToString("o"),
				DateTime.UtcNow.ToString("o"),
				runs.ToString(CultureInfo.InvariantCulture),
				F(totalDuration),
				F(totalDuration / div),
				F(bestDistance),
				F(totalDistance / div),
				totalJumps.ToString(CultureInfo.InvariantCulture),
				F(totalJumps / div),
				totalCoins.ToString(CultureInfo.InvariantCulture),
				F(speedSum / div),
				dSaw.ToString(CultureInfo.InvariantCulture),
				dMace.ToString(CultureInfo.InvariantCulture),
				dSpike.ToString(CultureInfo.InvariantCulture),
				dWater.ToString(CultureInfo.InvariantCulture),
				dFall.ToString(CultureInfo.InvariantCulture),
				Csv(AnalyticsManager.Notes)
			});
			Append(SESSIONS_FILE, SESSIONS_HEADER, row);
		}

		private static void Append(string fileName, string header, string row)
		{
			string dir = AnalyticsDirectory();
			Directory.CreateDirectory(dir);
			string path = Path.Combine(dir, fileName);
			bool isNew = !File.Exists(path);
			using (StreamWriter writer = new StreamWriter(path, true, Encoding.UTF8))
			{
				if (isNew)
				{
					writer.WriteLine(header);
				}
				writer.WriteLine(row);
			}
		}

		#endregion

		#region Helpers

		private static string AnalyticsDirectory()
		{
			// Application.dataPath ends in ".../Assets"; go one up to the project root.
			string projectRoot = Directory.GetParent(Application.dataPath).FullName;
			return Path.Combine(projectRoot, "Analytics");
		}

		private static string F(float value)
		{
			return value.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "";
			}
			if (value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0)
			{
				return "\"" + value.Replace("\"", "\"\"") + "\"";
			}
			return value;
		}

		private static string Sanitize(string value)
		{
			StringBuilder sb = new StringBuilder(value.Length);
			foreach (char c in value)
			{
				sb.Append(char.IsLetterOrDigit(c) ? c : '-');
			}
			return sb.Length > 0 ? sb.ToString() : "tester";
		}

		#endregion
	}
}
