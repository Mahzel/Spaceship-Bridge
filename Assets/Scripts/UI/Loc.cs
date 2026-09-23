using System.Collections.Generic;

/// <summary>
/// All player-facing strings go through Loc.Get(key, args). Today it reads a built-in English table;
/// later the body of Get() can be swapped for the Unity Localization package without touching any screen.
/// Args are string.Format arguments ({0}, {1:F1}...).
/// </summary>
public static class Loc
{
    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        // Systems dock tabs
        { "ui.tab.jump",    "JUMP" },
        { "ui.tab.wake",    "WAKE" },
        { "ui.tab.reactor", "REACTOR" },
        { "ui.tab.data",    "DATA" },
        { "ui.tab.tx",      "TX" },
        { "ui.tab.node",    "NODE" },

        // Sensor console modes
        { "ui.screen.waterfall",     "WATERFALL" },
        { "ui.screen.imager",        "IMAGER" },
        { "ui.screen.spectrometer",  "SPECTROMETER" },
        { "ui.screen.system",        "SYSTEM" },
        { "ui.screen.radar",         "RADAR" },
        { "ui.screen.power.on",      "SENSOR ON" },
        { "ui.screen.power.off",     "SENSOR OFF" },

        // Radar
        { "ui.radar.mode.sweep",       "SWEEP" },
        { "ui.radar.mode.track",       "TRACK" },
        { "ui.radar.beam",             "BEAM +/-" },
        { "ui.radar.beam.value",       "{0:F0}deg" },
        { "ui.radar.target",           "TARGET: {0}" },
        { "ui.radar.target.none",      "TARGET: none selected" },
        { "ui.radar.fire",             "FIRE PING" },
        { "ui.radar.pinging",          "PINGING... eta ~{0:F0}s" },
        { "ui.radar.noreturn",         "no return" },
        { "ui.radar.result.range",     "RANGE {0:F2} AU" },
        { "ui.radar.result.rangerate", "RANGE {0:F2} AU   RATE {1:+0.00;-0.00} km/s" },

        // System overview table
        { "ui.system.none",     "No bodies detected." },
        { "ui.system.col.name",  "NAME" },
        { "ui.system.col.class", "CLASS" },
        { "ui.system.col.az",   "AZ" },
        { "ui.system.col.el",   "EL" },
        { "ui.system.col.dist", "DIST" },

        // Status bar
        { "ui.power",    "POWER" },
        { "ui.hydrogen", "HYDROGEN" },
        { "ui.storage",  "STORAGE" },
        { "ui.run",      "RUN {0}   -   DAY {1:F1}" },
        { "ui.system",   "SYSTEM {0}" },
        { "ui.net",      "{0:+0.00;-0.00} /day\nsolar +{1:F2}  reactor +{3:F2}  load -{2:F2}" },
        { "ui.pause",    "II" },

        // Tracks
        { "ui.tracks",         "TRACKS" },
        { "ui.tracks.none",    "No contact." },
        { "ui.track.bearing",  "{0:F1} deg" },
        { "ui.track.rate",     "{0:+0.00;-0.00} deg/d" },
        { "ui.track.norate",   "-- deg/d" },
        { "ui.track.quality",  "{0:F0}%" },
        { "ui.track.searching","searching..." },
        { "ui.track.drop",     "x" },
        { "ui.track.nextname",     "NEXT" },
        { "ui.track.nextnamehint", "name" },
        { "ui.track.markhint",     "click waterfall/DSP to mark" },
        { "ui.heading.tick",       "HDG" },

        { "ui.track.range",    "R {0:F1} AU +/-{1:F0}%" },
        { "ui.track.norange",  "R --" },

        // Orbit
        { "ui.orbit",          "ORBIT" },
        { "ui.orbit.none",     "No stable orbit." },
        { "ui.orbit.primary",  "orbiting {0}" },
        { "ui.orbit.apsides",  "PERI {0:F2} AU   APO {1:F2} AU" },
        { "ui.orbit.shape",    "e {0:F3}   i {1:F1} deg" },
        { "ui.orbit.period",   "period {0:F1} d" },
        { "ui.orbit.nu",       "true anomaly {0:F0} deg" },

        // Maneuver node planning
        { "ui.node.time",             "T+ {0}" },
        { "ui.node.now",              "NOW" },
        { "ui.node.prograde",         "PROGRADE {0:+0.00;-0.00} km/s" },
        { "ui.node.normal",           "NORMAL {0:+0.00;-0.00} km/s" },
        { "ui.node.preview.none",     "No preview - orbit not valid." },
        { "ui.node.preview.shape",    "e {0:F3}   i {1:F1} deg   period {2:F1} d" },
        { "ui.node.arm",              "ARM" },
        { "ui.node.clear",            "CLEAR" },
        { "ui.node.warp",             "WARP TO NODE" },
        { "ui.node.warp.cancel",      "CANCEL WARP" },
        { "ui.node.queue",            "Node in {0:F1} d   dv {1:F2} km/s   ({2} queued)" },
        { "ui.node.queue.none",       "No node armed." },
        { "ui.node.target",           "TARGET: {0}" },
        { "ui.node.target.none",      "TARGET: none selected" },
        { "ui.node.transfer",         "PLOT TRANSFER" },
        { "ui.node.transfer.header",  "TRANSFER" },

