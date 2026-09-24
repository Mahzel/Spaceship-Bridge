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
///
/// Aiming is in the WORLD frame (bearing 0 = +Z, elevation + = up, same as tracks), so the picture doesn't
/// swing when the ship turns. Two modes:
///  - FREE AIM: slew by hand. AZ/EL steppers (a tenth of the FOV per click), arrow keys (half the FOV per
///    second, Shift = fine), drag the image to pan, click it to recentre on that point.
///  - TRACK: slaved to a LOCKED track (Prev/Next cycles them; SEL takes the one selected in the Track panel or
///    System view). Azimuth follows the track's bearing. Elevation follows the track's elevation only when that
///    estimate is tight compared with the FOV (ElSlaved); a coarse one (waterfall fan) leaves elevation manual,
///    so you can nod the camera up/down the bearing line to find the contact and FIX it. Any manual move of an
///    axis the track drives drops back to FREE AIM.
/// Moving the aim by hand clears the frame integrator (a slewing camera smears).
///
/// FIX: finds the brightest blob near the crosshair in the integrated frame and, if it sits on the target
/// track's bearing, writes its elevation into that track (ElevationSource.Imager) and counts as a detection:
/// it locks a searching track and holds its lock (TrackManager.ApplySupport), with the blob's precise bearing. That's the fine elevation
/// the spectrometer needs to put its slit on a contact. The target is the slaved track, else the selected one.
/// What shows up in the frame is whatever is physically there (SensorSight): nothing, one thing, or several.
/// </summary>
public sealed class ImagerScreen
{
    private const int DisplayW = 860;
    private const int DisplayH = 480;
    private const float StepFovFraction = 0.1f;     // AZ/EL stepper click, as a fraction of the FOV
    private const float KeySlewFovPerSec = 0.5f;    // arrow keys, FOVs per second
    private const float FixSearchFovFraction = 0.12f; // FIX looks this far (fraction of FOV) around the crosshair
    private const float FixMinSignal = 0.004f;      // absolute floor of the (compressed) brightness a blob needs
    private const float FixSnr = 6f;                // ... and it must be this many times the frame's background
    private const float FixGateMinDeg = 1f;         // bearing gate between the blob and the track, floor
    private const float NoiseExposureFloorRatio = 20f; // lowest exposure ceiling = this x the spec's block noise

    private GameObject _root;
    private string _flash;
    private float _flashHold;

    private ImagerSpec _spec;

    // Tunables
    private float _fovRaw;       // pre-zoom FOV, degrees
    private int   _blockSize;
    private float _gain = 1f;
    private float _exposureMul = 1f;
    private float _zoomFactor = 1f;
    private bool  _scanning;

    private float _offsetAz, _offsetEl;
    private int _trackId;          // 0 = free aim
    private readonly List<Track> _candidates = new List<Track>();

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

    private TextMeshProUGUI _fovLabel, _resLabel, _gainLabel, _expLabel, _targetLabel, _scanLabel, _aimLabel, _fixLabel;
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
        _root = root.gameObject;
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
        _image.raycastTarget = true;
        var aim = _image.gameObject.AddComponent<PointerAim>();
        aim.OnClick = OnImageClicked;
        aim.OnDragged = OnImageDragged;

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
        UIKit.AddButton(row1, "SEL", AimAtSelected, 50f, 32f);
        UIKit.AddButton(row1, "<", () => CycleTarget(-1), 34f, 32f);
        UIKit.AddButton(row1, ">", () => CycleTarget(1), 34f, 32f);

        RectTransform row2 = UIKit.Node("Row2", parent);
        UIKit.HStack(row2, 6f, 0, expandWidth: true);
        _fovLabel = Stepper(row2, "FOV", () => AdjustFov(-5f), () => AdjustFov(5f));
        _resLabel = Stepper(row2, "RES", () => AdjustBlock(2), () => AdjustBlock(-2));
        _gainLabel = Stepper(row2, "GAIN", () => AdjustGain(-0.25f), () => AdjustGain(0.25f));
        _expLabel = Stepper(row2, "EXP", () => AdjustExposure(-0.25f), () => AdjustExposure(0.25f));

