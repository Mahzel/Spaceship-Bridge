using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Utils;
using static UnityEngine.Object; // plain C# class (not a MonoBehaviour): Destroy/FindObjectsByType aren't inherited here

/// <summary>
/// Passive imager: measures apparent luminosity in a raster of directions around an aim point and draws it as
/// a false-colour scan. Fully code-built (UIKit), same pattern as SystemsDock's tabs (plain class, Build
/// returns the root GameObject, Refresh is called every frame by the owning console). A deliberate, aimed
/// action like recording: SensorConsole calls Hide() when another mode is selected, which stops the scan —
/// same as it always has. The maths (auto-exposure, block integration, point-spread) is carried over
/// unchanged from the original scan; only the UI chrome around it (sliders, dropdown, crosshair sprites) has
/// been rebuilt in code.
/// </summary>
public sealed class ImagerScreen
{
    private const int DisplayW = 860;
    private const int DisplayH = 480;

    private ImagerSpec _spec;

    // Tunables
    private float _fovRaw;       // pre-zoom FOV, degrees
    private int   _blockSize;
    private float _gain = 1f;
    private float _exposureMul = 1f;
    private float _zoomFactor = 1f;
    private bool  _scanning;

    private float _offsetAz, _offsetEl;
    private readonly List<CelestialBody> _bodies = new List<CelestialBody>();
    private int _targetIndex = -1; // -1 = dead ahead
    private SystemData _boundSystem;

    // Raster state
    private int _resX, _resY;
    private int _currentRow;
    private float _rowAccum;
    private float[][] _frames;
    private int _frameUsed, _frameCur;
    private float _autoExposureMax = GameConstants.IMAGER_AUTO_EXPOSURE_FLOOR;

    private Texture2D  _texture;
    private Color32[]  _clearBuffer;
    private Color32[]  _rowBuffer;
    private RawImage   _image;
    private Image       _crossH, _crossV;

    private struct BodySample { public float az, el, angularSize, luminosity; }
    private BodySample[] _samples = new BodySample[16];
    private int _sampleCount;

    private TextMeshProUGUI _fovLabel, _resLabel, _gainLabel, _expLabel, _targetLabel, _scanLabel;
    private Button _scanButton, _zoomButton;

    public float AimAzimuth   { get { return _offsetAz; } }
    public float AimElevation { get { return _offsetEl; } }

    public GameObject Build(Transform parent)
    {
        _spec = Game.State != null ? Game.State.GetSpec<ImagerSpec>() : null;
        if (_spec == null) _spec = ScriptableObject.CreateInstance<ImagerSpec>();

        UITheme t = UITheme.Current;
        _fovRaw = Mathf.Clamp(GameConstants.IMAGER_DEFAULT_FOV, _spec.fovMin, _spec.fovMax);
        _blockSize = Sanitize(GameConstants.IMAGER_DEFAULT_BLOCK_SIZE);

        RectTransform root = UIKit.Node("Imager", parent);
        UIKit.Size(root, flexibleWidth: 1f);
        var v = UIKit.VStack(root, t.spacing, 0);
        v.childAlignment = TextAnchor.UpperLeft;
        var fit = root.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        BuildDisplay(root);
        BuildControls(root);

        CreateBuffers();
        ClearImage();

        return root.gameObject;
    }

    private void BuildDisplay(Transform parent)
    {
        UITheme t = UITheme.Current;
        RectTransform area = UIKit.Node("DisplayArea", parent);
        UIKit.Size(area, preferredWidth: DisplayW, minHeight: DisplayH);

        _image = UIKit.AddRawImage(area, "Scan", Color.white);
        UIKit.Stretch(_image.rectTransform);

        Color c = t.accent; c.a = 0.5f;
        _crossH = UIKit.AddPanel(area, "CrossH", c);
        RectTransform h = _crossH.rectTransform;
        h.anchorMin = new Vector2(0f, 0.5f); h.anchorMax = new Vector2(1f, 0.5f);
        h.sizeDelta = new Vector2(0f, 2f);

        _crossV = UIKit.AddPanel(area, "CrossV", c);
        RectTransform vv = _crossV.rectTransform;
        vv.anchorMin = new Vector2(0.5f, 0f); vv.anchorMax = new Vector2(0.5f, 1f);
        vv.sizeDelta = new Vector2(2f, 0f);
    }

