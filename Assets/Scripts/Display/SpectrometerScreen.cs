using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Utils;
using static UnityEngine.Object; // plain C# class (not a MonoBehaviour): Destroy/FindObjectsByType aren't inherited here

/// <summary>
/// Spectral readout for a locked TRACK: an emission/absorption line chart plus a noisy DSP trace.
/// Fully code-built (UIKit), same plain-class pattern as ImagerScreen/SystemsDock (Build returns the root
/// GameObject, Refresh is called every frame by the owning console).
///
/// You can only take a spectrum of something the sensors have picked up: the Prev/Next cycle runs over
/// LOCKED (confirmed) tracks only, and SEL grabs the track selected in the Track panel or System view. There
/// is no manual aim: the slit points at the track's bearing AND elevation and collects whatever is physically
/// inside it (SensorSight.CollectInCone, great-circle). A track without an elevation, or with one too coarse
/// (SpectrometerSpec.maxPointingSigmaDeg), can't be pointed at: refine it first (imager FIX, radar TRACK).
/// Pointed correctly at nothing gives NO SIGNAL, and two sources give a blend. Dwell time lives on the track (TrackInfo), so it
/// survives looking away. When it completes, the spectrometer writes what it identified into the track, and
/// that becomes the System view's class, composition and atmosphere columns. If the signature in the slit
/// changes (a different body now dominates), the ID is thrown away and analysis restarts.
///
/// Target selection is NOT shared with ImagerScreen's aim state: locking the spectrometer is its own
/// deliberate action. Like the
/// imager, this is an aimed, manual read (not a passive listener like the waterfall): SensorConsole calls
/// Hide() when another mode is selected, which unlocks the target and stops the draw, matching the imager's
/// StopScan() behaviour.
///
/// Line identification is two independent limits stacked on top of each other, same as a real spectrograph:
///  - HARDWARE (SpectrometerSpec.resolvingPower): two real lines closer together than the instrument can
///    resolve at that wavelength are merged into one drawn feature (ClusterLines) - a low-tier spectrometer
///    can't tell the Na D doublet from one broader line, no matter how long you stare at it.
///  - INTEGRATION TIME (SpectrometerSpec.identifyDwellSeconds): even a cleanly resolved line only gets a
///    labeled species ID once you've stayed locked on the target long enough (TrackInfo.specDwellSeconds) - before
///    that it's just an unlabeled provisional tick, mirroring WaterfallProcessor's own SNR integration.
/// A cluster that still contains more than one distinct species once resolving power has done its best is
/// drawn with an ambiguous "A/B?" label instead of a clean ID - a genuine blend, not a UI placeholder.
/// </summary>
public sealed class SpectrometerScreen
{
    private const int SpecW = 420;
    private const int SpecH = 320; // taller plot area (was 150) - this IS the actual texture pixel height
                                    // (CreateTexture/DrawLine work in SpecH units directly), so it's a real
                                    // resolution gain, not just a bigger rect around the same bitmap
    private const int DspH = 80;
    private const float RedrawInterval = 0.2f;
    private const int MaxLabels = 20;

    private SpectrometerSpec _spec;

    private int _trackId;                      // 0 = no lock
    private readonly List<Track> _candidates = new List<Track>();
    private readonly List<CelestialBody> _inSlit = new List<CelestialBody>();
    private readonly List<CelestialBody> _contributors = new List<CelestialBody>();
    private readonly Spectrum _blend = new Spectrum { emissionLines = new List<SpectralLine>(), absorptionLines = new List<SpectralLine>() };
    private static readonly Spectrum Empty = new Spectrum { emissionLines = new List<SpectralLine>(), absorptionLines = new List<SpectralLine>() };

    private float _redrawAccum;
    private bool _hasContent;
    private float _maxSpectralIntensity = 1f;

    /// <summary>Whether the current track's lines are labelled (TrackInfo.identified), set before each redraw.</summary>
    private bool _resolved;

    /// <summary>Sources in the slit contribute to the spectrum when at least this fraction of the brightest.
    /// Anything fainter is lost in the noise and doesn't count as a blend.</summary>
    private const float BlendFraction = 0.01f;

    private Texture2D _texture;
    private Color[]   _clearBuffer;
    private RawImage  _image;
    private RectTransform _displayArea;
    private LineGraphic _dspLine;
    private float[] _dspData = new float[SpecW];
    private float[] _dspNormalized = new float[SpecW];

    private TextMeshProUGUI _targetLabel;
    private readonly TextMeshProUGUI[] _lineLabels = new TextMeshProUGUI[MaxLabels];
    private int _lineLabelCount;

    public GameObject Build(Transform parent)
    {
        _spec = Game.State != null ? Game.State.GetSpec<SpectrometerSpec>() : null;
        if (_spec == null) _spec = ScriptableObject.CreateInstance<SpectrometerSpec>();
        ResetView();

        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Spectrometer", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildDisplay(root);
        BuildWavelengthAxis(root);
        BuildDsp(root);
        BuildControls(root);
        SetView(_viewMin, _viewMax); // fills the zoom label now that it exists

        CreateTexture();
        ClearDisplay();

        return root.gameObject;
    }

