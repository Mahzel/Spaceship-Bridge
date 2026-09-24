using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The NODES minor mode of Navigation (UI shell rework - previously the NODE tab of SystemsDock): plan a
/// maneuver node (a time plus prograde/retrograde and normal delta-v), preview the orbit it would produce
/// without touching the ship, arm it so ManeuverPlan fires it automatically when simulated time reaches it,
/// and optionally warp straight there. Also hosts "plot a transfer" - a basic Hohmann solver to whatever
/// track is selected (Track strip, ContactsScreen or the NAV map all set TrackManager.SelectedId). Uses
/// OrbitFit.TryFit off the track's own range estimate, never a catalog/NodeData lookup - no identification
/// required, just a usable range (see OrbitFit's doc).
/// </summary>
public sealed class NodePanel
{
    private static readonly float[] BurnSizes = { 0.1f, 1f, 5f, 20f };
    private static readonly (float mult, string label)[] TimePresets =
    {
        (3600f,        "+1H"),
        (86400f,       "+1D"),
        (7f * 86400f,  "+7D"),
        (30f * 86400f, "+30D"),
    };

    private TextMeshProUGUI _time, _prograde, _normal, _previewLine, _previewShape, _queueLine, _target;
    private Button[] _sizeButtons;
    private Button _warpButton;
    private TextMeshProUGUI _warpLabel;

    private int _sizeIndex = 1;
    private double _offsetSeconds;
    private bool _timeChosen; // has the player touched the time row (a preset, or NOW) since the last Arm/Clear?
    private float _progradeKmS, _normalKmS;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Node", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        // --- Time offset -----------------------------------------------------
        _time = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        RectTransform timeRow = UIKit.Node("TimeRow", root);
        UIKit.HStack(timeRow, 6f, 0, expandWidth: true);
        for (int i = 0; i < TimePresets.Length; i++)
        {
            float add = TimePresets[i].mult;
            UIKit.AddButton(timeRow, TimePresets[i].label, () => { _offsetSeconds += add; _timeChosen = true; }, 0f, 34f);
        }
        UIKit.AddButton(timeRow, Loc.Get("ui.node.now"), () => { _offsetSeconds = 0.0; _timeChosen = true; }, 0f, 34f);

        // --- Burn size selector (mirrors ManeuverPanel) -----------------------
        UIKit.AddLabel(root, Loc.Get("ui.burnsize"), t.fontSizeSmall, t.textDim);
        RectTransform sizeRow = UIKit.Node("SizeRow", root);
        UIKit.HStack(sizeRow, 6f, 0, expandWidth: true);
        _sizeButtons = new Button[BurnSizes.Length];
        for (int i = 0; i < BurnSizes.Length; i++)
        {
            int index = i;
            _sizeButtons[i] = UIKit.AddButton(sizeRow, BurnSizes[i].ToString("0.#"), () => _sizeIndex = index, 0f, 34f);
        }

        // --- Prograde/retrograde ----------------------------------------------
        _prograde = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        RectTransform proRow = UIKit.Node("ProRow", root);
        UIKit.HStack(proRow, 6f, 0, expandWidth: true);
        UIKit.AddButton(proRow, "-", () => _progradeKmS -= BurnSizes[_sizeIndex], 0f, 34f);
        UIKit.AddButton(proRow, "+", () => _progradeKmS += BurnSizes[_sizeIndex], 0f, 34f);

        // --- Normal (plane change) --------------------------------------------
        _normal = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        RectTransform nrmRow = UIKit.Node("NrmRow", root);
        UIKit.HStack(nrmRow, 6f, 0, expandWidth: true);
        UIKit.AddButton(nrmRow, "-", () => _normalKmS -= BurnSizes[_sizeIndex], 0f, 34f);
        UIKit.AddButton(nrmRow, "+", () => _normalKmS += BurnSizes[_sizeIndex], 0f, 34f);

        // --- Live preview -------------------------------------------------------
        _previewLine  = UIKit.AddLabel(root, "", t.fontSizeSmall, t.textDim);
        _previewShape = UIKit.AddLabel(root, "", t.fontSizeSmall, t.textDim);

        // --- Arm / clear / warp ---------------------------------------------------
        RectTransform armRow = UIKit.Node("ArmRow", root);
        UIKit.HStack(armRow, 6f, 0, expandWidth: true);
        UIKit.AddButton(armRow, Loc.Get("ui.node.arm"), Arm, 0f, 34f);
        UIKit.AddButton(armRow, Loc.Get("ui.node.clear"), ClearAll, 0f, 34f);
        _warpButton = UIKit.AddButton(armRow, Loc.Get("ui.node.warp"), ToggleWarp, 0f, 34f);
        _warpLabel = _warpButton.GetComponentInChildren<TextMeshProUGUI>();

        _queueLine = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        TextMeshProUGUI armHint = UIKit.AddLabel(root, Loc.Get("ui.node.arm.hint"), t.fontSizeSmall, t.textDim);
        armHint.textWrappingMode = TextWrappingModes.Normal;
        UIKit.Size(armHint.rectTransform, preferredWidth: 380f);

        // --- Transfer to selected target -------------------------------------------
        UIKit.AddLabel(root, Loc.Get("ui.node.transfer.header"), t.fontSizeSmall, t.accent);
        _target = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        UIKit.AddButton(root, Loc.Get("ui.node.transfer"), PlotTransfer, 0f, 34f);