        RectTransform row3 = UIKit.Node("Row3", parent);
        var h3 = UIKit.HStack(row3, 6f, 0);
        h3.childAlignment = TextAnchor.MiddleLeft;
        StepperButtons(row3, "AZ", () => SlewManual(-StepFovFraction * EffectiveFov(), 0f), () => SlewManual(StepFovFraction * EffectiveFov(), 0f));
        StepperButtons(row3, "EL", () => SlewManual(0f, -StepFovFraction * EffectiveFov()), () => SlewManual(0f, StepFovFraction * EffectiveFov()));
        _aimLabel = UIKit.AddLabel(row3, "", t.fontSizeSmall, t.text);
        UIKit.Size(_aimLabel.rectTransform, preferredWidth: 250f);
        UIKit.AddButton(row3, Loc.Get("ui.imager.fix"), OnFix, 70f, 30f);
        _fixLabel = UIKit.AddLabel(row3, "", t.fontSizeSmall, t.textDim);
        UIKit.Size(_fixLabel.rectTransform, flexibleWidth: 1f);
    }

    // "LABEL - +" with no value readout (the combined AZ/EL readout sits next to them).
    private static void StepperButtons(Transform parent, string name, UnityEngine.Events.UnityAction dec, UnityEngine.Events.UnityAction inc)
    {
        UITheme t = UITheme.Current;
        RectTransform group = UIKit.Node(name, parent);
        UIKit.HStack(group, 2f, 0);
        UIKit.AddLabel(group, name, t.fontSizeSmall, t.textDim);
        UIKit.AddButton(group, "-", dec, 28f, 28f);
        UIKit.AddButton(group, "+", inc, 28f, 28f);
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

    /// <summary>5-degree steps above 5 degrees, halving/doubling below it (down to the hardware's fovMin).</summary>
    private void AdjustFov(float delta)
    {
        float f = _fovRaw;
        if (delta < 0f && f <= 5f + 1e-3f) f *= 0.5f;
        else if (delta > 0f && f < 5f - 1e-3f) f = Mathf.Min(5f, f * 2f);
        else f += delta;
        _fovRaw = Mathf.Clamp(f, _spec.fovMin, _spec.fovMax);
        RestartRaster();
    }

    /// <summary>Picks up a new imager tier after a refit (the fitted spec object changes at launch).</summary>
    private void SyncSpec()
    {
        ImagerSpec fitted = Game.State != null ? Game.State.GetSpec<ImagerSpec>() : null;
        if (fitted == null || fitted == _spec) return;
        _spec = fitted;
        _fovRaw = Mathf.Clamp(_fovRaw, _spec.fovMin, _spec.fovMax);
        _blockSize = Sanitize(_blockSize);
        RestartRaster();
    }

    private static string FovText(float fov)
    {
        return fov >= 10f ? fov.ToString("F0") : fov >= 1f ? fov.ToString("F1") : fov >= 0.1f ? fov.ToString("F2") : fov.ToString("F3");
    }

    private void AdjustBlock(int delta)
    {
        _blockSize = Sanitize(_blockSize + delta);
        RestartRaster();
    }

    private void AdjustGain(float delta) { _gain = Mathf.Clamp(_gain + delta, 0.1f, 10f); }
    private void AdjustExposure(float delta) { _exposureMul = Mathf.Clamp(_exposureMul + delta, 0.1f, 5f); }

    // Cycles FREE AIM plus every LOCKED track, wrapping either way.
    private void CycleTarget(int dir)
    {
        if (Game.State == null) return;
        // Any track, searching included: pointing the imager down a searching track's bearing and FIXing the
        // blob there is how a contact too faint for the waterfall gets confirmed.
        _candidates.Clear();
        _candidates.AddRange(Game.State.Tracks.All);
        if (_candidates.Count == 0) { _trackId = 0; return; }

        int current = -1;
        for (int i = 0; i < _candidates.Count; i++) if (_candidates[i].id == _trackId) current = i;
        int span = _candidates.Count + 1;
        int raw  = ((current + 1) + dir % span + span) % span;
        _trackId = raw == 0 ? 0 : _candidates[raw - 1].id;
    }

    private void AimAtSelected()
    {
        if (Game.State == null) return;
        Track tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr != null) _trackId = tr.id;
    }

    /// <summary>The track the imager is slaved to, or null (free aim / dropped / wiped by a jump).</summary>
    private Track AimTrack()
    {
        if (_trackId == 0 || Game.State == null) return null;
        Track tr = Game.State.Tracks.Find(_trackId);
        if (tr == null) _trackId = 0;
        return tr;
    }

    // --- Manual aim ------------------------------------------------------------

    /// <summary>Moves the aim by hand. Touching an axis the slaved track drives releases the slave.</summary>
    private void SlewManual(float dAzDeg, float dElDeg)
    {
        if (dAzDeg == 0f && dElDeg == 0f) return;
        Track tr = AimTrack();
        if (tr != null && (dAzDeg != 0f || (dElDeg != 0f && ElSlaved(tr)))) _trackId = 0;

        _offsetAz = BearingMath.Wrap360(_offsetAz + dAzDeg);
        _offsetEl = Mathf.Clamp(_offsetEl + dElDeg, -90f, 90f);
        ResetIntegration();
    }

    /// <summary>Whether the track drives elevation: locked, with an elevation whose (aged) sigma is within a
    /// quarter of the vertical half-FOV, so the contact is sure to be in frame.</summary>
    private bool ElSlaved(Track tr)
    {
        if (tr == null || !tr.hasElevation) return false;
        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        return TrackManager.AgedElevationSigma(tr, now) <= 0.25f * HalfFovY();
    }

    private float HalfFovY() { return EffectiveFov() * 0.5f * _resY / Mathf.Max(1, _resX); }

    // Click: recentre on the clicked direction. Local (0,0) is the image centre = the current aim.
    private void OnImageClicked(Vector2 local, RectTransform rt)
    {
        if (rt.rect.width < 1f || rt.rect.height < 1f) return;
        float dAz = local.x / rt.rect.width * EffectiveFov();
        float dEl = local.y / rt.rect.height * 2f * HalfFovY();
        SlewManual(dAz, dEl);
    }

    // Drag: grab-and-pan, the sky moves with the pointer, so the aim moves the other way.
    private void OnImageDragged(Vector2 delta, Vector2 local, RectTransform rt)
    {
        if (rt.rect.width < 1f || rt.rect.height < 1f) return;
        SlewManual(-delta.x / rt.rect.width * EffectiveFov(), -delta.y / rt.rect.height * 2f * HalfFovY());
    }

    /// <summary>Drops the integrated history (the view moved) but keeps the raster position, so the picture
    /// keeps refreshing top to bottom instead of restarting.</summary>
    private void ResetIntegration()
    {
        if (_frames == null) return;
        for (int f = 0; f < _frames.Length; f++)
            if (_frames[f] != null) System.Array.Clear(_frames[f], 0, _frames[f].Length);
        _frameUsed = 1;
        _frameCur = 0;
    }

    /// <summary>
    /// Elevation fix from the picture. The brightest integrated block within FixSearchFovFraction of the
    /// crosshair seeds a blob; every connected block above half that brightness belongs to it (so a resolved
    /// disk, a planet seen from close by, is taken whole, not by whichever of its saturated blocks happens to
    /// be brightest). The blob's brightness-weighted CENTROID is the measurement, its equivalent radius the
    /// target's apparent size. It must sit on the target track's bearing (gate: a few blocks, or the blob's own
    /// radius if bigger) and must not be cut by the frame edge (widen the FOV first), otherwise nothing is
    /// written. The fix's sigma is half a block.
    /// </summary>
    private void OnFix()
    {
        if (Game.State == null || _frames == null) return;
        Track tr = AimTrack();
        if (tr == null) tr = Game.State.Tracks.Find(Game.State.Tracks.SelectedId);
        if (tr == null) { Flash(Loc.Get("ui.imager.fix.notrack")); return; }

        float fov = EffectiveFov();
        float block = fov / Mathf.Max(1, _resX);
        float halfX = fov * 0.5f, halfY = HalfFovY();
        int n = _resX * _resY;
        if (_fixAvg == null || _fixAvg.Length != n) { _fixAvg = new float[n]; _fixMark = new bool[n]; }
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            for (int f = 0; f < _frameUsed; f++) sum += _frames[f][i];
            _fixAvg[i] = _frameUsed > 0 ? sum / _frameUsed : 0f;
            _fixMark[i] = false;
        }

        int rx = Mathf.Max(1, Mathf.RoundToInt(FixSearchFovFraction * _resX));
        int cx = _resX / 2, cy = _resY / 2;
        float best = 0f; int bx = -1, by = -1;
        for (int y = Mathf.Max(0, cy - rx); y < Mathf.Min(_resY, cy + rx + 1); y++)
            for (int x = Mathf.Max(0, cx - rx); x < Mathf.Min(_resX, cx + rx + 1); x++)
            {
                float v = _fixAvg[y * _resX + x];
                if (v > best) { best = v; bx = x; by = y; }
            }

        // Detection threshold relative to the frame's own background (its 75th percentile block), not an absolute
        // brightness: a faint contact at a high exposure ceiling is still a clear blob if it stands well above the
        // noise. FixMinSignal is just a floor so a perfectly black frame can't "detect" rounding noise.
        float background = Percentile75(_fixAvg, n);
        float threshold = Mathf.Max(FixMinSignal, FixSnr * background);
        if (bx < 0 || best < threshold) { Flash(Loc.Get("ui.imager.fix.nothing")); return; }

        // Grow the blob (4-connected) from the seed.
        float floor = Mathf.Max(threshold, 0.5f * best);
        double wSum = 0, xSum = 0, ySum = 0;
        int count = 0;
        bool clipped = false;
        _fixStack.Clear();
        _fixStack.Add(by * _resX + bx);
        _fixMark[by * _resX + bx] = true;
        while (_fixStack.Count > 0)
        {
            int idx = _fixStack[_fixStack.Count - 1];
            _fixStack.RemoveAt(_fixStack.Count - 1);
            int x = idx % _resX, y = idx / _resX;
            float v = _fixAvg[idx];
            wSum += v; xSum += v * x; ySum += v * y; count++;
            if (x == 0 || y == 0 || x == _resX - 1 || y == _resY - 1) clipped = true;
            TryGrow(x - 1, y, floor); TryGrow(x + 1, y, floor); TryGrow(x, y - 1, floor); TryGrow(x, y + 1, floor);
        }
        if (clipped) { Flash(Loc.Get("ui.imager.fix.clipped")); return; }

        float fx = (float)(xSum / wSum), fy = (float)(ySum / wSum);
        float radiusDeg = Mathf.Sqrt(count / Mathf.PI) * block;

        // Same mapping ScanOneRow used to sample those blocks.
        float az = BearingMath.Wrap360(Mathf.Lerp(-halfX, halfX, (fx + 0.5f) / _resX) + _offsetAz);
        float el = Mathf.Lerp(-halfY, halfY, (fy + 0.5f) / _resY) + _offsetEl;

        float gate = Mathf.Max(Mathf.Max(FixGateMinDeg, 3f * block), radiusDeg);
        if (Mathf.Abs(BearingMath.Diff(az, tr.bearing)) > gate)
        {
            Flash(Loc.Get("ui.imager.fix.offbearing", tr.name, BearingMath.Diff(az, tr.bearing)));
            return;
        }

        double now = Game.Clock != null ? Game.Clock.SimSeconds : 0.0;
        bool taken = Game.State.Tracks.ApplyElevationFix(tr.id, now, el, 0.5f * block, ElevationSource.Imager);
        // A blob several blocks across is a resolved disk: remember its apparent size (the spectrometer can then
        // point anywhere on it).
        tr.angularRadiusDeg = radiusDeg > 1.5f * block ? radiusDeg : 0f;
        // The blob itself is a detection of the contact: it confirms (locks) the track and refines its bearing.
        ShipState ship = Game.State.Ship;
        Game.State.Tracks.ApplySupport(tr.id, now, ElevationSource.Imager, true, az, 0.5f * block,
                                       ship.x, ship.z, (float)ship.headingDeg);
        Flash(taken ? Loc.Get("ui.imager.fix.ok", tr.name, el, 0.5f * block)
                    : Loc.Get("ui.imager.fix.worse", tr.name));
    }

    private float[] _fixSort;

    private float Percentile75(float[] values, int n)
    {
        if (_fixSort == null || _fixSort.Length != n) _fixSort = new float[n];
        System.Array.Copy(values, _fixSort, n);
        System.Array.Sort(_fixSort);
        return _fixSort[Mathf.Clamp((int)(0.75f * n), 0, n - 1)];
    }

    private readonly List<int> _fixStack = new List<int>();
    private float[] _fixAvg;
    private bool[] _fixMark;

    private void TryGrow(int x, int y, float floor)
    {
        if (x < 0 || y < 0 || x >= _resX || y >= _resY) return;
        int idx = y * _resX + x;
        if (_fixMark[idx] || _fixAvg[idx] < floor) return;
        _fixMark[idx] = true;
        _fixStack.Add(idx);
    }

    private void Flash(string msg) { _flash = msg; _flashHold = 4f; }

    // --- Frame loop ------------------------------------------------------------

    /// <summary>Called every frame by SensorConsole, regardless of whether this mode is the one showing.</summary>
    public void Refresh(float unscaledDeltaSeconds)
    {
        SyncSpec();
        // Keyboard aim only while this tab is showing.
        if (_root != null && _root.activeInHierarchy)
        {
            Vector2 slew = AimKeys.Slew();
            if (slew != Vector2.zero)
            {
                float rate = KeySlewFovPerSec * EffectiveFov() * unscaledDeltaSeconds;
                SlewManual(slew.x * rate, slew.y * rate);
            }
        }
        if (_flashHold > 0f) _flashHold -= unscaledDeltaSeconds;

        RefreshLabels();

        // Paused: the frame and its integration freeze (aiming still works; a manual move clears it as usual).
        if (!_scanning || (Game.Clock != null && Game.Clock.Paused)) return;
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
        // Slave to the track: azimuth = its bearing, elevation = its elevation if it has one (world frame, same
        // as the samples). A searching track's bearing is where it was marked (or last seen): aim there too.
        Track tr = AimTrack();
        if (tr != null)
        {
            _offsetAz = tr.bearing;
            if (ElSlaved(tr)) _offsetEl = tr.elevationDeg;
        }

        if (_currentRow == 0)
        {
            SnapshotBodies();
            ComputeAutoExposure();
        }

        SnapshotBodies();

        float fov = EffectiveFov();
        float blockAngularResolution = fov / _resX;
        float halfFovX = fov / 2f;
        float halfFovY = fov / 2f * _resY / _resX;

        // Never stretch the picture past ~20x the per-block noise (Random 0..blockNoise below): an empty or very dim
        // frame stays dark instead of blowing its noise up to full white (which FIX would then take for a blob).
        float exposureRef = Mathf.Max(_autoExposureMax * _exposureMul, GameConstants.IMAGER_AUTO_EXPOSURE_FLOOR,
                                      NoiseExposureFloorRatio * _spec.blockNoise);
        // Sample each block at its CENTER (the image centre, where the crosshair is, falls on a block edge).
        float elevation = Mathf.Lerp(-halfFovY, halfFovY, (_currentRow + 0.5f) / _resY) + _offsetEl;

        float[] frame = _frames[_frameCur];
        int bandWidth = _resX * _blockSize;

        for (int sx = 0; sx < _resX; sx++)
        {
            float azimuth = Mathf.Lerp(-halfFovX, halfFovX, (sx + 0.5f) / _resX) + _offsetAz;
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
        List<CelestialBody> bodies = SensorSight.AllBodies();
        Transform ship = SensorSight.Ship();
        int n = bodies.Count;
        if (_samples.Length < n) _samples = new BodySample[Mathf.NextPowerOfTwo(Mathf.Max(1, n))];

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            CelestialBody b = bodies[i];
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.distance <= 0f) continue;

            _samples[count++] = new BodySample
            {
                az = SensorSight.WorldAzimuth(b, ship), el = SensorSight.WorldElevation(b, ship),
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
        float total = Random.Range(0f, _spec.blockNoise);
        float sigma = Mathf.Max(blockAngularResolution / 2f, 0.001f);

        for (int i = 0; i < _sampleCount; i++)
        {
            BodySample s = _samples[i];
            float dAz = Mathf.DeltaAngle(azimuth, s.az);
            float dEl = elevation - s.el;
            float angularDist = Mathf.Sqrt(dAz * dAz + dEl * dEl);
            if (angularDist >= s.angularSize + blockAngularResolution) continue;

            float weight;
            if (s.angularSize < blockAngularResolution)
            {
                // Unresolved (point) source: the block collects the fraction of its light that the PSF puts inside
                // the block's square. Flux-conserving, so a faint dot no longer fades to nothing when it happens to
                // sit between two sample points (the old peak-sampled PSF lost up to ~99% of it at small FOVs).
                float h = 0.5f * blockAngularResolution;
                weight = BlockFraction(dAz, h, sigma) * BlockFraction(dEl, h, sigma);
            }
            else if (angularDist <= s.angularSize) weight = 1f; // resolved disk: its surface fills the block
            else
            {
                float excess = angularDist - s.angularSize;
                weight = Mathf.Exp(-0.5f * (excess * excess) / (sigma * sigma));
            }
            total += s.luminosity * weight;
        }

        // Linear up to the knee, log above it, saturating at exposureRef. The knee stays at 2 for bright scenes (unchanged
        // look) but slides down to half the ceiling for dim ones (empty sky, a faint planet down a track bearing), since
        // CompressionLaw needs xMin < knee < xMax and the ceiling can fall all the way to IMAGER_AUTO_EXPOSURE_FLOOR.
        float knee = Mathf.Min(2f, 0.5f * exposureRef);
        return CompressionLaw(total, 0f, knee, exposureRef);
    }

    /// <summary>Fraction of a 1-D Gaussian (sigma) centred `d` away that falls within +/-h: the block integral.</summary>
    private static float BlockFraction(float d, float h, float sigma)
    {
        float k = 1f / (1.41421356f * sigma);
        return 0.5f * (Erf((h - d) * k) + Erf((h + d) * k));
    }

    /// <summary>Abramowitz &amp; Stegun 7.1.26 (|error| &lt; 1.5e-7).</summary>
    private static float Erf(float x)
    {
        float sign = x < 0f ? -1f : 1f;
        x = Mathf.Abs(x);
        float t = 1f / (1f + 0.3275911f * x);
        float y = 1f - (((((1.061405429f * t - 1.453152027f) * t) + 1.421413741f) * t - 0.284496736f) * t + 0.254829592f) * t * Mathf.Exp(-x * x);
        return sign * y;
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
        UIKit.SetText(_fovLabel, FovText(EffectiveFov()) + "°");
        UIKit.SetText(_resLabel, _blockSize.ToString());
        UIKit.SetText(_gainLabel, "x" + _gain.ToString("F2"));
        UIKit.SetText(_expLabel, "x" + _exposureMul.ToString("F2"));

        Track tr = AimTrack();
        string targetName = tr == null ? Loc.Get("ui.imager.free")
                          : tr.Locked ? Loc.Get("ui.imager.track", tr.name)
                          : Loc.Get("ui.imager.lost", tr.name);
        UIKit.SetText(_targetLabel, targetName);

        bool elSlaved = ElSlaved(tr);
        UIKit.SetText(_aimLabel, Loc.Get(elSlaved ? "ui.imager.aim.trk" : "ui.imager.aim", BearingMath.Wrap360(_offsetAz), _offsetEl));
        UIKit.SetText(_fixLabel, _flashHold > 0f ? _flash : "");

        UIKit.SetText(_scanLabel, _scanning ? "SCANNING" : "SCAN");
        UIKit.SetButtonActive(_scanButton, _scanning);
        UIKit.SetButtonActive(_zoomButton, _zoomFactor != 1f);

        Color c = t.accent; c.a = _zoomFactor != 1f ? 0.8f : 0.4f;
        _crossH.color = c; _crossV.color = c;
    }

}
