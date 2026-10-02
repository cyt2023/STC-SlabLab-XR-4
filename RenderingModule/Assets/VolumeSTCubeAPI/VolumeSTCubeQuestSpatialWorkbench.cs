using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
        : MonoBehaviour
    {
        private void OnEnable()
        {
            // Force the active desktop task surface to be reconstructed after a
            // Unity script-domain reload. Runtime-generated UI objects otherwise
            // survive visually while retaining the old, partially built layout.
            viewState.desktopMatrixPresentationReady = false;
        }

        private void Update()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                UpdateQuestImportHeadLock();
            UpdateKeyboard();
            UpdateSlabInteraction();
            UpdateTimeBoundaryInteraction();
            UpdateDepthBoundaryInteraction();
            UpdateAxisTokenInteraction();
            UpdatePaletteComponentInteraction();
            UpdateAxisRigHoverVisuals();
            UpdateAxisBucketFacing();
            UpdateDraftPivotPreviewDrag();
            UpdatePanelGrab();
            UpdateGroundEvidenceLink();
            EnforceSlabPreviewVisibility();
            UpdateVariablePaletteFollow(false);
            UpdateWorkflowToolbarFollow();
            WatchWaveImport();
            WatchFieldPresentation();
        }

        private void LateUpdate()
        {
            // XR hover/selection components can update renderer properties late
            // in the frame. Reassert the semantic selection colour afterwards
            // so a chosen variable remains visibly purple on Quest.
            ReassertVariablePaletteSelectionVisuals();
            UpdatePanelTypography();
            UpdateDesktopFocusPresentation();
        }

        private void OnDestroy()
        {
            if (waveClient != null)
            {
                waveClient.ImportProgress -= OnWaveImportProgress;
                waveClient.ImportCompleted -= OnWaveImportCompleted;
            }
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (input.questVoiceRecording)
                Microphone.End(questVoiceDevice);
            DestroyQuestNativeSpeechRecognizer();
#endif
            if (s4dClient != null)
                s4dClient.Cancel();
            if (slabTexture != null)
                Destroy(slabTexture);
            if (boundaryDayPreviewMaterial != null)
                Destroy(boundaryDayPreviewMaterial);
            if (boundaryDayPreviewDataMaterial != null)
                Destroy(boundaryDayPreviewDataMaterial);
            if (boundaryDayPreviewDataMesh != null)
                Destroy(boundaryDayPreviewDataMesh);
            if (boundaryDayPreviewLegendMaterial != null)
                Destroy(boundaryDayPreviewLegendMaterial);
            if (boundaryDayPreviewLegendTexture != null)
                Destroy(boundaryDayPreviewLegendTexture);
            DestroyTextures(matrixTextures);
            DestroyTextures(streamingCellTextures);
            if (sharedColorbarTexture != null)
                Destroy(sharedColorbarTexture);
            ClearSourcePreviewLayers();
            ClearAnalysisHistory();
            if (chartImage != null)
                Destroy(chartImage);
            if (uiAlwaysVisibleMaterial != null)
                Destroy(uiAlwaysVisibleMaterial);
            if (uiAlwaysVisibleFontMaterial != null)
                Destroy(uiAlwaysVisibleFontMaterial);
            if (variableDragBackingMaterial != null)
                Destroy(variableDragBackingMaterial);
            ClearPairedVariableVolumes();
            if (currentView != null)
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
        }

        private const int MaxFacetAxisBuckets = 9;

        private const int MaxFacetCells = MaxFacetAxisBuckets * MaxFacetAxisBuckets;

        private enum Stage { DatasetImport, Field, Slab, Matrix, Analyze, Result }

        private enum SpatialWorkflowStep
        {
            AxisBinding,
            SlabSkeleton,
            BoundaryAuthoring,
            Intent,
            SourcePreviewReady,
            Materializing,
            Result
        }

        private enum DimensionRole { Fixed, Faceted, Mapped }

        private enum BoundaryDimension { Time, Depth, Horizontal, Variable }

        private enum DraftOperation { None, Pivot, Drill, RollUp }

        private enum GroundMode { Aggregate, Playback }

        private enum AnalysisTaskMode { Distribution, Anomaly, Compare, Relationship }

        private enum DesktopFocusView
        {
            None,
            TimeSurface,
            TimeStc,
            SlabAxis,
            SlabSurface,
            SlabStc
        }

        private sealed class AnalysisNodeState
        {
            public string nodeId;
            public string parentNodeId;
            public DraftOperation bornFrom;
            public string jobId;
            public string snapshotId;
            public string datasetId;
            public string variableId;
            public string datasetDirectory;
            public string variableName;
            public string rawIntent;
            public string analysisQuestion;
            public string analyticTask;
            public string intentDisplayLabel;
            public bool hasResolvedIntent;
            public S4DDigestResult digest;
            public string digestError;
            public bool digestPending;
            public string title;
            public string subtitle;
            public Texture2D gridImage;
            public string chartResultJson;
            public int timeBoundaryStart;
            public int timeBoundaryEnd;
            public float depthBoundaryLow;
            public float depthBoundaryHigh;
            public int[] roleValues;
            public S4DIndexBucketRequest[] timeBuckets;
            public S4DIndexBucketRequest[] depthBuckets;
            public bool gridTransposed;
            public bool inspected;
            public bool boundarySuspect;
            public bool pinned;
            public bool dismissed;
            public bool stale;
            public bool[] staleCells;
            public bool[] verifiedCells;
            public bool[] suspectCells;
            public bool[] localizedCells;
            public bool[] pinnedCells;
            public float sharedMinimum;
            public float sharedMaximum;
            public string sharedUnit;
            public string[] cellSnapshotIds;
        }

        private sealed class TrailEventState
        {
            public int sequence;
            public string nodeId;
            public string kind;
            public string detail;
        }

        private sealed class RetainedResultView
        {
            public string nodeId;
            public Canvas canvas;
        }

        private readonly List<VolumeSTCubeSliceDataset> datasets = new List<VolumeSTCubeSliceDataset>();

        private readonly List<GameObject> timeMarkers = new List<GameObject>();

        private readonly List<AnalysisNodeState> analysisNodes = new List<AnalysisNodeState>();

        private readonly List<RetainedResultView> retainedResultViews =
            new List<RetainedResultView>();

        private readonly List<TrailEventState> trailEvents =
            new List<TrailEventState>();

        private int nextTrailEventSequence = 1;

        private readonly LineRenderer[] regionLines = new LineRenderer[40];

        private readonly GameObject[] timeBoundaryHandles = new GameObject[2];

        private readonly GameObject[] depthBoundaryPlanes = new GameObject[2];

        private readonly TextMesh[] timeBoundaryValueLabels = new TextMesh[2];

        private readonly TextMesh[] depthBoundaryValueLabels = new TextMesh[2];

        private Camera xrCamera;

        private VolumeSTCubeQuestRayInteractor rayInteractor;

        private Transform leftController;

        private Transform grabbedPanel;

        private float grabbedPanelDistance;

        private Material uiAlwaysVisibleMaterial;

        private Material uiAlwaysVisibleFontMaterial;

        private Material variableDragBackingMaterial;

        private bool groundDocked;

        private Vector3 panelPreGroundPosition;

        private Quaternion panelPreGroundRotation;

        private Vector3 panelPreGroundScale;

        private Font font;

        private Font worldFont;

        private TMPro.TMP_FontAsset crispFontAsset;

        private Canvas panelCanvas;

        private Canvas mainMenuCanvas;

        private Canvas boundaryCanvas;

        private Canvas trailCanvas;

        private Canvas facetGridCanvas;

        private Canvas aiFindingsCanvas;

        private Canvas slabPreviewCanvas;

        private Canvas intentCanvas;

        private Canvas draftCanvas;

        private Canvas workflowToolbarCanvas;

        private CanvasGroup panelCanvasGroup;

        private CanvasGroup facetGridCanvasGroup;

        private float nextDesktopTypographyRefresh;

        private Coroutine panelRefreshAnimation;

        private Coroutine facetGridRefreshAnimation;

        private RectTransform panelContent;

        private RectTransform mainMenuContent;

        private RectTransform boundaryContent;

        private RectTransform trailContent;

        private RectTransform facetGridContent;

        private RectTransform aiFindingsContent;

        private RectTransform slabPreviewContent;

        private RectTransform intentContent;

        private RectTransform draftContent;

        private Text statusText;

        private Image waveImportProgressFill;

        private TMPro.TextMeshProUGUI statusCrispText;

        private Text panelTitleText;

        private TMPro.TextMeshProUGUI panelTitleCrispText;

        private Text panelFlowText;

        private Text panelBrandText;

        private Text panelGripHintText;

        private Text fieldTimeSummaryText;

        private Text boundaryCurrentRangeText;

        private Text mainMenuDataLabel;

        private Text promptText;

        private Text intentPromptText;

        private Text slabLabel;

        private GameObject spatialRoot;

        private GameObject fieldDatasetSelectorRoot;

        private GameObject spatialAxisComposerRoot;

        private GameObject variablePaletteRoot;

        private GameObject variablePaletteExpandedRoot;

        private GameObject variablePaletteCollapseButton;

        private float variablePaletteHeight;

        private string variablePaletteDatasetSignature = string.Empty;

        private GameObject variableFixedRoleButton;

        private GameObject variableFacetedRoleButton;

        private GameObject variableSharedScopeButton;

        private GameObject variableCustomScopeButton;

        private readonly Dictionary<int, GameObject> variablePaletteTokens =
            new Dictionary<int, GameObject>();

        private readonly Dictionary<int, TextMesh> variablePaletteLabels =
            new Dictionary<int, TextMesh>();

        private GameObject draggedAxisToken;

        private int draggedAxisVariable = -1;

        private int draggedAxisDimension = -1;

        private float draggedAxisDistance;

        private Vector3 draggedAxisRestScale = Vector3.one;

        private Quaternion fieldAxisRemapRotation = Quaternion.identity;

        private Coroutine fieldAxisRemapCoroutine;

        private Quaternion boundaryAuthoringRestoreRotation = Quaternion.identity;

        private Coroutine boundaryAuthoringRotationCoroutine;

        private bool boundaryAuthoringCanonicalView;

        private float lastAxisTokenClickTime = -10.0f;

        private int lastAxisTokenClickVariable = -1;

        private int lastAxisTokenClickDimension = -1;

        private float lastVariableShellClickTime = -10.0f;

        private int lastVariableShellClickRig = -1;

        private enum PaletteComponentKind
        {
            None,
            Time,
            Depth,
            Variable,
            VariableAxis
        }

        private PaletteComponentKind draggedPaletteKind = PaletteComponentKind.None;

        private GameObject draggedPaletteToken;

        private GameObject draggedPaletteSourceToken;

        private Transform draggedPaletteOriginalParent;

        private Vector3 draggedPaletteOriginalLocalPosition;

        private Quaternion draggedPaletteOriginalLocalRotation;

        private Vector3 draggedPaletteOriginalLocalScale = Vector3.one;

        private int draggedPaletteVariable = -1;

        private float draggedPaletteDistance;

        private Vector3 draggedPaletteRestScale = Vector3.one;

        private Vector3 draggedPaletteStartRayPoint;

        private float draggedPaletteStartTime;

        private GameObject slabObject;

        private GameObject slabPreviewObject;

        private Material slabPreviewMaterial;

        private GameObject boundaryDayPreviewObject;

        private GameObject boundaryDayPreviewMapObject;

        private Material boundaryDayPreviewMaterial;

        private GameObject boundaryDayPreviewDataObject;

        private Material boundaryDayPreviewDataMaterial;

        private Mesh boundaryDayPreviewDataMesh;

        private GameObject boundaryDayPreviewLegendObject;

        private Material boundaryDayPreviewLegendMaterial;

        private Texture2D boundaryDayPreviewLegendTexture;

        private TextMesh boundaryDayPreviewLabel;

        private TextMesh boundaryDayPreviewStatsLabel;

        private TextMesh boundaryDayPreviewScaleLabel;

        private Coroutine boundaryDayPreviewAnimation;

        private Coroutine boundaryDayPreviewHideAnimation;

        private int boundaryDayPreviewTime = -1;

        private GameObject variableFacetStacksRoot;

        private readonly List<Texture2D> variableFacetStackTextures =
            new List<Texture2D>();

        // Multi-variable Fields use one real, current-time XYZ volume per
        // variable.  These are deliberately not 2D slice cards: each entry is
        // imported by the same RAW volume factory as the primary STC view.
        private readonly Dictionary<int, VolumeRenderedObject>
            pairedVariableVolumes = new Dictionary<int, VolumeRenderedObject>();

        private readonly Dictionary<int, int> pairedVariableVolumeTimes =
            new Dictionary<int, int>();

        private sealed class SpatialAxisRigState
        {
            public int boundVariable = -1;
            public int timeAxis = -1;
            public int depthAxis = -1;
            public int variableAxis = -1;
            public DimensionRole timeRole = DimensionRole.Faceted;
            public DimensionRole depthRole = DimensionRole.Faceted;
            public GameObject root;
            public GameObject timeToken;
            public GameObject depthToken;
            public GameObject variableToken;
            public readonly Renderer[] slotRenderers = new Renderer[3];
            public readonly List<Renderer> frameRenderers = new List<Renderer>();
            public readonly List<Color> frameColors = new List<Color>();
            public float frameVisibility;
            public bool frameRequestedVisible;
            public bool pendingDockAnimation;
            public bool hasCustomDock;
            public Vector3 customDockFieldPosition;
            public Quaternion customDockFieldRotation = Quaternion.identity;
            public bool usesSharedBoundaries = true;
            public int customTimeBoundaryStart = 9;
            public int customTimeBoundaryEnd = 19;
            public int customSelectedTime;
            public float customDepthBoundaryLow = 0.24f;
            public float customDepthBoundaryHigh = 0.68f;
            public int customSelectedZ;
        }

        private readonly List<SpatialAxisRigState> spatialAxisRigStates =
            new List<SpatialAxisRigState>();

        private GameObject regionRoot;

        private Transform timeRail;

        private LineRenderer groundLink;

        private readonly LineRenderer[] matPlotStcLinkSegments =
            new LineRenderer[18];

        private LineRenderer groundTimeRangeLine;

        private TextMesh groundTimeRangeLabel;

        private GameObject groundDepthBand;

        private readonly GameObject[] groundDepthRangePlanes = new GameObject[2];

        private TextMesh groundDepthRangeLabel;

        private Transform selectedFacetCellAnchor;

        private Coroutine groundPlaybackCoroutine;

        private TextMesh timeAxisLabel;

        private TextMesh variableAxisLabel;

        private TextMesh depthAxisLabel;

        private readonly LineRenderer[] timeBucketAxisSegments = new LineRenderer[3];

        private readonly LineRenderer[] depthBucketAxisSegments = new LineRenderer[3];

        private readonly TextMesh[] timeBucketAxisLabels = new TextMesh[3];

        private readonly TextMesh[] depthBucketAxisLabels = new TextMesh[3];

        private GameObject depthInspectionUpperStack;

        private GameObject depthInspectionLowerStack;

        private readonly List<Renderer> depthInspectionStackRenderers =
            new List<Renderer>();

        private TextMesh depthInspectionLabel;

        private Coroutine depthInspectionCoroutine;

        private int depthInspectionZ = -1;

        private int depthInspectionOriginalZ;

        private float depthInspectionOriginalNormalized;

        private VolumeSTCubeSliceDataset selectedDataset;

        private VolumeSTCubeView currentView;

        private VolumeSTCubeForVrSurfacePlayer forVrSurfacePlayer;

        private int pendingDatasetDisplayTime = -1;

        private bool resumePlaybackAfterDatasetLoad;

        // Authored pose of the Field pair, captured when it is built. Step 2's
        // composition is derived from this instead of from whatever a previous
        // stage left on the live transform.
        private Vector3 desktopFieldAuthoredPosition;

        private Vector3 desktopFieldAuthoredScale = Vector3.one;

        private Vector3 desktopBoundaryFieldPosition;

        private Vector3 desktopOverviewFieldPosition;

        private Vector3 desktopFieldScale = Vector3.one;

        private DesktopFocusView desktopFocusView;

        private Coroutine desktopFocusAnimation;

        private Transform desktopAxisParent;

        private Vector3 desktopAxisOriginalLocalPosition;

        private Quaternion desktopAxisOriginalLocalRotation;

        private Vector3 desktopAxisOriginalLocalScale = Vector3.one;

        private Vector3 desktopAxisBaseScale = Vector3.one;

        // Geometry of the last laid-out facet grid, so overlays that draw on top
        // of the cells (draft outlines, selection washes) follow the real card
        // size instead of the equal-slot size.
        private Vector2 facetGridCellSize;

        private Vector2 facetGridCellOrigin;

        // Desktop owns the axis body directly, so its size has to be authored
        // rather than inherited from the Field it used to be nested inside.

        // The desktop framing has more empty space than the headset view, so
        // the two Fields are presented a little larger than the Quest sizes.



        private VolumeRenderedObject groundAggregateVolume;

        private VolumeDataset groundAggregateDataset;

        private Texture2D slabTexture;

        private Texture2D[] matrixTextures = new Texture2D[0];

        private readonly Texture2D[] streamingCellTextures = new Texture2D[MaxFacetCells];

        private Texture2D matrixPreviewAtlas;

        private readonly List<Texture2D> sourcePreviewLayerAtlases =
            new List<Texture2D>();

        private readonly List<int> sourcePreviewVariableIndices =
            new List<int>();

        private readonly List<Canvas> sourcePreviewLayerCanvases =
            new List<Canvas>();

        private int sourcePreviewRequestCursor;

        private int sourcePreviewRenderVariableIndex = -1;

        private readonly List<int> materializationVariableIndices =
            new List<int>();

        private readonly List<Texture2D> materializedLayerAtlases =
            new List<Texture2D>();

        private readonly List<S4DFacetGridResult> materializedLayerResults =
            new List<S4DFacetGridResult>();

        private readonly Dictionary<string, S4DDigestResult> layerDigestCache =
            new Dictionary<string, S4DDigestResult>();

        private readonly List<Canvas> materializedLayerCanvases =
            new List<Canvas>();

        private int materializationVariableCursor = -1;

        private readonly float[] matrixMinimums = new float[MaxFacetCells];

        private readonly float[] matrixMaximums = new float[MaxFacetCells];

        private readonly float[] matrixMeans = new float[MaxFacetCells];

        private readonly bool[] matrixHasData = new bool[MaxFacetCells];

        private readonly float[] matrixValidFractions = new float[MaxFacetCells];

        private Texture2D s4dGridImage;

        private Texture2D sharedColorbarTexture;

        private Texture2D chartImage;

        private string s4dGridFailure = string.Empty;

        private string s4dChartResultJson;

        private string s4dSnapshotId;

        private string s4dJobId;

        private S4DDigestResult currentDigest;

        private string digestError = string.Empty;

        private string groundAggregateSnapshotId;

        private float s4dSharedMinimum;

        private float s4dSharedMaximum = 1.0f;

        private string s4dSharedUnit = string.Empty;

        private VolumeSTCubeS4DAnalysisClient s4dClient;

        private bool resumeMaterializationAfterManifest;

        private string datasetManifestError = string.Empty;

        private float groundSnapshotCellMean = float.NaN;

        private float groundReconstructedCellMean = float.NaN;

        private float groundValidFraction;

        private int[] matrixTimes = new int[MaxFacetAxisBuckets];

        private int[] matrixDepths = new int[MaxFacetAxisBuckets];

        private S4DIndexBucketRequest[] activeTimeBuckets;

        private S4DIndexBucketRequest[] activeDepthBuckets;

        private S4DIndexBucketRequest[] authoredTimeBuckets;

        private S4DIndexBucketRequest[] authoredDepthBuckets;

        // The authored ladder always retains all three semantic ranges.  These
        // masks describe which ranges the user wants to materialize in the
        // current Matrix. Keeping selection separate from authorship means a
        // 2 x 3 request never destroys the saved Before/During/After or
        // Surface/Middle/Deep definitions.
        private readonly bool[] selectedTimeBucketMask = { true, true, true };

        private readonly bool[] selectedDepthBucketMask = { true, true, true };

        private readonly List<Transform> axisBucketFacingGroups =
            new List<Transform>();

        private int activeGridColumns = 3;

        private int activeGridRows = 3;

        private bool activeGridTransposed;

        private bool facetGridLayered;

        private int facetGridPeeledLayers;

        private bool facetGridPreviousTransposed;

        private int facetGridPreviousColumns = 3;

        private int facetGridPreviousRows = 3;

        private bool pivotTransposed;

        private int selectedTime;

        private int selectedZ;

        private float slabNormalized = 0.5f;

        private Stage stage = Stage.DatasetImport;

        private SpatialWorkflowStep spatialWorkflowStep =
            SpatialWorkflowStep.AxisBinding;

        private bool datasetImportConfirmed;

        private int importSelectedVariableIndex = -1;

        private bool preconfigurationActive;

        private bool mainWorkspaceEntered;

        private bool questImportHeadLocked;

        private bool initialized;

        private float slabDragOffset;