    private void BuildDisplay(Transform parent)
    {
        RectTransform area = UIKit.Node("DisplayArea", parent);
        UIKit.Size(area, preferredWidth: SpecW, minHeight: SpecH);
        _displayArea = area;

        _image = UIKit.AddRawImage(area, "Spectrum", Color.white);
        UIKit.Stretch(_image.rectTransform);
        _image.raycastTarget = true; // wheel = zoom about the cursor, drag = pan
        var aim = _image.gameObject.AddComponent<PointerAim>();
        aim.OnScrolled = (wheel, local, rt) =>
        {
            float frac = rt.rect.width > 1f ? Mathf.Clamp01(local.x / rt.rect.width + 0.5f) : 0.5f;
            ZoomBy(wheel > 0f ? 1 : -1, frac);
        };
        aim.OnDragged = (delta, local, rt) =>
        {
            if (rt.rect.width > 1f && _zoomIndex > 0) Pan(-delta.x / rt.rect.width * (_viewMax - _viewMin));
        };

        UITheme t = UITheme.Current;
        for (int i = 0; i < MaxLabels; i++)
        {
            TextMeshProUGUI lbl = UIKit.AddLabel(area, "", 11, t.text, TextAlignmentOptions.Center);
            lbl.raycastTarget = false;
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(LabelWidth, 14f);
            lbl.gameObject.SetActive(false);
            _lineLabels[i] = lbl;
        }
    }

    private void BuildDsp(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform area = UIKit.AddPanel(parent, "DspArea", t.barBack).rectTransform;
        UIKit.Size(area, preferredWidth: SpecW, minHeight: DspH);

        RectTransform lineRt = UIKit.Node("DSPLine", area);
        UIKit.Stretch(lineRt, 2f, 2f, 2f, 2f);
        _dspLine = lineRt.gameObject.AddComponent<LineGraphic>();
        _dspLine.raycastTarget = false;
        _dspLine.color = t.accent;
    }

    private void BuildControls(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform row = UIKit.Node("Row", parent);
        UIKit.HStack(row, 6f, 0, expandWidth: true);

        _targetLabel = UIKit.AddLabel(row, "", t.fontSizeSmall, t.text, TextAlignmentOptions.MidlineLeft);
        UIKit.Size(_targetLabel.rectTransform, flexibleWidth: 1f);
        UIKit.AddButton(row, "SEL", LockSelected, 50f, 32f);
        UIKit.AddButton(row, "<", () => CycleTarget(-1), 34f, 32f);
        UIKit.AddButton(row, ">", () => CycleTarget(1), 34f, 32f);

        RectTransform row2 = UIKit.Node("ZoomRow", parent);
        UIKit.HStack(row2, 6f, 0).childAlignment = TextAnchor.MiddleLeft;
        UIKit.AddLabel(row2, Loc.Get("ui.wf.zoom"), t.fontSizeSmall, t.textDim);
        UIKit.AddButton(row2, "-", () => ZoomBy(-1, 0.5f), 28f, 28f);
        _zoomLabel = UIKit.AddLabel(row2, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(_zoomLabel.rectTransform, preferredWidth: 150f);
        UIKit.AddButton(row2, "+", () => ZoomBy(1, 0.5f), 28f, 28f);
        UIKit.AddButton(row2, Loc.Get("ui.spec.full"), ResetView, 50f, 28f);
        UIKit.AddLabel(row2, Loc.Get("ui.wf.zoomhint"), t.fontSizeSmall, t.textDim);

        _dopplerLabel = UIKit.AddLabel(parent, "", t.fontSizeSmall, t.textDim);
    }

    private void CreateTexture()
    {
        if (_texture != null) Destroy(_texture);
        _texture = new Texture2D(SpecW, SpecH, TextureFormat.RGBA32, false);
        _image.texture = _texture;

        _clearBuffer = new Color[SpecW * SpecH];
        for (int i = 0; i < _clearBuffer.Length; i++) _clearBuffer[i] = Color.black;
    }

    // --- Target selection ------------------------------------------------------

    // Cycles "no lock" plus every LOCKED track, wrapping either way. Independent of ImagerScreen.
    private void CycleTarget(int dir)
    {
        if (Game.State == null) return;
        Game.State.Tracks.CollectConfirmed(_candidates);

        int current = -1;
        for (int i = 0; i < _candidates.Count; i++) if (_candidates[i].id == _trackId) current = i;

        int next = -1;
        if (_candidates.Count > 0)
        {
            int span = _candidates.Count + 1;
            int raw = ((current + 1) + dir % span + span) % span;
            next = raw - 1;
        }
        SetTrack(next >= 0 ? _candidates[next].id : 0);
    }

    /// <summary>Locks onto the track selected elsewhere (Track panel / System view), if it's locked.</summary>
    private void LockSelected()
    {
        if (Game.State == null) return;
        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr != null && tr.Locked) SetTrack(tr.id);
    }

    private void SetTrack(int id)
    {
        bool wasLocked = _trackId != 0;
        _trackId = id;
        bool locked = _trackId != 0;
        if (locked != wasLocked) SetLocked(locked);
        _redrawAccum = RedrawInterval; // force an immediate redraw on the next Refresh
    }

