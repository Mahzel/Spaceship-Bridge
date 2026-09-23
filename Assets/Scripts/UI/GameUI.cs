using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Owns the overlay canvas for the always-present game UI (status bar, debrief). Created by Game.Boot,
/// so no scene setup is needed. Screens live under one Canvas Scaler (1920x1080 reference).
/// </summary>
public class GameUI : MonoBehaviour
{
    private readonly StatusBar _status = new StatusBar();
    private readonly TrackPanel _trackPanel = new TrackPanel();
    private readonly ManeuverPanel _maneuver = new ManeuverPanel();
    private readonly OrbitPanel _orbit = new OrbitPanel();
    private readonly SystemsDock _systems = new SystemsDock();
    private readonly SensorConsole _sensors = new SensorConsole();
    private readonly AtlasPanel _atlas = new AtlasPanel();
    private TMPro.TextMeshProUGUI _toast;
    private int _toastSerial;
    private float _toastUntil;
    private readonly DebriefScreen _debrief = new DebriefScreen();
    private float _nextRefresh;

    private void Awake()
    {
        var canvasGO = new GameObject("GameCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _status.Build(canvasGO.transform);
        _trackPanel.Build(canvasGO.transform);
        _maneuver.Build(canvasGO.transform);
        _orbit.Build(canvasGO.transform);
        _systems.Build(canvasGO.transform);
        _sensors.Build(canvasGO.transform);
        _atlas.Build(canvasGO.transform);
        BuildToast(canvasGO.transform);
        _debrief.Build(canvasGO.transform); // last = drawn on top

        if (Game.Run != null) Game.Run.Changed += OnRunChanged;
    }

    private void Start()
    {
        if (FindFirstObjectByType<EventSystem>() == null)
            Debug.LogWarning("[GameUI] No EventSystem in the scene: the buttons will not react. Add one via GameObject > UI > Event System.");
        OnRunChanged();
    }

    private void OnDestroy()
    {
        if (Game.Run != null) Game.Run.Changed -= OnRunChanged;
    }

    private void OnRunChanged()
    {
        _debrief.Refresh();
        _status.Refresh();
        _trackPanel.Refresh();
        _maneuver.Refresh();
        _orbit.Refresh();
        _systems.Refresh();
        _sensors.Refresh(0f);
        _atlas.Refresh();
    }

    private void BuildToast(Transform parent)
    {
        UITheme t = UITheme.Current;
        _toast = UIKit.AddLabel(parent, "", t.fontSizeBody, t.warning, TMPro.TextAlignmentOptions.Center);
        RectTransform rt = _toast.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -(t.statusBarHeight + 8f));
        rt.sizeDelta = new Vector2(900f, 34f);
        _toast.gameObject.SetActive(false);
    }

    private void UpdateToast()
    {
        if (Game.Wake == null || _toast == null) return;
        if (Game.Wake.MessageSerial != _toastSerial)
        {
            _toastSerial = Game.Wake.MessageSerial;
            UIKit.SetText(_toast, Game.Wake.LastMessage);
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + 8f;
        }
        else if (_toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
            _toast.gameObject.SetActive(false);
    }

    private void Update()
    {
        UpdateToast();

        // Sensor screens tick every frame (scan rows, DSP redraws) like RunDriver ticks WaterfallProcessor —
        // a hidden mode still needs a smooth accumulator, not 100ms chunks.
        _sensors.Refresh(Time.unscaledDeltaTime);

        // The clock and power move every frame; 10 Hz is plenty for the text.
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.1f;
        _status.Refresh();
        _trackPanel.Refresh();
        _maneuver.Refresh();
        _orbit.Refresh();
        _systems.Refresh();
        _atlas.Refresh();
    }
}
