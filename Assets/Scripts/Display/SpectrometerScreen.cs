using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Utils;
using static UnityEngine.Object; // plain C# class (not a MonoBehaviour): Destroy/FindObjectsByType aren't inherited here

/// <summary>
/// Spectral readout for a locked target: an emission/absorption line chart plus a noisy DSP trace.
/// Fully code-built (UIKit), same plain-class pattern as ImagerScreen/SystemsDock (Build returns the root
/// GameObject, Refresh is called every frame by the owning console). Target selection is its own Prev/Next
/// cycle — deliberately NOT shared with ImagerScreen's aim/target state, since locking the spectrometer onto
/// a body is its own deliberate action, independent of where the imager happens to be pointed. Like the
/// imager, this is an aimed, manual read (not a passive listener like the waterfall): SensorConsole calls
/// Hide() when another mode is selected, which unlocks the target and stops the draw, matching the imager's
/// StopScan() behaviour.
///
/// Line identification is two independent limits stacked on top of each other, same as a real spectrograph:
///  - HARDWARE (SpectrometerSpec.resolvingPower): two real lines closer together than the instrument can
///    resolve at that wavelength are merged into one drawn feature (ClusterLines) - a low-tier spectrometer
///    can't tell the Na D doublet from one broader line, no matter how long you stare at it.
///  - INTEGRATION TIME (SpectrometerSpec.identifyDwellSeconds): even a cleanly resolved line only gets a
///    labeled species ID once you've stayed locked on the target long enough (_lockDwellSeconds) - before
///    that it's just an unlabeled provisional tick, mirroring WaterfallProcessor's own SNR integration.
/// A cluster that still contains more than one distinct species once resolving power has done its best is
/// drawn with an ambiguous "A/B?" label instead of a clean ID - a genuine blend, not a UI placeholder.
/// </summary>
public sealed class SpectrometerScreen
{
    private const int SpecW = 420;
    private const int SpecH = 150;
    private const int DspH = 80;
    private const float RedrawInterval = 0.2f;
    private const int MaxLabels = 20;

    private SpectrometerSpec _spec;

    private readonly List<CelestialBody> _bodies = new List<CelestialBody>();
    private int _targetIndex = -1; // -1 = no lock
    private SystemData _boundSystem;

    private float _redrawAccum;
    private bool _hasContent;
    private float _maxSpectralIntensity = 1f;

    /// <summary>Seconds continuously locked on the current target - drives label confidence (see class doc).
    /// Reset whenever the locked target changes (including no-lock), incremented every real frame regardless
    /// of the texture's own throttled redraw interval, so it reflects true dwell time.</summary>
    private float _lockDwellSeconds;

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

        UITheme t = UITheme.Current;