        // Maneuver
        { "ui.maneuver",  "MANEUVER" },
        { "ui.heading",   "HEADING {0:F1} deg" },
        { "ui.velocity",  "VELOCITY {0:F2} km/s   BRG {1:F1} deg" },
        { "ui.burnsize",  "BURN SIZE (km/s)" },
        { "ui.burn.fwd",  "BURN" },
        { "ui.burn.retro","RETRO" },
        { "ui.burn.cost", "Cost {0:F1} H   (have {1:F1})" },

        // Jump
        { "ui.jump",       "JUMP DRIVE" },
        { "ui.jump.none",  "No system in range." },
        { "ui.jump.at",    "AT {0}   -   {1:F1} ly from home" },
        { "ui.jump.home",  "HOME" },
        { "ui.jump.dist",  "{0:F1} ly" },
        { "ui.jump.cost",  "{0:F0} H" },
        { "ui.jump.time",  "{0:F0} d" },
        { "ui.jump.back",  "back {0:F0} H" },
        { "ui.jump.go",    "JUMP" },

        // Wake conditions
        { "ui.wake",       "WAKE CONDITIONS" },
        { "ui.wake.hint",  "Time warp drops to x1 when one trips." },
        { "ui.wake.new",   "New contact" },
        { "ui.wake.lost",  "Contact lost" },
        { "ui.wake.power", "Power below" },
        { "ui.wake.loud",  "Contact louder than" },
        { "wake.new",      "WAKE: new contact {0}" },
        { "wake.lost",     "WAKE: contact {0} lost" },
        { "wake.loud",     "WAKE: {0} at {1:F0} sigma" },
        { "wake.power",    "WAKE: power at {0:F0}%" },

        // Rename / sector / reactor
        { "ui.track.name",      "Name" },
        { "ui.track.namehint",  "select a track" },
        { "ui.wake.sector",     "Limit new/loud contacts to a sector" },
        { "ui.wake.center",     "Center" },
        { "ui.wake.width",      "Half-width" },
        { "ui.wake.ontrack",    "On track" },
        { "ui.reactor",         "REACTOR" },
        { "ui.reactor.off",     "OFF" },
        { "ui.reactor.info",    "+{0:F2} power/day, burns {1:F3} H/day" },

        // Data / recording
        { "ui.data",            "DATA" },
        { "ui.data.sel",        "Selected: {0}" },
        { "ui.data.nosel",      "Select a confirmed track to record it." },
        { "ui.data.stub",       "Log stub" },
        { "ui.data.raw",        "Record raw" },
        { "ui.data.rawstop",    "Stop raw" },
        { "ui.data.compress",   "Compressed" },
        { "ui.data.free",       "Free {0:F1} / {1:F0}    value on board {2:F1}" },
        { "ui.data.none",       "Nothing recorded." },
        { "ui.data.dump",       "x" },
        { "ui.data.kind.stub",  "stub" },
        { "ui.data.kind.raw",   "raw" },
        { "ui.data.kind.rawc",  "raw (c)" },
        { "ui.data.rowname",    "{0}  {1}  {2}" },
        { "ui.data.size",       "{0:F1} u" },
        { "ui.data.value",      "v {0:F1}" },
        { "ui.data.rec",        "REC" },
        { "ui.data.lost",       "track lost" },
        { "ui.data.full",       "full" },
        { "debrief.data",       "Data on board: {0} records, value {1:F1}, storage {2:F1}." },

        // Transmitter
        { "ui.tx",              "TRANSMIT HOME" },
        { "ui.tx.dist",         "Home is {0:F3} ly away" },
        { "ui.tx.level",        "Power" },
        { "ui.tx.robust",       "Robust coding" },
        { "ui.tx.margin",       "Link margin {0:+0.0;-0.0} dB   (about {1:F0}% of packets lost)" },
        { "ui.tx.energy",       "Cost {0:F1} power per data unit sent" },
        { "ui.tx.hint",         "Sending keeps your copy. What arrived is only known at debrief." },
        { "ui.tx.count",        "Sent this run: {0} transmissions" },
        { "ui.tx.send",         "tx" },
        { "ui.tx.sent",         "sent {0}" },
        { "tx.power",           "Not enough power to transmit that." },
        { "debrief.tx",         "Transmitted {0} times: {1} of {2} packets got through, value received {3:F1}." },
        { "debrief.gasp",       "The dying probe sent {0} record(s) with its last reserve." },
        { "debrief.returned",   "Carried home: value {0:F1}." },
        { "debrief.total",      "Total value recovered: {0:F1}." },

        // Atlas
        { "ui.atlas",           "ATLAS" },
        { "ui.atlas.count",     "ATLAS ({0})" },
        { "ui.atlas.none",      "Nothing logged yet." },
        { "ui.atlas.col.system","SYSTEM" },
        { "ui.atlas.col.name",  "NAME" },
        { "ui.atlas.col.kind",  "KIND" },
        { "ui.atlas.col.value", "VALUE" },
        { "ui.atlas.col.status","STATUS" },
        { "ui.atlas.run",       "run {0}" },
        { "ui.atlas.wrong",     "FLAGGED WRONG" },

        // Debrief
        { "debrief.title",       "DEBRIEF" },
        { "debrief.run",         "Run #{0}" },
        { "debrief.cause.power", "The probe ran out of power." },
        { "debrief.cause.home",  "The probe returned home." },
        { "debrief.duration",    "Survived {0:F1} days." },
        { "debrief.relaunch",    "Launch replacement probe" },
    };

    public static string Get(string key, params object[] args)
    {
        string s;
        if (!English.TryGetValue(key, out s)) return "[" + key + "]";
        return args != null && args.Length > 0 ? string.Format(s, args) : s;
    }
}
