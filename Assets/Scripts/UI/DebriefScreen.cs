using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen dimmed overlay with a centered panel, shown while the run phase is Debrief.
/// For now it shows the outcome and the relaunch button; results (data returned, trust, atlas) plug in here.
/// </summary>
public sealed class DebriefScreen
{
    private GameObject _root;
    private TextMeshProUGUI _run, _cause, _duration, _data, _tx, _gasp, _returned, _total;
    private TextMeshProUGUI _review, _trust, _changes;
    private const int MaxChangeLines = 8;

    public void Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        Image dim = UIKit.AddPanel(parent, "Debrief", t.dimColor);
        dim.raycastTarget = true; // blocks clicks on everything below
        UIKit.Stretch(dim.rectTransform);
        _root = dim.gameObject;

        Image panel = UIKit.AddPanel(dim.transform, "Panel", t.panelColor);
        RectTransform prt = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(640f, 0f);

        var v = UIKit.VStack(prt, t.spacing, (int)(t.padding * 2f));
        v.childAlignment = TextAnchor.UpperCenter;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        UIKit.AddLabel(prt, Loc.Get("debrief.title"), t.fontSizeTitle, t.accent, TextAlignmentOptions.Center);
        UIKit.AddSpacer(prt, 8f);
        _run      = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim, TextAlignmentOptions.Center);
        _cause    = UIKit.AddLabel(prt, "", t.fontSizeBody, t.text,    TextAlignmentOptions.Center);
        _duration = UIKit.AddLabel(prt, "", t.fontSizeBody, t.text,    TextAlignmentOptions.Center);
        _data     = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim, TextAlignmentOptions.Center);
        _tx       = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim, TextAlignmentOptions.Center);
        _gasp     = UIKit.AddLabel(prt, "", t.fontSizeBody, t.warning, TextAlignmentOptions.Center);
        _returned = UIKit.AddLabel(prt, "", t.fontSizeBody, t.text,    TextAlignmentOptions.Center);
        _total    = UIKit.AddLabel(prt, "", t.fontSizeTitle, t.good,    TextAlignmentOptions.Center);
        UIKit.AddSpacer(prt, 8f);
        _review   = UIKit.AddLabel(prt, "", t.fontSizeBody, t.textDim, TextAlignmentOptions.Center);
        _trust    = UIKit.AddLabel(prt, "", t.fontSizeBody, t.text,    TextAlignmentOptions.Center);
        _changes  = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim, TextAlignmentOptions.Center);
        _changes.textWrappingMode = TextWrappingModes.Normal;
        _changes.richText = true;
        UIKit.AddSpacer(prt, 16f);
        UIKit.AddButton(prt, Loc.Get("debrief.relaunch"), () =>
        {
            if (Game.Run != null) Game.Run.BeginRefit(); // choose the next probe's loadout, then launch
        }, 0f, 52f);
        UIKit.AddButton(prt, Loc.Get("menu.mainmenu"), Game.ReturnToMainMenu, 0f, 40f);

        _root.SetActive(false);
    }

    public void Refresh()
    {
        if (_root == null) return;

        RunController run = Game.Run;
        bool show = run != null && run.Phase == RunPhase.Debrief && run.LastSummary != null;
        if (_root.activeSelf != show) _root.SetActive(show);
        if (!show) return;

        RunSummary s = run.LastSummary;
        UIKit.SetText(_run, Loc.Get("debrief.run", s.runNumber));
        UIKit.SetText(_cause, Loc.Get(s.cause == RunEndCause.PowerDepleted ? "debrief.cause.power" : "debrief.cause.home"));
        UIKit.SetText(_duration, Loc.Get("debrief.duration", s.durationDays));
        UIKit.SetText(_data, Loc.Get("debrief.data", s.dataRecords, s.dataValue, s.storageUsed));
        UIKit.SetText(_tx, s.txCount > 0 ? Loc.Get("debrief.tx", s.txCount, s.txPacketsOk, s.txPackets, s.txValueReceived) : "");
        UIKit.SetText(_gasp, s.lastGaspRecords > 0 ? Loc.Get("debrief.gasp", s.lastGaspRecords) : "");
        UIKit.SetText(_returned, s.cause == RunEndCause.ReturnedHome ? Loc.Get("debrief.returned", s.returnedValue) : "");
        UIKit.SetText(_total, Loc.Get("debrief.total", s.totalValue));

        UITheme t = UITheme.Current;
        UIKit.SetText(_review, Loc.Get("debrief.review", s.entriesReviewed, s.entriesDisputed, s.entriesCorrected));
        float dt = s.trustAfter - s.trustBefore;
        UIKit.SetText(_trust, Loc.Get("debrief.trust", s.trustBefore, s.trustAfter, dt));
        _trust.color = dt >= 0f ? t.good : t.danger;
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        if (s.trustChanges != null)
            foreach (TrustChange c in s.trustChanges)
            {
                if (shown == MaxChangeLines) { sb.Append("\n..."); break; }
                if (shown > 0) sb.Append('\n');
                string hex = ColorUtility.ToHtmlStringRGB(c.delta >= 0f ? t.good : t.danger);
                sb.Append("<color=#").Append(hex).Append('>').Append(Loc.Get("debrief.change", c.delta, c.reason)).Append("</color>");
                shown++;
            }
        UIKit.SetText(_changes, sb.ToString());
    }
}
