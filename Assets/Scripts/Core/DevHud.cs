using UnityEngine;

/// <summary>
/// TEMPORARY developer overlay (IMGUI): run status, power, time warp, and a bare-bones debrief.
/// It exists to exercise the run loop before the real UI (design kit, status bar, debrief screen) is built.
/// Created automatically in the editor and development builds only.
/// </summary>
public class DevHud : MonoBehaviour
{
    private static readonly float[] WarpMultipliers = { 1f, 10f, 30f, 60f };
    private static readonly GameClock.WarpUnit[] WarpUnits =
        { GameClock.WarpUnit.Seconds, GameClock.WarpUnit.Minutes, GameClock.WarpUnit.Hours, GameClock.WarpUnit.Days };
    private static readonly string[] WarpUnitLabels = { "s", "m", "h", "d" };

    private bool _collapsed;

    private void OnGUI()
    {
        GameState state = Game.State;
        RunController run = Game.Run;
        GameClock clock = Game.Clock;
        if (state == null || run == null || clock == null) return;

        const float w = 360f, expandedH = 176f, collapsedH = 26f;
        float h = _collapsed ? collapsedH : expandedH;
        GUILayout.BeginArea(new Rect(10f, Screen.height - h - 10f, w, h), GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"DEV  run #{run.RunNumber}  [{run.Phase}]");
        if (GUILayout.Button(_collapsed ? "▴" : "▾", GUILayout.Width(28f))) _collapsed = !_collapsed;
        GUILayout.EndHorizontal();

        if (!_collapsed)
        {
            string system = SystemManager.Current != null ? SystemManager.Current.CurrentSystemID : "-";
            GUILayout.Label($"System {system}   day {run.RunElapsedDays:F1}   flux {(SystemManager.Current != null ? SystemManager.Current.StellarFluxAtShip() : 0f):F4}");

            float net = run.IncomePerDay - run.LoadPerDay;
            GUILayout.Label($"Power {state.PowerStored:F1} / {state.PowerCapacity:F0}   "
                          + $"({net:+0.00;-0.00} /day = +{run.IncomePerDay:F2} solar  -{run.LoadPerDay:F2} load)");

            GUILayout.Label($"Warp {clock.WarpMultiplier:0}{WarpUnitLabels[System.Array.IndexOf(WarpUnits, clock.WarpUnitKind)]}"
                          + $"  ({clock.WarpFactor:0.##} sim-s/real-s){(clock.Paused ? " (paused)" : "")}");

            GUILayout.BeginHorizontal();
            foreach (float mult in WarpMultipliers)
                if (GUILayout.Button($"{mult:0}")) clock.SetWarp(mult, clock.WarpUnitKind);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            for (int i = 0; i < WarpUnits.Length; i++)
                if (GUILayout.Button(WarpUnitLabels[i])) clock.SetWarp(clock.WarpMultiplier, WarpUnits[i]);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Debug base rate:", GUILayout.Width(120f));
            bool fastDebug = clock.BaseRate > 1.0;
            if (GUILayout.Button(fastDebug ? $"x{GameConstants.TIME_MULTIPLIER:0} (DEBUG - click for real time)" : "real time x1 (click for debug 86400x)"))
                clock.BaseRate = fastDebug ? 1.0 : GameConstants.TIME_MULTIPLIER;
            GUILayout.EndHorizontal();
        }

        GUILayout.EndArea();

        if (run.Phase == RunPhase.Debrief) DrawDebrief(run);
    }

    private void DrawDebrief(RunController run)
    {
        RunSummary s = run.LastSummary;
        const float w = 360f, h = 190f;
        var rect = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);

        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label("DEBRIEF");
        GUILayout.Space(4f);
        GUILayout.Label($"Run #{s.runNumber}");
        GUILayout.Label(s.cause == RunEndCause.PowerDepleted
            ? "The probe ran out of power."
            : "The probe returned home.");
        GUILayout.Label($"Survived {s.durationDays:F1} days.");
        GUILayout.Space(10f);
        if (GUILayout.Button("Launch replacement probe", GUILayout.Height(30f)))
            run.BeginRun();
        GUILayout.EndArea();
    }
}
