using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Storage and deliberate recording. With a track selected: log its stub, or start/stop a raw recording
/// (optionally compressed). Lists what is on board, with a dump button to free space.
/// </summary>
public sealed class DataPanel
{
    private const int MaxRows = 10;

    private sealed class Row
    {
        public GameObject go;
        public TextMeshProUGUI name, size, value, status;
        public Button dump, send, conf;
        public TextMeshProUGUI confLabel;
        public int recordId;
    }

    private readonly List<Row> _rows = new List<Row>();
    private TextMeshProUGUI _selected, _empty, _free;
    private Button _stub, _raw, _compress;

    public GameObject Build(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform prt = UIKit.Node("Data", parent);
        UIKit.Size(prt, flexibleWidth: 1f);

        var v = UIKit.VStack(prt, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = prt.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        _selected = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.text);

        RectTransform actions = UIKit.Node("Actions", prt);
        UIKit.HStack(actions, 6f, 0, expandWidth: true);
        _stub     = UIKit.AddButton(actions, Loc.Get("ui.data.stub"), OnStub, 0f, 34f);
        _raw      = UIKit.AddButton(actions, Loc.Get("ui.data.raw"), OnRaw, 0f, 34f);
        _compress = UIKit.AddButton(actions, Loc.Get("ui.data.compress"), () =>
        {
            if (Game.State != null) Game.State.Data.compressNewRaw = !Game.State.Data.compressNewRaw;
        }, 0f, 34f);

        _free  = UIKit.AddLabel(prt, "", t.fontSizeSmall, t.textDim);
        _empty = UIKit.AddLabel(prt, Loc.Get("ui.data.none"), t.fontSizeSmall, t.textDim);

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

        row.name   = Cell(rt, 190f, t.text);
        row.size   = Cell(rt, 80f, t.text);
        row.value  = Cell(rt, 80f, t.text);
        row.status = Cell(rt, 90f, t.textDim);
        row.conf   = UIKit.AddButton(rt, "", () => ToggleConfidence(row), 34f, 30f);
        row.confLabel = row.conf.GetComponentInChildren<TextMeshProUGUI>();
        row.send   = UIKit.AddButton(rt, Loc.Get("ui.tx.send"), () => Send(row), 40f, 30f);
        row.dump   = UIKit.AddButton(rt, Loc.Get("ui.data.dump"), () => Dump(row), 34f, 30f);

        row.go.SetActive(false);
        return row;
    }

    private static TextMeshProUGUI Cell(Transform parent, float width, Color color)
    {
        TextMeshProUGUI l = UIKit.AddLabel(parent, "", UITheme.Current.fontSizeSmall, color);
        UIKit.Size(l.rectTransform, preferredWidth: width);
        return l;
    }

    private static Track SelectedTrack()
    {
        if (Game.State == null) return null;
        TrackManager tm = Game.State.Tracks;
        Track t = tm.Find(tm.SelectedId);
        return t != null && t.status == TrackStatus.Confirmed ? t : null;
    }

    private static string SystemId { get { return Game.Jump != null ? Game.Jump.CurrentSystemId : ""; } }
    private static double Now { get { return Game.Clock != null ? Game.Clock.SimSeconds : 0.0; } }

    private static void OnStub()
    {
        Track t = SelectedTrack();
        if (t != null) Game.State.Data.LogStub(t, SystemId, Game.State.Tracks.Generation, Now);
    }

    private static void OnRaw()
    {
        Track t = SelectedTrack();
        if (t == null) return;
        DataStore d = Game.State.Data;
        int gen = Game.State.Tracks.Generation;
        DataRecord active = d.ActiveRawFor(t.id, gen);
        if (active != null) d.StopRaw(active.id, "player");
        else d.StartRaw(t, SystemId, gen, Now);
    }

