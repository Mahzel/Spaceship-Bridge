using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top strip: run/day/system, power / hydrogen / storage bars, net power per day, pause + time warp.
/// Built in code from the UIKit; Refresh() reads Game.State / Game.Run / Game.Clock.
/// </summary>
public sealed class StatusBar
{
    private static readonly float[] WarpMultipliers = { 1f, 10f, 30f, 60f };
    private static readonly GameClock.WarpUnit[] WarpUnits =
        { GameClock.WarpUnit.Seconds, GameClock.WarpUnit.Minutes, GameClock.WarpUnit.Hours, GameClock.WarpUnit.Days };
    private static readonly string[] WarpUnitLabels = { "s", "m", "h", "d" };

    private TextMeshProUGUI _runLine, _systemLine, _net;
    private UIBar _power, _hydrogen, _storage;
    private Button _pause, _nav;
    private Button[] _warpMult;
    private Button[] _warpUnit;

    /// <summary>Wired by GameUI (which owns the NavScreen instance) so this stays a plain click callback,
    /// same as every other button here talking straight to Game.* statics.</summary>
    public void Build(Transform parent, System.Action onNavToggle)
    {
        UITheme t = UITheme.Current;

        Image bg = UIKit.AddPanel(parent, "StatusBar", t.panelColor);
        bg.raycastTarget = true; // clicks on the strip must not reach anything behind it
        RectTransform rt = bg.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, t.statusBarHeight);

        var row = UIKit.HStack(rt, t.spacing * 2f, (int)t.padding);
        row.childAlignment = TextAnchor.MiddleLeft;

        // Run / system info
        RectTransform info = UIKit.Node("Info", rt);
        var col = UIKit.VStack(info, 0f, 0, expandWidth: true);
        col.childAlignment = TextAnchor.MiddleLeft;
        UIKit.Size(info, preferredWidth: 260f);
        _runLine    = UIKit.AddLabel(info, "", t.fontSizeBody,  t.text);
        _systemLine = UIKit.AddLabel(info, "", t.fontSizeSmall, t.textDim);

        // Resource bars
        _power    = UIBar.Create(rt, Loc.Get("ui.power"),    260f, 40f);
        _hydrogen = UIBar.Create(rt, Loc.Get("ui.hydrogen"), 200f, 40f);
        _storage  = UIBar.Create(rt, Loc.Get("ui.storage"),  200f, 40f);

        // Net power
        _net = UIKit.AddLabel(rt, "", t.fontSizeSmall, t.text);
        UIKit.Size(_net.rectTransform, preferredWidth: 310f);

        UIKit.AddSpacer(rt, 0f, flexibleWidth: 1f);

        // Nav (only shown once a nav computer is fitted; toggles NavScreen, owned by GameUI)
        _nav = UIKit.AddButton(rt, Loc.Get("ui.nav.open"), () => onNavToggle?.Invoke(), 56f, 44f);

        // Pause + warp
        _pause = UIKit.AddButton(rt, Loc.Get("ui.pause"), () =>
        {
            if (Game.Clock != null) Game.Clock.SetPaused(!Game.Clock.Paused);
        }, 56f, 44f);

        RectTransform warpCol = UIKit.Node("Warp", rt);
        var warpV = UIKit.VStack(warpCol, 2f, 0);
        warpV.childAlignment = TextAnchor.MiddleLeft;

        RectTransform multRow = UIKit.Node("WarpMult", warpCol);
        UIKit.HStack(multRow, 2f, 0);
        _warpMult = new Button[WarpMultipliers.Length];
        for (int i = 0; i < WarpMultipliers.Length; i++)
        {
            float mult = WarpMultipliers[i];
            _warpMult[i] = UIKit.AddButton(multRow, mult.ToString("F0"), () =>
            {
                // Snap to the nearest ladder rung, so every button lands on a speed the hotkeys also step through.
                if (Game.Clock != null) SetLadder(mult, Game.Clock.WarpUnitKind);
            }, 40f, 30f);
        }