    private void BuildControls(Transform parent)
    {
        UITheme t = UITheme.Current;

        RectTransform row1 = UIKit.Node("Row1", parent);
        UIKit.HStack(row1, 6f, 0, expandWidth: true);
        _scanButton = UIKit.AddButton(row1, "SCAN", OnToggleScan, 100f, 32f);
        _scanLabel = _scanButton.GetComponentInChildren<TextMeshProUGUI>();
        _zoomButton = UIKit.AddButton(row1, "ZOOM", OnToggleZoom, 90f, 32f);
        _targetLabel = UIKit.AddLabel(row1, "", t.fontSizeSmall, t.text, TextAlignmentOptions.MidlineLeft);
        UIKit.Size(_targetLabel.rectTransform, flexibleWidth: 1f);
        UIKit.AddButton(row1, "<", () => CycleTarget(-1), 34f, 32f);
        UIKit.AddButton(row1, ">", () => CycleTarget(1), 34f, 32f);

        RectTransform row2 = UIKit.Node("Row2", parent);
        UIKit.HStack(row2, 6f, 0, expandWidth: true);
        _fovLabel = Stepper(row2, "FOV", () => AdjustFov(-5f), () => AdjustFov(5f));
        _resLabel = Stepper(row2, "RES", () => AdjustBlock(2), () => AdjustBlock(-2));
        _gainLabel = Stepper(row2, "GAIN", () => AdjustGain(-0.25f), () => AdjustGain(0.25f));
        _expLabel = Stepper(row2, "EXP", () => AdjustExposure(-0.25f), () => AdjustExposure(0.25f));
    }

    // A "- LABEL value +" trio, matching WakePanel/ReactorPanel's existing stepper style.
    private TextMeshProUGUI Stepper(Transform parent, string name, UnityEngine.Events.UnityAction dec, UnityEngine.Events.UnityAction inc)
    {
        UITheme t = UITheme.Current;
        RectTransform group = UIKit.Node(name, parent);
        UIKit.HStack(group, 2f, 0);
        UIKit.AddLabel(group, name, t.fontSizeSmall, t.textDim);
        UIKit.AddButton(group, "-", dec, 28f, 28f);
        TextMeshProUGUI label = UIKit.AddLabel(group, "", t.fontSizeSmall, t.text, TextAlignmentOptions.Center);
        UIKit.Size(label.rectTransform, preferredWidth: 56f);
        UIKit.AddButton(group, "+", inc, 28f, 28f);
        return label;
    }

    private int Sanitize(int requested)
    {
        if (requested % 2 == 0) requested++;
        return Mathf.Clamp(requested, Mathf.Max(1, _spec.minBlockSize), 20);
    }

    private float EffectiveFov() { return _fovRaw / _zoomFactor; }

    private void CreateBuffers()
    {
        if (_texture != null) Destroy(_texture);
        _texture = new Texture2D(DisplayW, DisplayH, TextureFormat.RGBA32, false);
        _image.texture = _texture;

        _clearBuffer = new Color32[DisplayW * DisplayH];
        for (int i = 0; i < _clearBuffer.Length; i++) _clearBuffer[i] = new Color32(0, 0, 0, 255);

        ComputeResolution();
        AllocateFrames();
    }

    private void ComputeResolution()
    {
        _resX = Mathf.Max(1, DisplayW / _blockSize);
        _resY = Mathf.Max(1, DisplayH / _blockSize);
        _rowBuffer = new Color32[_resX * _blockSize * _blockSize];
    }

    private void AllocateFrames()
    {
        _frames = new float[Mathf.Max(1, _spec.integratorDepth)][];
        _frames[0] = new float[_resX * _resY];
        _frameUsed = 1;
        _frameCur = 0;
        _currentRow = 0;
        _rowAccum = 0f;
    }

    private void ClearImage()
    {
        _texture.SetPixels32(_clearBuffer);
        _texture.Apply(false);
    }

    private void RestartRaster()
    {
        bool wasScanning = _scanning;
        StopScan();
        ComputeResolution();
        AllocateFrames();
        ClearImage();
        if (wasScanning) StartScan();
    }

