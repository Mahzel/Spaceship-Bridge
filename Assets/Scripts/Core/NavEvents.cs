using System.Collections.Generic;

public enum NavEventKind { Periapsis, Apoapsis, Burn }
public enum NavEventSeverity { Info, Warning, Critical }

/// <summary>One upcoming event on the ship's own timeline - see NavEvents.Collect.</summary>
public struct NavEvent
{
    public double time; // sim seconds
    public NavEventKind kind;
    public string label;
    public NavEventSeverity severity;
}

/// <summary>
/// Roadmap item 4 (handoff-navigation-ui.md), scoped down to what's cheaply and honestly computable from data
/// already on hand: the ship's own next periapsis/apoapsis passage (exact - ShipOrbit's own conic, the same
/// mean-anomaly math OrbitPanel already reads), flagged Critical instead of Info when periapsis is actually
/// below the primary's own surface radius (an item 8 "alert", folded into the same event rather than a
/// separate list - see the periapsis block's own comment for the unit conversion), and each queued maneuver
/// node's burn time (ManeuverPlan, exact - it's the player's own plan). Sorted soonest first.
///
/// NOT generated here yet, each needing its own prediction machinery this pass didn't build (see the doc):
/// SOI changes, closest approach to the selected track's fitted orbit, comms windows/blackouts, jump windows.
/// The strip and its "WARP TO" action read this one list, per the roadmap's own instruction that event
/// generation should live in one function every consumer shares.
/// </summary>
public static class NavEvents
{
    public static void Collect(List<NavEvent> result)
    {
        result.Clear();
        GameState state = Game.State;
        GameClock clock = Game.Clock;
        if (state == null || clock == null) return;
        double now = clock.SimSeconds;

        ShipOrbit orbit = state.ShipOrbit;
        if (orbit != null && orbit.Valid && !orbit.Hyperbolic && orbit.Elements.orbitalPeriod > 0.0)
        {
            double period = orbit.Elements.orbitalPeriod;
            double mDeg = KeplerOrbit.MeanAnomalyDeg(orbit.Elements, now);
            double toPeriapsis = Wrap360(360.0 - mDeg) / 360.0 * period;
            double toApoapsis = Wrap360(180.0 - mDeg) / 360.0 * period;

            // Impact risk: the periapsis altitude against the primary's own surface radius, both converted to
            // game units the same way (radiusSol -> metres -> AU -> game units, GameConstants' own real-unit
            // constants - the same conversion AtlasPanel already uses the metres/AU half of for body details,
            // just carried one step further into game units). Atmosphere drag/entry isn't modelled here -
            // this only catches an outright surface impact, not "close enough to burn up".
            bool impact = false;
            SystemManager sm = SystemManager.Current;
            if (sm != null && sm.CurrentData != null && orbit.PrimaryIndex >= 0 && orbit.PrimaryIndex < sm.CurrentData.nodes.Count)
            {
                NodeData primary = sm.CurrentData.nodes[orbit.PrimaryIndex];
                float radiusGameUnits = primary.radiusSol * GameConstants.SOLAR_RADIUS_IN_METERS
                                       / GameConstants.AU_IN_METERS * GameConstants.GAME_UNITS_PER_UA;
                impact = orbit.PeriapsisGame < radiusGameUnits;
            }

            result.Add(new NavEvent
            {
                time = now + toPeriapsis,
                kind = NavEventKind.Periapsis,
                severity = impact ? NavEventSeverity.Critical : NavEventSeverity.Info,
                label = Loc.Get(impact ? "ui.nav.event.periapsis.impact" : "ui.nav.event.periapsis")
            });
            result.Add(new NavEvent { time = now + toApoapsis, kind = NavEventKind.Apoapsis, label = Loc.Get("ui.nav.event.apoapsis") });
        }

        ManeuverPlan mp = state.Maneuver;
        if (mp != null)
        {
            IList<ManeuverPlan.Node> queue = mp.QueueForSave;
            for (int i = 0; i < queue.Count; i++)
            {
                ManeuverPlan.Node n = queue[i];
                result.Add(new NavEvent
                {
                    time = n.simSeconds,
                    kind = NavEventKind.Burn,
                    label = Loc.Get("ui.nav.event.burn", i + 1, n.TotalDvKmS)
                });
            }
        }

        result.Sort((a, b) => a.time.CompareTo(b.time));
    }

    private static double Wrap360(double deg)
    {
        deg %= 360.0;
        return deg < 0.0 ? deg + 360.0 : deg;
    }
}