        RectTransform unitRow = UIKit.Node("WarpUnit", warpCol);
        UIKit.HStack(unitRow, 2f, 0);
        _warpUnit = new Button[WarpUnits.Length];
        for (int i = 0; i < WarpUnits.Length; i++)
        {
            GameClock.WarpUnit unit = WarpUnits[i];
            _warpUnit[i] = UIKit.AddButton(unitRow, WarpUnitLabels[i], () =>
            {
                if (Game.Clock != null) SetLadder(Game.Clock.WarpMultiplier, unit);
            }, 40f, 30f);
        }
    }

    /// <summary>The ladder rung closest (in log speed) to mult x unit.</summary>
    private static void SetLadder(float mult, GameClock.WarpUnit unit)
    {
        float want = Mathf.Log(Mathf.Max(1e-3f, mult * GameClock.UnitSeconds(unit)));
        int best = 0; float bestD = float.MaxValue;
        for (int i = 0; i < GameClock.WarpLadder.Length; i++)
        {
            var w = GameClock.WarpLadder[i];
            float d = Mathf.Abs(Mathf.Log(w.mult * GameClock.UnitSeconds(w.unit)) - want);
            if (d < bestD) { bestD = d; best = i; }
        }
        Game.Clock.SetWarpLadder(best);
    }

    public void Refresh()
    {
        GameState state = Game.State;
        RunController run = Game.Run;
        GameClock clock = Game.Clock;
        if (state == null || run == null || clock == null || _runLine == null) return;

        UITheme t = UITheme.Current;

        string system = SystemManager.Current != null ? SystemManager.Current.CurrentSystemID : "-";
        UIKit.SetText(_runLine,    Loc.Get("ui.run", run.RunNumber, run.RunElapsedDays));
        UIKit.SetText(_systemLine, Loc.Get("ui.system.trust", system, state.Trust));

        float p = state.PowerCapacity > 0f ? state.PowerStored / state.PowerCapacity : 0f;
        Color powerColor = p < 0.10f ? t.danger : p < 0.25f ? t.warning : t.accent;
        _power.Set(p, $"{state.PowerStored:F0} / {state.PowerCapacity:F0}", powerColor);

        float h = state.HydrogenCapacity > 0f ? state.Hydrogen / state.HydrogenCapacity : 0f;
        _hydrogen.Set(h, $"{state.Hydrogen:F0} / {state.HydrogenCapacity:F0}", t.hydrogenColor);

        float s = state.StorageCapacity > 0f ? state.StorageUsed / state.StorageCapacity : 0f;
        _storage.Set(s, $"{state.StorageUsed:F0} / {state.StorageCapacity:F0}", t.storageColor);

        float net = run.IncomePerDay - run.LoadPerDay;
        UIKit.SetText(_net, Loc.Get("ui.net", net, run.SolarPerDay, run.LoadPerDay, run.ReactorPerDay));
        _net.color = net >= 0f ? t.good : t.warning;

        _nav.gameObject.SetActive(NavTier.HasSystemView(state.Loadout.Level(ProbeSystem.NavComputer)));

        UIKit.SetButtonActive(_pause, clock.Paused);
        for (int i = 0; i < _warpMult.Length; i++)
        {
            UIKit.SetButtonActive(_warpMult[i], !clock.Paused && Mathf.Approximately(clock.WarpMultiplier, WarpMultipliers[i]));
            // x60 only exists in days: 60 s = 1 m, 60 m = 1 h (and 60 h would be 2.5 d).
            _warpMult[i].interactable = WarpMultipliers[i] < 60f || clock.WarpUnitKind == GameClock.WarpUnit.Days;
        }
        for (int i = 0; i < _warpUnit.Length; i++)
            UIKit.SetButtonActive(_warpUnit[i], !clock.Paused && clock.WarpUnitKind == WarpUnits[i]);
    }
}