#if UNITY_EDITOR || SLABLAB_FLAT
        private float desktopDragStartMouseY;
        private float desktopDragStartSlab;
        private float desktopDepthBoundaryStartMouseY;
        private float desktopDepthBoundaryStartValue;
#endif


        private static Vector2 FlatPointerPosition
        {
            get
            {
                return Input.touchCount > 0
                    ? Input.GetTouch(0).position
                    : (Vector2)Input.mousePosition;
            }
        }

        private static bool FlatPointerHeld
        {
            get
            {
                if (Input.touchCount == 0)
                    return Input.GetMouseButton(0);
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase != TouchPhase.Ended && phase != TouchPhase.Canceled;
            }
        }

        private static bool FlatPointerReleased
        {
            get
            {
                if (Input.touchCount == 0)
                    return Input.GetMouseButtonUp(0);
                TouchPhase phase = Input.GetTouch(0).phase;
                return phase == TouchPhase.Ended || phase == TouchPhase.Canceled;
            }
        }

        private int activeTimeBoundary;

        private int timeBoundaryStart = 9;

        private int timeBoundaryEnd = 19;

        private int activeDepthBoundary;

        private float depthBoundaryLow = 0.24f;

        private float depthBoundaryHigh = 0.68f;

        private bool sharedBoundariesInitialized;

        private int sharedTimeBoundaryStart = 9;

        private int sharedTimeBoundaryEnd = 19;

        private int sharedSelectedTime;

        private float sharedDepthBoundaryLow = 0.24f;

        private float sharedDepthBoundaryHigh = 0.68f;

        private int sharedSelectedZ;

        private bool boundaryEditActive;

        private Stage boundaryReturnStage = Stage.Slab;

        private bool authorBoundaryConfirmed;

        private bool initialBoundarySetupActive;

        private bool initialTimeBoundaryComplete;

        private bool initialDepthBoundaryComplete;

        private int boundaryVariableQueueIndex;

        private int savedTimeBoundaryStart;

        private int savedTimeBoundaryEnd;

        private int savedSelectedTime;

        private float savedDepthBoundaryLow;

        private float savedDepthBoundaryHigh;

        private float volumeLocalMinY = -FieldHalfHeight * 0.82f;

        private float volumeLocalMaxY = FieldHalfHeight * 0.82f;

        private Vector2 regionStart;

        private Rect region = new Rect(0.28f, 0.28f, 0.44f, 0.44f);

        private int pendingDatasetLoadIndex = -1;

        private bool gridStale;

        private bool smallMultiples;

        private bool slabPreviewBuilt;

        private bool intentConfigured;

        private string intentMode = "CHARACTERIZE DISTRIBUTION";

        private string intentFocus = string.Empty;

        private string intentTask = "characterize_distribution";

        private string intentResolutionError = string.Empty;

        private float intentConfidence;

        private bool intentUsedFallback;

        private bool placementConfirmed;

        private bool inspected;

        private bool boundarySuspect;

        private bool evidenceLocalized;

        private string analysisQuestion =
            "Where and when does the selected variable show the strongest change?";

        private AnalysisTaskMode analysisTaskMode = AnalysisTaskMode.Anomaly;

        private BoundaryDimension boundaryDimension = BoundaryDimension.Time;

        private DraftOperation draftOperation = DraftOperation.None;

        private AnalysisNodeState currentAnalysisNode;

        private string draftSourceNodeId;

        private string pendingDeleteNodeId;

        private int nextAnalysisNodeNumber = 1;

        private GroundMode groundMode = GroundMode.Aggregate;

        private int selectedGridColumn = 1;

        private int selectedGridRow;

        private bool gridCellSelected;

        private bool selectedCellPinned;

        private readonly bool[] facetCellPinned = new bool[MaxFacetCells];

        private readonly bool[] facetCellInspected = new bool[MaxFacetCells];

        private readonly bool[] facetCellBoundarySuspect = new bool[MaxFacetCells];

        private readonly bool[] facetCellLocalized = new bool[MaxFacetCells];

        private readonly bool[] facetCellStale = new bool[MaxFacetCells];

        private readonly bool[] rematerializedCellMask = new bool[MaxFacetCells];

        private readonly string[] facetCellSnapshotIds = new string[MaxFacetCells];

        private bool rematerializingStaleCells;

        private readonly DimensionRole[] roles =
        {
            DimensionRole.Faceted,
            DimensionRole.Faceted,
            DimensionRole.Mapped,
            DimensionRole.Fixed
        };

        private readonly bool[] selectedTimeTicks = new bool[MaxFacetAxisBuckets];

        private readonly bool[] selectedDepthTicks = new bool[MaxFacetAxisBuckets];

        private readonly int[] timeRollupGroups = new int[MaxFacetAxisBuckets];

        private readonly int[] depthRollupGroups = new int[MaxFacetAxisBuckets];

        private int draftTargetDimension;

        private int activeRollupGroup = 1;

        // Pivot is a copied Slab configuration, not merely a visual matrix
        // rotation.  Modes 0/1 keep Time x Depth and swap its orientation;
        // modes 2/3 replace one comparison dimension with Variable and lock
        // the remaining spatial dimension to the currently selected bucket.
        private int pivotComparisonMode;

        private int pivotFixedTime;

        private int pivotFixedDepth;

        private readonly DimensionRole[] pivotSourceRoles =
            new DimensionRole[4];

        private RectTransform draftPivotPreviewRoot;

        private float draftPivotPreviewStartPointerAngle;

        private float draftPivotPreviewVisualAngle;

        private int digestColumnPage;

        private int digestRowPage;

        private float progress;

        private float displayedGridProgress;

        private float targetGridProgress;

        private Coroutine gridProgressAnimation;

        private Text facetGridProgressText;

        private Text facetGridProgressStageText;

        private Text facetGridValidatedText;

        private Image facetGridProgressFill;

        private Text desktopMatrixProgressText;

        private Text desktopMatrixProgressStageText;

        private Image desktopMatrixProgressFill;

        private readonly RawImage[] facetGridCellImages = new RawImage[MaxFacetCells];

        private readonly Text[] facetGridCellStateLabels = new Text[MaxFacetCells];

        private readonly GameObject[] facetGridCellPlaceholders = new GameObject[MaxFacetCells];

        private TouchScreenKeyboard keyboard;

        /// <summary>Buffer for the typed Time-range entry (see DesktopRangeEntry).</summary>
        private string boundaryRangeEntryText = string.Empty;

        // How long the opening screen needed before it had data to show. Read by
        // the interaction guard, which puts a budget on it.
        private float startupStartedAt;

        private float startupReadySeconds = -1.0f;

        private Coroutine questVoicePermissionCoroutine;

        private string vrKeyboardOriginalPrompt = string.Empty;

        private AudioClip questVoiceClip;

        private string questVoiceDevice = string.Empty;

        private Coroutine questVoiceAutoStopCoroutine;

        private MaterialPropertyBlock variableSelectionBlock;

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private AndroidJavaObject questSpeechRecognizer;
        private QuestSpeechRecognitionListener questSpeechListener;