    // --- Buttons -------------------------------------------------------------

    private void OnToggleScan()
    {
        if (_scanning) StopScan(); else StartScan();
    }

    private void StartScan()
    {
        ClearImage();
        _scanning = true;
        if (Game.State != null) Game.State.SetLoad("imager", _spec.powerDraw);
    }

    private void StopScan()
    {
        if (!_scanning) return;
        _scanning = false;
        if (Game.State != null) Game.State.SetLoad("imager", 0f);
    }

    /// <summary>Called by SensorConsole when another mode is selected — stops the (deliberate) scan.</summary>
    public void Hide() => StopScan();

    private void OnToggleZoom()
    {
        _zoomFactor = _zoomFactor == 1f ? GameConstants.IMAGER_ZOOM_FACTOR : 1f;
        RestartRaster();
    }

    private void AdjustFov(float delta)
    {
        _fovRaw = Mathf.Clamp(_fovRaw + delta, _spec.fovMin, _spec.fovMax);
        RestartRaster();
    }

    private void AdjustBlock(int delta)
    {
        _blockSize = Sanitize(_blockSize + delta);
        RestartRaster();
    }

    private void AdjustGain(float delta) { _gain = Mathf.Clamp(_gain + delta, 0.1f, 10f); }
    private void AdjustExposure(float delta) { _exposureMul = Mathf.Clamp(_exposureMul + delta, 0.1f, 5f); }

    // Cycles -1 (free aim, dead ahead) through 0.._bodies.Count-1 (locked to that body), wrapping either way.
    private void CycleTarget(int dir)
    {
        RefreshBodiesIfNeeded();
        if (_bodies.Count == 0) { _targetIndex = -1; return; }
        int span = _bodies.Count + 1;
        int raw  = ((_targetIndex + 1) + dir % span + span) % span;
        _targetIndex = raw - 1;
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

    // --- Frame loop ------------------------------------------------------------

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        RefreshLabels();

        if (!_scanning) return;
        float interval = Mathf.Max(0.005f, _spec.scanRowIntervalSeconds);
        _rowAccum += unscaledDeltaSeconds;
        int guard = 0;
        while (_rowAccum >= interval && guard++ < 64)
        {
            _rowAccum -= interval;
            ScanOneRow();
        }
    }

    private void ScanOneRow()
    {
        if (_currentRow == 0)
        {
            RefreshBodiesIfNeeded();
            SnapshotBodies();
            ComputeAutoExposure();
        }

        if (_targetIndex >= 0 && _targetIndex < _bodies.Count)
        {
            CelestialBody tgt = _bodies[_targetIndex];
            if (tgt != null && tgt.gameObject.activeInHierarchy)
            {
                _offsetAz = tgt.azimuth;
                _offsetEl = tgt.elevation;
            }
        }

        SnapshotBodies();

        float fov = EffectiveFov();
        float blockAngularResolution = fov / _resX;
        float halfFovX = fov / 2f;
        float halfFovY = fov / 2f * _resY / _resX;

        float exposureRef = Mathf.Max(_autoExposureMax * _exposureMul, GameConstants.IMAGER_AUTO_EXPOSURE_FLOOR);
        float elevation = Mathf.Lerp(-halfFovY, halfFovY, (float)_currentRow / _resY) + _offsetEl;

        float[] frame = _frames[_frameCur];
        int bandWidth = _resX * _blockSize;

        for (int sx = 0; sx < _resX; sx++)
        {
            float azimuth = Mathf.Lerp(-halfFovX, halfFovX, (float)sx / _resX) + _offsetAz;
            float luminosity = CalculateDirectionalLuminosity(azimuth, elevation, blockAngularResolution, exposureRef);

            int idx = _currentRow * _resX + sx;
            frame[idx] = luminosity;

            float sum = 0f;
            for (int f = 0; f < _frameUsed; f++) sum += _frames[f][idx];
            Color32 c = GetIntensityColor(sum / _frameUsed * _gain);

            int bx = sx * _blockSize;
            for (int by = 0; by < _blockSize; by++)
            {
                int r = by * bandWidth + bx;
                for (int k = 0; k < _blockSize; k++) _rowBuffer[r + k] = c;
            }
        }

        int y = _currentRow * _blockSize;
        _texture.SetPixels32(0, y, bandWidth, _blockSize, _rowBuffer);
        _texture.Apply(false);

        _currentRow++;
        if (_currentRow >= _resY)
        {
            _currentRow = 0;
            if (_frameUsed < _frames.Length)
            {
                _frameCur = _frameUsed++;
                _frames[_frameCur] = new float[_resX * _resY];
            }
            else
            {
                _frameCur = (_frameCur + 1) % _frames.Length;
            }
        }
    }