    private static void Send(Row row)
    {
        if (Game.State == null) return;
        DataRecord rec = Game.State.Data.Find(row.recordId);
        string why;
        if (rec != null) Game.State.Link.Send(rec, out why); // the button is disabled when it cannot be afforded
    }

    /// <summary>Declared confidence: Tentative (T) pays less and costs little if wrong, Confirmed (C) pays full
    /// and hurts if caught wrong at a later review. What is SENT keeps the value it had at send time.</summary>
    private static void ToggleConfidence(Row row)
    {
        if (Game.State == null) return;
        DataRecord rec = Game.State.Data.Find(row.recordId);
        if (rec == null) return;
        rec.confidence = rec.confidence == Confidence.Tentative ? Confidence.Confirmed : Confidence.Tentative;
    }

    private static void Dump(Row row)
    {
        if (Game.State != null) Game.State.Data.Remove(row.recordId);
    }

    public void Refresh()
    {
        if (Game.State == null || _selected == null) return;
        UITheme t = UITheme.Current;
        DataStore d = Game.State.Data;
        bool flying = Game.Run != null && Game.Run.Phase == RunPhase.Flight;

        Track sel = SelectedTrack();
        bool recording = sel != null && d.ActiveRawFor(sel.id, Game.State.Tracks.Generation) != null;

        UIKit.SetText(_selected, sel != null ? Loc.Get("ui.data.sel", sel.name) : Loc.Get("ui.data.nosel"));
        _stub.interactable = flying && sel != null && d.Free >= 0f;
        _raw.interactable  = flying && sel != null;
        UIKit.SetButtonActive(_raw, recording);
        UIKit.SetButtonActive(_compress, d.compressNewRaw);
        UIKit.SetText(((TextMeshProUGUI)_raw.GetComponentInChildren<TextMeshProUGUI>()),
                      recording ? Loc.Get("ui.data.rawstop") : Loc.Get("ui.data.raw"));

        UIKit.SetText(_free, Loc.Get("ui.data.free", d.Free, d.Capacity, d.TotalValue));
        _empty.gameObject.SetActive(d.Records.Count == 0);

        for (int i = 0; i < _rows.Count; i++)
        {
            Row r = _rows[i];
            if (i >= d.Records.Count) { if (r.go.activeSelf) r.go.SetActive(false); continue; }

            DataRecord rec = d.Records[i];
            if (!r.go.activeSelf) r.go.SetActive(true);
            r.recordId = rec.id;

            string kind = rec.kind == DataKind.Stub ? Loc.Get("ui.data.kind.stub")
                        : rec.compressed ? Loc.Get("ui.data.kind.rawc") : Loc.Get("ui.data.kind.raw");
            UIKit.SetText(r.name,  Loc.Get("ui.data.rowname", rec.label, kind, rec.systemId));
            UIKit.SetText(r.size,  Loc.Get("ui.data.size", rec.size));
            UIKit.SetText(r.value, Loc.Get("ui.data.value", rec.value));
            bool confirmed = rec.confidence == Confidence.Confirmed;
            UIKit.SetText(r.confLabel, Loc.Get(confirmed ? "ui.data.conf.c" : "ui.data.conf.t"));
            UIKit.SetButtonActive(r.conf, confirmed);
            int sent = Game.State.Link.SentCount(rec.id);
            r.send.interactable = flying && Game.State.Link.EnergyFor(rec, Game.State.Link.PowerLevel, Game.State.Link.Robust) < Game.State.PowerStored;
            UIKit.SetText(r.status, rec.catalogueName != null ? Loc.Get("ui.data.known", rec.catalogueName)
                                   : rec.recording ? Loc.Get("ui.data.rec")
                                   : sent > 0 ? Loc.Get("ui.tx.sent", sent)
                                   : rec.stopReason == "lost" ? Loc.Get("ui.data.lost")
                                   : rec.stopReason == "full" ? Loc.Get("ui.data.full") : "");
            r.status.color = rec.recording ? t.danger : t.textDim;
        }
    }
}