#endif

        private const string PreviousDefaultSpatialPrompt =
            "Compare the selected region with the rest of the XY slab and explain the strongest spatial difference.";

        private const string DefaultSpatialPrompt =
            "Generate a bar chart for each Time x Depth cell.";

        private string prompt = DefaultSpatialPrompt;

        private string matPlotUrl = "http://127.0.0.1:8010";

        private string s4dUrl = "http://127.0.0.1:8020";

        private VolumeSTCubeWaveClient waveClient;

        private bool waveImportAttempted;

        private bool pendingFieldPresentationReveal;

        private float pendingFieldRevealDeadline;

        private float waveRetryDeadline;

        private int waveRetryCount;

        private const int MaxWaveAutoRetries = 3;

        private string waveImportError = string.Empty;

        private string dataRoot;

        // A restrained scale keeps dense analytical panels readable in Quest without
        // making labels collide with their cards and controls.
        // Stable type scale across every world-space surface. Button labels use
        // a padded deterministic best-fit below, so readability no longer
        // depends on oversized text escaping its control.
        private const float UiFontScale = 1.12f;

        private static float ActiveUiFontScale
        {
            get
            {
#if UNITY_EDITOR || SLABLAB_FLAT
                // The editor Game view is viewed from farther away than a Quest
                // world-space panel and is often previewed above 1x. Give the
                // desktop build a deliberately larger type scale without
                // changing the headset layout.
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                    return UiFontScale * 1.85f;
#endif
                return UiFontScale;
            }
        }

        private const float FieldOpacity = 0.82f;

        private const float GroundContextOpacity = 0.16f;

        private const float FieldHalfWidth = 0.90f;

        private const float FieldHalfHeight = 0.84f;

        private const float FieldHalfDepth = 0.66f;

        // Symmetric spatial workbench geometry. The shared axis composer is the
        // visual hub: the primary and third Fields mirror each other across it,
        // while the second Field occupies the same-radius upper dock.
        private const float SpatialAxisDockX = FieldHalfWidth + 0.50f;

        private const float SpatialFieldOrbitY = FieldHalfHeight * 2.0f + 0.34f;

        private const float TimeRailHalfWidth = 0.82f;

        private const float FieldVerticalExaggeration = 2.15f;

        private static readonly Vector3 PrimaryToolDockPosition =
            new Vector3(0.44f, 1.48f, 1.05f);

        private static readonly Vector3 BoundaryToolDockPosition =
            new Vector3(0.20f, 1.98f, 1.02f);

        private static readonly Vector3 SlabPreviewDockPosition =
            new Vector3(-0.48f, 2.08f, 1.12f);

        private static readonly Vector3 IntentToolDockPosition =
            new Vector3(0.64f, 1.94f, 1.10f);

        private static readonly Vector3 DraftToolDockPosition =
            new Vector3(0.72f, 1.16f, 1.10f);

        private static Sprite roundedUiSprite;

        public bool IsBoundaryEditing
        {
            get { return boundaryEditActive; }
        }

        public bool DesktopTaskPanelIsCentral
        {
            get { return stage == Stage.DatasetImport || stage == Stage.Matrix; }
        }

        public bool DesktopMatrixTaskActive
        {
            get { return stage == Stage.Matrix; }
        }

        public bool DesktopMatrixProgressActive
        {
            get
            {
                // A Drill/Pivot/Roll-up keeps the previous matrix alive as its
                // immutable source while the replacement is generated. Do not
                // use s4dGridImage == null as the progress condition: that hid
                // the progress surface for every transformation job.
                return stage == Stage.Matrix &&
                    (IsPending(PendingJob.Analysis) || resumeMaterializationAfterManifest ||
                     IsPending(PendingJob.DatasetManifest));
            }
        }

        public bool DesktopMatrixPanelPrimary
        {
            get
            {
                return stage == Stage.Matrix &&
                    desktopFocusView == DesktopFocusView.SlabAxis;
            }
        }

        public bool DesktopCompactBarActive
        {
            get
            {
                return stage == Stage.Field || stage == Stage.Slab ||
                    stage == Stage.Matrix;
            }
        }

        public bool DesktopBoundaryBarActive
        {
            get { return stage == Stage.Field && boundaryEditActive; }
        }

        public bool DesktopAxisBarActive
        {
            get { return false; }
        }

        public bool DesktopWorkflowBarActive
        {
            get
            {
                return stage == Stage.Slab || stage == Stage.Matrix;
            }
        }

        public bool DesktopCanGenerateSlab
        {
            get
            {
                return stage == Stage.Slab && !slabPreviewBuilt &&
                    AreSpatialAxisBindingsComplete(out _);
            }
        }

        public bool DesktopCanOpenIntent
        {
            get
            {
                return stage == Stage.Slab && !IsPending(PendingJob.Analysis) &&
                    !resumeMaterializationAfterManifest &&
                    !IsPending(PendingJob.DatasetManifest) &&
                    AreSpatialAxisBindingsComplete(out _) &&
                    authorBoundaryConfirmed;
            }
        }

        public bool DesktopCanBuildMatrix
        {
            get
            {
                if (stage == Stage.Matrix && s4dGridImage != null &&
                    !IsPending(PendingJob.Analysis))
                    return true;
                return stage == Stage.Slab && slabPreviewBuilt &&
                    intentConfigured &&
                    AreSpatialAxisBindingsComplete(out _) &&
                    authorBoundaryConfirmed && !IsPending(PendingJob.Analysis) &&
                    !resumeMaterializationAfterManifest &&
                    !IsPending(PendingJob.DatasetManifest);
            }
        }

        public string DesktopFullMatrixLabel
        {
            get
            {
                return resumeMaterializationAfterManifest ||
                    IsPending(PendingJob.DatasetManifest)
                        ? "PREPARING..."
                        : IsPending(PendingJob.Analysis) ? "Generating..."
                        : stage == Stage.Matrix ? "View charts" : "Generate charts";
            }
        }

        public bool DesktopCanTransformMatrix
        {
            get
            {
                return stage == Stage.Matrix && s4dGridImage != null &&
                    !IsPending(PendingJob.Analysis) && !resumeMaterializationAfterManifest &&
                    !IsPending(PendingJob.DatasetManifest);
            }
        }

        public bool DesktopAxisBindingsComplete
        {
            get { return AreSpatialAxisBindingsComplete(out _); }
        }

        public bool DesktopSlabPreviewActive
        {
            get
            {
                return stage == Stage.Slab && slabPreviewBuilt &&
                    slabPreviewCanvas != null &&
                    slabPreviewCanvas.gameObject.activeSelf;
            }
        }

        public bool DesktopComposerPanelActive
        {
            get
            {
                return stage == Stage.Slab &&
                    ((intentCanvas != null &&
                      intentCanvas.gameObject.activeSelf) ||
                     (slabPreviewCanvas != null &&
                      slabPreviewCanvas.gameObject.activeSelf));
            }
        }

        public bool DesktopIntentPanelPrimary
        {
            get
            {
                return DesktopComposerPanelActive &&
                    intentCanvas != null &&
                    intentCanvas.gameObject.activeSelf &&
                    desktopFocusView == DesktopFocusView.SlabAxis;
            }
        }

        public string DesktopSlabActionLabel
        {
            get { return slabPreviewBuilt ? "MATPLOT INTENT" : "GENERATE SLAB"; }
        }

        public string DesktopAxisBindingLabel
        {
            get
            {
                if (slabPreviewBuilt)
                    return "SLAB READY  ·  CHOOSE THE ANALYSIS TASK";
                if (AreSpatialAxisBindingsComplete(out string missing))
                    return "VARIABLE  ·  TIME  ·  DEPTH READY";
                return ("DRAG TO AXES: " + missing).ToUpperInvariant();
            }
        }

        public string DesktopBoundaryRangeLabel
        {
            get { return TimeRangeSummary().ToUpperInvariant(); }
        }

        public string DesktopPlaybackLabel
        {
            get
            {
                return forVrSurfacePlayer != null
                    ? forVrSurfacePlayer.PlaybackButtonLabel : "PLAY";
            }
        }

        public string DesktopPlaybackSpeedLabel
        {
            get
            {
                return forVrSurfacePlayer != null
                    ? forVrSurfacePlayer.PlaybackSpeedLabel : "SPEED 1x";
            }
        }

        public int DesktopAnalysisCount { get { return analysisNodes.Count; } }

        /// <summary>
        /// Seconds from Start() until a dataset was registered, or -1 while the
        /// opening screen still has nothing to show. The interaction guard keeps
        /// a budget on this so "the first field takes a while to appear" cannot
        /// silently come back.
        /// </summary>
        public float DesktopStartupSeconds
        {
            get { return startupReadySeconds; }
        }

        private bool historyRestoring;

        public string DesktopWorkflowTitle
        {
            get
            {
                switch (stage)
                {
                    case Stage.DatasetImport: return "STEP 1  ·  LOAD LIVE WAVE DATA";
                    case Stage.Field: return "STEP 2  ·  CONFIGURE FIELD";
                    case Stage.Slab: return "STEP 3  ·  DEFINE THE SLAB";
                    case Stage.Matrix: return "STEP 4  ·  REVIEW THE MATRIX";
                    case Stage.Analyze: return "STEP 5  ·  ANALYZE";
                    case Stage.Result: return "STEP 6  ·  REVIEW FINDINGS";
                    default: return "STC SLABLAB";
                }
            }
        }

        public void Initialize(Camera camera, VolumeSTCubeQuestRayInteractor interactor,
            Transform leftControllerTransform = null)
        {
            xrCamera = camera;
            rayInteractor = interactor;
            leftController = leftControllerTransform;
            initialized = true;
        }

        private void Start()
        {
            if (!initialized)
                return;
            startupStartedAt = Time.realtimeSinceStartup;
            variableSelectionBlock = new MaterialPropertyBlock();
            // Reuse the Poppins face bundled by the reference D-drive project.
            // It remains embedded for both Windows and Quest builds.
            worldFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font = Resources.Load<Font>("Fonts/Poppins-Bold");
            if (font == null)
                font = worldFont;
            if (font != null)
                crispFontAsset = TMPro.TMP_FontAsset.CreateFontAsset(font);
            prompt = SlabLabSettings.GetSpatialPrompt(prompt);
            // Install the requested bar-chart test prompt exactly once on an
            // existing project/device. Later user-authored edits still persist.
            if (SlabLabSettings.BarPromptMigrationVersion == 0 ||
                string.IsNullOrWhiteSpace(prompt) ||
                prompt == PreviousDefaultSpatialPrompt)
            {
                prompt = DefaultSpatialPrompt;
                SlabLabSettings.SaveMigratedBarPrompt(prompt);
            }
            ResetSelectedTicksToActiveBuckets();
            matPlotUrl = SlabLabSettings.GetMatPlotUrl(matPlotUrl);
            s4dUrl = SlabLabSettings.GetWaveUrl(s4dUrl);
            waveClient = gameObject.GetComponent<VolumeSTCubeWaveClient>();
            if (waveClient == null)
                waveClient = gameObject.AddComponent<VolumeSTCubeWaveClient>();
            waveClient.serviceBaseUrl = s4dUrl;
            waveClient.ImportProgress += OnWaveImportProgress;
            waveClient.ImportCompleted += OnWaveImportCompleted;
            HideInitialSceneVolumes();
            CreateSpatialCube();
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            CreatePanel();
            CreateMainMenu();
            CreateBoundaryPanel();
            CreateTrailPanel();
            CreateFacetGridPanel();
            CreateAiFindingsPanel();
            CreateSlabPreviewPanel();
            CreateIntentPanel();
            CreateDraftPanel();
            CreateWorkflowToolbar();
            ApplyAlwaysVisiblePanelMaterials();
            // Wave is the app's only opening data source. Do not scan the old
            // bundled/local RAW folders before the live dataset is registered.
            datasets.Clear();
            selectedDataset = null;
            importSelectedVariableIndex = -1;
            stage = Stage.DatasetImport;
            BuildStage();
            ImportWaveServerDataset();
            // Dataset Import is the first workflow step on Quest as well as on
            // desktop. Mode-specific placement is selected at runtime so an
            // Editor VR preview cannot accidentally execute the desktop branch.
            panelCanvas.gameObject.SetActive(true);
            mainMenuCanvas.gameObject.SetActive(false);
            slabPreviewCanvas.gameObject.SetActive(false);
            intentCanvas.gameObject.SetActive(false);
            aiFindingsCanvas.gameObject.SetActive(false);
            workflowToolbarCanvas.gameObject.SetActive(false);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                questImportHeadLocked = false;
            else
            {
                questImportHeadLocked = true;
                if (spatialRoot != null)
                    spatialRoot.SetActive(false);
                StartCoroutine(PlaceInitialQuestWorkspace());
            }
        }

        private static readonly Vector3 AxisRigOrigin =
            new Vector3(-0.32f, 0.32f, 0.25f);

        private const float AxisRigLength = 0.70f;

        // Each unbound component owns a stable lower dock. VARIABLE must not
        // inherit TIME's position or silently follow it during an axis swap.
        private static readonly Vector3 UnboundVariableTokenPosition =
            new Vector3(-0.27f, -0.57f, 0.275f);

        private static readonly Vector3 UnboundTimeTokenPosition =
            new Vector3(0.0f, -0.57f, 0.275f);

        private static readonly Vector3 UnboundDepthTokenPosition =
            new Vector3(0.27f, -0.57f, 0.275f);

        private bool IsForVrSurfaceDataset =>
            VolumeSTCubeForVrSurfacePlayer.Supports(selectedDataset);

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            if (statusCrispText != null)
                statusCrispText.text = message;
            Debug.Log("VolumeSTCube Slab Lab: " + message);
        }
    }
}