        return root.gameObject;
    }

    /// <summary>
    /// The time row defaults to (and NOW resets to) an offset of 0 - "right now". ManeuverPlan.Tick() fires
    /// any node whose time is already <= the current sim time on the very next real frame, so arming at an
    /// untouched 0 offset used to burn (and pop) the node before the player ever saw it queued or could reach
    /// WARP TO NODE - it just vanished, taking the warp button's interactability with it. Only an offset the
    /// player picked ON PURPOSE (a +preset, or an explicit NOW click) may still be 0; an untouched panel gets
    /// bumped to the first preset instead of silently instant-firing.
    ///
    /// Also refuses to arm a ZERO-dv node (both steppers still at their default 0): this button and PLOT
    /// TRANSFER/CREATE NODES all write to the SAME single queue (ManeuverPlan has no separate "planned" vs
    /// "armed" state - queuing IS arming, see its own doc comment), so pressing this ARM button untouched,
    /// after already plotting a transfer, used to silently REPLACE the real transfer plan with a burn that
    /// does nothing (Execute already no-ops below the dv floor) - the node still gets popped and "consumed"
    /// off the queue when its time arrives, so it looked exactly like "the burn didn't execute". If you want
    /// to hand-set a burn, touch the PROGRADE/NORMAL steppers first.
    /// </summary>
    private void Arm()
    {
        if (Game.State == null || Game.Clock == null) return;
        if (Mathf.Abs(_progradeKmS) < 1e-6f && Mathf.Abs(_normalKmS) < 1e-6f) return;
        if (!_timeChosen) _offsetSeconds = TimePresets[0].mult;
        double when = Game.Clock.SimSeconds + _offsetSeconds;
        Game.State.Maneuver.SetSingle(when, _progradeKmS, _normalKmS);
    }

    private void ClearAll()
    {
        Game.State?.Maneuver.Clear();
        _progradeKmS = 0f;
        _normalKmS = 0f;
        _offsetSeconds = 0.0;
        _timeChosen = false;
    }

    private void ToggleWarp()
    {
        ManeuverPlan mp = Game.State != null ? Game.State.Maneuver : null;
        if (mp == null) return;
        if (mp.WarpingToNode) mp.CancelWarp();
        else mp.StartWarpToNode();
    }

    /// <summary>Plots to whatever track is selected, using ONLY what it has itself measured (OrbitFit.TryFit,
    /// off the track's own range estimate) - never a catalog/NodeData lookup. No identification required:
    /// range is enough to have a rough orbit and a rough burn. A bad fit makes a bad burn; that's the game.</summary>
    private void PlotTransfer()
    {
        if (Game.State == null || Game.Clock == null) return;
        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr == null || !OrbitFit.TryFit(tr, out OrbitElements target)) return;

        if (ManeuverPlan.SolveHohmann(target, Game.Clock.SimSeconds, out ManeuverPlan.Node departure, out ManeuverPlan.Node arrival))
            Game.State.Maneuver.SetPair(departure, arrival);
    }

    public void Refresh()
    {
        if (_time == null) return;
        GameState state = Game.State;

        UIKit.SetText(_time, Loc.Get("ui.node.time", FormatOffset(_offsetSeconds)));
        for (int i = 0; i < _sizeButtons.Length; i++)
            UIKit.SetButtonActive(_sizeButtons[i], i == _sizeIndex);

        UIKit.SetText(_prograde, Loc.Get("ui.node.prograde", _progradeKmS));
        UIKit.SetText(_normal,   Loc.Get("ui.node.normal", _normalKmS));

        if (state != null && Game.Clock != null)
        {
            ManeuverPlan.Preview p = ManeuverPlan.PreviewNode(Game.Clock.SimSeconds + _offsetSeconds, _progradeKmS, _normalKmS);
            if (p.valid)
            {
                UIKit.SetText(_previewLine, Loc.Get("ui.orbit.apsides", p.periapsisAu, p.apoapsisAu));
                UIKit.SetText(_previewShape, Loc.Get("ui.node.preview.shape", p.eccentricity, p.inclinationDeg, p.periodDays));
            }
            else
            {
                UIKit.SetText(_previewLine, Loc.Get("ui.node.preview.none"));
                UIKit.SetText(_previewShape, "");
            }
        }

        ManeuverPlan mp = state != null ? state.Maneuver : null;
        if (mp != null && mp.Armed)
        {
            ManeuverPlan.Node next = mp.Next.Value;
            double dtDays = Game.Clock != null ? (next.simSeconds - Game.Clock.SimSeconds) / 86400.0 : 0.0;
            UIKit.SetText(_queueLine, Loc.Get("ui.node.queue", dtDays, next.TotalDvKmS, mp.QueueCount));
        }
        else
        {
            UIKit.SetText(_queueLine, Loc.Get("ui.node.queue.none"));
        }

        if (_warpLabel != null)
            UIKit.SetText(_warpLabel, mp != null && mp.WarpingToNode ? Loc.Get("ui.node.warp.cancel") : Loc.Get("ui.node.warp"));
        if (_warpButton != null)
            _warpButton.interactable = mp != null && (mp.Armed || mp.WarpingToNode);

        UIKit.SetText(_target, string.IsNullOrEmpty(state?.TargetBodyName)
            ? Loc.Get("ui.node.target.none")
            : Loc.Get("ui.node.target", state.TargetBodyName));
    }

    private static string FormatOffset(double seconds)
    {
        if (seconds < 60.0) return seconds.ToString("F0") + "s";
        if (seconds < 3600.0) return (seconds / 60.0).ToString("F0") + "m";
        if (seconds < 86400.0) return (seconds / 3600.0).ToString("F1") + "h";
        return (seconds / 86400.0).ToString("F1") + "d";
    }
}
