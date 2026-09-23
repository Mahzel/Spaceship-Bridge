using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Jump drive: nearby systems with distance, hydrogen cost, transit time and the cost to get home from there.
/// Nothing is known about a destination except where it is. Home is always listed when in range: jumping there
/// ends the run as a return.
/// </summary>
public sealed class JumpPanel
{
    private const int MaxRows = 9;

    private sealed class Row
    {
        public GameObject go;
        public TextMeshProUGUI name, dist, cost, time, back;
        public Button jump;
        public GalaxySystem target;
    }

    private readonly List<Row> _rows = new List<Row>();
    private TextMeshProUGUI _where, _empty;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Jump", parent);
        UIKit.Size(prt, flexibleWidth: 1f);

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        _where = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _empty = UIKit.AddLabel(prt, Loc.Get("ui.jump.none"), t.fontSizeSmall, t.textDim);

        for (int i = 0; i < MaxRows; i++) _rows.Add(BuildRow(prt));

        return prt.gameObject;
    }

    private Row BuildRow(Transform parent)
    {
        UITheme t = UITheme.Current;
        var row = new Row();

        RectTransform rt = UIKit.Node("Row", parent);
        row.go = rt.gameObject;
        var h = UIKit.HStack(rt, 6f, 0);
        h.childAlignment = TextAnchor.MiddleLeft;

        row.name = Cell(rt, 170f, t.text);
        row.dist = Cell(rt, 90f, t.text);
        row.cost = Cell(rt, 80f, t.text);
        row.time = Cell(rt, 80f, t.text);
        row.back = Cell(rt, 130f, t.textDim);
        row.jump = UIKit.AddButton(rt, Loc.Get("ui.jump.go"), () => Go(row), 70f, 32f);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform parent, float width, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", UITheme.Current.fontSizeSmall, color);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }

    private static void Go(Row row)
    {
        if (Game.Jump != null) Game.Jump.Execute(row.target);
    }

    public void Refresh()
    {
        if (Game.Jump == null || Game.State == null || _where == null) return;
        UITheme t = UITheme.Current;
        JumpDrive drive = Game.Jump;
        GameState state = Game.State;

        UIKit.SetText(_where, Loc.Get("ui.jump.at", drive.CurrentSystemId, drive.DistanceFromHomeLy));

        IList<GalaxySystem> nearby = drive.Nearby();
        bool flying = Game.Run != null && Game.Run.Phase == RunPhase.Flight;
        _empty.gameObject.SetActive(nearby.Count == 0);

        // Home first, then the nearest others.
        string homeId = Galaxy.Home(state.WorldSeed).id;
        int shown = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < nearby.Count && shown < _rows.Count; i++)
            {
                bool isHome = nearby[i].id == homeId;
                if ((pass == 0) != isHome) continue;
                Fill(_rows[shown++], nearby[i], drive, state, flying, t);
            }
        }
        for (int i = shown; i < _rows.Count; i++)
            if (_rows[i].go.activeSelf) _rows[i].go.SetActive(false);
    }

    private static void Fill(Row r, GalaxySystem s, JumpDrive drive, GameState state, bool flying, UITheme t)
    {
        JumpPlan plan = drive.Plan(s);
        bool affordable = drive.CanAfford(plan);
        if (!r.go.activeSelf) r.go.SetActive(true);
        r.target = s;

        UIKit.SetText(r.name, plan.isHome ? Loc.Get("ui.jump.home") : s.id);
        UIKit.SetText(r.dist, Loc.Get("ui.jump.dist", plan.distanceLy));
        UIKit.SetText(r.cost, Loc.Get("ui.jump.cost", plan.hydrogenCost));
        UIKit.SetText(r.time, Loc.Get("ui.jump.time", plan.days));
        UIKit.SetText(r.back, plan.isHome ? "" : Loc.Get("ui.jump.back", plan.returnCost));

        r.cost.color = affordable ? t.text : t.danger;
        // Warn when, after this jump, the hydrogen left would not pay for the way home.
        bool strands = !plan.isHome && (state.Hydrogen - plan.hydrogenCost) < plan.returnCost;
        r.back.color = strands ? t.warning : t.textDim;

        r.jump.interactable = flying && affordable;
    }
}
