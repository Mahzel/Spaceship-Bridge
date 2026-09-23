using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wake conditions: what interrupts time warp. Toggle each condition, and step the thresholds.
/// (Sector-limited contact conditions come later.)
/// </summary>
public sealed class WakePanel
{
    private Button _new, _lost, _power, _loud, _sector;
    private TextMeshProUGUI _powerValue, _loudValue, _centerValue, _widthValue;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Wake", parent);
        UIKit.Size(prt, flexibleWidth: 1f);

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("ui.wake.hint"), t.fontSizeSmall, t.textDim);

        _new  = UIKit.AddButton(prt, Loc.Get("ui.wake.new"),  () => { W.newContact  = !W.newContact; },  0f, 34f);
        _lost = UIKit.AddButton(prt, Loc.Get("ui.wake.lost"), () => { W.lostContact = !W.lostContact; }, 0f, 34f);

        RectTransform powerRow = UIKit.Node("PowerRow", prt);
        UIKit.HStack(powerRow, 6f, 0, expandWidth: false);
        _power = UIKit.AddButton(powerRow, Loc.Get("ui.wake.power"), () => { W.lowPower = !W.lowPower; }, 250f, 34f);
        UIKit.AddButton(powerRow, "-", () => W.lowPowerPct = Mathf.Max(5f, W.lowPowerPct - 5f), 34f, 34f);
        _powerValue = UIKit.AddLabel(powerRow, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_powerValue.rectTransform, preferredWidth: 60f);
        UIKit.AddButton(powerRow, "+", () => W.lowPowerPct = Mathf.Min(90f, W.lowPowerPct + 5f), 34f, 34f);

        RectTransform loudRow = UIKit.Node("LoudRow", prt);
        UIKit.HStack(loudRow, 6f, 0, expandWidth: false);
        _loud = UIKit.AddButton(loudRow, Loc.Get("ui.wake.loud"), () => { W.loudContact = !W.loudContact; }, 250f, 34f);
        UIKit.AddButton(loudRow, "-", () => W.loudSigma = Mathf.Max(5f, W.loudSigma - 5f), 34f, 34f);
        _loudValue = UIKit.AddLabel(loudRow, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_loudValue.rectTransform, preferredWidth: 60f);
        UIKit.AddButton(loudRow, "+", () => W.loudSigma = Mathf.Min(60f, W.loudSigma + 5f), 34f, 34f);

        _sector = UIKit.AddButton(prt, Loc.Get("ui.wake.sector"), () => { W.useSector = !W.useSector; }, 0f, 34f);

        RectTransform centerRow = UIKit.Node("CenterRow", prt);
        var ch = UIKit.HStack(centerRow, 6f, 0, expandWidth: false);
        ch.childAlignment = TextAnchor.MiddleLeft;
        var cl = UIKit.AddLabel(centerRow, Loc.Get("ui.wake.center"), t.fontSizeSmall, t.textDim);
        UIKit.Size(cl.rectTransform, preferredWidth: 90f);
        UIKit.AddButton(centerRow, "-", () => W.sectorCenterDeg = BearingMath.Wrap360(W.sectorCenterDeg - 10f), 34f, 34f);
        _centerValue = UIKit.AddLabel(centerRow, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_centerValue.rectTransform, preferredWidth: 60f);
        UIKit.AddButton(centerRow, "+", () => W.sectorCenterDeg = BearingMath.Wrap360(W.sectorCenterDeg + 10f), 34f, 34f);
        UIKit.AddButton(centerRow, Loc.Get("ui.wake.ontrack"), OnTrack, 110f, 34f);

        RectTransform widthRow = UIKit.Node("WidthRow", prt);
        var wh = UIKit.HStack(widthRow, 6f, 0, expandWidth: false);
        wh.childAlignment = TextAnchor.MiddleLeft;
        var wl = UIKit.AddLabel(widthRow, Loc.Get("ui.wake.width"), t.fontSizeSmall, t.textDim);
        UIKit.Size(wl.rectTransform, preferredWidth: 90f);
        UIKit.AddButton(widthRow, "-", () => W.sectorHalfDeg = Mathf.Max(5f, W.sectorHalfDeg - 5f), 34f, 34f);
        _widthValue = UIKit.AddLabel(widthRow, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_widthValue.rectTransform, preferredWidth: 60f);
        UIKit.AddButton(widthRow, "+", () => W.sectorHalfDeg = Mathf.Min(90f, W.sectorHalfDeg + 5f), 34f, 34f);

        return prt.gameObject;
    }

    private static WakeMonitor W { get { return Game.Wake; } }

    // Centers the sector on the selected track's bearing.
    private static void OnTrack()
    {
        if (Game.State == null || W == null) return;
        TrackManager tm = Game.State.Tracks;
        for (int i = 0; i < tm.All.Count; i++)
            if (tm.All[i].id == tm.SelectedId) { W.sectorCenterDeg = tm.All[i].bearing; W.useSector = true; }
    }

    public void Refresh()
    {
        if (W == null || _new == null) return;
        UIKit.SetButtonActive(_new,   W.newContact);
        UIKit.SetButtonActive(_lost,  W.lostContact);
        UIKit.SetButtonActive(_power, W.lowPower);
        UIKit.SetButtonActive(_loud,  W.loudContact);
        UIKit.SetText(_powerValue, W.lowPowerPct.ToString("0") + "%");
        UIKit.SetText(_loudValue,  W.loudSigma.ToString("0") + " s");
        UIKit.SetButtonActive(_sector, W.useSector);
        UIKit.SetText(_centerValue, W.sectorCenterDeg.ToString("0") + "\u00B0");
        UIKit.SetText(_widthValue,  "\u00B1" + W.sectorHalfDeg.ToString("0") + "\u00B0");
    }
}