        RectTransform root = UIKit.Node("Spectrometer", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildDisplay(root);
        BuildDsp(root);
        BuildControls(root);

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
        UIKit.AddButton(row, "<", () => CycleTarget(-1), 34f, 32f);
        UIKit.AddButton(row, ">", () => CycleTarget(1), 34f, 32f);
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

    // Cycles -1 (no lock) through 0.._bodies.Count-1 (locked), wrapping either way. Independent of ImagerScreen.
    private void CycleTarget(int dir)
    {
        RefreshBodiesIfNeeded();
        bool wasLocked = _targetIndex >= 0;
        int previousIndex = _targetIndex;

        if (_bodies.Count == 0)
        {
            _targetIndex = -1;
        }
        else
        {
            int span = _bodies.Count + 1;
            int raw  = ((_targetIndex + 1) + dir % span + span) % span;
            _targetIndex = raw - 1;
        }

        bool locked = _targetIndex >= 0;
        if (locked != wasLocked) SetLocked(locked);
        if (_targetIndex != previousIndex) _lockDwellSeconds = 0f; // a new target starts unidentified again
        _redrawAccum = RedrawInterval; // force an immediate redraw on the next Refresh
    }

    private void SetLocked(bool locked)
    {
        if (Game.State != null) Game.State.SetLoad("spectrometer", locked ? _spec.powerDraw : 0f);
    }

    private void RefreshBodiesIfNeeded(bool force = false)
    {
        SystemData current = SystemManager.Current != null ? SystemManager.Current.CurrentData : null;
        if (!force && current == _boundSystem) return;
        _boundSystem = current;

        CelestialBody[] found = FindObjectsByType<CelestialBody>(FindObjectsSortMode.None);
        _bodies.Clear();
        foreach (CelestialBody b in found)
            if (!string.IsNullOrEmpty(b.bodyName)) _bodies.Add(b);
        _bodies.Sort((a, b) => string.CompareOrdinal(a.bodyName, b.bodyName));
        _targetIndex = -1;
    }

    /// <summary>Called by SensorConsole when another mode is selected — unlocks and stops the (deliberate) read.</summary>
    public void Hide()
    {
        if (_targetIndex >= 0) SetLocked(false);
        _targetIndex = -1;
        _lockDwellSeconds = 0f;
        ClearDisplay();
        _hasContent = false;
    }

    // --- Frame loop ------------------------------------------------------------

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        if (_targetIndex < 0)
        {
            RefreshLabel(null);
            if (_hasContent) { ClearDisplay(); _hasContent = false; }
            return;
        }

        RefreshBodiesIfNeeded();
        if (_targetIndex >= _bodies.Count)
        {
            SetLocked(false);
            _targetIndex = -1;
            _lockDwellSeconds = 0f;
            RefreshLabel(null);
            if (_hasContent) { ClearDisplay(); _hasContent = false; }
            return;
        }

        CelestialBody target = _bodies[_targetIndex];
        RefreshLabel(target);
        if (target == null || !target.gameObject.activeInHierarchy) return;

        _lockDwellSeconds += unscaledDeltaSeconds; // counts every frame, independent of the redraw throttle

        _redrawAccum += unscaledDeltaSeconds;
        if (_hasContent && _redrawAccum < RedrawInterval) return;
        _redrawAccum = 0f;

        DrawSpectrum(target.spectrum);
        DrawDsp(target);
        _texture.Apply(false);
        _hasContent = true;
    }

    private void RefreshLabel(CelestialBody target)
    {
        if (target == null) { UIKit.SetText(_targetLabel, "NO TARGET"); return; }

        bool resolved = _lockDwellSeconds >= _spec.identifyDwellSeconds;
        // Hand-built "NN%" rather than a "P0" format string: "P0" pulls its percent GLYPH from the current
        // culture, and on some locales that resolves to U+066A (Arabic percent sign) instead of plain '%' -
        // a glyph LiberationSans doesn't have, which TMP then logs as a missing-character error.
        float pct = Mathf.Clamp01(_lockDwellSeconds / Mathf.Max(_spec.identifyDwellSeconds, 1e-4f)) * 100f;
        string suffix = resolved ? "" : $"   analyzing... {pct:F0}%";
        UIKit.SetText(_targetLabel, "LOCK: " + target.bodyName + suffix);
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

        bool resolved = _lockDwellSeconds >= _spec.identifyDwellSeconds;
        _lineLabelCount = 0;

        foreach (Cluster c in emission)   DrawCluster(c, t.accent,  t.textDim, false, resolved);
        foreach (Cluster c in absorption) DrawCluster(c, t.textDim, t.textDim, true,  resolved);

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
        float xFrac = Mathf.InverseLerp(_spec.wavelengthMin, _spec.wavelengthMax, wavelength);
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

        int x = Mathf.RoundToInt(Mathf.InverseLerp(_spec.wavelengthMin, _spec.wavelengthMax, wavelength) * (SpecW - 1));
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
    }

    private void ClearTextureBuffer()
    {
        for (int i = 0; i < _clearBuffer.Length; i++) _clearBuffer[i] = Color.black;
    }

    private void ClearDisplay()
    {
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

    private void DrawDsp(CelestialBody target)
    {
        for (int i = 0; i < SpecW; i++)
            _dspData[i] = GaussianNoise(0f, GameConstants.NOISE_STDDEV);

        foreach (SpectralLine s in target.spectrum.emissionLines)
        {
            int xCenter = Mathf.RoundToInt(Mathf.InverseLerp(_spec.wavelengthMin, _spec.wavelengthMax, s.wavelength) * (SpecW - 1));
            float amplitude = s.intensity > 0f ? Mathf.Log10(1f + s.intensity / _maxSpectralIntensity * 9f) * 5f : 0f;
            SpreadSpectralSignal(_dspData, xCenter, amplitude);
        }
        foreach (SpectralLine s in target.spectrum.absorptionLines)
        {
            int xCenter = Mathf.RoundToInt(Mathf.InverseLerp(_spec.wavelengthMin, _spec.wavelengthMax, s.wavelength) * (SpecW - 1));
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