    private void SnapshotBodies()
    {
        int n = _bodies.Count;
        if (_samples.Length < n) _samples = new BodySample[Mathf.NextPowerOfTwo(Mathf.Max(1, n))];

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            CelestialBody b = _bodies[i];
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.distance <= 0f) continue;

            _samples[count++] = new BodySample
            {
                az = b.azimuth, el = b.elevation,
                angularSize = b.angularSize, luminosity = b.apparentLuminosity
            };
        }
        _sampleCount = count;
    }

    private void ComputeAutoExposure()
    {
        float fovHalf = EffectiveFov() / 2f;
        float sceneMax = GameConstants.IMAGER_AUTO_EXPOSURE_FLOOR;

        for (int i = 0; i < _sampleCount; i++)
        {
            BodySample s = _samples[i];
            float dAz = Mathf.Abs(Mathf.DeltaAngle(s.az, _offsetAz));
            float dEl = Mathf.Abs(s.el - _offsetEl);
            if (dAz > fovHalf || dEl > fovHalf) continue;
            if (s.luminosity > sceneMax) sceneMax = s.luminosity;
        }

        _autoExposureMax = Mathf.Lerp(_autoExposureMax, sceneMax, GameConstants.IMAGER_AUTO_EXPOSURE_LERP);
    }

    private float CalculateDirectionalLuminosity(float azimuth, float elevation, float blockAngularResolution, float exposureRef)
    {
        float total = Random.Range(0f, 0.005f);
        float sigma = Mathf.Max(blockAngularResolution / 2f, 0.001f);

        for (int i = 0; i < _sampleCount; i++)
        {
            BodySample s = _samples[i];
            float dAz = Mathf.DeltaAngle(azimuth, s.az);
            float dEl = elevation - s.el;
            float angularDist = Mathf.Sqrt(dAz * dAz + dEl * dEl);
            if (angularDist >= s.angularSize + blockAngularResolution) continue;

            float weight;
            if (angularDist <= s.angularSize) weight = 1f;
            else
            {
                float excess = angularDist - s.angularSize;
                weight = Mathf.Exp(-0.5f * (excess * excess) / (sigma * sigma));
            }
            total += s.luminosity * weight;
        }

        return CompressionLaw(total, 0f, 2f, exposureRef);
    }

    private static Color32 GetIntensityColor(float luminosity)
    {
        if (luminosity < 0.5f)
        {
            byte g = (byte)(Mathf.Clamp01(luminosity * 2f) * 255f);
            return new Color32(0, g, 0, 255);
        }
        byte tt = (byte)(Mathf.Clamp01((luminosity - 0.5f) * 2f) * 255f);
        return new Color32(tt, 255, tt, 255);
    }

    private void RefreshLabels()
    {
        UITheme t = UITheme.Current;
        UIKit.SetText(_fovLabel, EffectiveFov().ToString("F0") + "°");
        UIKit.SetText(_resLabel, _blockSize.ToString());
        UIKit.SetText(_gainLabel, "x" + _gain.ToString("F2"));
        UIKit.SetText(_expLabel, "x" + _exposureMul.ToString("F2"));

        string targetName = (_targetIndex >= 0 && _targetIndex < _bodies.Count && _bodies[_targetIndex] != null)
                           ? _bodies[_targetIndex].bodyName : "FREE AIM";
        UIKit.SetText(_targetLabel, targetName);

        UIKit.SetText(_scanLabel, _scanning ? "SCANNING" : "SCAN");
        UIKit.SetButtonActive(_scanButton, _scanning);
        UIKit.SetButtonActive(_zoomButton, _zoomFactor != 1f);

        Color c = t.accent; c.a = _zoomFactor != 1f ? 0.8f : 0.4f;
        _crossH.color = c; _crossV.color = c;
    }

}