    private void SetLocked(bool locked)
    {
        if (Game.State != null) Game.State.SetLoad("spectrometer", locked ? _spec.powerDraw : 0f);
    }

    /// <summary>Called by SensorConsole when this screen becomes the visible one: the track currently selected
    /// (Track panel / System view / radar) becomes the spectrometer's target. With nothing selected it keeps
    /// whatever it was on (Hide() clears that, so in practice: NO TARGET until you pick one).</summary>
    public void Show()
    {
        if (Game.State == null) return;
        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr != null) SetTrack(tr.id);
    }

    /// <summary>Called by SensorConsole when another mode is selected. Unlocks and stops the (deliberate) read.
    /// The dwell already banked on the track is kept.</summary>
    public void Hide()
    {
        if (_trackId != 0) SetLocked(false);
        _trackId = 0;
        ClearDisplay();
        _hasContent = false;
    }

    // --- Frame loop ------------------------------------------------------------

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        // A refit changes the fitted spectrometer (resolving power, pointing): pick it up.
        SpectrometerSpec fitted = Game.State != null ? Game.State.GetSpec<SpectrometerSpec>() : null;
        if (fitted != null && fitted != _spec) { _spec = fitted; ResetView(); ClearDisplay(); _hasContent = false; }

        Track tr = (_trackId != 0 && Game.State != null) ? Game.State.Tracks.Find(_trackId) : null;
        if (_trackId != 0 && tr == null) SetTrack(0); // dropped, or wiped by a system jump

        if (tr == null)
        {
            UIKit.SetText(_targetLabel, Loc.Get("ui.spec.none"));
            if (_hasContent) { ClearDisplay(); _hasContent = false; }
            return;
        }

        if (!tr.Locked)
        {
            // The tracker lost it: no bearing we trust, so no light to integrate.
            UIKit.SetText(_targetLabel, Loc.Get("ui.spec.lost", tr.name));
            if (_hasContent) { ClearDisplay(); _hasContent = false; }
            return;
        }

        // Pointing: needs an elevation, and a tight enough one.
        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        float elSigma = TrackManager.AgedElevationSigma(tr, now);
        // A resolved disk tolerates pointing errors up to half its apparent radius: the slit still lands on it.
        float pointingLimit = _spec.maxPointingSigmaDeg + 0.5f * tr.angularRadiusDeg;
        if (!tr.hasElevation || elSigma > pointingLimit)
        {
            UIKit.SetText(_targetLabel, !tr.hasElevation
                ? Loc.Get("ui.spec.noel", tr.name)
                : Loc.Get("ui.spec.coarseel", tr.name, elSigma, pointingLimit));
            if (_hasContent) { ClearDisplay(); _hasContent = false; }
            return;
        }

        SensorSight.CollectInCone(tr.bearing, tr.elevationDeg, _spec.slitHalfWidthDeg, _inSlit);
        _contributors.Clear();
        float brightest = _inSlit.Count > 0 ? _inSlit[0].apparentLuminosity : 0f;
        for (int i = 0; i < _inSlit.Count; i++)
            if (_inSlit[i].apparentLuminosity >= brightest * BlendFraction && _inSlit[i].spectrum != null)
                _contributors.Add(_inSlit[i]);

        // Paused: no dwell accrues and the trace freezes (it is only redrawn if there's nothing on it yet).
        bool paused = Game.Clock != null && Game.Clock.Paused;
        float dt = paused ? 0f : unscaledDeltaSeconds;
        _redrawAccum += dt;
        bool redraw = !_hasContent || _viewDirty || (!paused && _redrawAccum >= RedrawInterval);
        if (redraw) { _redrawAccum = 0f; _viewDirty = false; }

        if (_contributors.Count == 0 || brightest <= 0f)
        {
            // Pointed at a bearing with nothing bright enough in the slit: noise only, and no dwell accrues.
            UIKit.SetText(_targetLabel, Loc.Get("ui.spec.nosignal", tr.name));
            if (redraw)
            {
                _resolved = false;
                _maxSpectralIntensity = 1f;
                ClearTextureBuffer(); DrawAxes(); _texture.SetPixels(_clearBuffer);
                for (int i = 0; i < _lineLabelCount; i++) _lineLabels[i].gameObject.SetActive(false);
                _lineLabelCount = 0;
                DrawDsp(Empty);
                _texture.Apply(false);
                _hasContent = true;
            }
            return;
        }

        // Integrate. A change of signature (different dominant source(s)) invalidates the old ID.
        TrackInfo info = tr.info;
        string key = SignatureKey(_contributors);
        if (info.signatureKey != key)
        {
            if (info.signatureKey != null) info.Reset();
            info.signatureKey = key;
        }
        if (!info.identified)
        {
            info.specDwellSeconds += dt; // counts every frame, independent of the redraw throttle
            if (info.specDwellSeconds >= _spec.identifyDwellSeconds) Identify(info, _contributors);
        }

        RefreshLabel(tr);
        if (!redraw) return;

        _resolved = info.identified;
        // Every source is Doppler-shifted by its own line-of-sight velocity (Blend applies it, one source or many).
        Spectrum spectrum = Blend(_contributors);
        RefreshDoppler(tr, _contributors[0]);
        DrawSpectrum(spectrum);
        DrawDsp(spectrum);
        _texture.Apply(false);
        _hasContent = true;
    }

    private void RefreshLabel(Track tr)
    {
        TrackInfo info = tr.info;
        if (info.identified)
        {
            string cls = !string.IsNullOrEmpty(info.surfaceClass) ? info.surfaceClass : info.bodyType;
            UIKit.SetText(_targetLabel, Loc.Get(info.blended ? "ui.spec.id.blend" : "ui.spec.id", tr.name, cls));
            return;
        }
        // Hand-built percentage rather than a "P0" format: "P0" takes its percent GLYPH from the current
        // culture, and on some locales that's U+066A (Arabic percent sign), which LiberationSans doesn't
        // have, so TMP logs a missing-character error.
        float pct = Mathf.Clamp01(info.specDwellSeconds / Mathf.Max(_spec.identifyDwellSeconds, 1e-4f)) * 100f;
        UIKit.SetText(_targetLabel, Loc.Get("ui.spec.analyzing", tr.name, pct));
    }

    /// <summary>What the spectrometer concludes once dwell completes: the dominant source's properties, as
    /// derived from its spectrum. (The sim copies the answer rather than inverting the lines, but only after the
    /// measurement has earned it.) A blend is flagged, because the minor source's lines are mixed in.</summary>
    private static void Identify(TrackInfo info, List<CelestialBody> sources)
    {
        CelestialBody b = sources[0];
        info.identified = true;
        info.blended = sources.Count > 1;
        info.isStar = b.starLuminosity > 0f;
        info.catalogName = b.bodyName;
        info.bodyType = b.bodyType;
        info.surfaceClass = b.surfaceClass;
        info.temperatureK = b.temperature;
        info.surfaceTemperatureK = b.surfaceTemperature;
        info.metallicity = b.metallicity;
        info.ageGyr = b.ageGyr;
        info.composition.Clear();
        if (b.chemicalComposition != null) info.composition.AddRange(b.chemicalComposition);
        info.composition.Sort((x, y) => y.percentage.CompareTo(x.percentage));
        info.atmosphere = b.atmosphere;
    }

    private static string SignatureKey(List<CelestialBody> sources)
    {
        if (sources.Count == 1) return sources[0].bodyName;
        var names = new List<string>(sources.Count);
        for (int i = 0; i < sources.Count; i++) names.Add(sources[i].bodyName);
        names.Sort(string.CompareOrdinal);
        return string.Join("+", names);
    }

    /// <summary>Luminosity-weighted sum of several sources' lines: what a slit with more than one thing in it sees.</summary>
    private Spectrum Blend(List<CelestialBody> sources)
    {
        _blend.emissionLines.Clear();
        _blend.absorptionLines.Clear();
        float total = 0f;
        for (int i = 0; i < sources.Count; i++) total += Mathf.Max(0f, sources[i].apparentLuminosity);
        if (total <= 0f) total = 1f;
        for (int i = 0; i < sources.Count; i++)
        {
            float w = Mathf.Max(0f, sources[i].apparentLuminosity) / total;
            float shift = (float)(1.0 + SensorSight.RadialVelocityKmS(sources[i], SensorSight.Ship()) / SpeedOfLightKmS);
            AddWeighted(_blend.emissionLines, sources[i].spectrum.emissionLines, w, shift);
            AddWeighted(_blend.absorptionLines, sources[i].spectrum.absorptionLines, w, shift);
        }
        return _blend;
    }

    private static void AddWeighted(List<SpectralLine> into, List<SpectralLine> from, float w, float dopplerFactor)
    {
        if (from == null) return;
        for (int i = 0; i < from.Count; i++)
            into.Add(new SpectralLine { wavelength = from[i].wavelength * dopplerFactor, intensity = from[i].intensity * w, species = from[i].species });
    }

    // --- Doppler --------------------------------------------------------------------
    // lambda_obs = lambda_rest * (1 + v_r / c), v_r = line-of-sight velocity (receding +). Real and applied to every
    // line, but small: 30 km/s moves a 600 nm line by 0.06 nm, while this instrument resolves ~3 nm (R = 200). It
    // can still MEASURE v_r better than it resolves lines, by centroiding many lines over a long dwell:
    //   sigma_v ~ (c / R) / (2 sqrt(lines)) / sqrt(1 + dwell seconds)
    // The readout is that measurement: true v_r plus a fixed error draw (per signature) scaled by sigma_v.

    public const double SpeedOfLightKmS = 299792.458;
    private TextMeshProUGUI _dopplerLabel;

    private void RefreshDoppler(Track tr, CelestialBody main)
    {
        if (_dopplerLabel == null || main == null || main.spectrum == null) return;
        double v = SensorSight.RadialVelocityKmS(main, SensorSight.Ship());
        int lines = (main.spectrum.emissionLines != null ? main.spectrum.emissionLines.Count : 0)
                  + (main.spectrum.absorptionLines != null ? main.spectrum.absorptionLines.Count : 0);
        float dwell = Mathf.Min(tr.info.specDwellSeconds, _spec.identifyDwellSeconds);
        double sigma = SpeedOfLightKmS / _spec.resolvingPower / (2.0 * System.Math.Sqrt(System.Math.Max(1, lines)))
                     / System.Math.Sqrt(1.0 + dwell);
        double measured = v + sigma * FixedGauss(tr.info.signatureKey);
        double shiftNm = 600.0 * measured / SpeedOfLightKmS;
        UIKit.SetText(_dopplerLabel, Loc.Get("ui.spec.doppler", measured, sigma, shiftNm));
    }

    /// <summary>A repeatable standard-normal draw from a string (so the error doesn't flicker frame to frame).</summary>
    private static double FixedGauss(string key)
    {
        int h = 17;
        if (key != null) foreach (char c in key) h = unchecked(h * 31 + c);
        var r = new System.Random(h);
        double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
        return System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2);
    }

    // --- Spectrum line chart -----------------------------------------------------

    /// <summary>One drawn feature: either a single resolved line, or several real lines the instrument
    /// couldn't tell apart (see ClusterLines). Species is the union of every line folded in.</summary>
    private sealed class Cluster
    {
        public float TotalIntensity;
        public float MeanWavelength;
        public readonly HashSet<string> Species = new HashSet<string>();
        public int RawLineCount;

        public void Add(SpectralLine line)
        {
            float w = Mathf.Max(line.intensity, 1e-4f);
            float newSum = TotalIntensity + w;
            MeanWavelength = RawLineCount == 0 ? line.wavelength
                : MeanWavelength + (line.wavelength - MeanWavelength) * (w / newSum);
            TotalIntensity += line.intensity;
            RawLineCount++;
            if (!string.IsNullOrEmpty(line.species)) Species.Add(line.species);
        }

        public bool Ambiguous => Species.Count > 1;

        public string Label()
        {
            if (Species.Count == 0) return "?";
            if (Species.Count == 1) { foreach (string s in Species) return s; }
            var list = new List<string>(Species);
            list.Sort();
            return string.Join("/", list) + "?";
        }
    }

    /// <summary>Greedily merges lines closer together than the spectrometer can resolve at that wavelength
    /// (SpectrometerSpec.DeltaLambdaAt) into single clusters. Lines are visited in wavelength order so a
    /// multi-line blend (e.g. the Mg b triplet) chains correctly, each new line tested against the last raw
    /// wavelength actually added rather than the cluster's running mean.</summary>
    private List<Cluster> ClusterLines(List<SpectralLine> lines)
    {
        var result = new List<Cluster>();
        if (lines == null || lines.Count == 0) return result;

        var sorted = new List<SpectralLine>(lines);
        sorted.Sort((a, b) => a.wavelength.CompareTo(b.wavelength));

        Cluster current = new Cluster();
        float lastWavelength = sorted[0].wavelength;
        current.Add(sorted[0]);

        for (int i = 1; i < sorted.Count; i++)
        {
            SpectralLine line = sorted[i];
            float limit = _spec.DeltaLambdaAt((line.wavelength + lastWavelength) * 0.5f);
            if (line.wavelength - lastWavelength <= limit)
            {
                current.Add(line);
            }
            else
            {
                result.Add(current);
                current = new Cluster();
                current.Add(line);
            }
            lastWavelength = line.wavelength;
        }
        result.Add(current);
        return result;
    }

    private void DrawSpectrum(Spectrum spectrum)
    {
        UITheme t = UITheme.Current;
        ClearTextureBuffer();
        DrawAxes();

        List<Cluster> emission   = ClusterLines(spectrum.emissionLines);
        List<Cluster> absorption = ClusterLines(spectrum.absorptionLines);

        _maxSpectralIntensity = 1f;
        foreach (Cluster c in emission)   _maxSpectralIntensity = Mathf.Max(_maxSpectralIntensity, c.TotalIntensity);
        foreach (Cluster c in absorption) _maxSpectralIntensity = Mathf.Max(_maxSpectralIntensity, c.TotalIntensity);

        bool resolved = _resolved;
        _lineLabelCount = 0;

        foreach (Cluster c in emission)   DrawCluster(c, t.accent,  t.textDim, false, resolved);
        foreach (Cluster c in absorption) DrawCluster(c, t.text,    t.textDim, true,  resolved);

        for (int i = _lineLabelCount; i < MaxLabels; i++)
            if (_lineLabels[i].gameObject.activeSelf) _lineLabels[i].gameObject.SetActive(false);

        _texture.SetPixels(_clearBuffer);
    }

    private void DrawCluster(Cluster c, Color idColor, Color provisionalColor, bool isAbsorption, bool resolved)
    {
        if (c.TotalIntensity <= 0f) return;

        // A blend (more than one raw line folded in) is drawn a touch wider - a visible cue that this
        // feature is smeared together, on top of whatever the label ends up saying.
        int width = c.RawLineCount > 1 ? 3 : 1;
        Color barColor = resolved ? idColor : provisionalColor;
        DrawSpectralLine(c.MeanWavelength, c.TotalIntensity, barColor, isAbsorption, width);

        if (!resolved) return; // provisional: bar only, no species claimed yet

        Color labelColor = c.Ambiguous ? UITheme.Current.warning : idColor;
        PlaceLabel(c.Label(), c.MeanWavelength, !isAbsorption, labelColor);
    }

    private const float LabelWidth = 64f;

    private void PlaceLabel(string text, float wavelength, bool top, Color color)
    {
        if (_lineLabelCount >= MaxLabels) return;
        float xFrac = XFrac(wavelength);
        if (xFrac < 0f || xFrac > 1f) return;

        // A label is LabelWidth px wide and anchored at its own center (pivot 0.5): placed at xFrac=1 (a
        // line right at wavelengthMax) it would hang half its width off the right edge of the display area
        // and get clipped. Keep the whole label on-screen by pulling xFrac in from either edge by half a
        // label-width's worth of the area's actual current size (so this still holds up under canvas scaling).
        float areaWidth = _displayArea.rect.width;
        float halfLabelFrac = areaWidth > 1f ? (LabelWidth * 0.5f) / areaWidth : 0f;
        xFrac = Mathf.Clamp(xFrac, halfLabelFrac, 1f - halfLabelFrac);

        TextMeshProUGUI lbl = _lineLabels[_lineLabelCount++];
        UIKit.SetText(lbl, text);
        lbl.color = color;
        lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(xFrac, top ? 0.94f : 0.06f);
        lbl.gameObject.SetActive(true);
    }

    private void DrawSpectralLine(float wavelength, float intensity, Color color, bool isAbsorption, int width = 1)
    {
        if (intensity <= 0f) return;

        int x = Mathf.RoundToInt(XFrac(wavelength) * (SpecW - 1));
        if (x < 0 || x >= SpecW) return;

        float normalizedIntensity = Mathf.Clamp01(intensity / _maxSpectralIntensity);
        float logHeight = Mathf.Log10(1f + 9f * normalizedIntensity);
        int height = Mathf.RoundToInt(logHeight * (SpecH / 2f - 1f));
        if (height <= 0) return;

        int centerY = SpecH / 2;
        int half = width / 2;
        for (int dx = -half; dx <= half; dx++)
        {
            int px = x + dx;
            if (px < 0 || px >= SpecW) continue;
            for (int y = 0; y < height; y++)
            {
                int yPos = isAbsorption ? centerY - y : centerY + y;
                if (yPos >= 0 && yPos < SpecH) _clearBuffer[yPos * SpecW + px] = color;
            }
        }
    }

    private void DrawAxes()
    {
        UITheme t = UITheme.Current;
        int centerY = SpecH / 2;
        for (int x = 0; x < SpecW; x++) _clearBuffer[centerY * SpecW + x] = t.textDim;
        for (int y = 0; y < SpecH; y++) _clearBuffer[y * SpecW] = t.textDim;

        // Faint vertical grid at every major wavelength step, matching the labelled axis below the display,
        // plus minor ticks on the zero line halfway between.
        Color grid = t.textDim; grid.a = 1f; grid *= 0.45f; grid.a = 1f;
        float step = AxisStepNm();
        for (float w = Mathf.Ceil(_viewMin / (step * 0.5f)) * step * 0.5f; w <= _viewMax; w += step * 0.5f)
        {
            int x = Mathf.RoundToInt(XFrac(w) * (SpecW - 1));
            if (x <= 0 || x >= SpecW) continue;
            bool major = Mathf.Abs(Mathf.Repeat(w + 0.01f, step) - 0.01f) < 0.05f;
            if (major)
            {
                for (int y = 0; y < SpecH; y += 3) _clearBuffer[y * SpecW + x] = grid; // dotted
            }
            else
            {
                for (int y = centerY - 3; y <= centerY + 3; y++) _clearBuffer[y * SpecW + x] = t.textDim;
            }
        }
    }

    // --- Wavelength axis ---------------------------------------------------------

    private const float AxisH = 30f;
    private static readonly float[] AxisSteps = { 1f, 2f, 5f, 10f, 20f, 25f, 50f, 100f, 200f };

    /// <summary>Major step (nm) giving at most ~9 labels across the instrument's band.</summary>
    private float AxisStepNm()
    {
        float band = Mathf.Max(1f, _viewMax - _viewMin);
        for (int i = 0; i < AxisSteps.Length; i++) if (band / AxisSteps[i] <= 9f) return AxisSteps[i];
        return AxisSteps[AxisSteps.Length - 1];
    }

    /// <summary>
    /// A strip under the spectrum: a thin true-colour band (what each wavelength looks like to the eye; black
    /// outside the visible range) and nm labels at every major step. Rebuilt whenever the view zooms or pans.
    /// </summary>
    private void BuildWavelengthAxis(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform area = UIKit.Node("WavelengthAxis", parent);
        UIKit.Size(area, preferredWidth: SpecW, minHeight: AxisH);
        _axisArea = area;

        RawImage band = UIKit.AddRawImage(area, "Band", Color.white);
        RectTransform br = band.rectTransform;
        br.anchorMin = new Vector2(0f, 1f); br.anchorMax = new Vector2(1f, 1f);
        br.pivot = new Vector2(0.5f, 1f);
        br.sizeDelta = new Vector2(0f, 6f);
        br.anchoredPosition = Vector2.zero;
        _bandTex = new Texture2D(SpecW, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        band.texture = _bandTex;

        TextMeshProUGUI unit = UIKit.AddLabel(area, "nm", 11, t.textDim, TextAlignmentOptions.Right);
        RectTransform ur = unit.rectTransform;
        ur.anchorMin = ur.anchorMax = ur.pivot = new Vector2(1f, 0f);
        ur.sizeDelta = new Vector2(30f, 12f);
        ur.anchoredPosition = new Vector2(0f, -2f);

        RefreshAxis();
    }

    private RectTransform _axisArea;
    private Texture2D _bandTex;
    private readonly List<RectTransform> _axisTicks = new List<RectTransform>();
    private readonly List<TextMeshProUGUI> _axisLabels = new List<TextMeshProUGUI>();

    private void RefreshAxis()
    {
        if (_axisArea == null) return;
        UITheme t = UITheme.Current;
        var px = new Color[SpecW];
        for (int x = 0; x < SpecW; x++)
            px[x] = WavelengthToColor(Mathf.Lerp(_viewMin, _viewMax, x / (float)(SpecW - 1)));
        _bandTex.SetPixels(px);
        _bandTex.Apply(false);

        float step = AxisStepNm();
        int used = 0;
        for (float w = Mathf.Ceil(_viewMin / step) * step; w <= _viewMax + 1e-3f; w += step)
        {
            float frac = XFrac(w);
            if (used >= _axisTicks.Count)
            {
                Image tick = UIKit.AddPanel(_axisArea, "Tick", t.textDim);
                RectTransform trt = tick.rectTransform;
                trt.pivot = new Vector2(0.5f, 1f);
                trt.sizeDelta = new Vector2(1f, 10f);
                _axisTicks.Add(trt);
                TextMeshProUGUI l = UIKit.AddLabel(_axisArea, "", 11, t.textDim, TextAlignmentOptions.Center);
                RectTransform lr = l.rectTransform;
                lr.pivot = new Vector2(0.5f, 1f);
                lr.sizeDelta = new Vector2(40f, 14f);
                _axisLabels.Add(l);
            }
            RectTransform tr = _axisTicks[used];
            tr.gameObject.SetActive(true);
            tr.anchorMin = tr.anchorMax = new Vector2(frac, 1f);
            tr.anchoredPosition = Vector2.zero;

            // Keep edge labels fully on the strip (same idea as PlaceLabel).
            TextMeshProUGUI lbl = _axisLabels[used];
            lbl.gameObject.SetActive(true);
            UIKit.SetText(lbl, w.ToString("0"));
            RectTransform lrt = lbl.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(Mathf.Clamp(frac, 0.04f, 0.96f), 1f);
            lrt.anchoredPosition = new Vector2(0f, -11f);
            used++;
        }
        for (int i = used; i < _axisTicks.Count; i++)
        {
            _axisTicks[i].gameObject.SetActive(false);
            _axisLabels[i].gameObject.SetActive(false);
        }
    }

    // --- Zoom / pan ----------------------------------------------------------------
    // A visual zoom only: the instrument still records its whole band at its own resolving power; zooming just
    // spreads a slice of it across the display, so close features that ARE resolved can be told apart by eye.

    private static readonly int[] ZoomLevels = { 1, 2, 4, 8, 16 };
    private int _zoomIndex;
    private float _viewMin, _viewMax;
    private bool _viewDirty;
    private TextMeshProUGUI _zoomLabel;

    private float XFrac(float nm) => (nm - _viewMin) / Mathf.Max(1e-3f, _viewMax - _viewMin);

    private void ResetView()
    {
        _zoomIndex = 0;
        SetView(_spec.wavelengthMin, _spec.wavelengthMax);
    }

    /// <summary>Zooms one step in/out, keeping the wavelength under viewFrac (0..1 across the display) fixed.</summary>
    private void ZoomBy(int steps, float viewFrac)
    {
        int next = Mathf.Clamp(_zoomIndex + steps, 0, ZoomLevels.Length - 1);
        if (next == _zoomIndex) return;
        float pivot = Mathf.Lerp(_viewMin, _viewMax, viewFrac);
        _zoomIndex = next;
        float span = (_spec.wavelengthMax - _spec.wavelengthMin) / ZoomLevels[_zoomIndex];
        float min = pivot - viewFrac * span;
        SetView(min, min + span);
    }

    private void Pan(float nm)
    {
        SetView(_viewMin + nm, _viewMax + nm);
    }

    private void SetView(float min, float max)
    {
        float span = max - min;
        if (min < _spec.wavelengthMin) { min = _spec.wavelengthMin; max = min + span; }
        if (max > _spec.wavelengthMax) { max = _spec.wavelengthMax; min = max - span; }
        _viewMin = min; _viewMax = max;
        _viewDirty = true;
        RefreshAxis();
        if (_zoomLabel != null)
            UIKit.SetText(_zoomLabel, Loc.Get("ui.spec.zoom.value", ZoomLevels[_zoomIndex], _viewMin, _viewMax));
    }

    /// <summary>Approximate perceived colour of a wavelength (Bruton's piecewise fit, with the usual intensity
    /// roll-off at the ends of vision). Black outside 380..780 nm.</summary>
    private static Color WavelengthToColor(float nm)
    {
        float r = 0f, g = 0f, b = 0f;
        if (nm >= 380f && nm < 440f) { r = -(nm - 440f) / 60f; b = 1f; }
        else if (nm < 490f && nm >= 440f) { g = (nm - 440f) / 50f; b = 1f; }
        else if (nm < 510f && nm >= 490f) { g = 1f; b = -(nm - 510f) / 20f; }
        else if (nm < 580f && nm >= 510f) { r = (nm - 510f) / 70f; g = 1f; }
        else if (nm < 645f && nm >= 580f) { r = 1f; g = -(nm - 645f) / 65f; }
        else if (nm <= 780f && nm >= 645f) { r = 1f; }
        float k = 0f;
        if (nm >= 380f && nm < 420f) k = 0.3f + 0.7f * (nm - 380f) / 40f;
        else if (nm >= 420f && nm <= 700f) k = 1f;
        else if (nm > 700f && nm <= 780f) k = 0.3f + 0.7f * (780f - nm) / 80f;
        return new Color(r * k, g * k, b * k, 1f) * 0.8f + new Color(0f, 0f, 0f, 0.2f);
    }

    private void ClearTextureBuffer()
    {
        for (int i = 0; i < _clearBuffer.Length; i++) _clearBuffer[i] = Color.black;
    }

    private void ClearDisplay()
    {
        if (_dopplerLabel != null) UIKit.SetText(_dopplerLabel, "");
        ClearTextureBuffer();
        _texture.SetPixels(_clearBuffer);
        _texture.Apply(false);
        _dspLine.Clear();
        for (int i = 0; i < _lineLabelCount; i++) _lineLabels[i].gameObject.SetActive(false);
        _lineLabelCount = 0;
    }

    // --- DSP noisy trace ---------------------------------------------------------
    // Deliberately reads the RAW (unclustered) spectrum, not the drawn clusters: detecting that *something*
    // sits at a given wavelength is immediate (this is what a real-time DSP trace would show), it's only
    // identifying WHAT it is that takes dwell time - the same split as ELINT/ACINT classification confidence
    // vs. contact detection.

    private void DrawDsp(Spectrum spectrum)
    {
        for (int i = 0; i < SpecW; i++)
            _dspData[i] = GaussianNoise(0f, GameConstants.NOISE_STDDEV);

        if (spectrum.emissionLines != null)
        foreach (SpectralLine s in spectrum.emissionLines)
        {
            int xCenter = Mathf.RoundToInt(XFrac(s.wavelength) * (SpecW - 1));
            float amplitude = s.intensity > 0f ? Mathf.Log10(1f + s.intensity / _maxSpectralIntensity * 9f) * 5f : 0f;
            SpreadSpectralSignal(_dspData, xCenter, amplitude);
        }
        if (spectrum.absorptionLines != null)
        foreach (SpectralLine s in spectrum.absorptionLines)
        {
            int xCenter = Mathf.RoundToInt(XFrac(s.wavelength) * (SpecW - 1));
            float amplitude = s.intensity > 0f ? Mathf.Log10(1f + s.intensity / _maxSpectralIntensity * 9f) * 5f : 0f;
            SpreadSpectralSignal(_dspData, xCenter, -amplitude);
        }

        float maxAbs = 1e-6f;
        for (int i = 0; i < SpecW; i++) maxAbs = Mathf.Max(maxAbs, Mathf.Abs(_dspData[i]));

        for (int i = 0; i < SpecW; i++)
            _dspNormalized[i] = 0.5f + 0.5f * Mathf.Clamp(_dspData[i] / maxAbs, -1f, 1f);

        _dspLine.SetValues(_dspNormalized, SpecW);
    }

    // Spreads a signal over ±PSF_SPREAD_HALF_WIDTH bins with a Gaussian PSF (sigma = PSF_SIGMA).
    private static void SpreadSpectralSignal(float[] buffer, int center, float amplitude)
    {
        float sigma = GameConstants.PSF_SIGMA;
        for (int offset = -GameConstants.PSF_SPREAD_HALF_WIDTH; offset <= GameConstants.PSF_SPREAD_HALF_WIDTH; offset++)
        {
            int idx = center + offset;
            if (idx < 0 || idx >= buffer.Length) continue;
            float weight = Mathf.Exp(-0.5f * (offset * offset) / (sigma * sigma));
            buffer[idx] += amplitude * weight;
        }
    }

    // Gaussian noise via Box-Muller.
    private static float GaussianNoise(float mean, float stddev)
    {
        float u1 = Mathf.Max(1e-6f, 1f - Random.value);
        float u2 = 1f - Random.value;
        float normal = Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        return mean + stddev * normal;
    }
}
