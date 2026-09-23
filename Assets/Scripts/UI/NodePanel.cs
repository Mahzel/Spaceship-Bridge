using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The NODE tab of SystemsDock: plan a maneuver node (a time plus prograde/retrograde and normal
/// delta-v), preview the orbit it would produce without touching the ship, arm it so ManeuverPlan fires it
/// automatically when simulated time reaches it, and optionally warp straight there. Also hosts "plot a
/// transfer" - a basic Hohmann solver to whatever body is selected on the SYSTEM screen (Game.State.
/// TargetBodyName, set by clicking a row there).
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
            UIKit.AddButton(timeRow, TimePresets[i].label, () => _offsetSeconds += add, 0f, 34f);
        }
        UIKit.AddButton(timeRow, Loc.Get("ui.node.now"), () => _offsetSeconds = 0.0, 0f, 34f);

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

        // --- Transfer to selected target -------------------------------------------
        UIKit.AddLabel(root, Loc.Get("ui.node.transfer.header"), t.fontSizeSmall, t.accent);
        _target = UIKit.AddLabel(root, "", t.fontSizeSmall, t.text);
        UIKit.AddButton(root, Loc.Get("ui.node.transfer"), PlotTransfer, 0f, 34f);

        return root.gameObject;
    }

    private void Arm()
    {
        if (Game.State == null || Game.Clock == null) return;
        double when = Game.Clock.SimSeconds + _offsetSeconds;
        Game.State.Maneuver.SetSingle(when, _progradeKmS, _normalKmS);
    }

    private void ClearAll()
    {
        Game.State?.Maneuver.Clear();
        _progradeKmS = 0f;
        _normalKmS = 0f;
        _offsetSeconds = 0.0;
    }

    private void ToggleWarp()
    {
        ManeuverPlan mp = Game.State != null ? Game.State.Maneuver : null;
        if (mp == null) return;
        if (mp.WarpingToNode) mp.CancelWarp();
        else mp.StartWarpToNode();
    }

    private void PlotTransfer()
    {
        if (Game.State == null || Game.Clock == null) return;
        SystemManager sm = SystemManager.Current;
        if (sm == null || sm.CurrentData == null) return;

        NodeData target = FindTarget(sm.CurrentData, Game.State.TargetBodyName);
        if (target == null) return;

        if (ManeuverPlan.SolveHohmann(target, Game.Clock.SimSeconds, out ManeuverPlan.Node departure, out ManeuverPlan.Node arrival))
            Game.State.Maneuver.SetPair(departure, arrival);
    }

    private static NodeData FindTarget(SystemData sys, string name)
    {
        if (sys == null || string.IsNullOrEmpty(name)) return null;
        foreach (NodeData n in sys.nodes)
            if (n.name == name) return n;
        return null;
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
