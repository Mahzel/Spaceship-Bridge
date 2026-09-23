using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Transmit-home settings and the live link margin. Sending itself is done per record from the DataPanel.
/// The margin and the expected loss are what the probe can compute; what actually arrived is never shown here.
/// </summary>
public sealed class TransmitPanel
{
    private static readonly float[] Levels = { 0.25f, 0.5f, 1f };
    private static readonly string[] LevelLabels = { "25%", "50%", "100%" };

    private TextMeshProUGUI _dist, _margin, _energy, _count;
    private Button[] _level;
    private Button _robust;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Transmit", parent);
        UIKit.Size(prt, flexibleWidth: 1f);

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        _dist = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);

        RectTransform row = UIKit.Node("Levels", prt);
        UIKit.HStack(row, 6f, 0, expandWidth: true);
        UIKit.AddLabel(row, Loc.Get("ui.tx.level"), t.fontSizeSmall, t.textDim);
        _level = new Button[Levels.Length];
        for (int i = 0; i < Levels.Length; i++)
        {
            float lv = Levels[i];
            _level[i] = UIKit.AddButton(row, LevelLabels[i], () =>
            {
                if (Game.State != null) Game.State.Link.PowerLevel = lv;
            }, 0f, 32f);
        }
        _robust = UIKit.AddButton(row, Loc.Get("ui.tx.robust"), () =>
        {
            if (Game.State != null) Game.State.Link.Robust = !Game.State.Link.Robust;
        }, 0f, 32f);

        _margin = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);
        _energy = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _count  = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        UIKit.AddLabel(prt, Loc.Get("ui.tx.hint"), t.fontSizeSmall, t.textDim);

        return prt.gameObject;
    }

    public void Refresh()
    {
        if (Game.State == null || _dist == null) return;
        UITheme t = UITheme.Current;
        Transmitter link = Game.State.Link;

        double au = link.DistanceAu();
        LinkQuote q = link.Quote(au, link.PowerLevel, link.Robust);

        UIKit.SetText(_dist, Loc.Get("ui.tx.dist", au / Transmitter.AuPerLy));
        UIKit.SetText(_margin, Loc.Get("ui.tx.margin", q.marginDb, (1f - q.packetSuccess) * 100f));
        _margin.color = q.marginDb >= 4f ? t.good : q.marginDb >= 0f ? t.warning : t.danger;
        UIKit.SetText(_energy, Loc.Get("ui.tx.energy", q.energyPerUnit));
        UIKit.SetText(_count, Loc.Get("ui.tx.count", link.Sent.Count));

        for (int i = 0; i < _level.Length; i++)
            UIKit.SetButtonActive(_level[i], Mathf.Abs(link.PowerLevel - Levels[i]) < 0.01f);
        UIKit.SetButtonActive(_robust, link.Robust);
    }
}
