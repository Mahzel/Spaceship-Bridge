using System.Linq;
using UnityEngine;

/// <summary>
/// Global access point to the game services. Screens and systems read Game.Clock / Game.State / Game.Run
/// instead of finding objects in the scene.
///
/// Bootstraps itself before the first scene loads, so no scene setup is required.
/// </summary>
public static class Game
{
    public static GameClock     Clock { get; private set; }
    public static GameState     State { get; private set; }
    public static RunController Run   { get; private set; }
    public static JumpDrive     Jump  { get; private set; }
    public static WakeMonitor   Wake  { get; private set; }

    // Keeps things correct when "Enter Play Mode without domain reload" is enabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Clock = null;
        State = null;
        Run   = null;
        Jump  = null;
        Wake  = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        Clock = new GameClock();
        State = new GameState();
        State.WorldSeed = PlayerPrefs.GetInt("BaseSeed", GameConstants.DEFAULT_BASE_SEED);

        // Specs live in Assets/Resources/Specs/. The baseline probe only has the lowest-tier waterfall.
        var specs = Resources.LoadAll<SensorSpec>("Specs");
        State.InstallBaseline(specs);

        var probeSpecs = Resources.LoadAll<ProbeSpec>("Specs");
        if (probeSpecs.Length > 0) State.Probe = probeSpecs[0];

        Run  = new RunController(Clock, State);
        State.Link.Now = () => Clock.SimSeconds;
        State.Link.RunNumber = () => Run != null ? Run.RunNumber : 1;
        State.Link.DistanceAu = () =>
        {
            double ly = Jump != null ? Jump.DistanceFromHomeLy * Transmitter.AuPerLy : 0.0;
            double au = System.Math.Sqrt(State.Ship.x * State.Ship.x + State.Ship.z * State.Ship.z) / 100.0;
            return System.Math.Sqrt(ly * ly + au * au);
        };
        Jump = new JumpDrive(Clock, State, Run);
        Wake = new WakeMonitor(Clock, State, Run);

        // Earlier versions created [Game] with HideFlags.DontSave, which survives leaving Play mode as a
        // frozen ghost (dead buttons, old values). Remove any leftover before creating the real one.
        foreach (GameObject leftover in Resources.FindObjectsOfTypeAll<GameObject>())
        {
            if (leftover.name == "[Game]" && (leftover.hideFlags & HideFlags.DontSave) != 0)
                Object.DestroyImmediate(leftover);
        }

        // DontDestroyOnLoad only: it is destroyed when Play mode ends, no ghost left behind.
        var go = new GameObject("[Game]");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<GameClockDriver>();
        go.AddComponent<RunDriver>();

        // Always-present UI: status bar + debrief (replaces the temporary DevHud).
        go.AddComponent<GameUI>();
    }
}

/// <summary>Advances the clock from unscaled real time, before any other script runs.</summary>
[DefaultExecutionOrder(-1000)]
public class GameClockDriver : MonoBehaviour
{
    private void Update()
    {
        Game.Clock?.Tick(Time.unscaledDeltaTime);
    }
}

/// <summary>Runs the expedition loop each frame, after SystemManager has positioned the bodies.</summary>
[DefaultExecutionOrder(-50)]
public class RunDriver : MonoBehaviour
{
    private void Update()
    {
        if (Game.Run == null) return;

        float flux = SystemManager.Current != null ? SystemManager.Current.StellarFluxAtShip() : 0f;
        Game.Run.Tick(flux);
        if (Game.Wake != null) Game.Wake.Poll();

        // Sensor processing runs every real frame regardless of which console mode is on screen.
        if (Game.State != null && Game.State.Waterfall != null) Game.State.Waterfall.Tick(Time.unscaledDeltaTime);
        if (Game.State != null && Game.State.Radar != null) Game.State.Radar.Tick(Time.unscaledDeltaTime);

        // Maneuver nodes fire the instant SimSeconds reaches them, which is what SystemManager (running
        // earlier this same frame, DefaultExecutionOrder -100) has already advanced ShipOrbit to.
        if (Game.State != null && Game.State.Maneuver != null) Game.State.Maneuver.Tick();
    }
}
