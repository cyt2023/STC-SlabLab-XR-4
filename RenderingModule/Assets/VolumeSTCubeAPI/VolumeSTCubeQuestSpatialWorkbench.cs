using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Room-scale Quest workbench inspired by the Slab Lab storyboard: a persistent
    /// continuous cube, a directly draggable depth slab, a materialized time x depth
    /// matrix, a drawable XY region and a MatPlotAgent comparison surface.
    /// </summary>
    public sealed class VolumeSTCubeQuestSpatialWorkbench : MonoBehaviour
    {
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

        private sealed class AnalysisNodeState
        {
            public string nodeId;
            public string parentNodeId;
            public DraftOperation bornFrom;
            public string jobId;
            public string snapshotId;
            public string datasetId;
            public string variableId;
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
        private bool variablePaletteCollapsed;
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
        private bool draggedAxisSawTriggerHeld;
        private Vector3 draggedAxisRestScale = Vector3.one;
        private bool draggedAxisUsesDesktopPointer;
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
        private bool draggedPaletteSawTriggerHeld;
        private bool draggedPaletteUsesDesktopPointer;
        private float draggedPaletteStartTime;
        private bool legacyPanelVisible;
        private bool workflowToolbarPinned;
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
        private bool depthInspectionActive;
        private int depthInspectionZ = -1;
        private int depthInspectionOriginalZ;
        private float depthInspectionOriginalNormalized;
        private VolumeSTCubeSliceDataset selectedDataset;
        private VolumeSTCubeView currentView;
        private VolumeSTCubeForVrSurfacePlayer forVrSurfacePlayer;
        private int pendingDatasetDisplayTime = -1;
        private bool resumePlaybackAfterDatasetLoad;
        private bool desktopVisualizationAligned;
        private Vector3 desktopBoundaryFieldPosition;
        private Vector3 desktopOverviewFieldPosition;
        private Vector3 desktopFieldScale = Vector3.one;
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
        private bool sourcePreviewRunning;
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
        private bool digestRunning;
        private string groundAggregateSnapshotId;
        private float s4dSharedMinimum;
        private float s4dSharedMaximum = 1.0f;
        private string s4dSharedUnit = string.Empty;
        private VolumeSTCubeS4DAnalysisClient s4dClient;
        private bool datasetManifestResolving;
        private string datasetManifestError = string.Empty;
        private bool groundAggregateLoading;
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
#if !UNITY_EDITOR && !SLABLAB_FLAT
        private bool questImportHeadLocked;
#endif
        private bool initialized;
        private bool draggingSlab;
        private float slabDragOffset;
#if UNITY_EDITOR || SLABLAB_FLAT
        private float desktopDragStartMouseY;
        private float desktopDragStartSlab;
        private float desktopDepthBoundaryStartMouseY;
        private float desktopDepthBoundaryStartValue;
        private bool desktopEditingPrompt;
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
        private bool drawingRegion;
        private bool regionDragging;
        private bool timeBoundaryDragging;
        private int activeTimeBoundary;
        private int timeBoundaryStart = 9;
        private int timeBoundaryEnd = 19;
        private bool depthBoundaryDragging;
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
        private readonly List<int> boundaryVariableQueue = new List<int>();
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
        private bool jobRunning;
        private bool variableLoadRunning;
        private int pendingDatasetLoadIndex = -1;
        private bool gridStale;
        private bool cubeVisible = true;
        private bool smallMultiples;
        private bool slabPreviewBuilt;
        private bool intentConfigured;
        private bool intentResolving;
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
        private bool draftPivotPreviewDragging;
        private float draftPivotPreviewStartPointerAngle;
        private float draftPivotPreviewVisualAngle;
        private int digestColumnPage;
        private int digestRowPage;
        private float progress;
        private float displayedGridProgress;
        private float targetGridProgress;
        private int lastStageProgressBucket = -1;
        private Coroutine gridProgressAnimation;
        private Text facetGridProgressText;
        private Text facetGridProgressStageText;
        private Text facetGridValidatedText;
        private Image facetGridProgressFill;
        private readonly RawImage[] facetGridCellImages = new RawImage[MaxFacetCells];
        private readonly Text[] facetGridCellStateLabels = new Text[MaxFacetCells];
        private readonly GameObject[] facetGridCellPlaceholders = new GameObject[MaxFacetCells];
        private TouchScreenKeyboard keyboard;
        private Coroutine questVoicePermissionCoroutine;
        private bool voiceInputActive;
        private bool textInputActive;
        private bool keyboardInputWasVoice;
        private bool voiceReviewPending;
        private bool vrKeyboardVisible;
        private string vrKeyboardOriginalPrompt = string.Empty;
        private AudioClip questVoiceClip;
        private string questVoiceDevice = string.Empty;
        private bool questVoiceRecording;
        private bool questVoiceUploading;
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

        private static readonly Color Ink = new Color(0.98f, 0.995f, 1.0f, 1.0f);
        private static readonly Color Panel = new Color(0.010f, 0.022f, 0.036f, 0.975f);
        private static readonly Color Card = new Color(0.036f, 0.063f, 0.090f, 0.985f);
        private static readonly Color Cyan = new Color(0.05f, 0.92f, 1.0f, 1.0f);
        private static readonly Color Amber = new Color(1.0f, 0.62f, 0.13f, 1.0f);
        private static readonly Color Green = new Color(0.2f, 1.0f, 0.55f, 1.0f);
        private static readonly Color Purple = new Color(0.66f, 0.23f, 0.88f, 1.0f);
        private static readonly Color Muted = new Color(0.53f, 0.65f, 0.73f, 1.0f);
        private static readonly Color Danger = new Color(1.0f, 0.28f, 0.28f, 1.0f);
        private static readonly Color TimeColor = new Color(1.0f, 0.70f, 0.0f, 1.0f);
        private static readonly Color DepthColor = new Color(0.0f, 0.72f, 0.83f, 1.0f);
        private static readonly Color DepthAxisColor = new Color(0.34f, 0.56f, 1.0f, 1.0f);
        private static readonly Color HorizontalColor = new Color(0.26f, 0.63f, 0.28f, 1.0f);
        private static readonly Color VariableColor = new Color(0.56f, 0.14f, 0.67f, 1.0f);
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
            get { return stage == Stage.DatasetImport; }
        }

        public bool DesktopCompactBarActive
        {
            get { return stage == Stage.Field; }
        }

        public bool DesktopBoundaryBarActive
        {
            get { return boundaryEditActive; }
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

        public void DesktopOpenFieldSetup()
        {
            if (authorBoundaryConfirmed)
                EnterMainWorkspace();
            else
                OpenInitialAuthorBoundary();
        }

        public void DesktopTogglePlayback()
        {
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.TogglePlayback();
        }

        public void DesktopCyclePlaybackSpeed()
        {
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.CyclePlaybackSpeed();
        }

        public void DesktopCancelBoundary()
        {
            CancelBoundaryEdit();
        }

        public void DesktopConfirmBoundary()
        {
            ApplyBoundaryChange();
        }

        public string DesktopWorkflowTitle
        {
            get
            {
                switch (stage)
                {
                    case Stage.DatasetImport: return "STEP 1  ·  OPEN A DATASET";
                    case Stage.Field: return "STEP 2  ·  CONFIGURE FIELD";
                    case Stage.Slab: return "STEP 3  ·  DEFINE THE SLAB";
                    case Stage.Matrix: return "STEP 4  ·  REVIEW THE MATRIX";
                    case Stage.Analyze: return "STEP 5  ·  ANALYZE";
                    case Stage.Result: return "STEP 6  ·  REVIEW FINDINGS";
                    default: return "STC SLABLAB";
                }
            }
        }

        public void DesktopPreviousStep()
        {
            switch (stage)
            {
                case Stage.Field: OpenDatasetImportStage(); break;
                case Stage.Slab: Navigate(Stage.Field); break;
                case Stage.Matrix: Navigate(Stage.Slab); break;
                case Stage.Analyze: Navigate(Stage.Matrix); break;
                case Stage.Result: Navigate(Stage.Analyze); break;
                default: SetStatus("This is the first step."); break;
            }
        }

        public void DesktopNextStep()
        {
            switch (stage)
            {
                case Stage.DatasetImport: ConfirmDatasetImport(); break;
                case Stage.Field: Navigate(Stage.Slab); break;
                case Stage.Slab: Navigate(Stage.Matrix); break;
                case Stage.Matrix: Navigate(Stage.Analyze); break;
                case Stage.Analyze: Navigate(Stage.Result); break;
                default: SetStatus("This is the final step."); break;
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
            variableSelectionBlock = new MaterialPropertyBlock();
            // Reuse the Poppins face bundled by the reference D-drive project.
            // It remains embedded for both Windows and Quest builds.
            worldFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            font = Resources.Load<Font>("Fonts/Poppins-Bold");
            if (font == null)
                font = worldFont;
            if (font != null)
                crispFontAsset = TMPro.TMP_FontAsset.CreateFontAsset(font);
            prompt = PlayerPrefs.GetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            // Install the requested bar-chart test prompt exactly once on an
            // existing project/device. Later user-authored edits still persist.
            const string barPromptMigrationKey =
                "VolumeSTCube.Quest.BarPromptMigrationV2";
            if (PlayerPrefs.GetInt(barPromptMigrationKey, 0) == 0 ||
                string.IsNullOrWhiteSpace(prompt) ||
                prompt == PreviousDefaultSpatialPrompt)
            {
                prompt = DefaultSpatialPrompt;
                PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
                PlayerPrefs.SetInt(barPromptMigrationKey, 1);
                PlayerPrefs.Save();
            }
            ResetSelectedTicksToActiveBuckets();
            matPlotUrl = PlayerPrefs.GetString("VolumeSTCube.Quest.MatPlotUrl", matPlotUrl);
            s4dUrl = PlayerPrefs.GetString("VolumeSTCube.Quest.S4DUrl", s4dUrl);
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
            RefreshDatasets();
            if (datasets.Count > 0)
            {
                // Discovery may preselect the most useful variable, but opening
                // a dataset is an explicit user decision on both flat screen
                // and Quest. Auto-confirming here skipped the first workflow
                // page whenever bundled/local For_VR data was present.
                importSelectedVariableIndex = 0;
                for (int index = 0; index < datasets.Count; index++)
                {
                    if (string.Equals(datasets[index].Name, "Prediction_HS",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        importSelectedVariableIndex = index;
                        break;
                    }
                }
                stage = Stage.DatasetImport;
                BuildStage();
            }
#if UNITY_EDITOR || SLABLAB_FLAT
            panelCanvas.gameObject.SetActive(true);
            mainMenuCanvas.gameObject.SetActive(false);
            slabPreviewCanvas.gameObject.SetActive(false);
            intentCanvas.gameObject.SetActive(false);
            aiFindingsCanvas.gameObject.SetActive(false);
            workflowToolbarCanvas.gameObject.SetActive(false);
#else
            // Dataset Import is the first workflow step on Quest as well as on
            // desktop. Hiding every tool here left a new headset user looking at
            // an empty room until they discovered the controller shortcut.
            panelCanvas.gameObject.SetActive(true);
            mainMenuCanvas.gameObject.SetActive(false);
            slabPreviewCanvas.gameObject.SetActive(false);
                intentCanvas.gameObject.SetActive(false);
            aiFindingsCanvas.gameObject.SetActive(false);
            workflowToolbarCanvas.gameObject.SetActive(false);
            questImportHeadLocked = true;
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            StartCoroutine(PlaceInitialQuestWorkspace());
#endif
        }

        public void RecenterQuestWorkspace()
        {
#if !UNITY_EDITOR && !SLABLAB_FLAT
            StartCoroutine(PlaceInitialQuestWorkspace());
#endif
        }

#if !UNITY_EDITOR && !SLABLAB_FLAT
        private IEnumerator PlaceInitialQuestWorkspace()
        {
            // OpenXR tracking poses are not guaranteed to be available during
            // RuntimeInitializeOnLoad. Wait briefly, then anchor the workspace to
            // the user's actual launch gaze instead of the Guardian world axes.
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
                if (xrCamera != null &&
                    (xrCamera.transform.localPosition.sqrMagnitude > 0.01f ||
                     Quaternion.Angle(xrCamera.transform.localRotation,
                         Quaternion.identity) > 1.0f))
                    break;
            }
            if (xrCamera == null)
                yield break;

            // Panels must follow the actual gaze pitch. Projecting this direction
            // onto the floor made a panel anchored while the user looked slightly
            // up appear at the very bottom of both Quest eye buffers.
            Vector3 gaze = xrCamera.transform.forward.normalized;
            if (gaze.sqrMagnitude < 0.01f)
                gaze = transform.forward;
            Vector3 forward = Vector3.ProjectOnPlane(gaze, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 head = xrCamera.transform.position;

            if (stage == Stage.DatasetImport)
            {
                questImportHeadLocked = true;
                UpdateQuestImportHeadLock(true);
                Debug.Log("VolumeSTCube Quest import panel head-locked. head=" +
                    head.ToString("F2") + ", gaze=" + gaze.ToString("F2") +
                    ", panel=" + panelCanvas.transform.position.ToString("F2"));
                yield break;
            }

            questImportHeadLocked = false;
            PlaceQuestAnalysisWorkspace(head, forward, right);
        }

        private void PlaceQuestAnalysisWorkspace(Vector3 head, Vector3 forward,
            Vector3 right)
        {
            // Keep the room upright, but lift/lower its centre to the user's
            // current gaze. Using yaw only caused the whole cube-and-panel layout
            // to sit below the lenses whenever the user confirmed while looking up.
            float gazeLift = xrCamera != null
                ? Mathf.Clamp(xrCamera.transform.forward.y * 1.35f, -0.46f, 0.46f)
                : 0.0f;
            Vector3 workspaceHead = head + Vector3.up * gazeLift;
            Vector3 panelGaze = (forward + Vector3.down * 0.02f).normalized;

            // Room-scale analysis layout: the cube owns the left half of the
            // workspace and the active controller panel owns the right half.
            PositionQuestPanel(panelCanvas, workspaceHead + panelGaze * 1.62f +
                right * 0.66f, 0.00102f);
            PositionQuestPanel(mainMenuCanvas, workspaceHead + panelGaze * 1.58f +
                right * 0.62f, 0.00104f);
            PositionQuestPanel(boundaryCanvas, workspaceHead + panelGaze * 1.60f +
                right * 0.62f, 0.00094f);
            PositionQuestPanel(trailCanvas, workspaceHead + panelGaze * 1.66f +
                right * 0.62f, 0.00094f);
            PositionQuestPanel(facetGridCanvas, workspaceHead + panelGaze * 1.72f +
                right * 0.54f + Vector3.up * 0.24f, 0.00090f);
            PositionQuestPanel(slabPreviewCanvas, workspaceHead + panelGaze * 1.74f -
                right * 0.08f + Vector3.up * 0.64f, 0.00084f);
            PositionQuestPanel(intentCanvas, workspaceHead + panelGaze * 1.70f +
                right * 0.62f + Vector3.up * 0.58f, 0.00084f);
            PositionQuestPanel(draftCanvas, workspaceHead + panelGaze * 1.58f +
                right * 1.02f + Vector3.down * 0.34f, 0.00082f);

            if (spatialRoot != null)
            {
                spatialRoot.SetActive(true);
                spatialRoot.transform.position = workspaceHead + forward * 1.92f -
                    right * 0.72f + Vector3.down * 0.08f;
                spatialRoot.transform.rotation = Quaternion.LookRotation(
                    forward, Vector3.up);
            }
            Debug.Log("VolumeSTCube Quest workspace anchored to headset. " +
                "head=" + head.ToString("F2") +
                ", gazeLift=" + gazeLift.ToString("F2") +
                ", panel=" + panelCanvas.transform.position.ToString("F2") +
                ", field=" + (spatialRoot != null
                    ? spatialRoot.transform.position.ToString("F2")
                    : "missing"));
        }

        private void UpdateQuestImportHeadLock(bool immediate = false)
        {
            if (!questImportHeadLocked || panelCanvas == null || xrCamera == null ||
                stage != Stage.DatasetImport || grabbedPanel != null)
                return;
            Vector3 gaze = xrCamera.transform.forward.normalized;
            Vector3 targetPosition = xrCamera.transform.position + gaze * 1.30f;
            Quaternion targetRotation = Quaternion.LookRotation(gaze,
                xrCamera.transform.up);
            float blend = immediate ? 1.0f : 1.0f - Mathf.Exp(-10.0f * Time.deltaTime);
            panelCanvas.transform.position = Vector3.Lerp(
                panelCanvas.transform.position, targetPosition, blend);
            panelCanvas.transform.rotation = Quaternion.Slerp(
                panelCanvas.transform.rotation, targetRotation, blend);
            panelCanvas.transform.localScale = Vector3.one * 0.00152f;
        }

        private void PositionQuestPanel(Canvas canvas, Vector3 worldPosition,
            float worldScale)
        {
            if (canvas == null)
                return;
            canvas.transform.position = worldPosition;
            canvas.transform.localScale = Vector3.one * worldScale;
            FacePanelTowardViewer(canvas.transform);
        }
#endif

        private void Update()
        {
#if !UNITY_EDITOR && !SLABLAB_FLAT
            UpdateQuestImportHeadLock();
#endif
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
        }

        private void LateUpdate()
        {
            // XR hover/selection components can update renderer properties late
            // in the frame. Reassert the semantic selection colour afterwards
            // so a chosen variable remains visibly purple on Quest.
            ReassertVariablePaletteSelectionVisuals();
            UpdatePanelTypography();
        }

        private void UpdatePanelTypography()
        {
            if (Time.unscaledTime < nextDesktopTypographyRefresh)
                return;
            nextDesktopTypographyRefresh = Time.unscaledTime + 0.35f;
            Canvas[] panels =
            {
                panelCanvas, mainMenuCanvas, boundaryCanvas, trailCanvas,
                facetGridCanvas, aiFindingsCanvas, slabPreviewCanvas,
                intentCanvas, draftCanvas
            };
            for (int panelIndex = 0; panelIndex < panels.Length; panelIndex++)
            {
                Canvas panel = panels[panelIndex];
                if (panel == null || !panel.gameObject.activeInHierarchy)
                    continue;
                MaximizeTextInsidePanel(panel);
            }
        }

        private void MaximizeTextInsidePanel(Canvas panel)
        {
            Text[] labels = panel.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                Text label = labels[index];
                if (label == null || !label.gameObject.activeInHierarchy)
                    continue;
                RectTransform labelRect = label.rectTransform;
                Button ownerButton = label.GetComponentInParent<Button>();
                float availableHeight = Mathf.Abs(labelRect.rect.height);
                if (ownerButton != null &&
                    label.transform.IsChildOf(ownerButton.transform))
                {
                    RectTransform buttonRect = ownerButton.transform as RectTransform;
                    if (buttonRect != null)
                    {
                        // Only enlarge the text's usable inset. The button and
                        // panel geometry remain exactly as authored.
                        labelRect.sizeDelta = new Vector2(
                            Mathf.Max(8.0f, buttonRect.rect.width - 8.0f),
                            Mathf.Max(8.0f, buttonRect.rect.height - 6.0f));
                        labelRect.anchoredPosition = new Vector2(0.0f, 1.0f);
                        availableHeight = Mathf.Abs(labelRect.rect.height);
                    }
                }
                if (availableHeight < 4.0f)
                    continue;
                bool multiline = label.text.IndexOf('\n') >= 0;
                float fillRatio = ownerButton != null ? 0.84f :
                    multiline ? 0.76f : 0.92f;
                float platformMaximum = 52.0f;
                float platformMinimum = 14.0f;
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                {
                    platformMaximum = 72.0f;
                    platformMinimum = 18.0f;
                }
#endif
                float maximum = Mathf.Clamp(availableHeight * fillRatio,
                    platformMinimum, platformMaximum);
                float minimum = Mathf.Min(maximum, platformMinimum);
                TMPro.TextMeshProUGUI crisp =
                    label.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (crisp == null)
                {
                    crisp = AddCrispTextOverlay(label, label.text, maximum,
                        minimum, ownerButton != null ||
                        label.fontStyle == FontStyle.Bold);
                }
                if (crisp != null)
                {
                    // The workflow mutates legacy Text values in several later
                    // stages. Mirror them into one consistent SDF renderer so
                    // every panel stays as sharp as the opening screen.
                    crisp.text = label.text;
                    crisp.color = label.color;
                    crisp.alignment = ToTmpAlignment(label.alignment);
                    crisp.fontStyle = ownerButton != null ||
                        label.fontStyle == FontStyle.Bold
                            ? TMPro.FontStyles.Bold
                            : TMPro.FontStyles.Normal;
                    crisp.enableWordWrapping = multiline;
                    crisp.enableAutoSizing = true;
                    crisp.fontSizeMin = minimum;
                    crisp.fontSizeMax = maximum;
                    crisp.overflowMode = TMPro.TextOverflowModes.Truncate;
                    crisp.lineSpacing = multiline ? -12.0f : 0.0f;
                }
            }
        }

        public void TogglePanel()
        {
            if (mainMenuCanvas == null)
                return;
            bool next = !mainMenuCanvas.gameObject.activeSelf;
            if (next)
            {
                ShowPrimaryTool(mainMenuCanvas);
                BuildMainMenu();
            }
            else
            {
                mainMenuCanvas.gameObject.SetActive(false);
            }
        }

        public void ToggleSlabFrame()
        {
            if (panelCanvas == null)
                return;
            bool next = !panelCanvas.gameObject.activeSelf;
            if (!next)
            {
                panelCanvas.gameObject.SetActive(false);
                if (slabPreviewCanvas != null)
                    slabPreviewCanvas.gameObject.SetActive(false);
                if (intentCanvas != null)
                    intentCanvas.gameObject.SetActive(false);
                return;
            }
            ShowPrimaryTool(panelCanvas);
            if (slabPreviewBuilt && slabPreviewCanvas != null)
            {
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
#if !UNITY_EDITOR && !SLABLAB_FLAT
            if (leftController != null)
                PlaceSlabFrameNearLeftHand();
#endif
        }

        private void ShowPrimaryTool(Canvas tool)
        {
            if (tool == null)
                return;
            HidePrimaryToolsExcept(tool);
            tool.gameObject.SetActive(true);
        }

        private void ShowComposerTool(Canvas tool)
        {
            // Composer panels replace the current task surface. The persistent
            // toolbar and 3D Field remain as spatial context.
            HidePrimaryToolsExcept(tool);
            if (slabPreviewCanvas != null && slabPreviewCanvas != tool)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null && intentCanvas != tool)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null && draftCanvas != tool)
                draftCanvas.gameObject.SetActive(false);
            if (tool != null)
                tool.gameObject.SetActive(true);
        }

        private void HidePrimaryToolsExcept(Canvas exception)
        {
            if (mainMenuCanvas != null && mainMenuCanvas != exception)
                mainMenuCanvas.gameObject.SetActive(false);
            if (boundaryCanvas != null && boundaryCanvas != exception)
            {
                boundaryCanvas.gameObject.SetActive(false);
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
            }
            if (trailCanvas != null && trailCanvas != exception)
                trailCanvas.gameObject.SetActive(false);
            if (panelCanvas != null && panelCanvas != exception)
            {
                SetGroundDock(false);
                panelCanvas.gameObject.SetActive(false);
            }
            // One focused task surface at a time. The Field and persistent
            // toolbar remain visible context; floating editors never stack on
            // top of one another.
            if (slabPreviewCanvas != null && slabPreviewCanvas != exception)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null && intentCanvas != exception)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null && draftCanvas != exception)
                draftCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && facetGridCanvas != exception)
            {
                facetGridCanvas.gameObject.SetActive(false);
                SetFacetSelectionEvidencePreview(false);
            }
            if (aiFindingsCanvas != null && aiFindingsCanvas != exception)
                aiFindingsCanvas.gameObject.SetActive(false);
            if (exception != slabPreviewCanvas)
            {
                for (int index = 0; index < sourcePreviewLayerCanvases.Count; index++)
                    if (sourcePreviewLayerCanvases[index] != null)
                        sourcePreviewLayerCanvases[index].gameObject.SetActive(false);
            }
            if (exception != facetGridCanvas)
            {
                for (int index = 0; index < materializedLayerCanvases.Count; index++)
                    if (materializedLayerCanvases[index] != null)
                        materializedLayerCanvases[index].gameObject.SetActive(false);
            }
        }

        public void ToggleFacetGrid()
        {
            if (facetGridCanvas == null)
                return;
            bool next = !facetGridCanvas.gameObject.activeSelf;
            if (next)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                SetFacetSelectionEvidencePreview(gridCellSelected);
            }
            else
            {
                facetGridCanvas.gameObject.SetActive(false);
                SetFacetSelectionEvidencePreview(false);
            }
        }

        public void ResetVolumeLayout()
        {
            FrameVolume();
        }

        private void EnforceSlabPreviewVisibility()
        {
            if (slabPreviewObject == null)
                return;
            bool horizontalAuthoring = boundaryEditActive &&
                boundaryDimension == BoundaryDimension.Horizontal;
            bool depthLayerInspection = boundaryEditActive &&
                boundaryDimension == BoundaryDimension.Depth &&
                depthInspectionActive;
            // The permanent XY slab remains hidden, but while the user holds a
            // Depth cut the exact RAW z layer is an active inspection object and
            // must stay visible beside its Field.
            bool shouldShow = horizontalAuthoring || depthLayerInspection;
            if (boundaryEditActive &&
                boundaryDimension != BoundaryDimension.Horizontal &&
                !depthLayerInspection)
                shouldShow = false;
            if (slabPreviewObject.activeSelf != shouldShow)
                slabPreviewObject.SetActive(shouldShow);
            Renderer previewRenderer = slabPreviewObject.GetComponent<Renderer>();
            if (previewRenderer != null)
                previewRenderer.enabled = shouldShow;
            if (!shouldShow)
                HideAllAuxiliarySliceRenderers();
        }

        private void HideAllAuxiliarySliceRenderers()
        {
            if (spatialRoot == null || slabTexture == null)
                return;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            Renderer[] renderers = spatialRoot.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null ||
                    (volumeRoot != null && renderer.transform.IsChildOf(volumeRoot)) ||
                    (boundaryDayPreviewObject != null &&
                     renderer.transform.IsChildOf(boundaryDayPreviewObject.transform)))
                    continue;
                Material material = renderer.sharedMaterial;
                // Accessing Material.mainTexture on Unlit/Color emits a warning
                // every frame because that shader has no _MainTex property.
                // Besides filling Editor.log, the repeated logging causes an
                // avoidable hitch while authoring boundaries in VR.
                if (material != null && material.HasProperty("_MainTex") &&
                    material.mainTexture == slabTexture)
                    renderer.enabled = false;
            }
        }

        public void RotateField(float yawDegrees)
        {
            if (spatialRoot == null || Mathf.Abs(yawDegrees) < 0.0001f)
                return;
            spatialRoot.transform.Rotate(Vector3.up, yawDegrees, Space.World);
        }

        private void PlaceSlabFrameNearLeftHand()
        {
            if (panelCanvas == null || leftController == null || xrCamera == null)
                return;
            Vector3 towardView = Vector3.ProjectOnPlane(xrCamera.transform.forward, Vector3.up).normalized;
            if (towardView.sqrMagnitude < 0.01f)
                towardView = transform.forward;
            panelCanvas.transform.position =
                leftController.position + towardView * 0.42f + Vector3.up * 0.14f;
            FacePanelTowardViewer(panelCanvas.transform);
        }

        private void UpdatePanelGrab()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                grabbedPanel = null;
                return;
            }
            if (rayInteractor == null)
                return;
            if (rayInteractor.GripPressed)
            {
                if (UnityEngine.Physics.Raycast(rayInteractor.PointerRay, out UnityEngine.RaycastHit hit,
                    rayInteractor.maxDistance, 1 << 5, QueryTriggerInteraction.Collide))
                {
                    VolumeSTCubeQuestPanelHandle handle =
                        hit.collider.GetComponentInParent<VolumeSTCubeQuestPanelHandle>();
                    if (handle != null)
                    {
#if !UNITY_EDITOR && !SLABLAB_FLAT
                        questImportHeadLocked = false;
#endif
                        grabbedPanel = handle.transform;
                        if (workflowToolbarCanvas != null &&
                            grabbedPanel == workflowToolbarCanvas.transform)
                            workflowToolbarPinned = true;
                        grabbedPanelDistance = Mathf.Clamp(hit.distance, 0.45f, 2.2f);
                        SetStatus("Panel released from its anchor. Move the controller; release Grip to pin.");
                    }
                }
            }

            if (grabbedPanel != null && rayInteractor.GripHeld)
            {
                grabbedPanel.position = rayInteractor.PointerRay.origin +
                    rayInteractor.PointerRay.direction * grabbedPanelDistance;
                FacePanelTowardViewer(grabbedPanel);
            }

            if (grabbedPanel != null && rayInteractor.GripReleased)
            {
                SpatialAxisRigState axisRig = FindAxisRigByRoot(grabbedPanel);
                if (axisRig != null)
                {
                    SnapAxisRigToNearestFieldFace(axisRig);
                    SetStatus("Axis body magnetically attached to the nearest Field face.");
                }
                else
                    SetStatus(grabbedPanel.name + " pinned in the workspace.");
                grabbedPanel = null;
            }
        }

        private SpatialAxisRigState FindAxisRigByRoot(Transform candidate)
        {
            if (candidate == null)
                return null;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                if (state.root != null && state.root.transform == candidate)
                    return state;
            }
            return null;
        }

        private void SnapAxisRigToNearestFieldFace(SpatialAxisRigState state)
        {
            if (state == null || state.root == null || spatialRoot == null)
                return;
            int rigIndex = spatialAxisRigStates.IndexOf(state);
            int boundCount = 0;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
                if (spatialAxisRigStates[index].boundVariable >= 0)
                    boundCount++;
            int count = roles[3] == DimensionRole.Fixed ? 1 :
                Mathf.Min(datasets.Count, Mathf.Max(1, boundCount + 1));
            Vector3 center = PairedFieldCenter(Mathf.Max(0, rigIndex), count);
            Quaternion fieldRotation = Quaternion.Euler(0.0f,
                PairedFieldYaw(Mathf.Max(0, rigIndex), count), 0.0f);
            const float bodyClearance = 0.48f;
            Vector3[] faceOffsets =
            {
                new Vector3(FieldHalfWidth + bodyClearance, 0.0f, 0.0f),
                new Vector3(-FieldHalfWidth - bodyClearance, 0.0f, 0.0f),
                new Vector3(0.0f, FieldHalfHeight + bodyClearance, 0.0f),
                new Vector3(0.0f, -FieldHalfHeight - bodyClearance, 0.0f),
                new Vector3(0.0f, 0.0f, FieldHalfDepth + bodyClearance),
                new Vector3(0.0f, 0.0f, -FieldHalfDepth - bodyClearance)
            };
            Vector3 bestFieldPosition = center + fieldRotation * faceOffsets[0];
            Vector3 bestWorldPosition = spatialRoot.transform.TransformPoint(
                bestFieldPosition);
            float bestDistance = Vector3.SqrMagnitude(
                state.root.transform.position - bestWorldPosition);
            for (int face = 1; face < faceOffsets.Length; face++)
            {
                Vector3 fieldPosition = center + fieldRotation * faceOffsets[face];
                Vector3 worldPosition = spatialRoot.transform.TransformPoint(
                    fieldPosition);
                float distance = Vector3.SqrMagnitude(
                    state.root.transform.position - worldPosition);
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestFieldPosition = fieldPosition;
                bestWorldPosition = worldPosition;
            }
            state.hasCustomDock = true;
            state.customDockFieldPosition = bestFieldPosition;
            state.customDockFieldRotation = Quaternion.Inverse(
                spatialRoot.transform.rotation) * state.root.transform.rotation;
            StartCoroutine(AnimateWorldMove(state.root.transform,
                bestWorldPosition));
        }

        private void FacePanelTowardViewer(Transform panel)
        {
            if (panel == null || xrCamera == null)
                return;
            Vector3 awayFromViewer = panel.position - xrCamera.transform.position;
            awayFromViewer.y = 0.0f;
            if (awayFromViewer.sqrMagnitude > 0.001f)
                panel.rotation = Quaternion.LookRotation(awayFromViewer.normalized, Vector3.up);
        }

        private void CreateSpatialCube()
        {
            spatialRoot = new GameObject("Slab Lab Continuous Cube");
            spatialRoot.transform.SetParent(transform, false);
            spatialRoot.transform.localPosition = new Vector3(-0.90f, 1.47f, 2.35f);

            Vector3[] corners =
            {
                new Vector3(-FieldHalfWidth,-FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,-FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,-FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,-FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,FieldHalfHeight,-FieldHalfDepth),
                new Vector3(FieldHalfWidth,FieldHalfHeight,FieldHalfDepth),
                new Vector3(-FieldHalfWidth,FieldHalfHeight,FieldHalfDepth)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7}
            };
            CreateHolographicFieldFrame(spatialRoot.transform, corners, edges,
                "Primary Field");

            CreateFieldDatasetSelector();
            CreateAnalysisAxes();

            slabObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slabObject.name = "Directly draggable slab";
            slabObject.layer = 5;
            slabObject.transform.SetParent(spatialRoot.transform, false);
            slabObject.transform.localScale = new Vector3(
                FieldHalfWidth * 1.72f, 0.024f, FieldHalfDepth * 1.70f);
            Material slabMaterial = new Material(Shader.Find("Sprites/Default"));
            slabMaterial.color = new Color(0.06f, 0.9f, 1.0f, 0.32f);
            Renderer defaultSlabRenderer = slabObject.GetComponent<Renderer>();
            defaultSlabRenderer.material = slabMaterial;
            // Keep the collider as the depth interaction surface, but remove the
            // old always-visible cyan plate. Author Boundary owns the visible cuts.
            defaultSlabRenderer.enabled = false;
            VolumeSTCubeQuestClickTarget slabTarget = slabObject.AddComponent<VolumeSTCubeQuestClickTarget>();
            slabTarget.Clicked = BeginSlabInteraction;
            CreateDepthBoundaryPlane(0);
            CreateDepthBoundaryPlane(1);
            UpdateDepthBoundaryPlanes();
            SetDepthBoundaryVisibility(false);

            slabPreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            slabPreviewObject.name = "Slab XY data texture";
            Destroy(slabPreviewObject.GetComponent<Collider>());
            slabPreviewObject.transform.SetParent(spatialRoot.transform, false);
            slabPreviewObject.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);
            // Sprites/Default is already retained by the controller laser and supports
            // a main texture; Unlit/Texture may be stripped from an Android build.
            slabPreviewMaterial = new Material(Shader.Find("Sprites/Default"));
            slabPreviewObject.GetComponent<Renderer>().material = slabPreviewMaterial;
            slabPreviewObject.SetActive(false);
            CreateDepthInspectionVisuals();

            regionRoot = new GameObject("Selected XY region");
            regionRoot.transform.SetParent(spatialRoot.transform, false);
            for (int i = 0; i < regionLines.Length; i++)
                regionLines[i] = CreateWorldLine("Region edge", regionRoot.transform, Vector3.zero, Vector3.zero, Green, 0.012f);
            regionRoot.SetActive(false);

            CreateTimeRail();
            CreateGroundEvidenceVisuals();
            groundLink = CreateWorldLine("Ground evidence link", transform,
                Vector3.zero, Vector3.zero, Purple, 0.008f);
            groundLink.useWorldSpace = true;
            groundLink.gameObject.SetActive(false);
            for (int index = 0; index < matPlotStcLinkSegments.Length; index++)
            {
                matPlotStcLinkSegments[index] = CreateWorldLine(
                    "MatPlot to STC dashed provenance " + index, transform,
                    Vector3.zero, Vector3.zero, Purple, 0.010f);
                matPlotStcLinkSegments[index].useWorldSpace = true;
                matPlotStcLinkSegments[index].gameObject.SetActive(false);
            }
            UpdateSlabVisual(false);
            UpdateRegionVisual();
            CreateSpatialAxisComposerRoot();
        }

        private void CreateFieldDatasetSelector()
        {
            fieldDatasetSelectorRoot = new GameObject("Field display dataset selector");
            fieldDatasetSelectorRoot.transform.SetParent(spatialRoot.transform, false);
            // Mount the selector on the upper front face of the Field.  It stays
            // outside the volume renderer, so its colliders remain easy to hit.
            fieldDatasetSelectorRoot.transform.localPosition = new Vector3(
                0.0f, FieldHalfHeight - 0.085f, -FieldHalfDepth - 0.052f);
            // Text meshes are authored toward local +Z. The viewer looks at the
            // Field from its -Z side, so turn this front-mounted strip around.
            fieldDatasetSelectorRoot.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            RefreshFieldDatasetSelector();
        }

        private void RefreshFieldDatasetSelector()
        {
            if (fieldDatasetSelectorRoot == null)
                return;

            Transform root = fieldDatasetSelectorRoot.transform;
            for (int index = root.childCount - 1; index >= 0; index--)
                Destroy(root.GetChild(index).gameObject);

            fieldDatasetSelectorRoot.SetActive(datasets.Count > 0);
            if (datasets.Count == 0)
                return;

            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Display data selector backing";
            plate.transform.SetParent(root, false);
            plate.transform.localPosition = new Vector3(0.0f, 0.0f, -0.018f);
            plate.transform.localScale = new Vector3(1.68f, 0.145f, 0.026f);
            Destroy(plate.GetComponent<Collider>());
            plate.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(0.01f, 0.08f, 0.10f, 0.96f));

            CreatePalettePhysicalText(root, "DISPLAY DATA",
                new Vector3(0.0f, 0.044f, 0.002f), 0.0090f, Cyan,
                0.38f, 0.035f);

            int count = datasets.Count;
            float gap = 0.018f;
            float availableWidth = 1.56f;
            float buttonWidth = Mathf.Min(0.37f,
                (availableWidth - gap * Mathf.Max(0, count - 1)) / count);
            float totalWidth = buttonWidth * count + gap * Mathf.Max(0, count - 1);
            float startX = -totalWidth * 0.5f + buttonWidth * 0.5f;
            for (int index = 0; index < count; index++)
            {
                int capturedIndex = index;
                bool selected = datasets[index] == selectedDataset;
                GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
                button.name = "Display " + datasets[index].Name;
                button.layer = 5;
                button.transform.SetParent(root, false);
                button.transform.localPosition = new Vector3(
                    -(startX + index * (buttonWidth + gap)), -0.025f, 0.006f);
                button.transform.localScale = new Vector3(buttonWidth, 0.060f, 0.036f);
                button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                    selected
                        ? new Color(0.02f, 0.78f, 0.92f, 1.0f)
                        : new Color(0.055f, 0.10f, 0.15f, 1.0f));
                button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    () => SelectFieldDisplayDataset(capturedIndex);
                // Keep the caption outside the scaled cube transform. Parenting
                // it to the button would shrink the glyph mesh a second time.
                CreatePalettePhysicalText(root,
                    GetFieldDatasetShortLabel(datasets[index]),
                    new Vector3(button.transform.localPosition.x, -0.025f,
                        0.027f), 0.0092f,
                    selected ? Ink : Color.white,
                    Mathf.Max(0.12f, buttonWidth - 0.035f), 0.039f);
            }
        }

        private static string GetFieldDatasetShortLabel(
            VolumeSTCubeSliceDataset dataset)
        {
            string name = dataset != null ? dataset.Name.ToUpperInvariant() : "DATA";
            bool prediction = name.Contains("PREDICTION") || name.Contains("PRED");
            bool water = name.Contains("WATER") || name.Contains("LEVEL");
            if (prediction)
                return water ? "PRED WATER" : "PRED HS";
            if (name.Contains("GROUNDTRUTH") || name.Contains("TRUTH") || name.Contains("GROUND"))
                return water ? "TRUE WATER" : "TRUE HS";
            return name.Length <= 12 ? name : name.Substring(0, 12);
        }

        private void SelectFieldDisplayDataset(int index)
        {
            if (index < 0 || index >= datasets.Count)
                return;
            if (datasets[index] == selectedDataset)
            {
                SetStatus("Field is already showing " + datasets[index].Name + ".");
                RefreshFieldDatasetSelector();
                return;
            }
            LoadDataset(index);
        }

        private void CreateSpatialAxisComposerRoot()
        {
            spatialAxisComposerRoot = new GameObject("Spatial dimension composer");
            spatialAxisComposerRoot.transform.SetParent(spatialRoot.transform, false);
            spatialAxisComposerRoot.transform.localPosition =
                new Vector3(ActiveSpatialAxisDockX(), 0.06f, -0.18f);
            // The composer is an interaction object, not distant scenery. Pull
            // it toward the viewer so controller dragging remains comfortable.
            if (xrCamera != null)
            {
                Vector3 towardViewer = xrCamera.transform.position -
                    spatialAxisComposerRoot.transform.position;
                if (towardViewer.sqrMagnitude > 0.01f)
                    spatialAxisComposerRoot.transform.position +=
                        towardViewer.normalized * 0.20f;
            }
            RefreshSpatialAxisControllers();
        }

        private float ActiveSpatialAxisDockX()
        {
            if (!IsForVrSurfaceDataset)
                return SpatialAxisDockX;
            // The animated Field is shifted left and the independent XYT Field
            // occupies the presentation center. Dock the tri-axis body beyond
            // the XYT Field's right edge instead of beside the old Field.
            return VolumeSTCubeForVrFieldSwapLayout.ActiveSeparation +
                VolumeSTCubeForVrXytCompanion.IndependentFieldHalfWidth + 0.48f;
        }

        private void CreateAnalysisAxes()
        {
            float bottom = -FieldHalfHeight + 0.115f;
            float innerDepth = FieldHalfDepth - 0.070f;
            float left = -FieldHalfWidth + 0.075f;

            CreateWorldLine("Analysis axis Y Variable", spatialRoot.transform,
                new Vector3(left, bottom, -FieldHalfDepth + 0.07f),
                new Vector3(left, bottom, innerDepth),
                new Color(VariableColor.r, VariableColor.g, VariableColor.b, 0.82f),
                0.009f);
            CreateWorldLine("Analysis axis Z Depth", spatialRoot.transform,
                new Vector3(left, -FieldHalfHeight + 0.075f, innerDepth),
                new Vector3(left, FieldHalfHeight - 0.075f, innerDepth),
                new Color(DepthAxisColor.r, DepthAxisColor.g, DepthAxisColor.b, 0.92f),
                0.010f);

            variableAxisLabel = CreateWorldLabel("Y  VARIABLE", new Vector3(
                    left + 0.025f, bottom + 0.050f, -FieldHalfDepth + 0.12f),
                0.0080f, TextAnchor.LowerLeft, VariableColor);
            depthAxisLabel = CreateWorldLabel("Z  DEPTH", new Vector3(
                    left + 0.035f, FieldHalfHeight - 0.055f, innerDepth - 0.020f),
                0.0080f, TextAnchor.UpperLeft, DepthAxisColor);

            Color[] depthColors =
            {
                new Color(0.20f, 0.82f, 1.0f, 0.95f),
                new Color(0.36f, 0.55f, 1.0f, 0.95f),
                new Color(0.63f, 0.36f, 0.96f, 0.95f)
            };
            for (int index = 0; index < 3; index++)
            {
                depthBucketAxisSegments[index] = CreateWorldLine(
                    "Depth bucket axis " + index, spatialRoot.transform,
                    Vector3.zero, Vector3.zero, depthColors[index], 0.018f);
                depthBucketAxisLabels[index] = CreateWorldLabel(
                    "DEPTH BUCKET", Vector3.zero, 0.0065f,
                    TextAnchor.MiddleLeft, depthColors[index]);
                depthBucketAxisSegments[index].gameObject.SetActive(false);
                depthBucketAxisLabels[index].gameObject.SetActive(false);
            }
            CreateAxisOriginHub(new Vector3(left, bottom, innerDepth));
            UpdateAnalysisAxisLabels();
        }

        private void EnsureSpatialAxisRigStates()
        {
            while (spatialAxisRigStates.Count < datasets.Count)
                spatialAxisRigStates.Add(new SpatialAxisRigState());
            while (spatialAxisRigStates.Count > datasets.Count)
                spatialAxisRigStates.RemoveAt(spatialAxisRigStates.Count - 1);
        }

        private void RefreshSpatialAxisControllers()
        {
            if (spatialAxisComposerRoot == null)
                return;
            for (int child = spatialAxisComposerRoot.transform.childCount - 1;
                child >= 0; child--)
                Destroy(spatialAxisComposerRoot.transform.GetChild(child).gameObject);
            EnsureSpatialAxisRigStates();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].root = null;
                spatialAxisRigStates[index].timeToken = null;
                spatialAxisRigStates[index].depthToken = null;
                spatialAxisRigStates[index].variableToken = null;
                spatialAxisRigStates[index].frameRenderers.Clear();
                spatialAxisRigStates[index].frameColors.Clear();
                spatialAxisRigStates[index].frameVisibility = 0.0f;
                spatialAxisRigStates[index].frameRequestedVisible = false;
                for (int slot = 0; slot < 3; slot++)
                    spatialAxisRigStates[index].slotRenderers[slot] = null;
            }
            if (datasets.Count == 0)
                return;

            // The composer is one shared spatial controller. Variable state is
            // still stored per dataset, but additional variables create Field
            // visualisations around this rig rather than duplicate controllers.
            int count = 1;
            for (int index = 0; index < count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                string rigName = BoundVariableIndices().Count > 0
                    ? "shared variables" : "empty";
                GameObject rig = new GameObject(rigName + " axis body");
                rig.transform.SetParent(spatialAxisComposerRoot.transform, false);
                // Fixed slots prevent existing bodies from jumping or changing
                // size when the next variable body is progressively revealed.
                Quaternion pairedFieldRotation = Quaternion.Euler(
                    0.0f, PairedFieldYaw(index, count), 0.0f);
                // Keep the shared controller in the original, readable dock to
                // the right of the primary Field. Additional Fields grow around
                // this stable anchor: the second above it, the third to its
                // right. Binding a variable therefore never moves the controls.
                Vector3 desiredPositionInFieldSpace = new Vector3(
                    ActiveSpatialAxisDockX(), 0.06f, -0.18f);
                // Position each controller beside its own complete Field copy.
                // Converting through world space keeps the pairing correct even
                // after the headset-anchored workspace has been rotated.
                if (state.hasCustomDock && !IsForVrSurfaceDataset)
                    desiredPositionInFieldSpace = state.customDockFieldPosition;
                Vector3 dockedWorldPosition = spatialRoot.transform.TransformPoint(
                    desiredPositionInFieldSpace);
                bool animateDock = state.pendingDockAnimation;
                rig.transform.position = animateDock
                    ? spatialRoot.transform.TransformPoint(
                        desiredPositionInFieldSpace +
                        new Vector3(0.18f, 0.0f, 0.0f))
                    : dockedWorldPosition;
                // Look exactly along the horizontal X/Y angle bisector. A pure
                // +45 degree yaw keeps every vertical cube edge upright, sends
                // X and Y to opposite sides on screen, and leaves Z downward.
                rig.transform.rotation = state.hasCustomDock &&
                    !IsForVrSurfaceDataset
                    ? spatialRoot.transform.rotation * state.customDockFieldRotation
                    : spatialRoot.transform.rotation * pairedFieldRotation *
                        Quaternion.Euler(0.0f, 45.0f, 0.0f);
                // Grow the frame, axes, labels, and hit targets as one unit so
                // the composer remains comfortably readable and interactive in Quest.
                rig.transform.localScale = Vector3.one * 0.84f;
                // Grip anywhere on the physical axis body to reposition it. The
                // same marker used by floating panels keeps Quest interaction
                // consistent without competing with Trigger-based token drags.
                rig.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = Cyan;
                state.root = rig;
                if (animateDock)
                {
                    state.pendingDockAnimation = false;
                    StartCoroutine(AnimateWorldMove(rig.transform,
                        dockedWorldPosition));
                }
                CreateVariableBindingShell(index, state);
                CreateAxisRigLines(index, state);
                state.timeToken = CreateAxisDimensionToken(index, 0, state,
                    "TIME", TimeColor);
                state.depthToken = CreateAxisDimensionToken(index, 1, state,
                    "DEPTH", DepthColor);
                if (state.timeAxis >= 0)
                    CreateAxisRoleButtons(index, 0, state);
                if (state.depthAxis >= 0)
                    CreateAxisRoleButtons(index, 1, state);
                UpdateAxisRigTokenPositions(index, false);
            }
            CreateSpatialComponentPalette();
        }

        private void CreateVariableBindingShell(int variableIndex,
            SpatialAxisRigState state)
        {
            List<int> boundVariables = BoundVariableIndices();
            bool bound = boundVariables.Count > 0;
            bool selected = selectedDataset != null && boundVariables.Contains(
                datasets.IndexOf(selectedDataset));
            Color color = selected ? VariableColor : bound
                ? new Color(VariableColor.r, VariableColor.g,
                    VariableColor.b, 0.72f)
                : new Color(0.20f, 0.68f, 0.82f, 0.58f);
            // Leave breathing room around the upper range selectors.
            float half = 0.54f;
            Vector3[] corners =
            {
                new Vector3(-half,-half,-half), new Vector3(half,-half,-half),
                new Vector3(half,-half,half), new Vector3(-half,-half,half),
                new Vector3(-half,half,-half), new Vector3(half,half,-half),
                new Vector3(half,half,half), new Vector3(-half,half,half)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                float meanDepth = (corners[edges[edge, 0]].z +
                    corners[edges[edge, 1]].z) * 0.5f;
                float edgeAlpha = meanDepth >= 0.0f ? 0.78f : 0.38f;
                Color frameColor = new Color(color.r, color.g, color.b, edgeAlpha);
                LineRenderer frameLine = CreateWorldLine(
                    "Variable shell", state.root.transform,
                    corners[edges[edge, 0]], corners[edges[edge, 1]],
                    frameColor,
                    meanDepth >= 0.0f ? 0.0082f : 0.0055f);
                state.frameRenderers.Add(frameLine);
                state.frameColors.Add(frameColor);
                frameLine.enabled = false;
            }

            CreateMagneticAxisDock(state.root.transform, color, bound);

            GameObject shellTarget = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shellTarget.name = "Axis composer header";
            shellTarget.layer = 5;
            shellTarget.transform.SetParent(state.root.transform, false);
            shellTarget.transform.localPosition = new Vector3(0.0f, 0.52f, 0.0f);
            shellTarget.transform.localRotation =
                Quaternion.Inverse(state.root.transform.localRotation);
            shellTarget.transform.localScale = new Vector3(0.34f, 0.085f, 0.050f);
            Material shellMaterial = new Material(Shader.Find("Sprites/Default"));
            // Keep the drop header opaque.  A translucent cube directly in
            // front of the wire frame caused Quest's mobile renderer to
            // alternate its draw order with the frame and appear to flash.
            shellMaterial.color = bound
                ? new Color(color.r * 0.34f, color.g * 0.34f,
                    color.b * 0.34f, 1.0f)
                : new Color(0.025f, 0.18f, 0.24f, 1.0f);
            shellTarget.GetComponent<Renderer>().material = shellMaterial;
            TextMesh variableLabel = CreateWorldLabel(
                state.variableAxis >= 0 ? "VARIABLE" : "AXIS",
                Vector3.back * 0.038f, 0.0060f,
                TextAnchor.MiddleCenter, Ink, shellTarget.transform);
            variableLabel.transform.localScale =
                new Vector3(2.5f, 8.3f, 15.4f);
        }

        private List<int> BoundVariableIndices()
        {
            List<int> result = new List<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable >= 0 && variable < datasets.Count &&
                    !result.Contains(variable))
                    result.Add(variable);
            }
            return result;
        }

        private string BoundVariableLabel(List<int> variables)
        {
            if (variables == null || variables.Count == 0)
                return "DROP VARIABLE HERE";
            StringBuilder label = new StringBuilder();
            for (int index = 0; index < variables.Count; index++)
            {
                if (index > 0)
                    label.Append("  +  ");
                label.Append(datasets[variables[index]].Name.ToUpperInvariant());
            }
            return label.ToString();
        }

        private void CreateMagneticAxisDock(Transform parent, Color color,
            bool bound)
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            plate.name = "Magnetic field dock";
            plate.transform.SetParent(parent, false);
            plate.transform.localPosition = new Vector3(-0.47f, 0.0f, 0.0f);
            plate.transform.localScale = new Vector3(0.035f, 0.34f, 0.32f);
            Destroy(plate.GetComponent<Collider>());
            Color dockColor = bound ? VariableColor : Cyan;
            plate.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(dockColor.r * 0.35f, dockColor.g * 0.35f,
                    dockColor.b * 0.35f, 1.0f));

            for (int index = 0; index < 4; index++)
            {
                GameObject node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                node.name = "Magnetic dock node";
                node.transform.SetParent(parent, false);
                node.transform.localPosition = new Vector3(-0.495f,
                    index < 2 ? -0.13f : 0.13f,
                    (index & 1) == 0 ? -0.12f : 0.12f);
                node.transform.localScale = Vector3.one * 0.045f;
                Destroy(node.GetComponent<Collider>());
                node.GetComponent<Renderer>().material =
                    CreateStableOpaqueMaterial(new Color(dockColor.r,
                        dockColor.g, dockColor.b, 1.0f));
            }
        }

        private void CreateSpatialComponentPalette()
        {
            if (spatialAxisRigStates.Count == 0 ||
                spatialAxisRigStates[0].root == null)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            variablePaletteTokens.Clear();
            variablePaletteLabels.Clear();
            variableFixedRoleButton = null;
            variableFacetedRoleButton = null;
            variableSharedScopeButton = null;
            variableCustomScopeButton = null;
            variablePaletteCollapseButton = null;

            // VARIABLE uses the same physical pill, label hierarchy, material,
            // and hit target as TIME/DEPTH.  Keeping the caption as a child of
            // the pill prevents it from disappearing when the token moves.
            GameObject palette = GameObject.CreatePrimitive(
                PrimitiveType.Sphere);
            palette.name = "VARIABLE draggable axis token";
            palette.layer = 5;
            palette.transform.SetParent(state.root.transform, false);
            variablePaletteRoot = palette;
            variablePaletteExpandedRoot = null;

            Vector3 variablePosition = state.variableAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.variableAxis) *
                    AxisRigLength
                : UnboundVariableTokenPosition;
            palette.transform.localPosition = variablePosition;
            palette.transform.localScale = new Vector3(
                0.205f, 0.082f, 0.054f);
            palette.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(new Color(
                    VariableColor.r, VariableColor.g, VariableColor.b, 1.0f));
            palette.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                BeginPaletteComponentDrag(palette,
                    PaletteComponentKind.VariableAxis, -1);
            TextMesh variableLabel = CreateWorldLabel("VARIABLE",
                Vector3.back * 0.038f, 0.0049f,
                TextAnchor.MiddleCenter, Ink, palette.transform);
            variableLabel.fontStyle = FontStyle.Bold;
            variableLabel.transform.localScale =
                new Vector3(4.88f, 12.2f, 18.5f);
            state.variableToken = palette;

            // Only after VARIABLE has been snapped to an axis does its compact
            // categorical selector unfold from the purple token.
            if (state.variableAxis >= 0)
                CreateVariableSelectionPanel(state);
        }

        private void CreateVariableSelectionPanel(SpatialAxisRigState state)
        {
            int itemCount = datasets.Count;
            if (itemCount <= 0)
                return;
            GameObject panel = new GameObject("Variable selection flyout");
            // Dock the selector to the controller rather than the non-uniformly
            // scaled VARIABLE pill.  It therefore stays square, readable, and
            // consistently available at the lower-right of the tri-axis even
            // when VARIABLE is bound to the downward Z axis.
            panel.transform.SetParent(state.root.transform, false);
            panel.transform.localPosition = new Vector3(
                0.53f, -0.21f, -0.045f);
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one * 0.08f;
            StartCoroutine(AnimateLocalScale(panel.transform, Vector3.one));
            GameObject backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backing.name = "Variable selector backing";
            backing.transform.SetParent(panel.transform, false);
            backing.transform.localPosition = new Vector3(0.0f,
                -(itemCount - 1) * 0.055f, 0.018f);
            backing.transform.localScale = new Vector3(0.305f,
                0.105f + itemCount * 0.105f, 0.024f);
            backing.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(new Color(
                    0.025f, 0.030f, 0.050f, 1.0f));
            Destroy(backing.GetComponent<Collider>());
            CreateWorldLabel("VARIABLES", new Vector3(0.0f, 0.082f,
                    -0.004f), 0.0038f, TextAnchor.MiddleCenter,
                VariableColor, panel.transform);

            for (int index = 0; index < itemCount; index++)
                CreateVariableChoiceButton(panel.transform, index,
                    new Vector3(0.0f, 0.018f - index * 0.105f, -0.005f));
        }

        private void CreateVariableChoiceButton(Transform parent,
            int variableIndex, Vector3 position)
        {
            bool selected = spatialAxisRigStates.Exists(state =>
                state.boundVariable == variableIndex);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            button.name = datasets[variableIndex].Name +
                " variable choice";
            button.layer = 5;
            button.transform.SetParent(parent, false);
            button.transform.localPosition = position;
            button.transform.localScale = new Vector3(0.270f, 0.076f, 0.046f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(selected
                    ? new Color(0.70f, 0.20f, 0.88f, 1.0f)
                    : new Color(0.095f, 0.115f, 0.145f, 1.0f));
            int capturedVariable = variableIndex;
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => ToggleAxisVariableSelection(capturedVariable);
            variablePaletteTokens[variableIndex] = button;
            TextMesh label = CreateWorldLabel(
                (selected ? "✓  " : string.Empty) +
                    datasets[variableIndex].Name.ToUpperInvariant(),
                position + new Vector3(0.0f, 0.0f, -0.030f),
                0.0037f, TextAnchor.MiddleCenter,
                selected ? Ink : Muted, parent);
            // Assign a plain ASCII state marker after construction. This also
            // avoids platform-dependent glyph fallback on Quest.
            label.text = (selected ? "[X] " : "[ ] ") +
                GetFieldDatasetShortLabel(datasets[variableIndex]);
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            variablePaletteLabels[variableIndex] = label;
        }

        private void CreateVariablePaletteCollapseButton()
        {
            if (variablePaletteRoot == null)
                return;
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = "Variable palette collapse toggle";
            button.layer = 5;
            button.transform.SetParent(variablePaletteRoot.transform, false);
            button.transform.localScale = new Vector3(0.046f, 0.046f, 0.024f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(VariableColor);
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                ToggleVariablePaletteCollapsed;
            variablePaletteCollapseButton = button;
            ApplyVariablePaletteCollapsedState();
        }

        private void ToggleVariablePaletteCollapsed()
        {
            if (draggedPaletteToken != null)
                return;
            variablePaletteCollapsed = !variablePaletteCollapsed;
            ApplyVariablePaletteCollapsedState();
            SetStatus(variablePaletteCollapsed
                ? "Variables collapsed. Select the purple VAR tile to reopen."
                : "Variables expanded.");
        }

        private void ApplyVariablePaletteCollapsedState()
        {
            if (variablePaletteExpandedRoot != null)
                variablePaletteExpandedRoot.SetActive(!variablePaletteCollapsed);
            if (variablePaletteCollapseButton == null)
                return;
            Transform button = variablePaletteCollapseButton.transform;
            button.localPosition = variablePaletteCollapsed
                ? new Vector3(0.0f, 0.0f, 0.010f)
                : new Vector3(0.126f, variablePaletteHeight * 0.5f - 0.024f,
                    0.010f);
            for (int child = button.childCount - 1; child >= 0; child--)
                Destroy(button.GetChild(child).gameObject);
            CreatePalettePhysicalText(button,
                variablePaletteCollapsed ? "VAR" : "HIDE",
                new Vector3(-0.006f, 0.0f, 0.020f), 0.0030f, Ink);
        }

        private GameObject CreatePhysicalPaletteLabel(Transform parent,
            string label, Vector3 position, float characterSize, Color color)
        {
            return CreatePalettePhysicalText(parent, label, position,
                characterSize, color);
        }

        private GameObject CreateVariableRoleButton(Transform parent, string label,
            DimensionRole role, Vector2 position)
        {
            GameObject root = new GameObject(label + " variable role root");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = label + " variable role button";
            button.layer = 5;
            button.transform.SetParent(root.transform, false);
            button.transform.localPosition = Vector3.zero;
            // Match the proven TIME/DEPTH physical-button proportions. Keeping
            // real depth here prevents the caption from falling into the card's
            // own depth buffer when the palette or held card rotates.
            button.transform.localScale = new Vector3(0.090f, 0.030f, 0.024f);
            button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                roles[3] == role ? VariableColor :
                    new Color(0.10f, 0.16f, 0.21f, 1.0f));
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => SetSpatialVariableRole(role);
            CreatePalettePhysicalText(root.transform, label,
                // Keep the caption just above the physical face. The previous
                // 5 cm stand-off produced noticeable rightward parallax when
                // the head-follow panel was viewed at an angle.
                new Vector3(-0.012f, 0.0f, 0.020f), 0.0030f, Ink);
            return button;
        }

        private void UpdateVariableRoleButtons()
        {
            UpdateVariableRoleButton(variableFixedRoleButton,
                DimensionRole.Fixed);
            UpdateVariableRoleButton(variableFacetedRoleButton,
                DimensionRole.Faceted);
        }

        private void UpdateVariableRoleButton(GameObject button,
            DimensionRole role)
        {
            if (button == null)
                return;
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = roles[3] == role
                    ? VariableColor : new Color(0.10f, 0.16f, 0.21f, 1.0f);
        }

        private GameObject CreateVariableBoundaryScopeButton(Transform parent,
            string label, bool custom, Vector2 position)
        {
            GameObject root = new GameObject(label + " boundary scope root");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = label + " boundary scope button";
            button.layer = 5;
            button.transform.SetParent(root.transform, false);
            button.transform.localScale = new Vector3(0.090f, 0.030f, 0.024f);
            button.GetComponent<Renderer>().material = CreateStableOpaqueMaterial(
                new Color(0.10f, 0.16f, 0.21f, 1.0f));
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
            {
                SetActiveBoundaryScope(custom);
                UpdateVariableBoundaryScopeButtons();
            };
            CreatePalettePhysicalText(root.transform, label,
                new Vector3(-0.012f, 0.0f, 0.020f), 0.0025f, Ink);
            return button;
        }

        private void UpdateVariableBoundaryScopeButtons()
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            bool custom = state != null && !state.usesSharedBoundaries;
            UpdateVariableBoundaryScopeButton(variableSharedScopeButton,
                !custom);
            UpdateVariableBoundaryScopeButton(variableCustomScopeButton,
                custom);
        }

        private void UpdateVariableBoundaryScopeButton(GameObject button,
            bool active)
        {
            if (button == null)
                return;
            Renderer renderer = button.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material.color = active
                    ? Cyan : new Color(0.10f, 0.16f, 0.21f, 1.0f);
        }

        private void CreatePaletteComponent(Transform parent, string label,
            Color color, Vector2 position, PaletteComponentKind kind,
            int variableIndex)
        {
            // Use the exact physical-object path used by TIME and DEPTH. A UI
            // Button clone depends on Canvas rebuild and the dynamic font atlas;
            // it could be correct for one pickup frame and disappear on the next.
            // This cube and TextMesh are one persistent object that is moved
            // directly during drag and returned to this same slot on release.
            GameObject token = new GameObject(label + " palette component");
            token.transform.SetParent(parent, false);
            token.transform.localPosition = new Vector3(
                position.x, position.y, 0.010f);
            token.transform.localRotation = Quaternion.identity;
            token.transform.localScale = Vector3.one;
            GameObject card = GameObject.CreatePrimitive(PrimitiveType.Cube);
            card.name = label + " physical card";
            card.layer = 5;
            card.transform.SetParent(token.transform, false);
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
            card.transform.localScale = new Vector3(0.158f, 0.046f, 0.030f);
            if (variableDragBackingMaterial == null)
                variableDragBackingMaterial = CreateStableOpaqueMaterial(
                    new Color(color.r, color.g, color.b, 1.0f));
            card.GetComponent<Renderer>().sharedMaterial =
                variableDragBackingMaterial;
            PaletteComponentKind capturedKind = kind;
            int capturedVariable = variableIndex;
            card.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginPaletteComponentDrag(token, capturedKind,
                    capturedVariable);
            if (kind == PaletteComponentKind.Variable)
                variablePaletteTokens[variableIndex] = token;
            CreatePalettePhysicalText(token.transform, label,
                new Vector3(-0.012f, 0.0f, 0.022f), 0.0072f, Ink);
        }

        private GameObject CreatePalettePhysicalText(Transform parent,
            string value, Vector3 localPosition, float characterSize,
            Color color, float requestedMaximumWidth = 0.0f,
            float requestedMaximumHeight = 0.0f)
        {
            int pixelColumns = Mathf.Max(5, value.Length * 6 - 1);
            float physicalTextScale = 1.0f;
            float maximumHeight = requestedMaximumHeight > 0.0f
                ? requestedMaximumHeight : 0.028f;
            float maximumWidth = requestedMaximumWidth > 0.0f
                ? requestedMaximumWidth : 0.120f;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                physicalTextScale = 1.55f;
                if (requestedMaximumHeight <= 0.0f)
                    maximumHeight = 0.038f;
                if (requestedMaximumWidth <= 0.0f)
                    maximumWidth = 0.145f;
            }
#endif
            float height = Mathf.Clamp(characterSize * 4.2f *
                physicalTextScale, 0.010f, maximumHeight);
            float pixelHeight = height / 7.0f;
            float pixelWidth = Mathf.Min(pixelHeight,
                maximumWidth / pixelColumns);
            float left = -pixelColumns * pixelWidth * 0.5f;
            float bottom = -height * 0.5f;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            for (int index = 0; index < value.Length; index++)
            {
                string[] glyph = PaletteGlyph(value[index]);
                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row][column] != '1')
                            continue;
                        float x0 = left + (index * 6 + column) * pixelWidth;
                        float x1 = x0 + pixelWidth * 0.82f;
                        float y1 = bottom + (7 - row) * pixelHeight;
                        float y0 = y1 - pixelHeight * 0.82f;
                        int start = vertices.Count;
                        vertices.Add(new Vector3(x0, y0, 0.0f));
                        vertices.Add(new Vector3(x1, y0, 0.0f));
                        vertices.Add(new Vector3(x1, y1, 0.0f));
                        vertices.Add(new Vector3(x0, y1, 0.0f));
                        // Both windings make the glyph visible from either side
                        // of the moving spatial palette.
                        triangles.Add(start);
                        triangles.Add(start + 1);
                        triangles.Add(start + 2);
                        triangles.Add(start);
                        triangles.Add(start + 2);
                        triangles.Add(start + 3);
                        triangles.Add(start + 2);
                        triangles.Add(start + 1);
                        triangles.Add(start);
                        triangles.Add(start + 3);
                        triangles.Add(start + 2);
                        triangles.Add(start);
                    }
                }
            }
            Mesh mesh = new Mesh();
            mesh.name = value + " stable pixel caption mesh";
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            GameObject caption = new GameObject(value + " stable pixel caption",
                typeof(MeshFilter), typeof(MeshRenderer));
            caption.name = value + " stable pixel caption";
            caption.layer = 5;
            caption.transform.SetParent(parent, false);
            caption.transform.localPosition = localPosition;
            caption.transform.localRotation = Quaternion.Euler(
                0.0f, 180.0f, 0.0f);
            caption.transform.localScale = Vector3.one;
            caption.GetComponent<MeshFilter>().sharedMesh = mesh;
            Material material = CreateStableOpaqueMaterial(color);
            material.name = value + " stable pixel caption material";
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0.0f);
            if (material.HasProperty("_ZTest"))
                material.SetFloat("_ZTest",
                    (float)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = 4000;
            caption.GetComponent<Renderer>().material = material;
            caption.GetComponent<Renderer>().sortingOrder = 32760;
            return caption;
        }

        private static string[] PaletteGlyph(char raw)
        {
            char c = char.ToUpperInvariant(raw);
            switch (c)
            {
                case 'A': return new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'B': return new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
                case 'C': return new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
                case 'D': return new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
                case 'E': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
                case 'F': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
                case 'G': return new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" };
                case 'H': return new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
                case 'I': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
                case 'J': return new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
                case 'K': return new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
                case 'L': return new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
                case 'M': return new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
                case 'N': return new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
                case 'O': return new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'P': return new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
                case 'Q': return new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
                case 'R': return new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
                case 'S': return new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
                case 'T': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
                case 'U': return new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
                case 'V': return new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
                case 'W': return new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
                case 'X': return new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
                case 'Y': return new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
                case 'Z': return new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
                case '0': return new[] { "01110", "10001", "10011", "10101", "11001", "10001", "01110" };
                case '1': return new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" };
                case '2': return new[] { "01110", "10001", "00001", "00010", "00100", "01000", "11111" };
                case '3': return new[] { "11110", "00001", "00001", "01110", "00001", "00001", "11110" };
                case '4': return new[] { "00010", "00110", "01010", "10010", "11111", "00010", "00010" };
                case '5': return new[] { "11111", "10000", "10000", "11110", "00001", "00001", "11110" };
                case '6': return new[] { "01110", "10000", "10000", "11110", "10001", "10001", "01110" };
                case '7': return new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" };
                case '8': return new[] { "01110", "10001", "10001", "01110", "10001", "10001", "01110" };
                case '9': return new[] { "01110", "10001", "10001", "01111", "00001", "00001", "01110" };
                case '-': return new[] { "00000", "00000", "00000", "11111", "00000", "00000", "00000" };
                case '/': return new[] { "00001", "00010", "00010", "00100", "01000", "01000", "10000" };
                case '_': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "11111" };
                case ' ': return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" };
                default: return new[] { "01110", "10001", "00010", "00100", "00100", "00000", "00100" };
            }
        }

        private TMPro.TextMeshProUGUI CreatePaletteForegroundLabel(Transform parent,
            string value, Vector3 localPosition, float characterSize,
            float uniformScale, Color color)
        {
            // Use the same world-space UI rendering path as the legible workflow
            // toolbar. TextMesh is unreliable here because the palette cards use
            // thin, non-uniformly scaled 3D primitives.
            GameObject canvasObject = new GameObject(value + " surface label",
                typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 5;
            canvasObject.transform.SetParent(parent, false);
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.localPosition = localPosition;
            // The palette holder faces its physical cards toward the viewer;
            // flip the one-sided UI surface back toward that same viewer.
            canvasRect.localRotation = Quaternion.Euler(0.0f, 180.0f, 0.0f);
            float labelWidth = Mathf.Clamp(value.Length * 22.0f, 130.0f, 430.0f);
            canvasRect.sizeDelta = new Vector2(labelWidth, 62.0f);
            float surfaceScale = 0.001f * Mathf.Clamp(
                uniformScale / 2.25f, 0.82f, 1.18f);
            // The physical card is deliberately non-uniformly scaled. Cancel
            // that parent scale so the child Canvas keeps square glyphs and a
            // constant readable world size both in the palette and while held.
            Vector3 parentScale = parent.localScale;
            canvasRect.localScale = new Vector3(
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                surfaceScale / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));

            Canvas labelCanvas = canvasObject.GetComponent<Canvas>();
            labelCanvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            labelCanvas.worldCamera = xrCamera;
            labelCanvas.overrideSorting = true;
            labelCanvas.sortingOrder = 32760;

            GameObject textObject = new GameObject(value, typeof(RectTransform));
            textObject.layer = 5;
            textObject.transform.SetParent(canvasRect, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            TMPro.TextMeshProUGUI text =
                textObject.AddComponent<TMPro.TextMeshProUGUI>();
            text.font = crispFontAsset != null
                ? crispFontAsset : TMPro.TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = Mathf.Clamp(characterSize * 6500.0f, 24.0f, 44.0f);
            text.fontStyle = TMPro.FontStyles.Bold;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.overflowMode = TMPro.TextOverflowModes.Overflow;
            text.color = color;
            text.raycastTarget = false;
            Material foregroundMaterial = new Material(text.fontSharedMaterial);
            foregroundMaterial.name = value + " palette foreground font";
            if (foregroundMaterial.HasProperty("_CullMode"))
                foregroundMaterial.SetFloat("_CullMode", 0.0f);
            if (foregroundMaterial.HasProperty("_ZTestMode"))
                foregroundMaterial.SetFloat("_ZTestMode",
                    (float)UnityEngine.Rendering.CompareFunction.Always);
            foregroundMaterial.renderQueue = 4000;
            text.fontMaterial = foregroundMaterial;
            text.canvasRenderer.cullTransparentMesh = false;
            text.ForceMeshUpdate(true, true);
            return text;
        }

        private static Vector3 AxisSlotDirection(int slot)
        {
            return slot == 0 ? Vector3.right :
                slot == 1 ? Vector3.back : Vector3.down;
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
        // A complete field plus its side-mounted axis body forms one spatial
        // work unit. Additional variables occupy a shallow arc around the user
        // instead of forming a distant straight row.
        private static Vector3 PairedFieldCenter(int rigIndex, int count)
        {
            if (count <= 1 || rigIndex <= 0)
                return Vector3.zero;
            if (rigIndex == 1)
                // Variable 2: above the shared tri-axis controller.
                return new Vector3(SpatialAxisDockX,
                    SpatialFieldOrbitY, 0.0f);
            if (rigIndex == 2)
                // Variable 3 mirrors the primary Field across the controller.
                return new Vector3(SpatialAxisDockX * 2.0f,
                    0.0f, 0.0f);
            // Variable 4 completes the upper-right position without moving the
            // established first three Fields.
            return new Vector3(SpatialAxisDockX * 2.0f,
                SpatialFieldOrbitY, 0.0f);
        }

        private static float PairedFieldYaw(int rigIndex, int count)
        {
            if (count <= 1 || rigIndex <= 0)
                return 0.0f;
            return rigIndex == 1 ? -35.0f : rigIndex == 2 ? 35.0f : 0.0f;
        }

        private void CreateAxisRigLines(int variableIndex,
            SpatialAxisRigState state)
        {
            Color[] colors = { TimeColor, VariableColor, DepthAxisColor };
            if (state.timeAxis >= 0 && state.depthAxis >= 0 &&
                state.timeAxis != state.depthAxis)
            {
                for (int slot = 0; slot < 3; slot++)
                    colors[slot] = slot == state.timeAxis ? TimeColor :
                        slot == state.depthAxis ? DepthColor : VariableColor;
            }
            string[] names = { "X", "Y", "Z" };
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Axis origin hub";
            hub.transform.SetParent(state.root.transform, false);
            hub.transform.localPosition = AxisRigOrigin;
            hub.transform.localScale = Vector3.one * 0.075f;
            Destroy(hub.GetComponent<Collider>());
            Material hubMaterial = CreateStableOpaqueMaterial(
                new Color(0.58f, 0.86f, 1.0f, 1.0f));
            hub.GetComponent<Renderer>().material = hubMaterial;
            for (int slot = 0; slot < 3; slot++)
            {
                Vector3 direction = AxisSlotDirection(slot);
                Color axisColor = new Color(colors[slot].r, colors[slot].g,
                    colors[slot].b, 0.98f);
                LineRenderer axisLine = CreateWorldLine(
                    "Axis slot " + names[slot], state.root.transform,
                    AxisRigOrigin,
                    AxisRigOrigin + direction * (AxisRigLength - 0.035f),
                    axisColor, 0.014f);
                axisLine.startWidth = 0.009f;
                axisLine.endWidth = 0.018f;
                axisLine.startColor = new Color(axisColor.r, axisColor.g,
                    axisColor.b, 0.48f);
                axisLine.endColor = axisColor;
                GameObject target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                target.name = names[slot] + " axis drop slot";
                target.layer = 5;
                target.transform.SetParent(state.root.transform, false);
                target.transform.localPosition = AxisRigOrigin +
                    direction * AxisRigLength;
                target.transform.localScale = Vector3.one * 0.082f;
                Material material = CreateStableOpaqueMaterial(new Color(
                    colors[slot].r, colors[slot].g, colors[slot].b, 1.0f));
                state.slotRenderers[slot] = target.GetComponent<Renderer>();
                state.slotRenderers[slot].material = material;
                CreateWorldLabel(names[slot], AxisRigOrigin +
                    direction * (AxisRigLength + 0.145f), 0.0072f,
                    TextAnchor.MiddleCenter, colors[slot], state.root.transform);
            }
            if (state.timeAxis >= 0 && state.depthAxis >= 0)
            {
#if false // Redundant panel-count caption removed for the compact VR layout.
                int selectedTimes = state.timeRole == DimensionRole.Fixed
                    ? 1 : SelectedBucketCount(selectedTimeBucketMask);
                int selectedDepths = state.depthRole == DimensionRole.Fixed
                    ? 1 : SelectedBucketCount(selectedDepthBucketMask);
                CreateWorldLabel("MATPLOT  " + selectedTimes + " × " +
                    selectedDepths + "   •   " +
                    (selectedTimes * selectedDepths) + " PANELS",
                    new Vector3(0.0f, -0.585f, 0.275f), 0.0042f,
                    TextAnchor.MiddleCenter, Muted, state.root.transform);
#endif
                int valueSlot = state.variableAxis >= 0
                    ? state.variableAxis
                    : RemainingAxis(state.timeAxis, state.depthAxis);
#if false // Variable markers already communicate this axis.
                CreateWorldLabel("VARIABLES", AxisRigOrigin +
                    AxisSlotDirection(valueSlot) * 0.35f +
                    new Vector3(0.0f, 0.07f, 0.0f), 0.0055f,
                    TextAnchor.MiddleCenter, VariableColor,
                    state.root.transform);
#endif
                if (BoundVariableIndices().Count > 0)
                    CreateFacetedVariableAxisMarkers(state, valueSlot);
            }
        }

        private void CreateFacetedVariableAxisMarkers(
            SpatialAxisRigState state, int valueSlot)
        {
            List<int> variables = BoundVariableIndices();
            if (state == null || state.root == null || variables.Count == 0)
                return;
            Vector3 direction = AxisSlotDirection(valueSlot);
            int count = variables.Count;
            for (int index = 0; index < count; index++)
            {
                int variableIndex = variables[index];
                float along = count == 1 ? 0.43f :
                    Mathf.Lerp(0.20f, 0.50f, index / (float)(count - 1));
                GameObject marker = GameObject.CreatePrimitive(
                    PrimitiveType.Sphere);
                marker.name = datasets[variableIndex].Name +
                    " faceted variable marker";
                marker.layer = 5;
                marker.transform.SetParent(state.root.transform, false);
                marker.transform.localPosition = AxisRigOrigin +
                    direction * along;
                marker.transform.localScale = new Vector3(
                    0.105f, 0.072f, 0.060f);
                bool active = selectedDataset == datasets[variableIndex];
                marker.GetComponent<Renderer>().material =
                    CreateStableOpaqueMaterial(active
                        ? new Color(0.78f, 0.25f, 0.95f, 1.0f)
                        : new Color(VariableColor.r * 0.72f,
                            VariableColor.g * 0.72f,
                            VariableColor.b * 0.72f, 1.0f));
                int capturedVariable = variableIndex;
                marker.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    () => LoadDataset(capturedVariable);

                Vector3 labelOffset = valueSlot == 2
                    ? new Vector3(0.15f, 0.0f, 0.0f)
                    : new Vector3(0.0f, 0.095f, 0.0f);
                TextMesh label = CreateWorldLabel(
                    GetFieldDatasetShortLabel(datasets[variableIndex]),
                    marker.transform.localPosition + labelOffset,
                    0.0038f, TextAnchor.MiddleLeft,
                    active ? Ink : VariableColor, state.root.transform);
                label.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
            }
        }

        private GameObject CreateAxisDimensionToken(int variableIndex,
            int dimension, SpatialAxisRigState state, string label, Color color)
        {
            GameObject token = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            token.name = label + " draggable axis token";
            token.layer = 5;
            token.transform.SetParent(state.root.transform, false);
            token.transform.localScale = new Vector3(0.205f, 0.082f, 0.054f);
            Material material = CreateStableOpaqueMaterial(
                new Color(color.r, color.g, color.b, 1.0f));
            token.GetComponent<Renderer>().material = material;
            int capturedVariable = variableIndex;
            int capturedDimension = dimension;
            token.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginAxisTokenDrag(capturedVariable, capturedDimension);
            TextMesh tokenLabel = CreateWorldLabel(label,
                Vector3.back * 0.038f, 0.0052f,
                TextAnchor.MiddleCenter, Ink, token.transform);
            tokenLabel.transform.localScale = new Vector3(4.88f, 12.2f, 18.5f);
            return token;
        }

        private void CreateAxisRoleButtons(int variableIndex, int dimension,
            SpatialAxisRigState state)
        {
            // Time and Depth are authored once in the pre-workspace Field.
            // The tri-axis only maps those saved definitions to axes, so the
            // values shown here are deliberately read-only. Put the summary
            // directly over its token so it reads as part of that button,
            // rather than as unrelated text floating above the controller.
            int axis = dimension == 0 ? state.timeAxis : state.depthAxis;
            if (axis < 0)
                return;
            Vector3 tokenPosition = AxisRigOrigin +
                AxisSlotDirection(axis) * AxisRigLength;
            CreateAxisBoundaryStrip(dimension, state, tokenPosition);
        }

        private void CreateAxisBoundaryStrip(int dimension,
            SpatialAxisRigState state, Vector3 tokenPosition)
        {
            Color accent = dimension == 0 ? TimeColor : DepthColor;
            Color backing = new Color(accent.r * 0.24f,
                accent.g * 0.24f, accent.b * 0.24f, 0.72f);
            Color light = Color.Lerp(accent, Color.white, 0.42f);
            light.a = 0.94f;
            // Lift the selector away from its main TIME/DEPTH token.  The extra
            // gap keeps the three bucket buttons and split labels from reading
            // as one crowded control in the headset.
            Vector3 outward = (tokenPosition - AxisRigOrigin).normalized;
            Vector3 center = tokenPosition + outward * 0.065f +
                new Vector3(0.0f, 0.215f, -0.060f);

            // A short stem visually attaches the range strip to the draggable
            // Time/Depth pill while leaving enough air around the label.
            CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                " range connector", state.root.transform,
                tokenPosition + new Vector3(0.0f, 0.055f, -0.050f),
                center + new Vector3(0.0f, -0.045f, 0.0f),
                new Color(light.r, light.g, light.b, 0.66f), 0.011f);

            bool fixedRole = dimension == 0
                ? state.timeRole == DimensionRole.Fixed
                : state.depthRole == DimensionRole.Fixed;
            if (fixedRole)
            {
                const float fixedHalfWidth = 0.145f;
                CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                    " fixed range backing", state.root.transform,
                    center + Vector3.left * fixedHalfWidth,
                    center + Vector3.right * fixedHalfWidth,
                    backing, 0.070f);
                CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                    " fixed range", state.root.transform,
                    center + Vector3.left * fixedHalfWidth,
                    center + Vector3.right * fixedHalfWidth,
                    light, 0.050f);
                string value = dimension == 0
                    ? (selectedDataset != null
                        ? selectedDataset.GetTimeLabel(Mathf.Clamp(
                            state.usesSharedBoundaries
                                ? sharedSelectedTime : state.customSelectedTime,
                            0, Mathf.Max(0, selectedDataset.TimeCount - 1)))
                        : "DAY")
                    : "Z=" + (state.usesSharedBoundaries
                        ? sharedSelectedZ : state.customSelectedZ);
                CreateWorldLabel(value.ToUpperInvariant(),
                    center + new Vector3(0.0f, 0.072f, -0.004f),
                    0.0034f, TextAnchor.MiddleCenter, light,
                    state.root.transform);
                return;
            }

            S4DIndexBucketRequest[] buckets = dimension == 0
                ? authoredTimeBuckets : authoredDepthBuckets;
            if (buckets == null || buckets.Length != 3)
            {
                EnsureSavedAuthorBoundaries();
                buckets = dimension == 0
                    ? authoredTimeBuckets : authoredDepthBuckets;
            }
            if (buckets == null || buckets.Length != 3)
                return;

            // All three choices get equal, generous hit targets.  Exact source
            // ranges remain in the label rather than shrinking shorter buckets.
            const float halfWidth = 0.270f;
            const float buttonGap = 0.014f;
            const float buttonWidth =
                (halfWidth * 2.0f - buttonGap * 2.0f) / 3.0f;
            CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                " range backing", state.root.transform,
                center + Vector3.left * halfWidth,
                center + Vector3.right * halfWidth,
                backing, 0.078f);
            for (int index = 0; index < 3; index++)
            {
                float left = -halfWidth + index * (buttonWidth + buttonGap);
                float right = left + buttonWidth;
                Color segmentColor = Color.Lerp(accent, Color.white,
                    0.28f + index * 0.13f);
                segmentColor.a = 0.94f;
                CreateAxisBucketToggle(dimension, index, state,
                    center + Vector3.right * ((left + right) * 0.5f),
                    buttonWidth,
                    buckets[index], segmentColor);
                if (index < 2)
                {
                    Vector3 split = center + Vector3.right *
                        (right + buttonGap * 0.5f);
                    CreateWorldLine((dimension == 0 ? "Time" : "Depth") +
                        " range split " + index, state.root.transform,
                        split + Vector3.down * 0.045f,
                        split + Vector3.up * 0.045f,
                        light, 0.010f);
                    int splitValue = buckets[index] != null &&
                        buckets[index].indices != null &&
                        buckets[index].indices.Length > 0
                            ? buckets[index].indices[
                                buckets[index].indices.Length - 1]
                            : 0;
                    if (dimension == 0)
                        splitValue++;
                    CreateWorldLabel(splitValue.ToString(),
                        split + new Vector3(0.0f, 0.086f, -0.004f),
                        0.0032f, TextAnchor.MiddleCenter, light,
                        state.root.transform);
                }
            }
        }

        private void CreateAxisBucketToggle(int dimension, int bucketIndex,
            SpatialAxisRigState state, Vector3 position, float width,
            S4DIndexBucketRequest bucket, Color accent)
        {
            bool selected = dimension == 0
                ? selectedTimeBucketMask[bucketIndex]
                : selectedDepthBucketMask[bucketIndex];
            GameObject facingGroup = new GameObject(
                (dimension == 0 ? "Time " : "Depth ") +
                "bucket facing group " + bucketIndex);
            facingGroup.transform.SetParent(state.root.transform, false);
            facingGroup.transform.localPosition = position;
            facingGroup.transform.localRotation = Quaternion.identity;
            axisBucketFacingGroups.Add(facingGroup.transform);

            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            button.name = (dimension == 0 ? "Time " : "Depth ") +
                "bucket toggle " + bucketIndex;
            button.layer = 5;
            button.transform.SetParent(facingGroup.transform, false);
            button.transform.localPosition = Vector3.zero;
            button.transform.localScale = new Vector3(width, 0.086f, 0.055f);
            Color fill = selected
                ? new Color(accent.r, accent.g, accent.b, 1.0f)
                : new Color(0.045f, 0.085f, 0.105f, 1.0f);
            button.GetComponent<Renderer>().material =
                CreateStableOpaqueMaterial(fill);
            int capturedDimension = dimension;
            int capturedBucket = bucketIndex;
            button.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = () =>
                ToggleAxisBucketSelection(capturedDimension, capturedBucket);

            string semantic = bucket != null &&
                !string.IsNullOrWhiteSpace(bucket.label)
                    ? bucket.label.ToUpperInvariant()
                    : (dimension == 0
                        ? new[] { "BEFORE", "DURING", "AFTER" }[bucketIndex]
                        : new[] { "SURFACE", "MIDDLE", "DEEP" }[bucketIndex]);
            TextMesh label = CreateWorldLabel(semantic,
                new Vector3(0.0f, 0.001f, -0.033f),
                0.0028f, TextAnchor.MiddleCenter,
                selected ? Ink : Muted, facingGroup.transform);
            label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
        }

        private void UpdateAxisBucketFacing()
        {
            if (xrCamera == null)
                return;
            for (int index = axisBucketFacingGroups.Count - 1;
                index >= 0; index--)
            {
                Transform group = axisBucketFacingGroups[index];
                if (group == null)
                {
                    axisBucketFacingGroups.RemoveAt(index);
                    continue;
                }
                Vector3 awayFromViewer = group.position -
                    xrCamera.transform.position;
                if (awayFromViewer.sqrMagnitude < 0.0001f)
                    continue;
                group.rotation = Quaternion.LookRotation(
                    awayFromViewer.normalized, xrCamera.transform.up);
            }
        }

        private static string AxisBucketRangeText(
            S4DIndexBucketRequest bucket, bool oneBased)
        {
            if (bucket == null || bucket.indices == null ||
                bucket.indices.Length == 0)
                return "--";
            int first = bucket.indices[0] + (oneBased ? 1 : 0);
            int last = bucket.indices[bucket.indices.Length - 1] +
                (oneBased ? 1 : 0);
            return first == last ? first.ToString() : first + "–" + last;
        }

        private void ToggleAxisBucketSelection(int dimension, int bucketIndex)
        {
            bool[] mask = dimension == 0
                ? selectedTimeBucketMask : selectedDepthBucketMask;
            if (bucketIndex < 0 || bucketIndex >= mask.Length)
                return;
            int selectedCount = 0;
            for (int index = 0; index < mask.Length; index++)
                if (mask[index])
                    selectedCount++;
            if (mask[bucketIndex] && selectedCount <= 1)
            {
                SetStatus("Keep at least one " +
                    (dimension == 0 ? "Time" : "Depth") +
                    " range selected for MatPlot.");
                return;
            }
            mask[bucketIndex] = !mask[bucketIndex];
            InvalidateSlabConfiguration(
                "MatPlot range selection changed", true);
            RefreshSpatialAxisControllers();
            int timeCount = SelectedBucketCount(selectedTimeBucketMask);
            int depthCount = SelectedBucketCount(selectedDepthBucketMask);
            SetStatus("MATPLOT GRID  " + timeCount + " × " + depthCount +
                "  =  " + (timeCount * depthCount) + " PANELS. " +
                "Open MatPlot Intent to continue.");
        }

        private static int SelectedBucketCount(bool[] mask)
        {
            int count = 0;
            if (mask != null)
                for (int index = 0; index < mask.Length; index++)
                    if (mask[index])
                        count++;
            return Mathf.Max(1, count);
        }

        private void ResetAxisBucketSelection()
        {
            for (int index = 0; index < selectedTimeBucketMask.Length; index++)
                selectedTimeBucketMask[index] = true;
            for (int index = 0; index < selectedDepthBucketMask.Length; index++)
                selectedDepthBucketMask[index] = true;
        }

        private string AxisBoundarySummary(int dimension,
            SpatialAxisRigState state)
        {
            if (dimension == 0)
            {
                if (state != null && state.timeRole == DimensionRole.Fixed)
                {
                    int value = state.usesSharedBoundaries
                        ? sharedSelectedTime : state.customSelectedTime;
                    return selectedDataset != null
                        ? selectedDataset.GetTimeLabel(Mathf.Clamp(value, 0,
                            Mathf.Max(0, selectedDataset.TimeCount - 1)))
                        : "DAY " + (value + 1);
                }
                if (authoredTimeBuckets != null &&
                    authoredTimeBuckets.Length == 3)
                    return AuthoredBucketSummary(authoredTimeBuckets, true)
                        .ToUpperInvariant();
                return TimeRangeSummary().ToUpperInvariant();
            }
            if (state != null && state.depthRole == DimensionRole.Fixed)
            {
                int value = state.usesSharedBoundaries
                    ? sharedSelectedZ : state.customSelectedZ;
                return "Z=" + value;
            }
            if (authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketSummary(authoredDepthBuckets, false)
                    .ToUpperInvariant();
            return DepthRangeSummary().ToUpperInvariant();
        }

        private static int RemainingAxis(int first, int second)
        {
            for (int slot = 0; slot < 3; slot++)
                if (slot != first && slot != second)
                    return slot;
            return 1;
        }

        private void BeginAxisTokenDrag(int variableIndex, int dimension)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count ||
                rayInteractor == null)
                return;
            float now = Time.unscaledTime;
            bool doubleClick = lastAxisTokenClickVariable == variableIndex &&
                lastAxisTokenClickDimension == dimension &&
                now - lastAxisTokenClickTime <= 0.36f;
            lastAxisTokenClickTime = now;
            lastAxisTokenClickVariable = variableIndex;
            lastAxisTokenClickDimension = dimension;
            if (doubleClick)
            {
                UnbindAxisToken(variableIndex, dimension);
                return;
            }

            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            draggedAxisToken = dimension == 0 ? state.timeToken : state.depthToken;
            if (draggedAxisToken == null)
                return;
            draggedAxisVariable = variableIndex;
            draggedAxisDimension = dimension;
            draggedAxisUsesDesktopPointer = false;
#if UNITY_EDITOR || SLABLAB_FLAT
            draggedAxisUsesDesktopPointer =
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled;
#endif
            bool pointerHeld = draggedAxisUsesDesktopPointer
                ? FlatPointerHeld
                : rayInteractor.TriggerHeld;
            draggedAxisSawTriggerHeld = pointerHeld;
            draggedAxisRestScale = draggedAxisToken.transform.localScale;
            Ray pointerRay = AxisDragPointerRay();
            draggedAxisDistance = Mathf.Clamp(Vector3.Distance(
                pointerRay.origin, draggedAxisToken.transform.position),
                0.35f, 2.4f);
            SetStatus("Drag " + (dimension == 0 ? "Time" : "Depth") +
                " to a glowing axis slot; release Trigger to bind. Double-click to unbind.");
        }

        private void UpdateAxisTokenInteraction()
        {
            if (draggedAxisToken == null || rayInteractor == null ||
                draggedAxisVariable < 0 ||
                draggedAxisVariable >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[draggedAxisVariable];
            Ray pointerRay = AxisDragPointerRay();
            bool pointerHeld = rayInteractor.TriggerHeld;
            bool pointerReleased = rayInteractor.TriggerReleased;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (draggedAxisUsesDesktopPointer)
            {
                pointerHeld = FlatPointerHeld;
                pointerReleased = FlatPointerReleased;
            }
#endif
            if (pointerHeld)
            {
                draggedAxisSawTriggerHeld = true;
                Vector3 target = pointerRay.origin +
                    pointerRay.direction * draggedAxisDistance;
                int rayNearest = NearestAxisSlotFromRay(state,
                    pointerRay, out float rayDistance,
                    out Vector3 raySnapPoint);
                if (rayDistance < 0.19f)
                    target = Vector3.Lerp(target, raySnapPoint, 0.84f);
                float follow = 1.0f - Mathf.Exp(-28.0f * Time.unscaledDeltaTime);
                draggedAxisToken.transform.position = Vector3.Lerp(
                    draggedAxisToken.transform.position, target, follow);
                draggedAxisToken.transform.localScale = Vector3.Lerp(
                    draggedAxisToken.transform.localScale,
                    draggedAxisRestScale * 1.22f,
                    1.0f - Mathf.Exp(-16.0f * Time.unscaledDeltaTime));
                for (int slot = 0; slot < state.slotRenderers.Length; slot++)
                {
                    Renderer renderer = state.slotRenderers[slot];
                    if (renderer == null)
                        continue;
                    Color color = slot == rayNearest && rayDistance < 0.19f
                        ? Green : new Color(0.18f, 0.52f, 0.66f, 0.50f);
                    renderer.material.color = color;
                }
            }
            bool released = pointerReleased ||
                (draggedAxisSawTriggerHeld && !pointerHeld);
            if (!released)
                return;

            int selectedSlot = NearestAxisSlotFromRay(state,
                pointerRay, out float selectedDistance,
                out Vector3 ignoredSnapPoint);
            if (selectedDistance <= 0.19f)
                BindAxisToken(draggedAxisVariable, draggedAxisDimension,
                    selectedSlot);
            else
            {
                draggedAxisToken.transform.localScale = draggedAxisRestScale;
                UpdateAxisRigTokenPositions(draggedAxisVariable, true);
            }
            ClearAxisDragHighlight(state);
            draggedAxisToken = null;
            draggedAxisVariable = -1;
            draggedAxisDimension = -1;
            draggedAxisSawTriggerHeld = false;
            draggedAxisUsesDesktopPointer = false;
        }

        private Ray AxisDragPointerRay()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            if (draggedAxisUsesDesktopPointer && xrCamera != null)
                return xrCamera.ScreenPointToRay(FlatPointerPosition);
#endif
            return rayInteractor != null
                ? rayInteractor.PointerRay
                : new Ray(transform.position, transform.forward);
        }

        private int NearestAxisSlotFromRay(SpatialAxisRigState state,
            Ray ray, out float distance, out Vector3 snapPoint)
        {
            int nearest = 0;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            Vector3 direction = ray.direction.normalized;
            // TIME, DEPTH and VARIABLE are symmetric draggable components.
            // Whichever component the user is holding may target any axis;
            // occupied components swap back to the free/source position.
            int slotCount = 3;
            for (int slot = 0; slot < slotCount; slot++)
            {
                Vector3 point = state.root.transform.TransformPoint(
                    AxisRigOrigin + AxisSlotDirection(slot) * AxisRigLength);
                float along = Mathf.Max(0.0f,
                    Vector3.Dot(point - ray.origin, direction));
                Vector3 closest = ray.origin + direction * along;
                float next = Vector3.Distance(point, closest);
                if (next < distance)
                {
                    distance = next;
                    nearest = slot;
                    snapPoint = point;
                }
            }
            return nearest;
        }

        private void BeginPaletteComponentDrag(GameObject token,
            PaletteComponentKind kind, int variableIndex)
        {
            if (token == null || rayInteractor == null)
                return;
            CancelStaleAxisDragForPalette();
            CancelStalePaletteDrag();
            draggedPaletteKind = kind;
            draggedPaletteVariable = variableIndex;
            draggedPaletteUsesDesktopPointer = false;
#if UNITY_EDITOR || SLABLAB_FLAT
            draggedPaletteUsesDesktopPointer =
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled;
#endif
            bool pointerHeld = draggedPaletteUsesDesktopPointer
                ? FlatPointerHeld
                : rayInteractor.TriggerHeld;
            draggedPaletteSawTriggerHeld = pointerHeld;
            draggedPaletteStartTime = Time.unscaledTime;
            // Detach every palette component while it is held. The palette
            // itself follows the viewer and is scaled as a panel; leaving TIME
            // or DEPTH parented to it made that parent update cancel most of a
            // desktop mouse drag. Variables already used this detached path,
            // which is why they remained draggable while TIME appeared stuck.
            draggedPaletteSourceToken = token;
            draggedPaletteOriginalParent = token.transform.parent;
            draggedPaletteOriginalLocalPosition = token.transform.localPosition;
            draggedPaletteOriginalLocalRotation = token.transform.localRotation;
            draggedPaletteOriginalLocalScale = token.transform.localScale;
            token.transform.SetParent(null, true);
            draggedPaletteToken = token;
            draggedPaletteRestScale = draggedPaletteToken.transform.localScale;
            Ray pointerRay = PaletteDragPointerRay();
            draggedPaletteDistance = Mathf.Clamp(Vector3.Distance(
                pointerRay.origin,
                draggedPaletteToken.transform.position),
                0.35f, 2.6f);
            draggedPaletteStartRayPoint = pointerRay.origin +
                pointerRay.direction * draggedPaletteDistance;
            SetStatus(kind == PaletteComponentKind.Variable
                ? "Drag the variable onto a translucent outer frame."
                : kind == PaletteComponentKind.VariableAxis
                    ? "Drag VARIABLE onto an axis. Its variable selector will open after it snaps."
                : "Drag " + kind.ToString().ToUpperInvariant() +
                    " onto X, Y, or downward Z; the endpoint will glow and snap.");
        }

        private void UpdatePaletteComponentInteraction()
        {
            if (draggedPaletteToken == null || rayInteractor == null)
                return;
            float dragAge = Time.unscaledTime - draggedPaletteStartTime;
            Ray pointerRay = PaletteDragPointerRay();
            bool pointerHeld = rayInteractor.TriggerHeld;
            bool pointerReleased = rayInteractor.TriggerReleased;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (draggedPaletteUsesDesktopPointer)
            {
                pointerHeld = FlatPointerHeld;
                pointerReleased = FlatPointerReleased;
            }
#endif
            // The interactor and workbench can update in either order. Keep a
            // short pickup grace window so the first moving frame is never lost
            // while TriggerHeld is being handed over.
            bool shouldFollow = pointerHeld || dragAge <= 0.14f;
            if (shouldFollow)
            {
                if (pointerHeld)
                    draggedPaletteSawTriggerHeld = true;
                Vector3 target = pointerRay.origin +
                    pointerRay.direction * draggedPaletteDistance;
                if (draggedPaletteKind == PaletteComponentKind.Variable)
                {
                    int rigIndex = NearestRigFrameFromRay(pointerRay,
                        out float distance, out Vector3 snap);
                    HighlightVariableFrame(rigIndex, distance < 0.34f);
                    if (rigIndex >= 0 && distance < 0.34f)
                        // Keep the card under the user's ray.  A strong snap here
                        // put it directly behind the DROP VARIABLE header and
                        // made the drag appear to vanish.
                        target = Vector3.Lerp(target, snap, 0.16f);
                }
                else
                {
                    FindNearestRigSlotFromRay(pointerRay,
                        out int rigIndex, out int slot, out float distance,
                        out Vector3 snap);
                    HighlightSpatialDropSlot(rigIndex, slot, distance < 0.19f);
                    if (rigIndex >= 0 && distance < 0.19f)
                        target = Vector3.Lerp(target, snap, 0.86f);
                }
                // Match the TIME/DEPTH token's responsive pickup and scale
                // animation so all three component types feel identical.
                float follow = 1.0f - Mathf.Exp(-20.0f * Time.unscaledDeltaTime);
                draggedPaletteToken.transform.position = Vector3.Lerp(
                    draggedPaletteToken.transform.position, target, follow);
                if (draggedPaletteKind == PaletteComponentKind.Variable &&
                    xrCamera != null)
                {
                    Vector3 facing = draggedPaletteToken.transform.position -
                        xrCamera.transform.position;
                    Vector3 uprightFacing = Vector3.ProjectOnPlane(
                        facing, Vector3.up);
                    if (uprightFacing.sqrMagnitude > 0.001f)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(
                            -uprightFacing.normalized, Vector3.up);
                        draggedPaletteToken.transform.rotation =
                            Quaternion.Slerp(
                                draggedPaletteToken.transform.rotation,
                                targetRotation,
                                1.0f - Mathf.Exp(-16.0f *
                                    Time.unscaledDeltaTime));
                    }
                }
                if (draggedPaletteKind == PaletteComponentKind.Variable)
                {
                    // The source palette card and moving physical card now keep
                    // the same apparent size. TIME/DEPTH can use their existing
                    // lift animation; variables must not jump larger and cover
                    // their own caption at pickup.
                    draggedPaletteToken.transform.localScale = Vector3.Lerp(
                        draggedPaletteToken.transform.localScale,
                        draggedPaletteRestScale * 1.04f,
                        1.0f - Mathf.Exp(-14.0f * Time.unscaledDeltaTime));
                }
                else
                {
                    Vector3 liftedScale = draggedPaletteRestScale * 1.22f;
                    draggedPaletteToken.transform.localScale = Vector3.Lerp(
                        draggedPaletteToken.transform.localScale, liftedScale,
                        1.0f - Mathf.Exp(-16.0f * Time.unscaledDeltaTime));
                }
            }
            // TriggerReleased is only true for one Update on the interactor.
            // Script execution order can otherwise make the workbench miss it,
            // leaving a drag permanently stuck.  Once this drag has observed a
            // held trigger, a not-held state is an unambiguous release.
            bool released = pointerReleased ||
                (draggedPaletteSawTriggerHeld && !pointerHeld) ||
                (!draggedPaletteSawTriggerHeld && !pointerHeld &&
                    dragAge > 0.32f);
            if (!released)
                return;

            if (draggedPaletteKind == PaletteComponentKind.Variable)
            {
                int rigIndex = NearestRigFrameFromRay(pointerRay,
                    out float distance, out Vector3 ignored);
                Vector3 currentRayPoint = pointerRay.origin +
                    pointerRay.direction * draggedPaletteDistance;
                float travel = Vector3.Distance(draggedPaletteStartRayPoint,
                    currentRayPoint);
                // The palette sits beside the rig. Without an intentional-travel
                // guard, a plain click can already be within the generous outer
                // frame threshold and looks like the button simply disappears.
                bool placed = travel >= 0.055f && rigIndex >= 0 &&
                    distance <= 0.34f;
                if (placed)
                    BindVariableToAxisRig(rigIndex, draggedPaletteVariable);
                else
                    SetStatus("Variable not placed. Drag the labelled card onto " +
                        "a translucent cube frame, then release.");
                // A miss changes no state. Rebuilding the entire composer and
                // palette here caused the full right-hand panel to flash.
            }
            else
            {
                FindNearestRigSlotFromRay(pointerRay,
                    out int rigIndex, out int slot, out float distance,
                    out Vector3 ignored);
                if (rigIndex >= 0 && distance <= 0.19f)
                {
                    if (draggedPaletteKind == PaletteComponentKind.VariableAxis)
                        BindVariableSelectorToAxis(slot);
                    else
                        BindAxisToken(rigIndex,
                            draggedPaletteKind == PaletteComponentKind.Time
                                ? 0 : 1, slot);
                }
                else
                    RefreshSpatialAxisControllers();
            }
            ClearAllSpatialDropHighlights();
            HighlightVariableFrame(-1, false);
            RestoreDraggedPaletteSource();
            draggedPaletteToken = null;
            draggedPaletteKind = PaletteComponentKind.None;
            draggedPaletteVariable = -1;
            draggedPaletteSawTriggerHeld = false;
            draggedPaletteUsesDesktopPointer = false;
            draggedPaletteStartTime = 0.0f;
            draggedPaletteStartRayPoint = Vector3.zero;
        }

        private Ray PaletteDragPointerRay()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            if (draggedPaletteUsesDesktopPointer && xrCamera != null)
                return xrCamera.ScreenPointToRay(FlatPointerPosition);
#endif
            return rayInteractor != null
                ? rayInteractor.PointerRay
                : new Ray(transform.position, transform.forward);
        }

        private void CancelStalePaletteDrag()
        {
            if (draggedPaletteToken == null)
                return;
            RestoreDraggedPaletteSource();
            ClearAllSpatialDropHighlights();
            HighlightVariableFrame(-1, false);
            draggedPaletteToken = null;
            draggedPaletteKind = PaletteComponentKind.None;
            draggedPaletteVariable = -1;
            draggedPaletteSawTriggerHeld = false;
            draggedPaletteUsesDesktopPointer = false;
            draggedPaletteStartTime = 0.0f;
            draggedPaletteStartRayPoint = Vector3.zero;
        }

        private void CancelStaleAxisDragForPalette()
        {
            if (draggedAxisToken == null)
                return;
            if (draggedAxisVariable >= 0 &&
                draggedAxisVariable < spatialAxisRigStates.Count)
            {
                SpatialAxisRigState state =
                    spatialAxisRigStates[draggedAxisVariable];
                draggedAxisToken.transform.localScale = draggedAxisRestScale;
                UpdateAxisRigTokenPositions(draggedAxisVariable, true);
                ClearAxisDragHighlight(state);
            }
            draggedAxisToken = null;
            draggedAxisVariable = -1;
            draggedAxisDimension = -1;
            draggedAxisSawTriggerHeld = false;
            draggedAxisUsesDesktopPointer = false;
        }

        private void RestoreDraggedPaletteSource()
        {
            if (draggedPaletteSourceToken == null)
                return;
            if (draggedPaletteOriginalParent != null)
            {
                draggedPaletteSourceToken.transform.SetParent(
                    draggedPaletteOriginalParent, false);
                draggedPaletteSourceToken.transform.localPosition =
                    draggedPaletteOriginalLocalPosition;
                draggedPaletteSourceToken.transform.localRotation =
                    draggedPaletteOriginalLocalRotation;
                draggedPaletteSourceToken.transform.localScale =
                    draggedPaletteOriginalLocalScale;
            }
            Collider sourceCollider =
                draggedPaletteSourceToken.GetComponent<Collider>();
            if (sourceCollider != null)
                sourceCollider.enabled = true;
            draggedPaletteSourceToken = null;
            draggedPaletteOriginalParent = null;
            draggedPaletteOriginalLocalPosition = Vector3.zero;
            draggedPaletteOriginalLocalRotation = Quaternion.identity;
            draggedPaletteOriginalLocalScale = Vector3.one;
            UpdateVariablePaletteTokenVisibility();
        }

        private void UpdateVariablePaletteTokenVisibility()
        {
            foreach (KeyValuePair<int, GameObject> pair in variablePaletteTokens)
            {
                if (pair.Value == null)
                    continue;
                bool bound = spatialAxisRigStates.Exists(state =>
                    state.boundVariable == pair.Key);
                // Choices are toggles, not consumable drag cards. A selected
                // variable stays visible so a second click can deselect it.
                pair.Value.SetActive(true);
                Renderer renderer = pair.Value.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = bound
                        ? new Color(0.70f, 0.20f, 0.88f, 1.0f)
                        : new Color(0.095f, 0.115f, 0.145f, 1.0f);
                pair.Value.transform.localScale = bound
                    ? new Vector3(0.282f, 0.080f, 0.050f)
                    : new Vector3(0.270f, 0.076f, 0.046f);
                if (variablePaletteLabels.TryGetValue(pair.Key,
                    out TextMesh label) && label != null)
                {
                    label.text = (bound ? "[X] " : "[ ] ") +
                        datasets[pair.Key].Name.ToUpperInvariant();
                    label.color = bound ? Ink : Muted;
                    label.fontStyle = bound ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private void ReassertVariablePaletteSelectionVisuals()
        {
            // Direct-interaction hover effects are allowed to add outlines, but
            // they must never erase the persistent selected/unselected state.
            if (variablePaletteRoot == null || variablePaletteTokens.Count == 0)
                return;
            foreach (KeyValuePair<int, GameObject> pair in variablePaletteTokens)
            {
                if (pair.Value == null || !pair.Value.activeInHierarchy)
                    continue;
                bool selected = spatialAxisRigStates.Exists(state =>
                    state != null && state.boundVariable == pair.Key);
                Renderer renderer = pair.Value.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (variableSelectionBlock == null)
                        variableSelectionBlock = new MaterialPropertyBlock();
                    variableSelectionBlock.Clear();
                    Color color = selected
                        ? new Color(0.72f, 0.18f, 0.92f, 1.0f)
                        : new Color(0.075f, 0.090f, 0.120f, 1.0f);
                    variableSelectionBlock.SetColor("_Color", color);
                    variableSelectionBlock.SetColor("_BaseColor", color);
                    renderer.SetPropertyBlock(variableSelectionBlock);
                }
                if (variablePaletteLabels.TryGetValue(pair.Key,
                    out TextMesh label) && label != null)
                {
                    string variableName = pair.Key >= 0 && pair.Key < datasets.Count &&
                        datasets[pair.Key] != null &&
                        !string.IsNullOrWhiteSpace(datasets[pair.Key].Name)
                            ? datasets[pair.Key].Name
                            : "VARIABLE " + (pair.Key + 1);
                    label.text = (selected ? "[X] " : "[ ] ") +
                        variableName.ToUpperInvariant();
                    label.color = selected ? Color.white : Muted;
                    label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                }
            }
        }

        private static Material CreateStableOpaqueMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            Material material = new Material(shader);
            color.a = 1.0f;
            material.color = color;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return material;
        }

        private void FindNearestRigSlotFromRay(Ray ray, out int rigIndex,
            out int slot, out float distance, out Vector3 snapPoint)
        {
            rigIndex = -1;
            slot = 0;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                if (state.root == null)
                    continue;
                int nextSlot = NearestAxisSlotFromRay(state, ray,
                    out float nextDistance, out Vector3 nextPoint);
                if (nextDistance < distance)
                {
                    rigIndex = index;
                    slot = nextSlot;
                    distance = nextDistance;
                    snapPoint = nextPoint;
                }
            }
        }

        private int NearestRigFrameFromRay(Ray ray, out float distance,
            out Vector3 snapPoint)
        {
            int nearest = -1;
            distance = float.MaxValue;
            snapPoint = ray.origin;
            Vector3 direction = ray.direction.normalized;
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                Transform root = spatialAxisRigStates[index].root != null
                    ? spatialAxisRigStates[index].root.transform : null;
                if (root == null)
                    continue;
                Ray localRay = new Ray(root.InverseTransformPoint(ray.origin),
                    root.InverseTransformDirection(ray.direction).normalized);
                Bounds dropBounds = new Bounds(Vector3.zero,
                    Vector3.one * 1.06f);
                if (dropBounds.IntersectRay(localRay, out float enter))
                {
                    nearest = index;
                    distance = 0.0f;
                    snapPoint = root.TransformPoint(
                        new Vector3(0.0f, 0.50f, 0.0f));
                    break;
                }
                Vector3 point = root.TransformPoint(new Vector3(0.0f, 0.12f, 0.0f));
                float along = Mathf.Max(0.0f,
                    Vector3.Dot(point - ray.origin, direction));
                float next = Vector3.Distance(point,
                    ray.origin + direction * along);
                if (next < distance)
                {
                    nearest = index;
                    distance = next;
                    snapPoint = point;
                }
            }
            return nearest;
        }

        private void HighlightVariableFrame(int rigIndex, bool active)
        {
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                state.frameRequestedVisible = active && index == rigIndex;
            }
        }

        private void UpdateAxisRigHoverVisuals()
        {
            int hoverRig = -1;
            bool hoverActive = false;
            if (rayInteractor != null && spatialAxisRigStates.Count > 0)
            {
                hoverRig = NearestRigFrameFromRay(rayInteractor.PointerRay,
                    out float distance, out Vector3 ignored);
                hoverActive = hoverRig >= 0 && distance <= 0.14f;
            }
            HighlightVariableFrame(hoverRig, hoverActive);

            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                float target = state.frameRequestedVisible ? 1.0f : 0.0f;
                float speed = state.frameRequestedVisible ? 8.0f : 4.5f;
                state.frameVisibility = Mathf.MoveTowards(
                    state.frameVisibility, target,
                    Time.unscaledDeltaTime * speed);
                for (int edge = 0; edge < state.frameRenderers.Count; edge++)
                {
                    Renderer renderer = state.frameRenderers[edge];
                    if (renderer == null)
                        continue;
                    renderer.enabled = state.frameVisibility > 0.015f;
                    if (!renderer.enabled)
                        continue;
                    Color baseColor = state.frameColors[Mathf.Min(edge,
                        state.frameColors.Count - 1)];
                    renderer.material.color = new Color(baseColor.r,
                        baseColor.g, baseColor.b,
                        baseColor.a * state.frameVisibility);
                }
            }
        }

        private void HighlightSpatialDropSlot(int rigIndex, int slot,
            bool active)
        {
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                for (int axis = 0; axis < state.slotRenderers.Length; axis++)
                    if (state.slotRenderers[axis] != null)
                        state.slotRenderers[axis].material.color =
                            active && index == rigIndex && axis == slot
                            ? new Color(Green.r, Green.g, Green.b, 1.0f)
                            : new Color(0.18f, 0.52f, 0.66f, 1.0f);
            }
        }

        private void ClearAllSpatialDropHighlights()
        {
            HighlightSpatialDropSlot(-1, -1, false);
        }

        private void SetSpatialVariableRole(DimensionRole role)
        {
            if (role != DimensionRole.Fixed && role != DimensionRole.Faceted)
                return;
            if (roles[3] == role)
                return;
            roles[3] = role;
            if (role == DimensionRole.Faceted &&
                spatialAxisRigStates.Count > 0)
            {
                // Faceted variables are categorical Z positions. Reserve Z
                // for those variable nodes and normalize Time/Depth to X/Y.
                SpatialAxisRigState shared = spatialAxisRigStates[0];
                shared.timeAxis = 0;
                shared.depthAxis = 1;
                for (int index = 1; index < spatialAxisRigStates.Count; index++)
                {
                    spatialAxisRigStates[index].timeAxis = 0;
                    spatialAxisRigStates[index].depthAxis = 1;
                }
            }
            if (role == DimensionRole.Fixed && spatialAxisRigStates.Count > 0)
            {
                int selectedIndex = selectedDataset != null
                    ? datasets.IndexOf(selectedDataset) : -1;
                int sourceIndex = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable == selectedIndex);
                if (sourceIndex < 0)
                    sourceIndex = spatialAxisRigStates.FindIndex(state =>
                        state.boundVariable >= 0);
                SpatialAxisRigState target = spatialAxisRigStates[0];
                if (sourceIndex > 0)
                {
                    SpatialAxisRigState source = spatialAxisRigStates[sourceIndex];
                    target.boundVariable = source.boundVariable;
                    target.timeAxis = source.timeAxis;
                    target.depthAxis = source.depthAxis;
                    target.timeRole = source.timeRole;
                    target.depthRole = source.depthRole;
                }
                for (int index = 1; index < spatialAxisRigStates.Count; index++)
                    spatialAxisRigStates[index].boundVariable = -1;
            }
            RefreshVariableFacetStacks();
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            UpdateAnalysisAxisLabels();
            InvalidateSlabConfiguration("Variable role changed");
            SetStatus(role == DimensionRole.Fixed
                ? "Variable FIXED: one variable controls one STC field."
                : "Variable FACETED: bind multiple variables to the shared axis controller; each receives an upright Field.");
        }

        private void BindVariableToAxisRig(int rigIndex, int variableIndex)
        {
            if (spatialAxisRigStates.Count == 0 ||
                variableIndex < 0 || variableIndex >= datasets.Count)
                return;
            rigIndex = 0;
            int existing = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variableIndex);
            if (existing < 0)
            {
                int empty = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable < 0);
                if (empty < 0)
                {
                    SetStatus("All detected variables are already bound.");
                    RestoreDraggedPaletteSource();
                    return;
                }
                SpatialAxisRigState shared = spatialAxisRigStates[0];
                SpatialAxisRigState target = spatialAxisRigStates[empty];
                target.boundVariable = variableIndex;
                target.timeAxis = shared.timeAxis;
                target.depthAxis = shared.depthAxis;
                target.timeRole = shared.timeRole;
                target.depthRole = shared.depthRole;
            }
            UpdateAutomaticVariableRole();
            spatialAxisRigStates[0].pendingDockAnimation = true;
            // The first detected variable is commonly already being prepared
            // when the user drops its card. Do not silently discard that drop
            // or destroy/recreate the same view: make the existing STC visible
            // and let its texture-ready coroutine finish normally.
            if (currentView != null && selectedDataset == datasets[variableIndex])
            {
                currentView.SetVisible(cubeVisible);
                RefreshVariableFacetStacks();
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            else
            {
                LoadDataset(variableIndex);
            }
            InvalidateSlabConfiguration("Variable binding changed", true);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            SetStatus(datasets[variableIndex].Name +
                " bound to the shared axis controller" +
                (roles[3] == DimensionRole.Fixed
                    ? ". One variable means FIXED."
                    : ". Multiple variables mean FACETED."));
        }

        private void ToggleAxisVariableSelection(int variableIndex)
        {
            if (variableIndex < 0 || variableIndex >= datasets.Count ||
                spatialAxisRigStates.Count == 0)
                return;
            int existing = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variableIndex);
            if (existing < 0)
            {
                BindVariableToAxisRig(0, variableIndex);
                return;
            }

            string variableName = datasets[variableIndex].Name;
            spatialAxisRigStates[existing].boundVariable = -1;
            UpdateAutomaticVariableRole();
            List<int> remaining = BoundVariableIndices();
            if (selectedDataset == datasets[variableIndex] &&
                remaining.Count > 0)
                LoadDataset(remaining[0]);
            else
            {
                RefreshVariableFacetStacks();
                RefreshSpatialAxisControllers();
            }
            InvalidateSlabConfiguration("Variable selection changed", true);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteTokenVisibility();
            SetStatus(variableName + " deselected. " + remaining.Count +
                " variable" + (remaining.Count == 1 ? string.Empty : "s") +
                " will be used by MatPlot.");
        }

        private void UpdateAutomaticVariableRole()
        {
            List<int> bound = BoundVariableIndices();
            roles[3] = bound.Count > 1
                ? DimensionRole.Faceted : DimensionRole.Fixed;
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState shared = spatialAxisRigStates[0];
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = shared.timeAxis;
                spatialAxisRigStates[index].depthAxis = shared.depthAxis;
                spatialAxisRigStates[index].variableAxis = shared.variableAxis;
                spatialAxisRigStates[index].timeRole = shared.timeRole;
                spatialAxisRigStates[index].depthRole = shared.depthRole;
            }
        }

        private int NearestAxisSlot(SpatialAxisRigState state,
            Vector3 worldPosition, out float distance)
        {
            int nearest = 0;
            distance = float.MaxValue;
            for (int slot = 0; slot < 3; slot++)
            {
                Vector3 point = state.root.transform.TransformPoint(
                    AxisRigOrigin + AxisSlotDirection(slot) * AxisRigLength);
                float next = Vector3.Distance(point, worldPosition);
                if (next < distance)
                {
                    distance = next;
                    nearest = slot;
                }
            }
            return nearest;
        }

        private void ClearAxisDragHighlight(SpatialAxisRigState state)
        {
            for (int slot = 0; slot < state.slotRenderers.Length; slot++)
                if (state.slotRenderers[slot] != null)
                    state.slotRenderers[slot].material.color =
                        new Color(0.18f, 0.52f, 0.66f, 0.50f);
        }

        private void BindAxisToken(int variableIndex, int dimension, int slot)
        {
            variableIndex = 0;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            slot = Mathf.Clamp(slot, 0, 2);
            bool variableDisplaced = slot == state.variableAxis;
            if (dimension == 0)
            {
                if (slot == state.variableAxis)
                    state.variableAxis = -1;
                if (slot == state.depthAxis)
                    state.depthAxis = state.timeAxis;
                state.timeAxis = slot;
            }
            else
            {
                if (slot == state.variableAxis)
                    state.variableAxis = -1;
                if (slot == state.timeAxis)
                    state.timeAxis = state.depthAxis;
                state.depthAxis = slot;
            }
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = state.timeAxis;
                spatialAxisRigStates[index].depthAxis = state.depthAxis;
                spatialAxisRigStates[index].variableAxis = state.variableAxis;
            }
            UpdateAxisRigTokenPositions(variableIndex, true);
            if (state.boundVariable >= 0 &&
                state.boundVariable < datasets.Count &&
                datasets[state.boundVariable] == selectedDataset)
                ApplySelectedAxisRigState(variableIndex, true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            InvalidateSlabConfiguration("Axis binding changed", true);
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " bound to " + new[] { "X", "Y", "Z" }[slot] +
                (variableDisplaced
                    ? ". Variable returned to its lower dock."
                    : ". VALUE moved to the remaining axis."));
        }

        private void BindVariableSelectorToAxis(int slot)
        {
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            slot = Mathf.Clamp(slot, 0, 2);
            int previous = state.variableAxis;
            if (slot == state.timeAxis)
                state.timeAxis = previous;
            if (slot == state.depthAxis)
                state.depthAxis = previous;
            state.variableAxis = slot;
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeAxis = state.timeAxis;
                spatialAxisRigStates[index].depthAxis = state.depthAxis;
                spatialAxisRigStates[index].variableAxis = slot;
            }
            InvalidateSlabConfiguration("Variable axis binding changed", true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            SetStatus("VARIABLE bound to " + new[] { "X", "Y", "Z" }[slot] +
                ". Select one or more variables in the panel emitted from the purple button.");
        }

        private IEnumerator RefreshAxisControllersAfterSnap()
        {
            yield return new WaitForSecondsRealtime(0.27f);
            RefreshSpatialAxisControllers();
            // Rebuild the paired low-resolution STC views only after the token
            // has completed its snap animation. Disk reads during the drag/drop
            // frame made the controller feel as if it stalled on release.
            RefreshVariableFacetStacks();
        }

        private void UnbindAxisToken(int variableIndex, int dimension)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            if (dimension == 0)
                state.timeAxis = -1;
            else
                state.depthAxis = -1;
            UpdateAxisRigTokenPositions(variableIndex, true);
            StartCoroutine(RefreshAxisControllersAfterSnap());
            InvalidateSlabConfiguration("Axis binding removed", true);
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " unbound. Drag it onto an available axis to continue.");
        }

        private void UpdateAxisRigTokenPositions(int variableIndex, bool animate)
        {
            if (variableIndex < 0 || variableIndex >= spatialAxisRigStates.Count)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[variableIndex];
            Vector3 timePosition = state.timeAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.timeAxis) * AxisRigLength
                : UnboundTimeTokenPosition;
            Vector3 depthPosition = state.depthAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.depthAxis) * AxisRigLength
                : UnboundDepthTokenPosition;
            Vector3 variablePosition = state.variableAxis >= 0
                ? AxisRigOrigin + AxisSlotDirection(state.variableAxis) *
                    AxisRigLength
                : UnboundVariableTokenPosition;
            if (state.timeToken != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(state.timeToken.transform,
                        timePosition));
                else
                    state.timeToken.transform.localPosition = timePosition;
            }
            if (state.depthToken != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(state.depthToken.transform,
                        depthPosition));
                else
                    state.depthToken.transform.localPosition = depthPosition;
            }
            if (variableIndex == 0 && variablePaletteRoot != null)
            {
                if (animate)
                    StartCoroutine(AnimateLocalMove(
                        variablePaletteRoot.transform, variablePosition));
                else
                    variablePaletteRoot.transform.localPosition =
                        variablePosition;
            }
        }

        private IEnumerator AnimateLocalMove(Transform target, Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.localPosition;
            float elapsed = 0.0f;
            const float duration = 0.24f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                target.localPosition = Vector3.Lerp(start, destination, t);
                yield return null;
            }
            if (target != null)
                target.localPosition = destination;
        }

        private IEnumerator AnimateLocalScale(Transform target,
            Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.localScale;
            float elapsed = 0.0f;
            const float duration = 0.22f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float eased = 1.0f - Mathf.Pow(1.0f - normalized, 3.0f);
                float settle = Mathf.Sin(normalized * Mathf.PI) *
                    (1.0f - normalized) * 0.08f;
                target.localScale = Vector3.LerpUnclamped(start,
                    destination, eased + settle);
                yield return null;
            }
            if (target != null)
                target.localScale = destination;
        }

        private IEnumerator AnimateWorldMove(Transform target,
            Vector3 destination)
        {
            if (target == null)
                yield break;
            Vector3 start = target.position;
            float elapsed = 0.0f;
            const float duration = 0.30f;
            while (target != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                // Ease-out with a very small overshoot gives the side dock a
                // magnetic catch rather than a mechanical linear slide.
                float eased = 1.0f - Mathf.Pow(1.0f - normalized, 3.0f);
                float overshoot = Mathf.Sin(normalized * Mathf.PI) *
                    (1.0f - normalized) * 0.08f;
                target.position = Vector3.LerpUnclamped(start, destination,
                    eased + overshoot);
                yield return null;
            }
            if (target != null)
                target.position = destination;
        }

        private void SetSpatialAxisRole(int variableIndex, int dimension,
            DimensionRole role)
        {
            if (mainWorkspaceEntered)
            {
                SetStatus("Time and Depth were saved in Field Setup and are read-only in the tri-axis workspace.");
                return;
            }
            if (spatialAxisRigStates.Count == 0)
                return;
            variableIndex = 0;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            if (dimension == 0)
                state.timeRole = role;
            else
                state.depthRole = role;
            for (int index = 1; index < spatialAxisRigStates.Count; index++)
            {
                spatialAxisRigStates[index].timeRole = state.timeRole;
                spatialAxisRigStates[index].depthRole = state.depthRole;
            }
            if (state.boundVariable >= 0 &&
                state.boundVariable < datasets.Count &&
                datasets[state.boundVariable] == selectedDataset)
                ApplySelectedAxisRigState(variableIndex, false);
            RefreshVariableFacetStacks();
            InvalidateSlabConfiguration((dimension == 0 ? "Time" : "Depth") +
                " role changed");
            RefreshSpatialAxisControllers();
            SetStatus((dimension == 0 ? "Time" : "Depth") + " is now " +
                RoleLabel(role) + " for " +
                (state.boundVariable >= 0 && state.boundVariable < datasets.Count
                    ? datasets[state.boundVariable].Name : "this axis body") + ".");
        }

        private void SelectAxisRigVariable(int rigIndex)
        {
            List<int> boundVariables = BoundVariableIndices();
            if (boundVariables.Count == 0)
            {
                SetStatus("The shared axis controller is empty. Drag a variable onto it.");
                return;
            }
            rigIndex = 0;
            int selectedIndex = selectedDataset != null
                ? datasets.IndexOf(selectedDataset) : -1;
            int activeOffset = boundVariables.IndexOf(selectedIndex);
            int targetVariable = boundVariables[(activeOffset + 1) %
                boundVariables.Count];
            float now = Time.unscaledTime;
            bool doubleClick = lastVariableShellClickRig == rigIndex &&
                now - lastVariableShellClickTime <= 0.36f;
            lastVariableShellClickRig = rigIndex;
            lastVariableShellClickTime = now;
            if (doubleClick)
            {
                int stateIndex = spatialAxisRigStates.FindIndex(item =>
                    item.boundVariable == selectedIndex);
                if (stateIndex < 0)
                    stateIndex = spatialAxisRigStates.FindIndex(item =>
                        item.boundVariable == targetVariable);
                string name = datasets[spatialAxisRigStates[stateIndex].boundVariable].Name;
                spatialAxisRigStates[stateIndex].boundVariable = -1;
                UpdateAutomaticVariableRole();
                InvalidateSlabConfiguration("Variable binding removed", true);
                RefreshSpatialAxisControllers();
                RefreshVariableFacetStacks();
                UpdateVariablePaletteTokenVisibility();
                SetStatus(name + " unbound from this axis body.");
                return;
            }
            if (datasets[targetVariable] != selectedDataset)
                LoadDataset(targetVariable);
            else
                ApplySelectedAxisRigState(0, true);
        }

        private void ApplySelectedAxisRigState(int variableIndex,
            bool animateGraph)
        {
            if (spatialAxisRigStates.Count == 0)
                return;
            SpatialAxisRigState state = spatialAxisRigStates[0];
            roles[0] = state.timeRole;
            roles[1] = state.depthRole;
            roles[2] = DimensionRole.Mapped;
            // Axis assignment controls Preview/Matrix ordering only. The STC
            // Field remains upright and spatially independent from the rig.
            fieldAxisRemapRotation = Quaternion.identity;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            if (volumeRoot != null)
                volumeRoot.localRotation = Quaternion.identity;
            FrameVolume();
            UpdateAnalysisAxisLabels();
            InvalidateSlabConfiguration("Spatial axis mapping changed", true);
        }

        private static Quaternion FieldRotationForAxisRig(
            SpatialAxisRigState state)
        {
            if (state == null || state.timeAxis < 0 || state.depthAxis < 0 ||
                state.timeAxis == state.depthAxis)
                return Quaternion.identity;
            Vector3 timeDirection = AxisSlotDirection(state.timeAxis);
            Vector3 depthDirection = AxisSlotDirection(state.depthAxis);
            return Quaternion.LookRotation(
                Vector3.Cross(timeDirection, depthDirection), depthDirection);
        }

        private void StartFieldAxisRemap(Quaternion target)
        {
            if (fieldAxisRemapCoroutine != null)
                StopCoroutine(fieldAxisRemapCoroutine);
            fieldAxisRemapCoroutine = StartCoroutine(
                AnimateFieldAxisRemap(target));
        }

        private IEnumerator AnimateFieldAxisRemap(Quaternion target)
        {
            Quaternion start = fieldAxisRemapRotation;
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            float elapsed = 0.0f;
            const float duration = 0.34f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                fieldAxisRemapRotation = Quaternion.Slerp(start, target, t);
                if (volumeRoot != null)
                    volumeRoot.localRotation = fieldAxisRemapRotation;
                yield return null;
            }
            fieldAxisRemapRotation = target;
            if (volumeRoot != null)
                volumeRoot.localRotation = target;
            // Rotation changes the renderer's world-space bounds. Refit after
            // every axis remap so the corresponding STC remains fully inside
            // its field frame instead of escaping through a side of the cube.
            FrameVolume();
            fieldAxisRemapCoroutine = null;
        }

        private void EnterBoundaryAuthoringView()
        {
            if (boundaryAuthoringCanonicalView)
                return;
            boundaryAuthoringCanonicalView = true;
            boundaryAuthoringRestoreRotation = fieldAxisRemapRotation;
            if (fieldAxisRemapCoroutine != null)
            {
                StopCoroutine(fieldAxisRemapCoroutine);
                fieldAxisRemapCoroutine = null;
            }
            StartBoundaryAuthoringRotation(Quaternion.identity);
        }

        private void ExitBoundaryAuthoringView()
        {
            if (!boundaryAuthoringCanonicalView)
                return;
            boundaryAuthoringCanonicalView = false;
            fieldAxisRemapRotation = boundaryAuthoringRestoreRotation;
            StartBoundaryAuthoringRotation(boundaryAuthoringRestoreRotation);
        }

        private void StartBoundaryAuthoringRotation(Quaternion target)
        {
            if (boundaryAuthoringRotationCoroutine != null)
                StopCoroutine(boundaryAuthoringRotationCoroutine);
            boundaryAuthoringRotationCoroutine = StartCoroutine(
                AnimateBoundaryAuthoringRotation(target));
        }

        private IEnumerator AnimateBoundaryAuthoringRotation(Quaternion target)
        {
            Transform volumeRoot = currentView != null &&
                currentView.rootObject != null
                    ? currentView.rootObject.transform : null;
            if (volumeRoot == null)
            {
                boundaryAuthoringRotationCoroutine = null;
                yield break;
            }
            Quaternion start = volumeRoot.localRotation;
            float elapsed = 0.0f;
            const float duration = 0.42f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Smooth cubic easing makes the reorientation readable in VR
                // without the abrupt one-frame jump of the old slice editor.
                t = t * t * (3.0f - 2.0f * t);
                volumeRoot.localRotation = Quaternion.Slerp(start, target, t);
                RecenterVolumeBoundsOnField(volumeRoot);
                yield return null;
            }
            volumeRoot.localRotation = target;
            // Recompute scale and bounds for the finished orientation. This is
            // what makes "turn upright" an in-place Field animation rather than
            // an orbit around the imported RAW object's off-centre pivot.
            FrameVolume();
            boundaryAuthoringRotationCoroutine = null;
        }

        private void RecenterVolumeBoundsOnField(Transform volumeRoot)
        {
            if (volumeRoot == null || spatialRoot == null)
                return;
            Renderer[] renderers = volumeRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds(volumeRoot.position, Vector3.zero);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                    combined.Encapsulate(renderer.bounds);
            }
            if (hasBounds)
                volumeRoot.position += spatialRoot.transform.position - combined.center;
        }

        private void CreateAxisOriginHub(Vector3 origin)
        {
            GameObject hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "XYZ axis origin hub";
            Destroy(hub.GetComponent<Collider>());
            hub.transform.SetParent(spatialRoot.transform, false);
            hub.transform.localPosition = origin;
            hub.transform.localScale = Vector3.one * 0.085f;
            Material hubMaterial = new Material(Shader.Find("Sprites/Default"));
            hubMaterial.color = new Color(0.88f, 0.96f, 1.0f, 0.98f);
            Renderer hubRenderer = hub.GetComponent<Renderer>();
            hubRenderer.material = hubMaterial;
            hubRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            hubRenderer.receiveShadows = false;

            CreateWorldLine("Origin X glow", spatialRoot.transform, origin,
                origin + Vector3.right * 0.24f, TimeColor, 0.026f);
            CreateWorldLine("Origin Y glow", spatialRoot.transform, origin,
                origin + Vector3.back * 0.24f, VariableColor, 0.026f);
            CreateWorldLine("Origin Z glow", spatialRoot.transform, origin,
                origin + Vector3.up * 0.24f, DepthAxisColor, 0.026f);
            CreateWorldLabel("X", origin + Vector3.right * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, TimeColor);
            CreateWorldLabel("Y", origin + Vector3.back * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, VariableColor);
            CreateWorldLabel("Z", origin + Vector3.up * 0.29f,
                0.0065f, TextAnchor.MiddleCenter, DepthAxisColor);
            CreateWorldLabel("0", origin + new Vector3(0.045f, 0.045f, -0.045f),
                0.0055f, TextAnchor.MiddleCenter, Ink);
        }

        private void UpdateAnalysisAxisLabels()
        {
            if (timeAxisLabel != null)
            {
                timeAxisLabel.text = selectedDataset == null
                    ? "X  TIME"
                    : "X  ·  TIME";
                timeAxisLabel.transform.localPosition = new Vector3(
                    0.0f,
                    boundaryEditActive &&
                    boundaryDimension == BoundaryDimension.Time ? 0.335f : 0.205f,
                    -0.045f);
            }
            if (variableAxisLabel != null)
            {
                variableAxisLabel.text = selectedDataset == null
                    ? "Y  VARIABLE"
                    : "Y  ·  " + selectedDataset.Name;
            }
            if (depthAxisLabel != null)
            {
                // A For_VR Hong Kong dataset is a 2D surface changing over
                // time. Its vertical extrusion represents the displayed value,
                // not an observed depth layer, so never label that axis DEPTH.
                depthAxisLabel.text = selectedDataset != null &&
                    IsForVrSurfaceDataset
                        ? "Z  ·  VALUE"
                        : selectedDataset == null
                            ? "Z  DEPTH"
                            : "Z  ·  DEPTH";
            }
            UpdateAxisBucketGuides();
        }

        private void UpdateAxisBucketGuides()
        {
            // CUT handles and finalized buckets are two different visual modes.
            // Never draw both at once: the shared corner becomes unreadable in VR.
            bool hasSavedBuckets =
                authoredTimeBuckets != null && authoredTimeBuckets.Length == 3 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length == 3;
            bool showBuckets = selectedDataset != null &&
                hasSavedBuckets && !boundaryEditActive;
            if (selectedDataset == null)
            {
                for (int index = 0; index < 3; index++)
                {
                    if (timeBucketAxisSegments[index] != null)
                        timeBucketAxisSegments[index].gameObject.SetActive(false);
                    if (timeBucketAxisLabels[index] != null)
                        timeBucketAxisLabels[index].gameObject.SetActive(false);
                    if (depthBucketAxisSegments[index] != null)
                        depthBucketAxisSegments[index].gameObject.SetActive(false);
                    if (depthBucketAxisLabels[index] != null)
                        depthBucketAxisLabels[index].gameObject.SetActive(false);
                }
                return;
            }
            int timeCount = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.TimeCount)
                : 30;
            int depthCount = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.DimZ)
                : 92;

            int[] timeFirst = { 0, timeBoundaryStart + 1, timeBoundaryEnd + 1 };
            int[] timeLast = { timeBoundaryStart, timeBoundaryEnd, timeCount - 1 };
            int depthCutA = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * depthCount),
                1, Mathf.Max(1, depthCount - 2));
            int depthCutB = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * depthCount),
                depthCutA + 1, depthCount - 1);
            int[] depthFirst = { 0, depthCutA, depthCutB };
            int[] depthLast = { depthCutA - 1, depthCutB - 1, depthCount - 1 };

            if (authorBoundaryConfirmed)
            {
                for (int index = 0; index < 3; index++)
                {
                    if (authoredTimeBuckets != null &&
                        index < authoredTimeBuckets.Length)
                        TryGetIndexRange(authoredTimeBuckets[index],
                            out timeFirst[index], out timeLast[index]);
                    if (authoredDepthBuckets != null &&
                        index < authoredDepthBuckets.Length)
                        TryGetIndexRange(authoredDepthBuckets[index],
                            out depthFirst[index], out depthLast[index]);
                }
            }

            string[] timeNames = { "BEFORE", "DURING", "AFTER" };
            string[] depthNames = { "SURFACE", "MIDDLE", "DEEP" };
            float timeDenominator = Mathf.Max(1, timeCount - 1);
            float depthDenominator = Mathf.Max(1, depthCount - 1);
            float depthAxisBottom = -FieldHalfHeight + 0.075f;
            float depthAxisTop = FieldHalfHeight - 0.075f;
            float depthAxisX = -FieldHalfWidth + 0.075f;
            float depthAxisZ = FieldHalfDepth - 0.070f;

            for (int index = 0; index < 3; index++)
            {
                if (timeBucketAxisSegments[index] != null)
                {
                    timeBucketAxisSegments[index].gameObject.SetActive(showBuckets);
                    float x0 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                        timeFirst[index] / timeDenominator);
                    float x1 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                        timeLast[index] / timeDenominator);
                    SetLine(timeBucketAxisSegments[index],
                        new Vector3(x0, 0.025f, -0.008f),
                        new Vector3(x1, 0.025f, -0.008f));
                    if (timeBucketAxisLabels[index] != null)
                    {
                        timeBucketAxisLabels[index].gameObject.SetActive(showBuckets);
                        timeBucketAxisLabels[index].text = timeNames[index] + "  " +
                            selectedDataset.GetTimeLabel(timeFirst[index]) + "–" +
                            selectedDataset.GetTimeLabel(timeLast[index]);
                        // The colored segment already communicates the range;
                        // keep only the semantic bucket name in the Field.
                        timeBucketAxisLabels[index].text = timeNames[index];
                        timeBucketAxisLabels[index].transform.localPosition =
                            new Vector3((x0 + x1) * 0.5f, 0.105f, -0.012f);
                    }
                }

                if (depthBucketAxisSegments[index] != null)
                {
                    depthBucketAxisSegments[index].gameObject.SetActive(showBuckets);
                    float y0 = Mathf.Lerp(depthAxisBottom, depthAxisTop,
                        depthFirst[index] / depthDenominator);
                    float y1 = Mathf.Lerp(depthAxisBottom, depthAxisTop,
                        depthLast[index] / depthDenominator);
                    SetLine(depthBucketAxisSegments[index],
                        new Vector3(depthAxisX, y0, depthAxisZ),
                        new Vector3(depthAxisX, y1, depthAxisZ));
                    if (depthBucketAxisLabels[index] != null)
                    {
                        depthBucketAxisLabels[index].gameObject.SetActive(showBuckets);
                        depthBucketAxisLabels[index].text = depthNames[index] +
                            "  z" + depthFirst[index] + "–" + depthLast[index];
                        depthBucketAxisLabels[index].text = depthNames[index];
                        depthBucketAxisLabels[index].transform.localPosition =
                            new Vector3(depthAxisX + 0.045f, (y0 + y1) * 0.5f,
                                depthAxisZ - 0.018f);
                    }
                }
            }
        }

        private void CreateDepthInspectionVisuals()
        {
            depthInspectionUpperStack = CreateDepthInspectionStack(
                "Depth inspection upper remainder", 1.0f);
            depthInspectionLowerStack = CreateDepthInspectionStack(
                "Depth inspection lower remainder", -1.0f);
            depthInspectionLabel = CreateWorldLabel(
                "Z DEPTH SLICE", new Vector3(0.0f, 0.56f, -FieldHalfDepth * 0.98f),
                0.010f, TextAnchor.MiddleCenter, DepthColor);
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(false);
        }

        private GameObject CreateDepthInspectionStack(string name, float direction)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(spatialRoot.transform, false);
            for (int layer = 0; layer < 4; layer++)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Context layer " + (layer + 1);
                quad.transform.SetParent(root.transform, false);
                Destroy(quad.GetComponent<Collider>());
                quad.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                quad.transform.localPosition =
                    new Vector3(0.0f, direction * layer * 0.040f, 0.0f);
                quad.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.74f, FieldHalfDepth * 1.74f, 1.0f);
                Material material = new Material(Shader.Find("Sprites/Default"));
                material.color = new Color(0.70f, 0.80f, 1.0f,
                    Mathf.Lerp(0.34f, 0.10f, layer / 3.0f));
                quad.GetComponent<Renderer>().material = material;
                depthInspectionStackRenderers.Add(quad.GetComponent<Renderer>());
            }
            return root;
        }

        private void CreateGroundEvidenceVisuals()
        {
            groundTimeRangeLine = CreateWorldLine("Ground time bucket range", timeRail,
                Vector3.zero, Vector3.zero, TimeColor, 0.052f);
            groundTimeRangeLine.gameObject.SetActive(false);
            groundTimeRangeLabel = CreateWorldLabel("TIME BUCKET", Vector3.zero, 0.007f,
                TextAnchor.MiddleCenter, TimeColor, timeRail);
            groundTimeRangeLabel.gameObject.SetActive(false);

            groundDepthBand = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundDepthBand.name = "Ground depth bucket band";
            groundDepthBand.transform.SetParent(spatialRoot.transform, false);
            Destroy(groundDepthBand.GetComponent<Collider>());
            Material bandMaterial = new Material(Shader.Find("Sprites/Default"));
            // The range volume is only a spatial hint.  Keep it extremely faint so
            // it never turns into an opaque wall in Quest's forward renderer.
            bandMaterial.color = new Color(Purple.r, Purple.g, Purple.b, 0.022f);
            groundDepthBand.GetComponent<Renderer>().material = bandMaterial;

            for (int index = 0; index < groundDepthRangePlanes.Length; index++)
            {
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plane.name = index == 0
                    ? "Ground depth bucket lower"
                    : "Ground depth bucket upper";
                plane.transform.SetParent(spatialRoot.transform, false);
                plane.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.84f, 0.010f, FieldHalfDepth * 1.78f);
                Destroy(plane.GetComponent<Collider>());
                Material planeMaterial = new Material(Shader.Find("Sprites/Default"));
                // Ground selection is an immutable MatPlot footprint, not an
                // editable depth boundary.  Both cuts therefore use the same
                // purple evidence colour; cyan remains reserved for authoring.
                Color boundaryColor = Purple;
                planeMaterial.color = new Color(
                    boundaryColor.r, boundaryColor.g, boundaryColor.b,
                    0.075f);
                // Evidence cuts must stay light and translucent on Quest.
                planeMaterial.SetInt("_ZWrite", 0);
                planeMaterial.renderQueue =
                    (int)UnityEngine.Rendering.RenderQueue.Transparent + 8;
                Renderer planeRenderer = plane.GetComponent<Renderer>();
                planeRenderer.material = planeMaterial;
                planeRenderer.sortingOrder = -20;
                groundDepthRangePlanes[index] = plane;
            }
            groundDepthRangeLabel = CreateWorldLabel("DEPTH BUCKET", Vector3.zero, 0.0065f,
                TextAnchor.MiddleLeft, Purple);
            SetGroundEvidenceVisuals(false);
        }

        private void CreateDepthBoundaryPlane(int index)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plane.name = index == 0 ? "Depth boundary lower" : "Depth boundary upper";
            plane.layer = 5;
            plane.transform.SetParent(spatialRoot.transform, false);
            plane.transform.localScale = new Vector3(
                FieldHalfWidth * 1.72f, 0.020f, FieldHalfDepth * 1.72f);
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = index == 0
                ? new Color(DepthColor.r, DepthColor.g, DepthColor.b, 0.28f)
                : new Color(Cyan.r, Cyan.g, Cyan.b, 0.20f);
            plane.GetComponent<Renderer>().material = material;
            int boundaryIndex = index;
            plane.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginDepthBoundaryDrag(boundaryIndex);
            depthBoundaryPlanes[index] = plane;
            depthBoundaryValueLabels[index] = CreateWorldLabel(
                index == 0 ? "LOWER  z=00" : "UPPER  z=00",
                Vector3.zero, 0.010f, TextAnchor.MiddleRight,
                index == 0 ? DepthColor : Cyan);
        }

        private void BeginDepthBoundaryDrag(int index)
        {
            if (boundaryCanvas == null || !boundaryCanvas.gameObject.activeSelf ||
                boundaryDimension != BoundaryDimension.Depth)
            {
                SetStatus("Open Author Boundary / Depth before moving depth planes.");
                return;
            }
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            activeDepthBoundary = fixedDepth ? 0 : Mathf.Clamp(index, 0, 1);
            depthBoundaryDragging = true;
            BeginDepthSliceInspection(
                fixedDepth ? slabNormalized :
                activeDepthBoundary == 0 ? depthBoundaryLow : depthBoundaryHigh);
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                desktopDepthBoundaryStartMouseY = FlatPointerPosition.y;
                desktopDepthBoundaryStartValue =
                    fixedDepth ? slabNormalized :
                    activeDepthBoundary == 0 ? depthBoundaryLow : depthBoundaryHigh;
            }
#endif
            SetStatus(fixedDepth
                ? "Dragging the fixed Depth plane. Release to select one z value."
                : "Dragging the " + (activeDepthBoundary == 0 ? "lower" : "upper") +
                  " depth boundary plane.");
        }

        private void UpdateDepthBoundaryInteraction()
        {
            if (!depthBoundaryDragging || rayInteractor == null)
                return;
            if (rayInteractor.TriggerHeld)
            {
                bool fixedDepth = roles[1] == DimensionRole.Fixed;
                float next;
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                    next = Mathf.Clamp01(desktopDepthBoundaryStartValue +
                        (FlatPointerPosition.y - desktopDepthBoundaryStartMouseY) /
                        Mathf.Max(360.0f, Screen.height * 0.72f));
                else
#endif
                {
                    float target = GetHandDepthNormalized();
                    float current = fixedDepth
                        ? slabNormalized
                        : activeDepthBoundary == 0
                        ? depthBoundaryLow
                        : depthBoundaryHigh;
                    float follow = 1.0f - Mathf.Exp(-22.0f * Time.unscaledDeltaTime);
                    next = Mathf.Lerp(current, target, follow);
                }

                if (fixedDepth)
                {
                    slabNormalized = Mathf.Clamp01(next);
                    int maxDepth = selectedDataset != null
                        ? Mathf.Max(1, selectedDataset.DimZ - 1) : 90;
                    selectedZ = Mathf.RoundToInt(slabNormalized * maxDepth);
                }
                else if (activeDepthBoundary == 0)
                    depthBoundaryLow = Mathf.Clamp(next, 0.0f, depthBoundaryHigh - 0.05f);
                else
                    depthBoundaryHigh = Mathf.Clamp(next, depthBoundaryLow + 0.05f, 1.0f);
                UpdateDepthBoundaryPlanes();
                UpdateDepthSliceInspection(next);
            }
            if (rayInteractor.TriggerReleased)
            {
                bool fixedDepth = roles[1] == DimensionRole.Fixed;
                int maxDepth = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.DimZ - 1)
                    : 90;
                if (fixedDepth)
                {
                    selectedZ = Mathf.RoundToInt(slabNormalized * maxDepth);
                    slabNormalized = selectedZ / (float)maxDepth;
                    depthInspectionOriginalNormalized = slabNormalized;
                    depthInspectionOriginalZ = selectedZ;
                }
                else if (activeDepthBoundary == 0)
                    depthBoundaryLow = Mathf.Round(depthBoundaryLow * maxDepth) /
                        maxDepth;
                else
                    depthBoundaryHigh = Mathf.Round(depthBoundaryHigh * maxDepth) /
                        maxDepth;
                UpdateDepthBoundaryPlanes();
                depthBoundaryDragging = false;
                EndDepthSliceInspection(false);
                SetStatus(fixedDepth
                    ? "Fixed Depth selected: z=" + selectedZ + "."
                    : "Depth band grounded: " + DepthBoundaryLabel() + ".");
                if (boundaryCanvas != null && boundaryCanvas.gameObject.activeSelf)
                    BuildBoundaryPanel();
            }
        }

        private void BeginDepthSliceInspection(float normalized)
        {
            if (selectedDataset == null || slabPreviewObject == null)
                return;
            normalized = Mathf.Clamp01(normalized);
            if (!depthInspectionActive)
            {
                depthInspectionOriginalZ = selectedZ;
                depthInspectionOriginalNormalized = slabNormalized;
            }
            UpdateDepthSliceInspection(normalized);
            if (depthInspectionActive)
            {
                if (depthInspectionCoroutine != null)
                    StopCoroutine(depthInspectionCoroutine);
                float reopeningY = Mathf.Lerp(
                    volumeLocalMinY, volumeLocalMaxY, normalized);
                depthInspectionCoroutine = StartCoroutine(
                    AnimateDepthInspection(true, reopeningY));
                return;
            }

            depthInspectionActive = true;
            slabPreviewObject.SetActive(true);
            Renderer inspectionRenderer = slabPreviewObject.GetComponent<Renderer>();
            if (inspectionRenderer != null)
                inspectionRenderer.enabled = true;

            float selectedY = Mathf.Lerp(
                volumeLocalMinY, volumeLocalMaxY, normalized);
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            // Keep the 3D field unobstructed. Only the exact selected z layer
            // travels outward; it updates continuously while the cut is held.
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(true);
            slabPreviewObject.transform.localPosition =
                fieldCenter + new Vector3(0.0f, selectedY, 0.0f);
            slabPreviewObject.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);

            if (depthInspectionCoroutine != null)
                StopCoroutine(depthInspectionCoroutine);
            depthInspectionCoroutine = StartCoroutine(
                AnimateDepthInspection(true, selectedY));
        }

        private void UpdateDepthSliceInspection(float normalized)
        {
            if (selectedDataset == null)
                return;
            normalized = Mathf.Clamp01(normalized);
            int nextZ = Mathf.Clamp(
                Mathf.RoundToInt(normalized * (selectedDataset.DimZ - 1)),
                0, selectedDataset.DimZ - 1);
            slabNormalized = normalized;
            selectedZ = nextZ;
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            if (depthInspectionZ != nextZ || slabTexture == null)
            {
                depthInspectionZ = nextZ;
                RefreshSlabTexture();
                for (int index = 0;
                    index < depthInspectionStackRenderers.Count; index++)
                {
                    Material material =
                        depthInspectionStackRenderers[index].material;
                    material.mainTexture = slabTexture;
                    material.mainTextureScale = Vector2.one;
                    material.mainTextureOffset = Vector2.zero;
                }
            }
            if (depthInspectionLabel != null)
            {
                depthInspectionLabel.text =
                    "Z  DEPTH SLICE  |  z=" + nextZ +
                    "\n90° INSPECTION VIEW";
                float side = DepthInspectionSide(fieldCenter);
                depthInspectionLabel.transform.localPosition = new Vector3(
                    fieldCenter.x + side * (FieldHalfWidth + 0.31f),
                    fieldCenter.y +
                        (activeDepthBoundary == 1 ? 0.72f : -0.27f),
                    fieldCenter.z - FieldHalfDepth * 0.78f);
            }
            if (depthInspectionActive && depthInspectionCoroutine == null &&
                slabPreviewObject != null)
            {
                // Once the slice has opened beside the cube, keep its vertical
                // placement tied to the ray-controlled cut instead of leaving it
                // behind at the position where dragging began.
                float side = DepthInspectionSide(fieldCenter);
                slabPreviewObject.transform.localPosition = new Vector3(
                    fieldCenter.x + side * (FieldHalfWidth + 0.31f),
                    fieldCenter.y + Mathf.Lerp(-0.48f, 0.48f, normalized),
                    fieldCenter.z - FieldHalfDepth * 0.78f);
            }
            UpdateAnalysisAxisLabels();
        }

        // Open the live layer into the nearest free lane. In particular the
        // right-most faceted Field opens inward, away from the Variables panel.
        private float DepthInspectionSide(Vector3 fieldCenter)
        {
            return fieldCenter.x > SpatialAxisDockX + 0.20f ? -1.0f : 1.0f;
        }

        private void EndDepthSliceInspection(bool immediate)
        {
            if (!depthInspectionActive)
                return;
            if (depthInspectionCoroutine != null)
                StopCoroutine(depthInspectionCoroutine);
            // Close back into the cut that produced the inspection view.
            // The original browsing depth is restored only after the visual
            // pieces have reached this exact cut plane.
            float selectedY = Mathf.Lerp(
                volumeLocalMinY, volumeLocalMaxY, slabNormalized);
            if (immediate)
            {
                FinishDepthInspectionRestore(selectedY);
                return;
            }
            depthInspectionCoroutine = StartCoroutine(
                AnimateDepthInspection(false, selectedY));
        }

        private System.Collections.IEnumerator AnimateDepthInspection(
            bool opening, float selectedY)
        {
            Vector3 previewStartPosition =
                slabPreviewObject.transform.localPosition;
            Quaternion previewStartRotation =
                slabPreviewObject.transform.localRotation;
            Vector3 previewStartScale =
                slabPreviewObject.transform.localScale;
            Vector3 previewTargetPosition = opening
                ? ActiveBoundaryFieldCenter() + new Vector3(
                    DepthInspectionSide(ActiveBoundaryFieldCenter()) *
                        (FieldHalfWidth + 0.31f),
                    activeDepthBoundary == 1 ? 0.50f : -0.50f,
                    -FieldHalfDepth * 0.78f)
                : ActiveBoundaryFieldCenter() +
                    new Vector3(0.0f, selectedY + 0.013f, 0.0f);
            Quaternion previewTargetRotation = opening
                ? Quaternion.identity
                : Quaternion.Euler(90.0f, 0.0f, 0.0f);
            Vector3 previewTargetScale = opening
                ? new Vector3(
                    0.46f, 0.34f, 1.0f)
                : new Vector3(
                    FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);

            float elapsed = 0.0f;
            const float duration = 0.34f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(
                    0.0f, 1.0f, Mathf.Clamp01(elapsed / duration));
                slabPreviewObject.transform.localPosition =
                    Vector3.Lerp(previewStartPosition, previewTargetPosition, t);
                slabPreviewObject.transform.localRotation =
                    Quaternion.Slerp(previewStartRotation, previewTargetRotation, t);
                slabPreviewObject.transform.localScale =
                    Vector3.Lerp(previewStartScale, previewTargetScale, t);
                yield return null;
            }

            if (!opening)
                FinishDepthInspectionRestore(selectedY);
            depthInspectionCoroutine = null;
        }

        private void FinishDepthInspectionRestore(float selectedY)
        {
            depthInspectionActive = false;
            depthInspectionCoroutine = null;
            depthInspectionUpperStack.SetActive(false);
            depthInspectionLowerStack.SetActive(false);
            depthInspectionLabel.gameObject.SetActive(false);
            slabPreviewObject.SetActive(false);
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            slabPreviewObject.transform.localPosition =
                fieldCenter + new Vector3(0.0f, selectedY + 0.013f, 0.0f);
            slabPreviewObject.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            slabPreviewObject.transform.localScale =
                new Vector3(FieldHalfWidth * 1.64f, FieldHalfDepth * 1.64f, 1.0f);
            if (slabObject != null)
                slabObject.SetActive(!UsesFacetedVariableFields());
            if (regionRoot != null)
                regionRoot.SetActive(false);
            ApplyPrimaryVolumeVisibility();
            slabNormalized = depthInspectionOriginalNormalized;
            selectedZ = depthInspectionOriginalZ;
            depthInspectionZ = -1;
            RefreshSlabTexture();
            UpdateSlabVisual(true);
        }

        private void UpdateDepthBoundaryPlanes()
        {
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            int maxDepth = selectedDataset != null
                ? Mathf.Max(1, selectedDataset.DimZ - 1)
                : 90;
            for (int index = 0; index < depthBoundaryPlanes.Length; index++)
            {
                if (depthBoundaryPlanes[index] == null)
                    continue;
                float value = fixedDepth && index == 0
                    ? slabNormalized
                    : index == 0 ? depthBoundaryLow : depthBoundaryHigh;
                float y = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY, value);
                bool highlighted = boundaryEditActive &&
                    boundaryDimension == BoundaryDimension.Depth &&
                    (!depthBoundaryDragging || index == activeDepthBoundary);
                depthBoundaryPlanes[index].transform.localPosition =
                    fieldCenter + new Vector3(0.0f, y, 0.0f);
                depthBoundaryPlanes[index].transform.localScale =
                    new Vector3(FieldHalfWidth * 1.72f,
                        highlighted ? 0.028f : 0.012f,
                        FieldHalfDepth * 1.72f);
                Renderer planeRenderer = depthBoundaryPlanes[index].GetComponent<Renderer>();
                if (planeRenderer != null)
                {
                    Color planeColor = index == 0 ? DepthColor : Cyan;
                    planeColor.a = highlighted
                        ? (depthBoundaryDragging ? 0.58f : 0.38f)
                        : 0.12f;
                    planeRenderer.material.color = planeColor;
                }
                if (depthBoundaryValueLabels[index] != null)
                {
                    int z = Mathf.RoundToInt(value * maxDepth);
                    depthBoundaryValueLabels[index].text = fixedDepth && index == 0
                        ? "FIXED DEPTH  z=" + z
                        : (index == 0 ? "LOWER  " : "UPPER  ") + "z=" + z;
                    depthBoundaryValueLabels[index].transform.localPosition =
                        fieldCenter + new Vector3(FieldHalfWidth - 0.025f,
                            y + 0.035f, -FieldHalfDepth * 0.92f);
                }
            }
            UpdateAxisBucketGuides();
        }

        private void SetDepthBoundaryVisibility(bool visible)
        {
            bool fixedDepth = roles[1] == DimensionRole.Fixed;
            for (int index = 0; index < depthBoundaryPlanes.Length; index++)
            {
                bool itemVisible = visible && (!fixedDepth || index == 0);
                if (depthBoundaryPlanes[index] != null)
                    depthBoundaryPlanes[index].SetActive(itemVisible);
                if (depthBoundaryValueLabels[index] != null)
                    depthBoundaryValueLabels[index].gameObject.SetActive(itemVisible);
            }
        }

        private string DepthBoundaryLabel()
        {
            int maxDepth = selectedDataset != null ? Mathf.Max(1, selectedDataset.DimZ - 1) : 90;
            int lower = Mathf.RoundToInt(depthBoundaryLow * maxDepth);
            int upper = Mathf.RoundToInt(depthBoundaryHigh * maxDepth);
            return "z" + lower.ToString("00") + "-z" + upper.ToString("00");
        }

        private void CreateTimeRail()
        {
            GameObject rail = new GameObject("Day 1 to Day 30 rail");
            rail.transform.SetParent(spatialRoot.transform, false);
            rail.transform.localPosition =
                new Vector3(0.0f, -FieldHalfHeight + 0.095f, FieldHalfDepth * 0.88f);
            timeRail = rail.transform;
            CreateWorldLine("Time rail backing", rail.transform,
                new Vector3(-TimeRailHalfWidth, 0.0f, 0.0f),
                new Vector3(TimeRailHalfWidth, 0.0f, 0.0f), Card, 0.032f);
            CreateWorldLine("Time rail", rail.transform,
                new Vector3(-TimeRailHalfWidth, 0.0f, -0.002f),
                new Vector3(TimeRailHalfWidth, 0.0f, -0.002f), Amber, 0.016f);
            timeAxisLabel = CreateWorldLabel("X  ·  TIME / DAY",
                new Vector3(0.0f, 0.235f, -0.045f),
                0.0090f, TextAnchor.MiddleCenter, TimeColor, rail.transform);
            CreateWorldLabel("day 1", new Vector3(-TimeRailHalfWidth, -0.075f, 0.0f),
                0.011f, TextAnchor.MiddleLeft, Ink, rail.transform);
            CreateWorldLabel("day 30", new Vector3(TimeRailHalfWidth, -0.075f, 0.0f),
                0.011f, TextAnchor.MiddleRight, Ink, rail.transform);

            Color[] timeColors =
            {
                new Color(1.0f, 0.48f, 0.10f, 0.96f),
                new Color(1.0f, 0.72f, 0.06f, 0.98f),
                new Color(1.0f, 0.88f, 0.24f, 0.96f)
            };
            for (int index = 0; index < 3; index++)
            {
                timeBucketAxisSegments[index] = CreateWorldLine(
                    "Time bucket axis " + index, rail.transform,
                    Vector3.zero, Vector3.zero, timeColors[index], 0.025f);
                timeBucketAxisLabels[index] = CreateWorldLabel(
                    "TIME BUCKET", Vector3.zero, 0.0058f,
                    TextAnchor.MiddleCenter, timeColors[index], rail.transform);
                timeBucketAxisSegments[index].gameObject.SetActive(false);
                timeBucketAxisLabels[index].gameObject.SetActive(false);
            }
            CreateTimeBoundaryHandle(0, "START");
            CreateTimeBoundaryHandle(1, "END");
            UpdateTimeBoundaryHandles();
            SetTimeBoundaryHandleVisibility(false);
            UpdateAxisBucketGuides();
        }

        private void CreateTimeBoundaryHandle(int index, string label)
        {
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            handle.name = "Time boundary " + label;
            handle.layer = 5;
            handle.transform.SetParent(timeRail, false);
            handle.transform.localScale = new Vector3(0.035f, 0.135f, 0.035f);
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = index == 0 ? TimeColor : Amber;
            handle.GetComponent<Renderer>().material = material;
            int boundaryIndex = index;
            handle.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                () => BeginTimeBoundaryDrag(boundaryIndex);
            timeBoundaryHandles[index] = handle;
            timeBoundaryValueLabels[index] = CreateWorldLabel(
                label + "  day --", Vector3.zero, 0.0115f,
                TextAnchor.MiddleCenter, index == 0 ? TimeColor : Amber, timeRail);
        }

        private void BeginTimeBoundaryDrag(int index)
        {
            if (boundaryCanvas == null || !boundaryCanvas.gameObject.activeSelf ||
                boundaryDimension != BoundaryDimension.Time)
            {
                SetStatus("Open Author Boundary / Time before moving interval flags.");
                return;
            }
            activeTimeBoundary = Mathf.Clamp(index, 0, 1);
            timeBoundaryDragging = true;
            SetStatus("Dragging " + (activeTimeBoundary == 0 ? "start" : "end") +
                " flag on the continuous time rail.");
        }

        private void UpdateTimeBoundaryInteraction()
        {
            if (!timeBoundaryDragging || rayInteractor == null || timeRail == null)
                return;
            if (rayInteractor.TriggerHeld)
            {
                bool fixedTime = roles[0] == DimensionRole.Fixed;
                Plane railPlane = new Plane(spatialRoot.transform.forward, timeRail.position);
                if (railPlane.Raycast(rayInteractor.PointerRay, out float distance))
                {
                    Vector3 local = timeRail.InverseTransformPoint(
                        rayInteractor.PointerRay.GetPoint(distance));
                    int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
                    int index = Mathf.RoundToInt(Mathf.InverseLerp(
                        -TimeRailHalfWidth, TimeRailHalfWidth, local.x) *
                        Mathf.Max(1, count - 1));
                    if (fixedTime)
                        selectedTime = Mathf.Clamp(index, 0, count - 1);
                    else if (activeTimeBoundary == 0)
                        timeBoundaryStart = Mathf.Clamp(index, 0, timeBoundaryEnd - 1);
                    else
                        timeBoundaryEnd = Mathf.Clamp(index, timeBoundaryStart + 1, count - 1);
                    UpdateTimeBoundaryHandles();
                    PreviewBoundaryTime(fixedTime
                        ? selectedTime
                        : activeTimeBoundary == 0
                        ? timeBoundaryStart
                        : timeBoundaryEnd);
                }
            }
            if (rayInteractor.TriggerReleased)
            {
                bool fixedTime = roles[0] == DimensionRole.Fixed;
                timeBoundaryDragging = false;
                CommitBoundaryTimePreview();
                // Keep the selected-time evidence card pinned after release.
                // It is an independent scientific snapshot and must not follow
                // or flicker with the continuously playing ground surface.
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
                SetStatus(fixedTime
                    ? "Fixed Time selected: " +
                      (selectedDataset != null
                          ? selectedDataset.GetTimeLabel(selectedTime)
                          : "time " + (selectedTime + 1)) + "."
                    : "Time interval grounded: day " + (timeBoundaryStart + 1) +
                      " to day " + (timeBoundaryEnd + 1) + ".");
                if (boundaryCanvas != null && boundaryCanvas.gameObject.activeSelf)
                    BuildBoundaryPanel();
            }
        }

        private void PreviewBoundaryTime(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            int nextTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            bool changed = selectedTime != nextTime ||
                boundaryDayPreviewTime != nextTime;
            selectedTime = nextTime;
            SpatialAxisRigState boundaryState = ActiveVariableBoundaryState();
            if (boundaryState != null && !boundaryState.usesSharedBoundaries)
                boundaryState.customSelectedTime = selectedTime;
            else
                sharedSelectedTime = selectedTime;
            // Keep the primary 3D Field stable while a boundary is moving.
            // Rebuilding the volume for every crossed day caused unrelated
            // textures to flash inside the cube. Only the rear day-preview
            // surface changes during the drag; the Field commits once on release.
            if (changed || slabTexture == null)
                RefreshSlabTexture();
            ShowBoundaryDayPreview(nextTime);
            RebuildTimeMarkers();
            UpdateSlabVisual(false);
            SetStatus("Boundary preview: " +
                selectedDataset.GetTimeLabel(selectedTime) +
                ". The rear Field panel shows this fixed-time geographic snapshot; release to pin it.");
        }

        private void CommitBoundaryTimePreview()
        {
            if (selectedDataset == null)
                return;
            // Boundary selection owns a separate rear-face preview. It must
            // never pause, reset, or replace the continuously playing surface.
            if (forVrSurfacePlayer != null)
            {
                // Intentionally leave the independent player untouched.
            }
            else if (UsesFacetedVariableFields())
                RefreshVariableFacetStacks();
            else
                ApplyTimeFilter();
            ApplyPrimaryVolumeVisibility();
            RefreshSlabTexture();
            RebuildTimeMarkers();
            UpdateSlabVisual(false);
        }

        private void EnsureBoundaryDayPreview()
        {
            if (boundaryDayPreviewObject != null || spatialRoot == null)
                return;
            boundaryDayPreviewObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            boundaryDayPreviewObject.name = "Boundary day preview on rear Field face";
            Collider previewCollider = boundaryDayPreviewObject.GetComponent<Collider>();
            if (previewCollider != null)
                Destroy(previewCollider);
            boundaryDayPreviewObject.transform.SetParent(spatialRoot.transform, false);
            boundaryDayPreviewMaterial = new Material(Shader.Find("Sprites/Default"));
            boundaryDayPreviewMaterial.color = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            boundaryDayPreviewMaterial.renderQueue = 3050;
            Renderer previewRenderer = boundaryDayPreviewObject.GetComponent<Renderer>();
            previewRenderer.enabled = false;
            previewRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;

            boundaryDayPreviewMapObject = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            boundaryDayPreviewMapObject.name = "Selected-time Hong Kong map";
            Collider mapCollider = boundaryDayPreviewMapObject.GetComponent<Collider>();
            if (mapCollider != null)
                Destroy(mapCollider);
            boundaryDayPreviewMapObject.transform.SetParent(
                boundaryDayPreviewObject.transform, false);
            boundaryDayPreviewMapObject.transform.localPosition =
                new Vector3(0.205f, 0.0f, 0.0f);
            boundaryDayPreviewMapObject.transform.localScale =
                new Vector3(0.58f, 0.58f, 1.0f);
            Renderer mapRenderer = boundaryDayPreviewMapObject.GetComponent<Renderer>();
            mapRenderer.material = boundaryDayPreviewMaterial;
            mapRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            mapRenderer.receiveShadows = false;

            boundaryDayPreviewDataObject = new GameObject(
                "Exact geographic selected-time layer", typeof(MeshFilter),
                typeof(MeshRenderer));
            boundaryDayPreviewDataObject.transform.SetParent(
                boundaryDayPreviewMapObject.transform, false);
            boundaryDayPreviewDataMesh = new Mesh
            {
                name = "Selected-time exact Hong Kong mesh"
            };
            boundaryDayPreviewDataMesh.MarkDynamic();
            boundaryDayPreviewDataObject.GetComponent<MeshFilter>().sharedMesh =
                boundaryDayPreviewDataMesh;
            boundaryDayPreviewDataMaterial = new Material(Shader.Find("Sprites/Default"));
            boundaryDayPreviewDataMaterial.renderQueue = 3060;
            MeshRenderer dataRenderer =
                boundaryDayPreviewDataObject.GetComponent<MeshRenderer>();
            dataRenderer.material = boundaryDayPreviewDataMaterial;
            dataRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            dataRenderer.receiveShadows = false;
            boundaryDayPreviewDataObject.SetActive(false);

            boundaryDayPreviewLegendObject = GameObject.CreatePrimitive(
                PrimitiveType.Quad);
            boundaryDayPreviewLegendObject.name = "Selected-time shared color scale";
            Collider legendCollider = boundaryDayPreviewLegendObject.GetComponent<Collider>();
            if (legendCollider != null)
                Destroy(legendCollider);
            boundaryDayPreviewLegendObject.transform.SetParent(
                boundaryDayPreviewMapObject.transform, false);
            boundaryDayPreviewLegendObject.transform.localPosition =
                new Vector3(0.455f, 0.0f, -0.018f);
            boundaryDayPreviewLegendObject.transform.localScale =
                new Vector3(0.028f, 0.70f, 1.0f);
            boundaryDayPreviewLegendTexture = new Texture2D(
                16, 128, TextureFormat.RGBA32, false, false)
            {
                name = "Selected-time shared physical scale",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            boundaryDayPreviewLegendMaterial = new Material(
                Shader.Find("Sprites/Default"));
            boundaryDayPreviewLegendMaterial.mainTexture =
                boundaryDayPreviewLegendTexture;
            boundaryDayPreviewLegendMaterial.renderQueue = 3070;
            boundaryDayPreviewLegendObject.GetComponent<Renderer>().material =
                boundaryDayPreviewLegendMaterial;
            boundaryDayPreviewLegendObject.SetActive(false);

            boundaryDayPreviewLabel = CreateWorldLabel(
                "SELECTED TIME", new Vector3(-0.31f, 0.205f, 0.015f),
                0.0046f, TextAnchor.MiddleCenter, Ink,
                boundaryDayPreviewObject.transform);
            // TextMesh renders its readable face opposite the textured Quad.
            // Turn only the label around so the day text is never mirrored.
            boundaryDayPreviewLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewStatsLabel = CreateWorldLabel(
                "MIN\nMEAN\nMAX", new Vector3(-0.31f, -0.165f, 0.015f),
                0.0045f, TextAnchor.MiddleCenter, Ink,
                boundaryDayPreviewObject.transform);
            boundaryDayPreviewStatsLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewScaleLabel = CreateWorldLabel(
                "SCALE", new Vector3(0.405f, 0.0f, 0.018f),
                0.0038f, TextAnchor.MiddleRight, Ink,
                boundaryDayPreviewMapObject.transform);
            boundaryDayPreviewScaleLabel.transform.localRotation =
                Quaternion.Euler(0.0f, 180.0f, 0.0f);
            boundaryDayPreviewScaleLabel.gameObject.SetActive(false);
            Color calloutColor = new Color(0.20f, 0.92f, 0.96f, 0.92f);
            CreateDashedLocalLine(boundaryDayPreviewObject.transform,
                "Selected-time callout lead A",
                new Vector3(-0.085f, 0.10f, -0.024f),
                new Vector3(-0.125f, 0.10f, -0.024f), 3,
                calloutColor, 0.005f);
            CreateDashedLocalLine(boundaryDayPreviewObject.transform,
                "Selected-time callout lead B",
                new Vector3(-0.125f, 0.10f, -0.024f),
                new Vector3(-0.14f, 0.18f, -0.024f), 3,
                calloutColor, 0.005f);
            CreateDashedRectangle(boundaryDayPreviewObject.transform,
                "Selected-time callout box", -0.49f, -0.14f,
                -0.44f, 0.44f, calloutColor, 0.005f);
            boundaryDayPreviewObject.SetActive(false);
        }

        private void ShowBoundaryDayPreview(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            EnsureBoundaryDayPreview();
            if (boundaryDayPreviewObject == null || boundaryDayPreviewMaterial == null)
                return;
            if (boundaryDayPreviewHideAnimation != null)
            {
                StopCoroutine(boundaryDayPreviewHideAnimation);
                boundaryDayPreviewHideAnimation = null;
            }
            bool wasVisible = boundaryDayPreviewObject.activeSelf;
            boundaryDayPreviewTime = timeIndex;
            string snapshotHeading = string.Empty;
            string snapshotStatistics = string.Empty;
            string snapshotScale = string.Empty;
            bool professionalGeographicPreview = forVrSurfacePlayer != null &&
                forVrSurfacePlayer.TryUpdateGeographicSnapshot(
                    boundaryDayPreviewDataMesh, timeIndex,
                    out snapshotHeading, out snapshotStatistics,
                    out snapshotScale);
            boundaryDayPreviewMaterial.mainTexture = professionalGeographicPreview
                ? Resources.Load<Texture2D>("HongKongOSM")
                : slabTexture;
            boundaryDayPreviewMaterial.mainTextureScale = Vector2.one;
            boundaryDayPreviewMaterial.mainTextureOffset = Vector2.zero;
            if (boundaryDayPreviewDataObject != null)
                boundaryDayPreviewDataObject.SetActive(professionalGeographicPreview);
            if (boundaryDayPreviewLegendObject != null)
                boundaryDayPreviewLegendObject.SetActive(professionalGeographicPreview);
            if (boundaryDayPreviewScaleLabel != null)
            {
                boundaryDayPreviewScaleLabel.gameObject.SetActive(
                    professionalGeographicPreview);
                boundaryDayPreviewScaleLabel.text = snapshotScale;
            }
            if (professionalGeographicPreview &&
                boundaryDayPreviewLegendTexture != null)
                forVrSurfacePlayer.UpdateSnapshotLegend(
                    boundaryDayPreviewLegendTexture);

            if (!wasVisible)
            {
                Vector3 localCamera = xrCamera != null
                    ? spatialRoot.transform.InverseTransformPoint(xrCamera.transform.position)
                    : new Vector3(0.0f, 0.0f, 2.0f);
                float viewerSign = localCamera.z >= 0.0f ? 1.0f : -1.0f;
                Vector3 previewPosition = new Vector3(
                    0.0f, 0.10f, -viewerSign * (FieldHalfDepth - 0.018f));
                boundaryDayPreviewObject.transform.localPosition = previewPosition;
                Vector3 towardViewer = localCamera - previewPosition;
                if (towardViewer.sqrMagnitude < 0.001f)
                    towardViewer = Vector3.forward * viewerSign;
                boundaryDayPreviewObject.transform.localRotation =
                    Quaternion.LookRotation(towardViewer.normalized, Vector3.up);
            }

            if (boundaryDayPreviewLabel != null)
                boundaryDayPreviewLabel.text = professionalGeographicPreview
                    ? snapshotHeading
                    : "SELECTED TIME " + (timeIndex + 1) + "\n" +
                      selectedDataset.GetTimeLabel(timeIndex);
            if (boundaryDayPreviewStatsLabel != null)
            {
                boundaryDayPreviewStatsLabel.gameObject.SetActive(
                    professionalGeographicPreview);
                boundaryDayPreviewStatsLabel.text = snapshotStatistics;
            }
            boundaryDayPreviewObject.SetActive(true);
            if (!wasVisible)
            {
                if (boundaryDayPreviewAnimation != null)
                    StopCoroutine(boundaryDayPreviewAnimation);
                boundaryDayPreviewAnimation = StartCoroutine(
                    AnimateBoundaryDayPreviewIn());
            }
            else
            {
                boundaryDayPreviewObject.transform.localScale =
                    BoundaryPreviewTargetScale(professionalGeographicPreview);
                boundaryDayPreviewMaterial.color =
                    BoundaryPreviewMapColor(professionalGeographicPreview, 0.96f);
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color = Ink;
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color = Ink;
            }
        }

        private IEnumerator AnimateBoundaryDayPreviewIn()
        {
            bool geographic = boundaryDayPreviewDataObject != null &&
                boundaryDayPreviewDataObject.activeSelf;
            Vector3 targetScale = BoundaryPreviewTargetScale(
                geographic);
            Vector3 startScale = targetScale * 0.91f;
            startScale.z = 1.0f;
            boundaryDayPreviewObject.transform.localScale = startScale;
            float elapsed = 0.0f;
            const float duration = 0.20f;
            while (elapsed < duration && boundaryDayPreviewObject != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                boundaryDayPreviewObject.transform.localScale =
                    Vector3.Lerp(startScale, targetScale, t);
                if (boundaryDayPreviewMaterial != null)
                    boundaryDayPreviewMaterial.color =
                        BoundaryPreviewMapColor(geographic,
                            Mathf.Lerp(0.16f, 0.96f, t));
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color =
                        new Color(Ink.r, Ink.g, Ink.b, t);
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color =
                        new Color(Ink.r, Ink.g, Ink.b, t);
                yield return null;
            }
            if (boundaryDayPreviewObject != null)
                boundaryDayPreviewObject.transform.localScale = targetScale;
            boundaryDayPreviewAnimation = null;
        }

        private static Vector3 BoundaryPreviewTargetScale(bool geographic)
        {
            float width = geographic
                ? FieldHalfWidth * 1.78f
                : FieldHalfWidth * 1.48f;
            // The geographic snapshot uses the projected Hong Kong aspect
            // ratio instead of stretching the map to fill the rear face.
            float height = geographic
                ? width / 1.397f
                : FieldHalfHeight * 1.18f;
            return new Vector3(width, height, 1.0f);
        }

        private static Color BoundaryPreviewMapColor(bool geographic, float alpha)
        {
            // Keep the basemap bright enough to read while the saturated,
            // high-opacity scientific layer remains visually dominant.
            return geographic
                ? new Color(0.80f, 0.82f, 0.82f, alpha)
                : new Color(1.0f, 1.0f, 1.0f, alpha);
        }

        private void CreateDashedRectangle(Transform parent, string name,
            float left, float right, float bottom, float top,
            Color color, float width)
        {
            CreateDashedLocalLine(parent, name + " top",
                new Vector3(left, top, -0.024f),
                new Vector3(right, top, -0.024f), 8, color, width);
            CreateDashedLocalLine(parent, name + " right",
                new Vector3(right, top, -0.024f),
                new Vector3(right, bottom, -0.024f), 10, color, width);
            CreateDashedLocalLine(parent, name + " bottom",
                new Vector3(right, bottom, -0.024f),
                new Vector3(left, bottom, -0.024f), 8, color, width);
            CreateDashedLocalLine(parent, name + " left",
                new Vector3(left, bottom, -0.024f),
                new Vector3(left, top, -0.024f), 10, color, width);
        }

        private void CreateDashedLocalLine(Transform parent, string name,
            Vector3 start, Vector3 end, int dashCount, Color color, float width)
        {
            dashCount = Mathf.Max(1, dashCount);
            for (int index = 0; index < dashCount; index++)
            {
                float from = index / (float)dashCount;
                float to = Mathf.Min(1.0f, from + 0.56f / dashCount);
                CreateWorldLine(name + " " + index, parent,
                    Vector3.Lerp(start, end, from),
                    Vector3.Lerp(start, end, to), color, width);
            }
        }

        private void HideBoundaryDayPreviewSmoothly()
        {
            if (boundaryDayPreviewObject == null ||
                !boundaryDayPreviewObject.activeSelf)
                return;
            if (boundaryDayPreviewHideAnimation != null)
                StopCoroutine(boundaryDayPreviewHideAnimation);
            boundaryDayPreviewHideAnimation = StartCoroutine(
                AnimateBoundaryDayPreviewOut());
        }

        private IEnumerator AnimateBoundaryDayPreviewOut()
        {
            yield return new WaitForSecondsRealtime(0.28f);
            float elapsed = 0.0f;
            const float duration = 0.18f;
            Color startColor = boundaryDayPreviewMaterial != null
                ? boundaryDayPreviewMaterial.color : Color.white;
            while (elapsed < duration && boundaryDayPreviewObject != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0.0f, 1.0f,
                    Mathf.Clamp01(elapsed / duration));
                if (boundaryDayPreviewMaterial != null)
                    boundaryDayPreviewMaterial.color = new Color(
                        startColor.r, startColor.g, startColor.b,
                        Mathf.Lerp(startColor.a, 0.0f, t));
                if (boundaryDayPreviewLabel != null)
                    boundaryDayPreviewLabel.color = new Color(
                        Ink.r, Ink.g, Ink.b, 1.0f - t);
                if (boundaryDayPreviewStatsLabel != null)
                    boundaryDayPreviewStatsLabel.color = new Color(
                        Ink.r, Ink.g, Ink.b, 1.0f - t);
                yield return null;
            }
            if (boundaryDayPreviewObject != null)
                boundaryDayPreviewObject.SetActive(false);
            boundaryDayPreviewHideAnimation = null;
        }

        private void UpdateTimeBoundaryHandles()
        {
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            bool fixedTime = roles[0] == DimensionRole.Fixed;
            for (int index = 0; index < timeBoundaryHandles.Length; index++)
            {
                if (timeBoundaryHandles[index] == null)
                    continue;
                int value = fixedTime && index == 0
                    ? selectedTime
                    : index == 0 ? timeBoundaryStart : timeBoundaryEnd;
                bool highlighted = true;
                float x = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                    value / (float)Mathf.Max(1, count - 1));
                timeBoundaryHandles[index].transform.localPosition = new Vector3(x, 0.085f, 0.0f);
                timeBoundaryHandles[index].transform.localScale =
                    new Vector3(highlighted ? 0.050f : 0.026f,
                        highlighted ? 0.170f : 0.105f,
                        highlighted ? 0.050f : 0.026f);
                Renderer handleRenderer = timeBoundaryHandles[index].GetComponent<Renderer>();
                if (handleRenderer != null)
                {
                    Color handleColor = index == 0 ? TimeColor : Amber;
                    handleColor.a = highlighted ? 1.0f : 0.28f;
                    handleRenderer.material.color = handleColor;
                }
                if (timeBoundaryValueLabels[index] != null)
                {
                    timeBoundaryValueLabels[index].text = fixedTime && index == 0
                        ? "FIXED TIME  day " + (value + 1)
                        : (index == 0 ? "CUT A" : "CUT B") + "  day " + (value + 1);
                    timeBoundaryValueLabels[index].transform.localPosition =
                        new Vector3(x, 0.225f, 0.0f);
                }
            }
            UpdateAxisBucketGuides();
        }

        private void SetTimeBoundaryHandleVisibility(bool visible)
        {
            bool fixedTime = roles[0] == DimensionRole.Fixed;
            for (int index = 0; index < timeBoundaryHandles.Length; index++)
            {
                bool itemVisible = visible && (!fixedTime || index == 0);
                if (timeBoundaryHandles[index] != null)
                    timeBoundaryHandles[index].SetActive(itemVisible);
                if (timeBoundaryValueLabels[index] != null)
                    timeBoundaryValueLabels[index].gameObject.SetActive(itemVisible);
            }
        }

        private void RebuildTimeMarkers()
        {
            for (int i = 0; i < timeMarkers.Count; i++)
                Destroy(timeMarkers[i]);
            timeMarkers.Clear();
            if (selectedDataset == null)
                return;

            Transform rail = spatialRoot.transform.Find("Day 1 to Day 30 rail");
            int groundTimeFirst = 0;
            int groundTimeLast = -1;
            int ignoredDepthFirst;
            int ignoredDepthLast;
            bool hasGroundRange = false;
            if (groundDocked)
                hasGroundRange = TryGetGroundBucketRanges(
                    out groundTimeFirst, out groundTimeLast,
                    out ignoredDepthFirst, out ignoredDepthLast);
            for (int i = 0; i < selectedDataset.TimeCount; i++)
            {
                int timeIndex = i;
                bool inGroundRange = hasGroundRange &&
                    i >= groundTimeFirst && i <= groundTimeLast;
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                marker.name = "Time " + (i + 1);
                marker.layer = 5;
                marker.transform.SetParent(rail, false);
                float x = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                    i / (float)Mathf.Max(1, selectedDataset.TimeCount - 1));
                marker.transform.localPosition = new Vector3(x, 0.0f, 0.0f);
                marker.transform.localScale = Vector3.one *
                    (i == selectedTime ? 0.074f : inGroundRange ? 0.046f : 0.026f);
                Material material = new Material(Shader.Find("Sprites/Default"));
                material.color = i == selectedTime
                    ? Amber
                    : inGroundRange
                        ? TimeColor
                        : new Color(0.20f, 0.37f, 0.44f, 0.78f);
                marker.GetComponent<Renderer>().material = material;
                VolumeSTCubeQuestClickTarget target = marker.AddComponent<VolumeSTCubeQuestClickTarget>();
                target.Clicked = () => SetTime(timeIndex);
                timeMarkers.Add(marker);
            }
        }

        private Canvas CreateFloatingCanvas(string name, Vector3 position, Vector2 size, float scale, Color accent)
        {
            GameObject panelObject = new GameObject(name, typeof(RectTransform));
            panelObject.layer = 5;
            panelObject.transform.SetParent(transform, false);
            panelObject.transform.localPosition = position;
            panelObject.transform.localRotation = Quaternion.identity;
            float displayScale = scale;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                displayScale *= name == "S4D Anchored Facet Grid" ? 1.24f : 1.12f;
#endif
            panelObject.transform.localScale = Vector3.one * displayScale;

            Canvas canvas = panelObject.AddComponent<Canvas>();
            canvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            canvas.worldCamera = xrCamera;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = panelObject.AddComponent<CanvasScaler>();
            // High-density text atlas for readable world-space UI in Quest 3.
            scaler.dynamicPixelsPerUnit =
#if UNITY_EDITOR || SLABLAB_FLAT
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled ? 48.0f :
#endif
                24.0f;
            scaler.referencePixelsPerUnit = 100.0f;
            panelObject.AddComponent<GraphicRaycaster>();

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            Image background = panelObject.AddComponent<Image>();
            background.color = Panel;
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            Shadow panelShadow = panelObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.68f);
            panelShadow.effectDistance = new Vector2(12.0f, -14.0f);
            Outline outline = panelObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.62f);
            outline.effectDistance = new Vector2(2, -2);

            BoxCollider panelCollider = panelObject.AddComponent<BoxCollider>();
            panelCollider.isTrigger = true;
            panelCollider.center = new Vector3(0.0f, 0.0f, 14.0f);
            panelCollider.size = new Vector3(size.x, size.y, 8.0f);
            panelObject.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = accent;

            GameObject chromeObject = new GameObject("Persistent panel chrome", typeof(RectTransform));
            chromeObject.transform.SetParent(rect, false);
            RectTransform chrome = chromeObject.GetComponent<RectTransform>();
            chrome.anchorMin = Vector2.zero;
            chrome.anchorMax = Vector2.one;
            chrome.sizeDelta = Vector2.zero;
            chrome.anchoredPosition = Vector2.zero;

            GameObject headerWash = new GameObject("Header wash", typeof(RectTransform));
            headerWash.transform.SetParent(chrome, false);
            RectTransform headerRect = headerWash.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1);
            headerRect.anchoredPosition = new Vector2(0, -5);
            headerRect.sizeDelta = new Vector2(-12, 66);
            Image headerWashImage = headerWash.AddComponent<Image>();
            headerWashImage.sprite = RoundedUiSprite();
            headerWashImage.type = Image.Type.Sliced;
            headerWashImage.color =
                new Color(accent.r * 0.12f, accent.g * 0.12f, accent.b * 0.12f, 0.72f);
            headerWashImage.raycastTarget = false;

            GameObject topRule = new GameObject("Accent rule", typeof(RectTransform));
            topRule.transform.SetParent(chrome, false);
            RectTransform ruleRect = topRule.GetComponent<RectTransform>();
            ruleRect.anchorMin = new Vector2(0, 1);
            ruleRect.anchorMax = new Vector2(1, 1);
            ruleRect.pivot = new Vector2(0.5f, 1);
            ruleRect.anchoredPosition = Vector2.zero;
            ruleRect.sizeDelta = new Vector2(0, 5);
            Image topRuleImage = topRule.AddComponent<Image>();
            topRuleImage.color = accent;
            topRuleImage.raycastTarget = false;

            GameObject sideRule = new GameObject("Accent edge", typeof(RectTransform));
            sideRule.transform.SetParent(chrome, false);
            RectTransform sideRect = sideRule.GetComponent<RectTransform>();
            sideRect.anchorMin = new Vector2(0, 0);
            sideRect.anchorMax = new Vector2(0, 1);
            sideRect.pivot = new Vector2(0, 0.5f);
            sideRect.anchoredPosition = new Vector2(2, 0);
            sideRect.sizeDelta = new Vector2(3, -10);
            Image sideRuleImage = sideRule.AddComponent<Image>();
            sideRuleImage.color = new Color(accent.r, accent.g, accent.b, 0.34f);
            sideRuleImage.raycastTarget = false;
            return canvas;
        }

        private void CreateWorkflowToolbar()
        {
            workflowToolbarCanvas = CreateFloatingCanvas(
                "S4D persistent workflow toolbar",
                new Vector3(0.0f, 1.05f, 0.82f),
                new Vector2(1510, 128), 0.00052f, Cyan);
            BuildWorkflowToolbar();
        }

        private void BuildWorkflowToolbar()
        {
            if (workflowToolbarCanvas == null)
                return;
            RectTransform content = workflowToolbarCanvas.GetComponent<RectTransform>();
            ClearChildren(content);
            string[] labels =
            {
                "MATPLOT\nINTENT", "FULL\nMATRIX",
                "PIVOT", "DRILL", "ROLL-UP", "LEGACY\nPANEL", "RESTART"
            };
            Action[] actions =
            {
                OpenIntentEditor, BeginGridPlacement,
                () => BeginDraft(DraftOperation.Pivot),
                () => BeginDraft(DraftOperation.Drill),
                () => BeginDraft(DraftOperation.RollUp), ToggleLegacyPanel,
                RestartApplicationWorkflow
            };
            Color[] colors =
            {
                AreSpatialAxisBindingsComplete(out _) && authorBoundaryConfirmed
                    ? Purple : Card,
                intentConfigured ? Amber : Card,
                Purple, TimeColor, Green, Card, Danger
            };
            float width = 188.0f;
            for (int index = 0; index < labels.Length; index++)
            {
                Button toolbarButton = CreateButton(content, labels[index],
                    new Vector2(-594 + index * 198, -5),
                    new Vector2(width - 8, 64), colors[index], actions[index]);
#if UNITY_EDITOR || SLABLAB_FLAT
                if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                {
                    Text toolbarLabel = toolbarButton != null
                        ? toolbarButton.GetComponentInChildren<Text>() : null;
                    if (toolbarLabel != null)
                    {
                        bool multiline = labels[index].IndexOf('\n') >= 0;
                        int fillFontSize = multiline
                            ? 30
                            : labels[index].Length >= 7 ? 43 : 50;
                        toolbarLabel.fontSize = fillFontSize;
                        toolbarLabel.resizeTextForBestFit = false;
                        toolbarLabel.resizeTextMinSize = fillFontSize;
                        toolbarLabel.resizeTextMaxSize = fillFontSize;
                        toolbarLabel.fontStyle = FontStyle.Bold;
                        toolbarLabel.lineSpacing = multiline ? 0.76f : 1.0f;
                        toolbarLabel.rectTransform.sizeDelta =
                            new Vector2(width - 18.0f, 58.0f);
                    }
                }
#endif
                UpgradeButtonLabelToCrispText(toolbarButton, labels[index]);
            }
        }

        private void RestartApplicationWorkflow()
        {
            // Return to the first screen without restarting the Android process.
            // Clear every transient analysis object first so re-entering the
            // workspace cannot reveal a previous Matrix, draft, or boundary.
            if (s4dClient != null)
                s4dClient.Cancel();
            jobRunning = false;
            sourcePreviewRunning = false;
            digestRunning = false;
            intentResolving = false;
            intentConfigured = false;
            slabPreviewBuilt = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            draftOperation = DraftOperation.None;
            if (gridProgressAnimation != null)
            {
                StopCoroutine(gridProgressAnimation);
                gridProgressAnimation = null;
            }

            ClearSourcePreviewLayers();
            ClearAnalysisHistory();
            ClearChart();
            ClearS4DGrid();
            ClearPairedVariableVolumes();
            ResetAxisBucketSelection();
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellLocalized, 0, facetCellLocalized.Length);
            Array.Clear(facetCellStale, 0, facetCellStale.Length);

            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates[index];
                state.boundVariable = -1;
                state.timeAxis = -1;
                state.depthAxis = -1;
                state.variableAxis = -1;
                state.timeRole = DimensionRole.Faceted;
                state.depthRole = DimensionRole.Faceted;
                state.usesSharedBoundaries = true;
                state.pendingDockAnimation = false;
                state.hasCustomDock = false;
            }
            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Faceted;
            roles[2] = DimensionRole.Mapped;
            roles[3] = DimensionRole.Fixed;
            prompt = DefaultSpatialPrompt;
            progress = 0.0f;
            displayedGridProgress = 0.0f;
            targetGridProgress = 0.0f;
            selectedCellPinned = false;
            gridCellSelected = false;
            boundaryEditActive = false;
            initialBoundarySetupActive = false;

            OpenDatasetImportStage();
            SetStatus("Application restarted. Choose a variable to begin.");
        }

        private void ToolbarGenerateSlab()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("Generate Slab locked: " + missing);
                return;
            }
            stage = Stage.Slab;
            PreviewSlab();
        }

        private bool AreSpatialAxisBindingsComplete(out string missing)
        {
            missing = string.Empty;
            if (spatialAxisRigStates.Count == 0)
            {
                missing = "no shared axis controller is available.";
                return false;
            }
            if (BoundVariableIndices().Count == 0)
            {
                missing = "bind at least one variable to the shared controller.";
                return false;
            }
            SpatialAxisRigState state = spatialAxisRigStates[0];
            if (state.variableAxis < 0)
            {
                missing = "bind Variable to an axis.";
                return false;
            }
            if (state.timeAxis < 0)
            {
                missing = "bind Time to an axis.";
                return false;
            }
            if (state.depthAxis < 0)
            {
                missing = "bind Depth to a different axis.";
                return false;
            }
            if (state.timeAxis == state.depthAxis)
            {
                missing = "Time and Depth need different axes.";
                return false;
            }
            return true;
        }

        private void ToggleLegacyPanel()
        {
            legacyPanelVisible = !legacyPanelVisible;
            if (panelCanvas != null)
            {
                panelCanvas.gameObject.SetActive(legacyPanelVisible);
                if (legacyPanelVisible)
                    BuildStage();
            }
            SetStatus(legacyPanelVisible
                ? "Legacy panel shown for comparison."
                : "Legacy panel hidden; spatial axis controls remain active.");
        }

        private void ReturnToSpatialWorkflow()
        {
            if (legacyPanelVisible && panelCanvas != null)
                ShowPrimaryTool(panelCanvas);
            else if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(mainWorkspaceEntered);
        }

        private void UpdateWorkflowToolbarFollow()
        {
            if (workflowToolbarCanvas == null || xrCamera == null ||
                workflowToolbarPinned ||
                !workflowToolbarCanvas.gameObject.activeSelf)
                return;
            Vector3 forward = Vector3.ProjectOnPlane(
                xrCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = xrCamera.transform.forward;
            Vector3 targetPosition = xrCamera.transform.position +
                forward * 1.02f - Vector3.up * 0.48f;
            float follow = 1.0f - Mathf.Exp(-3.8f * Time.unscaledDeltaTime);
            workflowToolbarCanvas.transform.position = Vector3.Lerp(
                workflowToolbarCanvas.transform.position, targetPosition, follow);
            Quaternion targetRotation = Quaternion.LookRotation(forward, Vector3.up);
            workflowToolbarCanvas.transform.rotation = Quaternion.Slerp(
                workflowToolbarCanvas.transform.rotation, targetRotation, follow);
        }

        private void UpdateVariablePaletteFollow(bool immediate)
        {
            if (variablePaletteRoot == null || xrCamera == null)
                return;
            bool shouldShow = datasetImportConfirmed && mainWorkspaceEntered &&
                spatialRoot != null && spatialRoot.activeInHierarchy;
            variablePaletteRoot.SetActive(shouldShow);
            if (!shouldShow)
                return;
            // The variable control is now a real tri-axis component, not an
            // independent head-follow panel. Its parent rig owns its pose.
            if (variablePaletteRoot.transform.parent != null)
                return;
            bool wasVisible = variablePaletteRoot.activeSelf;
            // Keep the source palette perfectly still while an item is held.
            // A head-following source panel moving behind a ray-following card
            // reads as flicker in-headset and makes the pickup feel detached.
            if (!immediate && draggedPaletteToken != null)
                return;
            if (!wasVisible)
                immediate = true;
            Vector3 gaze = xrCamera.transform.forward.normalized;
            // Keep the palette outside the three-Field viewing arc. It remains
            // comfortably reachable, but no longer sits in front of the right
            // variable's Continuous Field.
            Vector3 targetPosition = xrCamera.transform.position + gaze * 0.94f +
                xrCamera.transform.right * 0.64f + xrCamera.transform.up * 0.060f;
            Vector3 uprightGaze = Vector3.ProjectOnPlane(gaze, Vector3.up);
            if (uprightGaze.sqrMagnitude < 0.001f)
                uprightGaze = gaze;
            Quaternion targetRotation = Quaternion.LookRotation(
                -uprightGaze.normalized, Vector3.up);
            // The palette lives to the viewer's right, so a small clockwise yaw
            // presents its face instead of leaving the right edge receding from
            // the camera. Rotate the complete physical hierarchy as one unit so
            // captions, buttons and colliders remain perfectly aligned.
            targetRotation = Quaternion.AngleAxis(8.0f, Vector3.up) *
                targetRotation;
            // Head tracking contains sub-millimetre motion even while the user
            // is looking still. Re-rendering a high-resolution world-space UI
            // for every one of those samples causes visible shimmer in Quest.
            // Keep the palette pinned until the head actually moves.
            if (!immediate &&
                Vector3.Distance(variablePaletteRoot.transform.position,
                    targetPosition) < 0.035f &&
                Quaternion.Angle(variablePaletteRoot.transform.rotation,
                    targetRotation) < 3.0f)
                return;
            float follow = immediate ? 1.0f :
                1.0f - Mathf.Exp(-7.0f * Time.unscaledDeltaTime);
            variablePaletteRoot.transform.position = Vector3.Lerp(
                variablePaletteRoot.transform.position, targetPosition, follow);
            variablePaletteRoot.transform.rotation = Quaternion.Slerp(
                variablePaletteRoot.transform.rotation, targetRotation, follow);
        }

        private static Sprite RoundedUiSprite()
        {
            if (roundedUiSprite != null)
                return roundedUiSprite;

            const int textureSize = 64;
            const float radius = 15.0f;
            Texture2D texture = new Texture2D(textureSize, textureSize,
                TextureFormat.RGBA32, false);
            texture.name = "S4D Rounded UI";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[textureSize * textureSize];
            Vector2 center = new Vector2(textureSize * 0.5f, textureSize * 0.5f);
            Vector2 half = new Vector2(textureSize * 0.5f - radius,
                textureSize * 0.5f - radius);
            for (int y = 0; y < textureSize; y++)
            {
                for (int x = 0; x < textureSize; x++)
                {
                    Vector2 point = new Vector2(x + 0.5f, y + 0.5f) - center;
                    Vector2 corner = new Vector2(
                        Mathf.Max(Mathf.Abs(point.x) - half.x, 0.0f),
                        Mathf.Max(Mathf.Abs(point.y) - half.y, 0.0f));
                    float distance = corner.magnitude - radius;
                    byte alpha = (byte)Mathf.RoundToInt(
                        Mathf.Clamp01(0.5f - distance) * 255.0f);
                    pixels[y * textureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            texture.hideFlags = HideFlags.HideAndDontSave;
            roundedUiSprite = Sprite.Create(texture,
                new Rect(0, 0, textureSize, textureSize),
                new Vector2(0.5f, 0.5f), 100.0f, 0, SpriteMeshType.FullRect,
                new Vector4(18, 18, 18, 18));
            roundedUiSprite.name = "S4D Rounded UI Sprite";
            roundedUiSprite.hideFlags = HideFlags.HideAndDontSave;
            return roundedUiSprite;
        }

        private static Image CreateDecorativeSurface(RectTransform parent, string name,
            Vector2 position, Vector2 size, Color color)
        {
            GameObject surfaceObject = new GameObject(name, typeof(RectTransform));
            surfaceObject.transform.SetParent(parent, false);
            RectTransform surface = surfaceObject.GetComponent<RectTransform>();
            surface.sizeDelta = size;
            surface.anchoredPosition = position;
            Image image = surfaceObject.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void CreateMainMenu()
        {
            mainMenuCanvas = CreateFloatingCanvas(
                "S4D Main Menu",
                PrimaryToolDockPosition,
                new Vector2(560, 700),
                0.00072f,
                Cyan);
            mainMenuContent = mainMenuCanvas.GetComponent<RectTransform>();
            BuildMainMenu();
        }

        private void BuildMainMenu()
        {
            if (mainMenuContent == null)
                return;
            ClearChildren(mainMenuContent);
            CreateText(mainMenuContent, "S4D CANVAS", 18, FontStyle.Bold,
                new Vector2(0, 300), new Vector2(480, 28), TextAnchor.MiddleLeft, Muted);
            CreateText(mainMenuContent, "MAIN MENU", 34, FontStyle.Bold,
                new Vector2(0, 258), new Vector2(480, 48), TextAnchor.MiddleLeft, Ink);
            CreateText(mainMenuContent, "Continuous evidence and analysis workspace",
                16, FontStyle.Normal, new Vector2(0, 222), new Vector2(480, 28),
                TextAnchor.MiddleLeft, Muted);

            mainMenuDataLabel = CreateText(mainMenuContent,
                cubeVisible ? "DATA CONTAINER  /  VISIBLE" : "DATA CONTAINER  /  HIDDEN",
                15, FontStyle.Bold, new Vector2(0, 166), new Vector2(460, 26),
                TextAnchor.MiddleLeft, cubeVisible ? Green : Muted);

            CreateMenuButton(mainMenuContent,
                cubeVisible ? "Hide continuous data" : "Show continuous data",
                "Toggle the S4D Cube Container",
                new Vector2(0, 105), Cyan, ToggleDataVisibility);
            CreateMenuButton(mainMenuContent,
                "Author Boundary",
                "Time / Depth / Horizontal buckets",
                new Vector2(0, 8), Amber, ToggleBoundaryPanel);
            CreateMenuButton(mainMenuContent,
                smallMultiples ? "Display: small multiples" : "Display: animated volume",
                "Switch the continuous-field presentation",
                new Vector2(0, -89), Green, ToggleDataPresentation);
            CreateMenuButton(mainMenuContent,
                "Slab Frame",
                "Configure a new analysis",
                new Vector2(0, -186), Purple, ToggleSlabFrame);
            CreateMenuButton(mainMenuContent,
                "SlabTrail",
                "History, snapshots and workspace navigation",
                new Vector2(0, -283), Cyan, ToggleTrailPanel);
        }

        private void CreateMenuButton(RectTransform parent, string title, string subtitle,
            Vector2 position, Color accent, Action action)
        {
            GameObject cardObject = new GameObject(title, typeof(RectTransform));
            cardObject.layer = 5;
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = new Vector2(480, 82);
            card.anchoredPosition = position;
            Image image = cardObject.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = Card;
            Shadow cardShadow = cardObject.AddComponent<Shadow>();
            cardShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.44f);
            cardShadow.effectDistance = new Vector2(4, -5);
            Outline outline = cardObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.36f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject stripeObject = new GameObject("Dimension stripe", typeof(RectTransform));
            stripeObject.transform.SetParent(card, false);
            RectTransform stripe = stripeObject.GetComponent<RectTransform>();
            stripe.anchorMin = new Vector2(0, 0);
            stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            stripe.sizeDelta = new Vector2(7, 0);
            stripeObject.AddComponent<Image>().color = accent;

            CreateText(card, title, 20, FontStyle.Bold, new Vector2(16, 14),
                new Vector2(420, 30), TextAnchor.MiddleLeft, Ink);
            CreateText(card, subtitle, 14, FontStyle.Normal, new Vector2(16, -17),
                new Vector2(420, 24), TextAnchor.MiddleLeft, Muted);
            BoxCollider collider = cardObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(480, 82, 12);
            cardObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private void CreateBoundaryPanel()
        {
            boundaryCanvas = CreateFloatingCanvas(
                "Author Boundary Panel",
                BoundaryToolDockPosition,
                new Vector2(900, 760),
                0.00060f,
                Amber);
            boundaryContent = boundaryCanvas.GetComponent<RectTransform>();
            boundaryCanvas.gameObject.SetActive(false);
        }

        private void ToggleBoundaryPanel()
        {
            if (boundaryCanvas == null)
                return;
            bool next = !boundaryCanvas.gameObject.activeSelf;
            if (next)
            {
                initialBoundarySetupActive = false;
                BeginBoundaryEditSession(boundaryDimension);
            }
            else
            {
                CancelBoundaryEdit();
            }
        }

        private void BeginBoundaryEditSession(BoundaryDimension dimension)
        {
            // A panel may have been grabbed in an earlier step. Every authored
            // boundary session starts from the tested, fully visible dock.
            if (boundaryCanvas != null)
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
            boundaryReturnStage = stage;
            boundaryDimension = dimension;
            savedTimeBoundaryStart = timeBoundaryStart;
            savedTimeBoundaryEnd = timeBoundaryEnd;
            savedSelectedTime = selectedTime;
            savedDepthBoundaryLow = depthBoundaryLow;
            savedDepthBoundaryHigh = depthBoundaryHigh;
            boundaryEditActive = true;
            bool horizontalEditing = dimension == BoundaryDimension.Horizontal;
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(horizontalEditing);
            if (regionRoot != null)
                regionRoot.SetActive(horizontalEditing);
            FrameVolume();
            if (dimension == BoundaryDimension.Time ||
                dimension == BoundaryDimension.Depth)
                EnterBoundaryAuthoringView();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            ShowPrimaryTool(boundaryCanvas);
            if (!cubeVisible)
                ToggleDataVisibility();
            ApplyPrimaryVolumeVisibility();
            BuildBoundaryPanel();
        }

        private void CancelBoundaryEdit()
        {
            bool cancelledInitialSetup = initialBoundarySetupActive;
            EndDepthSliceInspection(true);
            if (boundaryEditActive)
            {
                timeBoundaryStart = savedTimeBoundaryStart;
                timeBoundaryEnd = savedTimeBoundaryEnd;
                depthBoundaryLow = savedDepthBoundaryLow;
                depthBoundaryHigh = savedDepthBoundaryHigh;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                selectedTime = savedSelectedTime;
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
                FrameVolume();
            }
            boundaryEditActive = false;
            initialBoundarySetupActive = false;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            ResetBoundaryInteractionFieldCenter();
            timeBoundaryDragging = false;
            depthBoundaryDragging = false;
            if (boundaryCanvas != null)
                boundaryCanvas.gameObject.SetActive(false);
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(false);
            if (regionRoot != null)
                regionRoot.SetActive(false);
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                forVrSurfacePlayer != null)
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
            ExitBoundaryAuthoringView();
            ReturnToSpatialWorkflow();
            stage = cancelledInitialSetup && !authorBoundaryConfirmed
                ? Stage.Field
                : boundaryReturnStage;
            BuildStage();
            SetStatus(cancelledInitialSetup
                ? "Author Boundary cancelled. Choose a variable to start again."
                : "Boundary edit cancelled. Previous bucket ranges restored.");
        }

        private void BuildBoundaryPanel()
        {
            if (boundaryContent == null)
                return;
            ClearChildren(boundaryContent);
            bool combinedForVrTime = IsForVrSurfaceDataset &&
                boundaryDimension == BoundaryDimension.Time;
            SetTimeBoundaryHandleVisibility(
                boundaryDimension == BoundaryDimension.Time &&
                !combinedForVrTime);
            SetDepthBoundaryVisibility(
                boundaryDimension == BoundaryDimension.Depth);
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                BuildDesktopBoundaryBar(combinedForVrTime);
                return;
            }
            CreateText(boundaryContent, "TEST REGIONS", 29, FontStyle.Bold,
                new Vector2(0, 330), new Vector2(820, 44), TextAnchor.MiddleLeft, Ink);
            SpatialAxisRigState activeBoundaryState =
                ActiveVariableBoundaryState();
            bool customBoundary = activeBoundaryState != null &&
                !activeBoundaryState.usesSharedBoundaries;
            CreateText(boundaryContent, BoundaryVariableProgressLabel(),
                14, FontStyle.Bold, new Vector2(-275, 291),
                new Vector2(270, 28), TextAnchor.MiddleLeft, VariableColor);
            CreateButton(boundaryContent, "SHARED", new Vector2(105, 291),
                new Vector2(190, 34), customBoundary ? Card : Cyan,
                () => SetActiveBoundaryScope(false));
            CreateButton(boundaryContent, "CUSTOM", new Vector2(310, 291),
                new Vector2(190, 34), customBoundary ? VariableColor : Card,
                () => SetActiveBoundaryScope(true));

            string[] tabs = initialBoundarySetupActive
                ? IsForVrSurfaceDataset
                    ? new[] { initialTimeBoundaryComplete ? "TIME SAVED" : "TIME" }
                    : new[]
                {
                    initialTimeBoundaryComplete ? "1  TIME SAVED" : "1  TIME",
                    initialDepthBoundaryComplete ? "2  DEPTH SAVED" : "2  DEPTH"
                }
                : new[] { "TIME", "DEPTH", "HORIZONTAL", "VARIABLE" };
            Color[] colors = { TimeColor, DepthColor, HorizontalColor, VariableColor };
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                float tabX = tabs.Length == 1 ? 0 : tabs.Length == 2 ? -210 + i * 420 : -315 + i * 210;
                float tabWidth = tabs.Length == 1 ? 400 : tabs.Length == 2 ? 390 : 192;
                CreateButton(boundaryContent, tabs[i], new Vector2(tabX, 235),
                    new Vector2(tabWidth, 48), (int)boundaryDimension == i ? colors[i] : Card,
                    () => SelectBoundaryDimension(index));
            }

            string instruction;
            string current;
            Color active = colors[(int)boundaryDimension];
            switch (boundaryDimension)
            {
                case BoundaryDimension.Time:
                    if (roles[0] == DimensionRole.Fixed)
                    {
                        instruction = "TIME  /  FIXED";
                        current = "FIXED TIME  " +
                            (selectedDataset != null
                                ? selectedDataset.GetTimeLabel(selectedTime)
                                : "day " + (selectedTime + 1)) +
                            "";
                    }
                    else
                    {
                        instruction = "BEFORE   /   DURING   /   AFTER";
                        // Keep the primary Time readout short enough to fill the
                        // centre card. The selected days should be identifiable
                        // in one glance, even in the desktop Game view.
                        current = TimeRangeSummary().ToUpperInvariant();
                    }
                    break;
                case BoundaryDimension.Depth:
                    if (roles[1] == DimensionRole.Fixed)
                    {
                        instruction = "DEPTH  /  FIXED";
                        current = "FIXED DEPTH  z=" + selectedZ +
                            "";
                    }
                    else
                    {
                        instruction = "SURFACE  ·  MIDDLE  ·  DEEP";
                        current = DepthBoundaryLabel();
                    }
                    break;
                case BoundaryDimension.Horizontal:
                    instruction = "REGION";
                    current = "TRIGGER + DRAG";
                    break;
                default:
                    instruction = "VARIABLE GROUP";
                    current = DatasetNamesSummary();
                    break;
            }

            CreatePanelCard(boundaryContent, new Vector2(0, 105), new Vector2(820, 190), active);
            CreateText(boundaryContent, instruction, 30, FontStyle.Bold,
                new Vector2(0, 148), new Vector2(750, 58),
                TextAnchor.MiddleLeft, Ink);
            boundaryCurrentRangeText = CreateText(boundaryContent, current,
                27, FontStyle.Bold,
                new Vector2(0, 80), new Vector2(750, 58),
                TextAnchor.MiddleLeft, active);

            if (boundaryDimension == BoundaryDimension.Horizontal)
            {
                CreateButton(boundaryContent, drawingRegion ? "Cancel circle" : "Draw circle on Cube",
                    new Vector2(-205, -38), new Vector2(380, 62),
                    drawingRegion ? Danger : HorizontalColor, ToggleRegionDrawing);
                CreateButton(boundaryContent, "Reset region", new Vector2(225, -38),
                    new Vector2(360, 62), Card, CenterRegion);
            }
            else if (combinedForVrTime)
            {
                CreateText(boundaryContent,
                    "DRAG CUT A AND CUT B DIRECTLY INSIDE THE SIX-EVENT STC",
                    18, FontStyle.Bold, new Vector2(0, -38),
                    new Vector2(790, 62), TextAnchor.MiddleCenter, TimeColor);
            }
            else
            {
                bool fixedDimension =
                    boundaryDimension == BoundaryDimension.Time
                        ? roles[0] == DimensionRole.Fixed
                        : roles[1] == DimensionRole.Fixed;
                string lower = fixedDimension
                    ? boundaryDimension == BoundaryDimension.Time
                        ? "PREVIOUS DAY" : "SHALLOWER"
                    : boundaryDimension == BoundaryDimension.Time
                        ? "MOVE CUT A  -" : "MOVE LOWER  -";
                string upper = fixedDimension
                    ? boundaryDimension == BoundaryDimension.Time
                        ? "NEXT DAY" : "DEEPER"
                    : boundaryDimension == BoundaryDimension.Time
                        ? "MOVE CUT B  +" : "MOVE UPPER  +";
                CreateButton(boundaryContent, lower, new Vector2(-205, -38),
                    new Vector2(380, 62), Card, () =>
                    {
                        if (fixedDimension)
                            NudgeBoundaryFixedValue(-1);
                        else
                            NudgeActiveBoundary(-1);
                    });
                CreateButton(boundaryContent, upper, new Vector2(225, -38),
                    new Vector2(360, 62), Card, () =>
                    {
                        if (fixedDimension)
                            NudgeBoundaryFixedValue(1);
                        else
                            NudgeActiveBoundary(1);
                    });
            }

            CreateButton(boundaryContent,
                initialBoundarySetupActive ? "BACK TO DATA" : "CANCEL",
                new Vector2(-220, -285),
                new Vector2(330, 64), Card, CancelBoundaryEdit);
            string confirmLabel = initialBoundarySetupActive
                ? boundaryDimension == BoundaryDimension.Time
                    ? IsForVrSurfaceDataset
                        ? "CONFIRM TIME RANGE"
                        : "SAVE TIME  >  DEPTH"
                    : boundaryVariableQueueIndex + 1 < boundaryVariableQueue.Count
                        ? "SAVE FIELD  >  NEXT VARIABLE"
                        : "SAVE ALL  >  OPEN SLAB"
                : "CONFIRM & SAVE";
            CreateButton(boundaryContent, confirmLabel, new Vector2(205, -285),
                new Vector2(430, 64), active, ApplyBoundaryChange);

            // Match the crisp SDF UI used by the original STC project without
            // changing this panel's or any button's authored dimensions.
            UpgradeCanvasLabelsToCrispText(boundaryContent,
                boundaryDimension == BoundaryDimension.Time ? current : null);
        }

        private void BuildDesktopBoundaryBar(bool combinedForVrTime)
        {
            RectTransform rect = boundaryCanvas != null
                ? boundaryCanvas.GetComponent<RectTransform>() : null;
            if (rect != null)
                rect.sizeDelta = new Vector2(1500.0f, 150.0f);
            BoxCollider collider = boundaryCanvas != null
                ? boundaryCanvas.GetComponent<BoxCollider>() : null;
            if (collider != null)
                collider.size = new Vector3(1500.0f, 150.0f, 8.0f);

            Color active = boundaryDimension == BoundaryDimension.Time
                ? TimeColor : boundaryDimension == BoundaryDimension.Depth
                    ? DepthColor : Cyan;
            string current = boundaryDimension == BoundaryDimension.Time
                ? TimeRangeSummary().ToUpperInvariant()
                : boundaryDimension == BoundaryDimension.Depth
                    ? DepthBoundaryLabel() : BoundaryDefaultLabel();
            boundaryCurrentRangeText = CreateText(boundaryContent, current,
                22, FontStyle.Bold, new Vector2(-480, 0),
                new Vector2(500, 72), TextAnchor.MiddleLeft, active);

            float backX = 225.0f;
            if (combinedForVrTime && forVrSurfacePlayer != null)
            {
                CreateButton(boundaryContent,
                    forVrSurfacePlayer.PlaybackButtonLabel,
                    new Vector2(-180, 0), new Vector2(170, 62), Cyan,
                    () =>
                    {
                        forVrSurfacePlayer.TogglePlayback();
                        BuildBoundaryPanel();
                    });
                CreateButton(boundaryContent,
                    forVrSurfacePlayer.PlaybackSpeedLabel,
                    new Vector2(15, 0), new Vector2(190, 62), Card,
                    () =>
                    {
                        forVrSurfacePlayer.CyclePlaybackSpeed();
                        BuildBoundaryPanel();
                    });
            }
            else
            {
                backX = -15.0f;
            }
            CreateButton(boundaryContent,
                initialBoundarySetupActive ? "BACK" : "CANCEL",
                new Vector2(backX, 0), new Vector2(180, 62), Card,
                CancelBoundaryEdit);
            CreateButton(boundaryContent,
                boundaryDimension == BoundaryDimension.Time
                    ? "CONFIRM TIME RANGE" : "CONFIRM",
                new Vector2(backX + 245.0f, 0),
                new Vector2(280, 62), active, ApplyBoundaryChange);
            UpgradeCanvasLabelsToCrispText(boundaryContent, current);
        }

        private void CreateTrailPanel()
        {
            trailCanvas = CreateFloatingCanvas(
                "S4D SlabTrail",
                PrimaryToolDockPosition,
                new Vector2(920, 650),
                0.00068f,
                Purple);
            trailContent = trailCanvas.GetComponent<RectTransform>();
            trailCanvas.gameObject.SetActive(false);
        }

        private void CreateFacetGridPanel()
        {
            facetGridCanvas = CreateFloatingCanvas(
                "S4D Anchored Facet Grid",
                new Vector3(0.28f, 1.76f, 1.20f),
                // One shared, larger editor surface is used by Pivot, Drill,
                // and Roll-up.  The extra margin keeps the row/column chips,
                // preview, and footer readable in Quest without overlap.
                new Vector2(1560, 900),
                0.00059f,
                Cyan);
            facetGridContent = facetGridCanvas.GetComponent<RectTransform>();
            facetGridCanvasGroup = facetGridCanvas.gameObject.AddComponent<CanvasGroup>();
            facetGridCanvas.sortingOrder = 105;
            facetGridCanvas.gameObject.SetActive(false);
        }

        private void CreateAiFindingsPanel()
        {
            aiFindingsCanvas = CreateFloatingCanvas(
                "AI Findings",
                new Vector3(0.22f, 1.76f, 1.12f),
                new Vector2(1360, 980),
                0.00058f,
                Green);
            aiFindingsContent = aiFindingsCanvas.GetComponent<RectTransform>();
            aiFindingsCanvas.sortingOrder = 132;
            aiFindingsCanvas.gameObject.SetActive(false);
        }

        private void CreateSlabPreviewPanel()
        {
            slabPreviewCanvas = CreateFloatingCanvas(
                "FacetSlab Configuration Preview",
                // Upper-left lane, inside the default Quest/desktop forward view.
                // Its right edge stops before the intent composer begins.
                SlabPreviewDockPosition,
                new Vector2(900, 610),
                0.00066f,
                Green);
            slabPreviewContent = slabPreviewCanvas.GetComponent<RectTransform>();
            slabPreviewCanvas.sortingOrder = 118;
            slabPreviewCanvas.gameObject.SetActive(false);
        }

        private void CreateIntentPanel()
        {
            intentCanvas = CreateFloatingCanvas(
                "MatPlotAgent Intent Composer",
                // Separate upper-right lane. Keep the entire prompt and action row
                // inside the initial forward view instead of requiring a head turn.
                IntentToolDockPosition,
                new Vector2(650, 520),
                0.00060f,
                Purple);
            intentContent = intentCanvas.GetComponent<RectTransform>();
            intentCanvas.sortingOrder = 122;
            intentCanvas.gameObject.SetActive(false);
        }

        private void CreateDraftPanel()
        {
            draftCanvas = CreateFloatingCanvas(
                "Spatial Analysis Draft",
                DraftToolDockPosition,
                new Vector2(760, 270),
                0.00062f,
                Purple);
            draftContent = draftCanvas.GetComponent<RectTransform>();
            draftCanvas.sortingOrder = 124;
            draftCanvas.gameObject.SetActive(false);
        }

        private void BuildSpatialDraftPanel()
        {
            if (draftContent == null || draftOperation == DraftOperation.None)
                return;
            ClearChildren(draftContent);
            Color accent = OperationColor(draftOperation);
            string title = draftOperation == DraftOperation.Pivot
                ? "PIVOT"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL"
                    : "ROLL-UP";
            CreateText(draftContent, title,
                28, FontStyle.Bold, new Vector2(-150, 96), new Vector2(400, 40),
                TextAnchor.MiddleLeft, Ink);
            CreateText(draftContent,
                draftOperation == DraftOperation.Pivot
                    ? "CLICK THE GRID TO SWAP AXES"
                    : draftOperation == DraftOperation.Drill
                        ? "CLICK A ROW OR COLUMN TO EXPAND"
                        : "CLICK ADJACENT CELLS TO MERGE",
                14, FontStyle.Bold, new Vector2(90, 96), new Vector2(460, 30),
                TextAnchor.MiddleRight, accent);

            if (draftOperation == DraftOperation.Pivot)
            {
                CreateText(draftContent,
                    pivotTransposed ? "DEPTH ACROSS  /  TIME DOWN" :
                        "TIME ACROSS  /  DEPTH DOWN",
                    16, FontStyle.Bold, new Vector2(0, 34),
                    new Vector2(690, 30), TextAnchor.MiddleCenter, Ink);
            }
            else
            {
                CreateButton(draftContent,
                    "TIME", new Vector2(-105, 34), new Vector2(190, 42),
                    draftTargetDimension == 0 ? TimeColor : Card,
                    () => SetDraftTargetDimension(0));
                CreateButton(draftContent,
                    "DEPTH", new Vector2(105, 34), new Vector2(190, 42),
                    draftTargetDimension == 1 ? DepthColor : Card,
                    () => SetDraftTargetDimension(1));
            }

            bool ready = DraftSelectionReady(out string selectionHint);
            CreateText(draftContent, ready ? DraftGridSummary() : selectionHint,
                13, FontStyle.Bold, new Vector2(-55, -18), new Vector2(570, 28),
                TextAnchor.MiddleLeft, ready ? Muted : Danger);
            CreateButton(draftContent, "CANCEL", new Vector2(-205, -91),
                new Vector2(250, 48), Card, CancelDraft);
            CreateButton(draftContent, "APPLY",
                new Vector2(155, -91), new Vector2(420, 48),
                ready ? accent : Card, ConfirmDraftAndGenerate);
        }

        private void ConfirmDraftAndGenerate()
        {
            if (!DraftSelectionReady(out string hint))
            {
                SetStatus(hint);
                if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                return;
            }
            // A bucket operation creates a new analytical branch. Preserve the
            // source matrix as an immutable world-space result before the live
            // Facet Grid is cleared and reused for the new MatPlotAgent job.
            // This makes the before/after relationship visible instead of
            // replacing the evidence the user operated on.
            RetainCurrentResultBesideLiveGrid();
            // Pivot commits the copied Slab's role changes only now. Cancelling
            // the draft therefore leaves both the source Slab and the active
            // axis composer untouched.
            ApplyPivotDraftConfiguration();
            // These operations transform an existing materialized result, so
            // reuse its resolved chart intent and start the new Grid directly.
            if (!intentConfigured)
            {
                AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                    currentAnalysisNode;
                if (source != null)
                {
                    if (!string.IsNullOrWhiteSpace(source.rawIntent))
                        prompt = source.rawIntent;
                    if (!string.IsNullOrWhiteSpace(source.analyticTask))
                        intentTask = source.analyticTask;
                    if (!string.IsNullOrWhiteSpace(source.intentDisplayLabel))
                        intentMode = source.intentDisplayLabel;
                    intentConfigured = source.hasResolvedIntent ||
                        !string.IsNullOrWhiteSpace(source.rawIntent);
                }
            }
            if (!intentConfigured)
            {
                // A transformed result must never reopen the composer. When a
                // legacy node lacks persisted intent metadata, retain the
                // current chart wording and use the safe distribution task.
                if (string.IsNullOrWhiteSpace(prompt))
                    prompt = "Recreate the transformed comparison using the current chart style.";
                if (string.IsNullOrWhiteSpace(intentTask))
                    intentTask = "distribution";
                if (string.IsNullOrWhiteSpace(intentMode))
                    intentMode = "DISTRIBUTION";
                intentConfigured = true;
            }
            BuildS4DGridRequest();
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            stage = Stage.Matrix;
            materializationVariableCursor = -1;
            // Clear every visual trace of the source result before rebuilding
            // the generation view. StartS4DGridJob also initializes these
            // values, but the panel is intentionally rebuilt once before that
            // call so the transition feels immediate.
            DestroyTextures(streamingCellTextures);
            progress = 0.0f;
            displayedGridProgress = 0.0f;
            targetGridProgress = 0.0f;
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            SetStatus(draftOperation +
                " confirmed. Starting MatPlotAgent generation...");
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            StartS4DGridJob();
        }

        private void RetainCurrentResultBesideLiveGrid()
        {
            if (facetGridCanvas == null)
                return;
            AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                currentAnalysisNode;
            if (source == null || source.gridImage == null ||
                string.IsNullOrWhiteSpace(source.nodeId))
                return;
            for (int index = 0; index < retainedResultViews.Count; index++)
                if (retainedResultViews[index] != null &&
                    retainedResultViews[index].nodeId == source.nodeId)
                    return;

            // Keep at most the two most recent ancestors in the immediate
            // comparison lane. Older branches remain recoverable in SlabTrail.
            while (retainedResultViews.Count >= 2)
                DestroyRetainedResultView(retainedResultViews[0]);

            Vector3 right = facetGridCanvas.transform.right.normalized;
            // Each panel moves half a lane, producing roughly 0.84 m between
            // centers: enough for the 0.59 m snapshot and 0.92 m live panel
            // to sit side by side without pushing either outside the VR view.
            const float branchSpacing = 0.42f;
            for (int index = 0; index < retainedResultViews.Count; index++)
            {
                Canvas retained = retainedResultViews[index].canvas;
                if (retained != null)
                    retained.transform.position -= right * branchSpacing;
            }

            Canvas snapshot = CreateFloatingCanvas(
                "Retained MatPlot Result " + source.nodeId,
                Vector3.zero, new Vector2(1180, 720), 0.00050f, Purple);
            snapshot.sortingOrder = 103;
            snapshot.transform.position = facetGridCanvas.transform.position -
                right * branchSpacing;
            snapshot.transform.rotation = facetGridCanvas.transform.rotation;
            snapshot.gameObject.SetActive(true);
            RetainedResultView view = new RetainedResultView
            {
                nodeId = source.nodeId,
                canvas = snapshot
            };
            retainedResultViews.Add(view);
            BuildRetainedResultView(view, source);

            // The active surface is the destination for the new branch. Moving
            // it to the adjacent lane makes generation progress visible while
            // the immutable source remains available for comparison.
            facetGridCanvas.transform.position += right * branchSpacing;
            RecordTrailEvent("BRANCH",
                source.nodeId + " retained beside the new " + draftOperation +
                " result", source);
        }

        private void BuildRetainedResultView(RetainedResultView view,
            AnalysisNodeState node)
        {
            if (view == null || view.canvas == null || node == null ||
                node.gridImage == null)
                return;
            RectTransform root = view.canvas.GetComponent<RectTransform>();
            CreateText(root, "PREVIOUS RESULT", 14, FontStyle.Bold,
                new Vector2(-520, 315), new Vector2(240, 28),
                TextAnchor.MiddleLeft, Purple);
            CreateText(root, string.IsNullOrWhiteSpace(node.title)
                    ? node.nodeId : node.title,
                25, FontStyle.Bold, new Vector2(-520, 278),
                new Vector2(760, 42), TextAnchor.MiddleLeft, Ink);
            string variable = string.IsNullOrWhiteSpace(node.variableId)
                ? "VARIABLE" : node.variableId.ToUpperInvariant();
            CreateText(root, node.nodeId + "  /  " + variable,
                13, FontStyle.Bold, new Vector2(-520, 242),
                new Vector2(760, 28), TextAnchor.MiddleLeft, Muted);

            int timeCount = Mathf.Clamp(node.timeBuckets != null
                ? node.timeBuckets.Length : 1, 1, MaxFacetAxisBuckets);
            int depthCount = Mathf.Clamp(node.depthBuckets != null
                ? node.depthBuckets.Length : 1, 1, MaxFacetAxisBuckets);
            int columns = node.gridTransposed ? depthCount : timeCount;
            int rows = node.gridTransposed ? timeCount : depthCount;
            Vector2 gridCenter = new Vector2(-55, -20);
            Vector2 gridSize = new Vector2(1000, 470);
            float cellWidth = gridSize.x / columns;
            float cellHeight = gridSize.y / rows;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int timeIndex = node.gridTransposed ? row : column;
                    int depthIndex = node.gridTransposed ? column : row;
                    string timeLabel = ActiveBucketLabel(node.timeBuckets,
                        timeIndex, new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(node.depthBuckets,
                        depthIndex, new[] { "surface", "middle", "deep" });
                    Vector2 center = gridCenter + new Vector2(
                        -gridSize.x * 0.5f + cellWidth * (column + 0.5f),
                        gridSize.y * 0.5f - cellHeight * (row + 0.5f));
                    CreateRetainedResultCell(root, node.gridImage,
                        center, new Vector2(cellWidth - 12, cellHeight - 12),
                        timeIndex, depthIndex, timeCount, depthCount,
                        depthLabel + "  /  " + timeLabel);
                }
            }

            CreateText(root, "IMMUTABLE SOURCE", 11, FontStyle.Bold,
                new Vector2(-485, -318), new Vector2(260, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateButton(root, "CLOSE", new Vector2(465, -315),
                new Vector2(170, 42), Card,
                () => DestroyRetainedResultView(view));
        }

        private void CreateRetainedResultCell(RectTransform parent,
            Texture2D atlas, Vector2 position, Vector2 size, int timeIndex,
            int depthIndex, int timeCount, int depthCount, string label)
        {
            GameObject cellObject = new GameObject(
                "Retained " + label, typeof(RectTransform));
            cellObject.layer = 5;
            cellObject.transform.SetParent(parent, false);
            RectTransform cell = cellObject.GetComponent<RectTransform>();
            cell.sizeDelta = size;
            cell.anchoredPosition = position;
            Image background = cellObject.AddComponent<Image>();
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            background.color = new Color(0.010f, 0.024f, 0.035f, 1.0f);
            background.raycastTarget = false;
            Outline outline = cellObject.AddComponent<Outline>();
            outline.effectColor = new Color(Purple.r, Purple.g, Purple.b, 0.58f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject imageObject = new GameObject(
                "Retained chart", typeof(RectTransform));
            imageObject.transform.SetParent(cell, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.sizeDelta = new Vector2(
                Mathf.Max(24, size.x - 14), Mathf.Max(20, size.y - 38));
            imageRect.anchoredPosition = new Vector2(0, -6);
            RawImage image = imageObject.AddComponent<RawImage>();
            image.texture = atlas;
            image.color = Color.white;
            image.raycastTarget = false;
            image.uvRect = new Rect(
                timeIndex / (float)Mathf.Max(1, timeCount),
                (Mathf.Max(1, depthCount) - 1 - depthIndex) /
                    (float)Mathf.Max(1, depthCount),
                1.0f / Mathf.Max(1, timeCount),
                1.0f / Mathf.Max(1, depthCount));
            CreateText(cell, label.ToUpperInvariant(),
                Mathf.Clamp(15 - Mathf.Max(timeCount, depthCount), 8, 12),
                FontStyle.Bold, new Vector2(0, size.y * 0.5f - 14),
                new Vector2(size.x - 16, 22), TextAnchor.MiddleLeft, Purple);
        }

        private void DestroyRetainedResultView(RetainedResultView view)
        {
            if (view == null)
                return;
            retainedResultViews.Remove(view);
            if (view.canvas != null)
                Destroy(view.canvas.gameObject);
        }

        private void ClearRetainedResultViews()
        {
            for (int index = retainedResultViews.Count - 1; index >= 0; index--)
            {
                RetainedResultView view = retainedResultViews[index];
                if (view != null && view.canvas != null)
                    Destroy(view.canvas.gameObject);
            }
            retainedResultViews.Clear();
        }

        private bool DraftSelectionReady(out string hint)
        {
            hint = string.Empty;
            if (draftOperation == DraftOperation.None ||
                draftOperation == DraftOperation.Pivot)
                return true;
            if (draftTargetDimension < 0 || draftTargetDimension > 1 ||
                roles[draftTargetDimension] != DimensionRole.Faceted)
            {
                hint = "CHOOSE A FACETED TIME OR DEPTH AXIS";
                return false;
            }
            bool[] selected = draftTargetDimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            if (draftOperation == DraftOperation.Drill)
            {
                if (CountSelectedTicks(selected) > 0)
                    return true;
                hint = "SELECT AT LEAST ONE BUCKET TO OPEN";
                return false;
            }
            int[] groups = draftTargetDimension == 0
                ? timeRollupGroups : depthRollupGroups;
            int count = DraftBucketCount(draftTargetDimension);
            for (int index = 1; index < count; index++)
            {
                if (groups[index] > 0 && groups[index] == groups[index - 1])
                    return true;
            }
            hint = "MARK TWO ADJACENT BUCKETS WITH THE SAME GROUP";
            return false;
        }

        private string DraftGridSummary()
        {
            S4DIndexBucketRequest[] time = DraftSourceBuckets(0);
            S4DIndexBucketRequest[] depth = DraftSourceBuckets(1);
            int oldTime = time != null ? Mathf.Max(1, time.Length) : 1;
            int oldDepth = depth != null ? Mathf.Max(1, depth.Length) : 1;
            int nextTime = oldTime;
            int nextDepth = oldDepth;
            if (draftOperation == DraftOperation.Drill)
            {
                if (draftTargetDimension == 0 && time != null)
                    nextTime = ExpandSelectedBuckets(time,
                        selectedTimeTicks, "preview_time").Length;
                else if (draftTargetDimension == 1 && depth != null)
                    nextDepth = ExpandSelectedBuckets(depth,
                        selectedDepthTicks, "preview_depth").Length;
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                if (draftTargetDimension == 0 && time != null)
                    nextTime = MergeBucketGroups(time,
                        timeRollupGroups, "preview_time").Length;
                else if (draftTargetDimension == 1 && depth != null)
                    nextDepth = MergeBucketGroups(depth,
                        depthRollupGroups, "preview_depth").Length;
            }
            int oldColumns = activeGridTransposed ? oldDepth : oldTime;
            int oldRows = activeGridTransposed ? oldTime : oldDepth;
            int nextColumns = pivotTransposed ? nextDepth : nextTime;
            int nextRows = pivotTransposed ? nextTime : nextDepth;
            string direction = pivotTransposed
                ? "DEPTH ACROSS · TIME DOWN"
                : "TIME ACROSS · DEPTH DOWN";
            return oldColumns + " × " + oldRows + "  →  " +
                nextColumns + " × " + nextRows + " PANELS   ·   " + direction;
        }

        private void BuildIntentPanel()
        {
            if (intentContent == null)
                return;
            ClearChildren(intentContent);

            if (vrKeyboardVisible)
            {
                BuildVrKeyboard();
                return;
            }

            CreateText(intentContent, "ANALYSIS TASK",
                30, FontStyle.Bold, new Vector2(0, 205), new Vector2(585, 42),
                TextAnchor.MiddleLeft, Ink);
            CreateText(intentContent, "CHOOSE THE QUESTION, THEN REFINE IT BY VOICE OR TEXT",
                14, FontStyle.Bold, new Vector2(0, 165), new Vector2(585, 26),
                TextAnchor.MiddleLeft, Purple);

            CreateButton(intentContent, "ANOMALY", new Vector2(-210, 126),
                new Vector2(132, 36),
                analysisTaskMode == AnalysisTaskMode.Anomaly ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Anomaly));
            CreateButton(intentContent, "COMPARE", new Vector2(-70, 126),
                new Vector2(132, 36),
                analysisTaskMode == AnalysisTaskMode.Compare ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Compare));
            CreateButton(intentContent, "DISTRIBUTION", new Vector2(70, 126),
                new Vector2(132, 36),
                analysisTaskMode == AnalysisTaskMode.Distribution ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Distribution));
            CreateButton(intentContent, "RELATION", new Vector2(210, 126),
                new Vector2(132, 36),
                analysisTaskMode == AnalysisTaskMode.Relationship ? Purple : Card,
                () => SelectAnalysisTask(AnalysisTaskMode.Relationship));

            intentPromptText = CreateTextBox(intentContent, prompt,
                new Vector2(-72, 50), new Vector2(420, 92), 15);
            // The task field itself is an explicit text-entry target in VR.
            // Users may either press TYPE or point at the current sentence.
            RectTransform intentPromptBox = intentPromptText != null
                ? intentPromptText.transform.parent as RectTransform
                : null;
            if (intentPromptBox != null)
            {
                BoxCollider promptCollider = intentPromptBox.gameObject.AddComponent<BoxCollider>();
                promptCollider.isTrigger = true;
                promptCollider.size = new Vector3(
                    intentPromptBox.sizeDelta.x,
                    intentPromptBox.sizeDelta.y,
                    12.0f);
                intentPromptBox.gameObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                    OpenTextKeyboard;
            }
            if (voiceReviewPending)
            {
                CreateButton(intentContent, "CONFIRM", new Vector2(238, 88),
                    new Vector2(116, 34), Green, ConfirmVoiceInput);
                CreateButton(intentContent, "VOICE", new Vector2(238, 50),
                    new Vector2(116, 34), Purple, StartVoiceInput);
                CreateButton(intentContent, "TYPE", new Vector2(238, 12),
                    new Vector2(116, 34), Card, OpenTextKeyboard);
            }
            else
            {
                CreateButton(intentContent,
                    questVoiceRecording ? "STOP" :
                    questVoiceUploading ? "SENDING" :
                    voiceInputActive ? "LISTENING" : "VOICE",
                    new Vector2(238, 70), new Vector2(116, 42),
                    voiceInputActive ? Amber : Purple, StartVoiceInput);
                CreateButton(intentContent, textInputActive ? "TYPING" : "TYPE",
                    new Vector2(238, 25), new Vector2(116, 42),
                    textInputActive ? Amber : Card, OpenTextKeyboard);
            }
            CreatePanelCard(intentContent, new Vector2(0, -48),
                new Vector2(585, 66), intentConfigured ? Green : Purple);
            CreateText(intentContent,
                intentResolving
                    ? "UNDERSTANDING..."
                    : intentConfigured
                        ? "READY  /  " + intentMode.ToUpperInvariant()
                        : string.IsNullOrWhiteSpace(intentResolutionError)
                            ? "READY"
                            : intentResolutionError,
                14, FontStyle.Bold, new Vector2(0, -48),
                new Vector2(535, 44), TextAnchor.MiddleLeft,
                intentConfigured ? Green :
                    intentResolving ? Amber :
                    string.IsNullOrWhiteSpace(intentResolutionError) ? Ink : Danger);

            CreateButton(intentContent,
                intentResolving ? "WORKING" : "APPLY",
                new Vector2(-150, -174),
                new Vector2(260, 52), intentResolving ? Card : Purple,
                ApplyIntentToFrame);
            CreateButton(intentContent,
                intentConfigured ? "FULL MATRIX" : "APPLY FIRST",
                new Vector2(165, -174),
                new Vector2(280, 52), intentConfigured ? Amber : Card,
                BeginGridPlacement);
            CreateButton(intentContent, "CLOSE", new Vector2(250, 214),
                new Vector2(100, 34), Card, () => intentCanvas.gameObject.SetActive(false));
        }

        private void BuildVrKeyboard()
        {
            CreateText(intentContent, "TYPE ANALYSIS TASK",
                27, FontStyle.Bold, new Vector2(0, 208), new Vector2(585, 38),
                TextAnchor.MiddleCenter, Ink);
            intentPromptText = CreateTextBox(intentContent, prompt,
                new Vector2(0, 155), new Vector2(575, 66), 16);

            string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
            float[] rowY = { 85.0f, 37.0f, -11.0f };
            for (int row = 0; row < rows.Length; row++)
            {
                string letters = rows[row];
                float width = letters.Length * 50.0f;
                for (int index = 0; index < letters.Length; index++)
                {
                    string key = letters[index].ToString();
                    float x = -width * 0.5f + 25.0f + index * 50.0f;
                    CreateButton(intentContent, key, new Vector2(x, rowY[row]),
                        new Vector2(44, 40), Card,
                        () => AppendVrKeyboardText(key));
                }
            }

            CreateButton(intentContent, "SPACE", new Vector2(-135, -65),
                new Vector2(210, 42), Card, () => AppendVrKeyboardText(" "));
            CreateButton(intentContent, "BACKSPACE", new Vector2(65, -65),
                new Vector2(170, 42), Card, VrKeyboardBackspace);
            CreateButton(intentContent, "CLEAR", new Vector2(215, -65),
                new Vector2(110, 42), Card, VrKeyboardClear);
            CreateButton(intentContent, "CANCEL", new Vector2(-155, -151),
                new Vector2(240, 52), Card, () => CloseVrKeyboard(false));
            CreateButton(intentContent, "DONE", new Vector2(155, -151),
                new Vector2(240, 52), Purple, () => CloseVrKeyboard(true));
        }

        private void AppendVrKeyboardText(string value)
        {
            prompt += value;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void VrKeyboardBackspace()
        {
            if (prompt.Length > 0)
                prompt = prompt.Substring(0, prompt.Length - 1);
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void VrKeyboardClear()
        {
            prompt = string.Empty;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void CloseVrKeyboard(bool commit)
        {
            if (!commit)
                prompt = vrKeyboardOriginalPrompt;
            vrKeyboardVisible = false;
            textInputActive = false;
            voiceReviewPending = commit;
            if (commit)
            {
                PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
                PlayerPrefs.Save();
                intentConfigured = false;
                intentResolutionError = string.Empty;
                SetStatus("Typed task ready. Apply it or edit again.");
            }
            else
                SetStatus("Text editing cancelled.");
            BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private void ApplyIntentToFrame()
        {
            if (intentResolving)
                return;
            if (voiceReviewPending)
                ConfirmVoiceInput();
            ResolveCurrentIntent();
        }

        private SpatialAxisRigState ActiveVariableBoundaryState()
        {
            if (selectedDataset == null)
                return null;
            int variable = datasets.IndexOf(selectedDataset);
            int stateIndex = spatialAxisRigStates.FindIndex(state =>
                state.boundVariable == variable);
            return stateIndex >= 0 ? spatialAxisRigStates[stateIndex] : null;
        }

        private void LoadEffectiveBoundaryValues(SpatialAxisRigState state)
        {
            bool custom = state != null && !state.usesSharedBoundaries;
            timeBoundaryStart = custom
                ? state.customTimeBoundaryStart : sharedTimeBoundaryStart;
            timeBoundaryEnd = custom
                ? state.customTimeBoundaryEnd : sharedTimeBoundaryEnd;
            selectedTime = custom
                ? state.customSelectedTime : sharedSelectedTime;
            depthBoundaryLow = custom
                ? state.customDepthBoundaryLow : sharedDepthBoundaryLow;
            depthBoundaryHigh = custom
                ? state.customDepthBoundaryHigh : sharedDepthBoundaryHigh;
            selectedZ = custom ? state.customSelectedZ : sharedSelectedZ;
            if (selectedDataset != null)
            {
                selectedTime = Mathf.Clamp(selectedTime, 0,
                    Mathf.Max(0, selectedDataset.TimeCount - 1));
                selectedZ = Mathf.Clamp(selectedZ, 0,
                    Mathf.Max(0, selectedDataset.DimZ - 1));
                slabNormalized = selectedDataset.DimZ > 1
                    ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
            }
        }

        private void StoreEffectiveBoundaryValues()
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            if (state != null && !state.usesSharedBoundaries)
            {
                state.customTimeBoundaryStart = timeBoundaryStart;
                state.customTimeBoundaryEnd = timeBoundaryEnd;
                state.customSelectedTime = selectedTime;
                state.customDepthBoundaryLow = depthBoundaryLow;
                state.customDepthBoundaryHigh = depthBoundaryHigh;
                state.customSelectedZ = selectedZ;
                return;
            }
            sharedTimeBoundaryStart = timeBoundaryStart;
            sharedTimeBoundaryEnd = timeBoundaryEnd;
            sharedSelectedTime = selectedTime;
            sharedDepthBoundaryLow = depthBoundaryLow;
            sharedDepthBoundaryHigh = depthBoundaryHigh;
            sharedSelectedZ = selectedZ;
            sharedBoundariesInitialized = true;
        }

        private void SetActiveBoundaryScope(bool custom)
        {
            SpatialAxisRigState state = ActiveVariableBoundaryState();
            if (state == null)
                return;
            List<int> boundVariables = BoundVariableIndices();
            for (int index = 0; index < boundVariables.Count; index++)
            {
                int variable = boundVariables[index];
                SpatialAxisRigState target = spatialAxisRigStates.Find(item =>
                    item.boundVariable == variable);
                if (target == null)
                    continue;
                if (custom && target.usesSharedBoundaries)
                {
                    target.customTimeBoundaryStart = sharedTimeBoundaryStart;
                    target.customTimeBoundaryEnd = sharedTimeBoundaryEnd;
                    target.customSelectedTime = sharedSelectedTime;
                    target.customDepthBoundaryLow = sharedDepthBoundaryLow;
                    target.customDepthBoundaryHigh = sharedDepthBoundaryHigh;
                    target.customSelectedZ = sharedSelectedZ;
                }
                target.usesSharedBoundaries = !custom;
            }
            if (initialBoundarySetupActive)
            {
                PrepareBoundaryVariableQueue();
                ActivateBoundaryVariableQueueEntry();
                state = ActiveVariableBoundaryState();
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                boundaryDimension = BoundaryDimension.Time;
            }
            LoadEffectiveBoundaryValues(state);
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            if (UsesFacetedVariableFields())
                RefreshVariableFacetStacks();
            else
                ApplyTimeFilter();
            ApplyPrimaryVolumeVisibility();
            UpdateSlabVisual(false);
            UpdateVariableBoundaryScopeButtons();
            BuildBoundaryPanel();
            SetStatus(custom
                ? "Custom ranges enabled. Every variable Field will author its own Time and Depth."
                : "Shared ranges enabled. One Time/Depth selection controls every variable Field.");
        }

        private void ConfirmVoiceInput()
        {
            voiceInputActive = false;
            textInputActive = false;
            voiceReviewPending = false;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            SetStatus("Recognized intent text confirmed. Select Resolve & Apply.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private void ResolveCurrentIntent()
        {
            intentConfigured = false;
            intentResolving = true;
            intentResolutionError = string.Empty;
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            BuildIntentPanel();
            SetStatus("Resolving the natural-language analysis intent...");
            VolumeSTCubeS4DAnalysisClient client =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 30);
            StartCoroutine(client.ResolveIntent(
                new S4DIntentResolutionRequest
                {
                    text = prompt,
                    variableId = selectedDataset != null
                        ? selectedDataset.VariableId
                        : string.Empty,
                    variableDisplayName = selectedDataset != null
                        ? selectedDataset.Name
                        : string.Empty,
                    unit = selectedDataset != null
                        ? selectedDataset.Unit
                        : string.Empty
                },
                OnIntentResolved));
        }

        private void OnIntentResolved(S4DIntentResolution resolution, string error)
        {
            intentResolving = false;
            if (resolution == null)
            {
                intentConfigured = false;
                intentResolutionError = string.IsNullOrWhiteSpace(error)
                    ? "The intent could not be resolved."
                    : error;
                BuildIntentPanel();
                SetStatus(intentResolutionError);
                return;
            }
            intentConfigured = true;
            intentMode = string.IsNullOrWhiteSpace(resolution.displayLabel)
                ? resolution.analyticTask.Replace("_", " ").ToUpperInvariant()
                : resolution.displayLabel;
            intentTask = string.IsNullOrWhiteSpace(resolution.analyticTask)
                ? "characterize_distribution"
                : resolution.analyticTask;
            intentFocus = resolution.focus;
            intentConfidence = resolution.confidence;
            intentUsedFallback = resolution.usedFallback;
            intentResolutionError = string.Empty;
            // Keep the user's raw text intact. The normalized instruction is
            // resolved metadata, not a replacement for the authored prompt.
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            BuildIntentPanel();
            RefreshIntentSurfaces();
            RecordTrailEvent("INTENT", intentMode);
            // Time/Depth buckets were already authored in the initial Field
            // setup. Intent resolution can therefore prepare the immutable
            // request directly; a separate Slab/source-preview gate only made
            // the main workflow longer without changing the request.
            BuildS4DGridRequest();
            spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
            BuildWorkflowToolbar();
            BuildIntentPanel();
            SetStatus(intentMode +
                " resolved. Full Matrix is ready; MatPlotAgent starts only when you confirm it.");
        }

        private void RefreshIntentSurfaces()
        {
            if (slabPreviewCanvas != null && slabPreviewCanvas.gameObject.activeSelf)
                BuildSlabPreviewPanel();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void BuildSlabPreviewPanel()
        {
            if (slabPreviewContent == null)
                return;
            ClearChildren(slabPreviewContent);

            CreateText(slabPreviewContent, "SOURCE PREVIEW",
                14, FontStyle.Bold, new Vector2(0, 266), new Vector2(820, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent,
                activeGridColumns + " × " + activeGridRows + " INTERVAL-MEAN GRID",
                27, FontStyle.Bold, new Vector2(0, 232), new Vector2(820, 38),
                TextAnchor.MiddleLeft, Ink);
            if (sourcePreviewRenderVariableIndex >= 0 &&
                sourcePreviewRenderVariableIndex < datasets.Count)
            {
                VolumeSTCubeSliceDataset layerDataset =
                    datasets[sourcePreviewRenderVariableIndex];
                CreateText(slabPreviewContent,
                    "VARIABLE LAYER  /  " + layerDataset.Name.ToUpperInvariant() +
                        (string.IsNullOrWhiteSpace(layerDataset.Unit)
                            ? string.Empty : "  [" + layerDataset.Unit + "]"),
                    12, FontStyle.Bold, new Vector2(250, 266),
                    new Vector2(320, 24), TextAnchor.MiddleRight,
                    VariableColor);
            }
            CreateText(slabPreviewContent,
                matrixPreviewAtlas != null
                    ? "Raw STC evidence · missing values excluded · shared scale"
                    : sourcePreviewRunning
                        ? "Computing interval means..."
                        : spatialWorkflowStep < SpatialWorkflowStep.Intent
                            ? "Confirm Time and Depth to continue."
                            : "Apply an intent to build the preview.",
                14, FontStyle.Normal, new Vector2(0, 199), new Vector2(820, 25),
                TextAnchor.MiddleLeft, matrixPreviewAtlas != null ? Green : Amber);

            int columns = Mathf.Max(1, activeGridColumns);
            int rows = Mathf.Max(1, activeGridRows);
            string[] timeLabels = BucketLabels(activeTimeBuckets,
                new[] { "before", "during", "after" });
            string[] depthLabels = BucketLabels(activeDepthBuckets,
                new[] { "surface", "middle", "deep" });
            string[] columnLabels = activeGridTransposed ? depthLabels : timeLabels;
            string[] rowLabels = activeGridTransposed ? timeLabels : depthLabels;
            Color columnColor = activeGridTransposed ? DepthColor : TimeColor;
            Color rowColor = activeGridTransposed ? TimeColor : DepthColor;
            float cellWidth = Mathf.Min(190.0f, 570.0f / columns);
            float cellHeight = Mathf.Min(96.0f, 288.0f / rows);
            Vector2 gridCenter = new Vector2(58, 30);

            for (int column = 0; column < columns; column++)
            {
                CreateText(slabPreviewContent, columnLabels[column], 15, FontStyle.Bold,
                    gridCenter + new Vector2((column - (columns - 1) * 0.5f) * cellWidth, 166),
                    new Vector2(cellWidth - 10, 24), TextAnchor.MiddleCenter, columnColor);
            }

            for (int row = 0; row < rows; row++)
            {
                CreateText(slabPreviewContent, rowLabels[row], 14, FontStyle.Bold,
                    gridCenter + new Vector2(-315.0f,
                        ((rows - 1) * 0.5f - row) * cellHeight),
                    new Vector2(150, 28), TextAnchor.MiddleRight, rowColor);
                for (int column = 0; column < columns; column++)
                {
                    int timeIndex = activeGridTransposed ? row : column;
                    int depthIndex = activeGridTransposed ? column : row;
                    GameObject cellObject = new GameObject(
                        depthLabels[depthIndex] + " x " + timeLabels[timeIndex] +
                            " data preview",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(slabPreviewContent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 14, cellHeight - 14);
                    cell.anchoredPosition = gridCenter + new Vector2(
                        (column - (columns - 1) * 0.5f) * cellWidth,
                        ((rows - 1) * 0.5f - row) * cellHeight);
                    Image cellBackground = cellObject.AddComponent<Image>();
                    cellBackground.sprite = RoundedUiSprite();
                    cellBackground.type = Image.Type.Sliced;
                    cellBackground.color = new Color(0.015f, 0.033f, 0.050f, 1.0f);
                    Shadow cellShadow = cellObject.AddComponent<Shadow>();
                    cellShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.48f);
                    cellShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(Green.r, Green.g, Green.b, 0.48f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    if (matrixPreviewAtlas != null)
                    {
                        RawImage preview = CreateRawImage(
                            cell, matrixPreviewAtlas, new Vector2(0, -5),
                            new Vector2(cellWidth - 22, cellHeight - 28));
                        preview.uvRect = new Rect(
                            timeIndex / (float)Mathf.Max(1, activeTimeBuckets.Length),
                            (activeDepthBuckets.Length - 1 - depthIndex) /
                                (float)Mathf.Max(1, activeDepthBuckets.Length),
                            1.0f / Mathf.Max(1, activeTimeBuckets.Length),
                            1.0f / Mathf.Max(1, activeDepthBuckets.Length));
                    }
                    else if (matrixTextures != null &&
                        spatialWorkflowStep >= SpatialWorkflowStep.SourcePreviewReady)
                    {
                        int fallbackIndex = depthIndex *
                            Mathf.Max(1, activeTimeBuckets.Length) + timeIndex;
                        Texture2D fallback = fallbackIndex >= 0 &&
                            fallbackIndex < matrixTextures.Length
                                ? matrixTextures[fallbackIndex]
                                : null;
                        if (fallback != null)
                        {
                            CreateRawImage(cell, fallback, new Vector2(0, -5),
                                new Vector2(cellWidth - 22, cellHeight - 28));
                        }
                        else
                        {
                            CreateText(cell, sourcePreviewRunning
                                    ? "COMPUTING\nLOCAL PREVIEW"
                                    : "PREVIEW\nUNAVAILABLE",
                                10, FontStyle.Bold, new Vector2(0, -5),
                                new Vector2(cellWidth - 22, cellHeight - 28),
                                TextAnchor.MiddleCenter, Muted);
                        }
                    }
                    else
                    {
                        CreateText(cell, sourcePreviewRunning
                                ? "COMPUTING\nINTERVAL MEAN"
                                : "AGGREGATE PREVIEW\nUNAVAILABLE",
                            10, FontStyle.Bold, new Vector2(0, -5),
                            new Vector2(cellWidth - 22, cellHeight - 28),
                            TextAnchor.MiddleCenter, Muted);
                    }

                    CreateText(cell,
                        depthLabels[depthIndex] + "  x  " + timeLabels[timeIndex],
                        10, FontStyle.Bold, new Vector2(0, cellHeight * 0.5f - 12),
                        new Vector2(cellWidth - 20, 18), TextAnchor.MiddleLeft,
                        intentConfigured ? Purple : Color.Lerp(TimeColor, DepthColor, 0.5f));
                }
            }

            CreateText(slabPreviewContent,
                FacetAxisSummary().ToUpperInvariant() + "  ·  " +
                    (selectedDataset != null
                        ? selectedDataset.Name.ToUpperInvariant() : "NO VARIABLE") +
                    "  ·  " + (intentConfigured ? intentMode : "WAITING FOR INTENT"),
                12, FontStyle.Bold, new Vector2(-150, -246), new Vector2(560, 28),
                TextAnchor.MiddleLeft, intentConfigured ? Purple : Amber);
            CreateButton(slabPreviewContent, "REBUILD PREVIEW",
                new Vector2(305, -242), new Vector2(235, 48), Green, PreviewSlab);
            CreateButton(slabPreviewContent, "CLOSE",
                new Vector2(355, 240), new Vector2(105, 36), Card,
                () => slabPreviewCanvas.gameObject.SetActive(false));

            // The first preview is viewed beside the full Field, so legacy
            // bitmap text becomes unreadable at this distance. Preserve the
            // complete layout and replace only its labels with SDF text.
            UpgradeCanvasLabelsToCrispText(slabPreviewContent, null);
        }

        private void BuildLegacySlabPreviewPanel()
        {
            if (slabPreviewContent == null)
                return;
            ClearChildren(slabPreviewContent);

            CreateText(slabPreviewContent, "FACETSLAB  /  CONFIGURATION PREVIEW",
                14, FontStyle.Bold, new Vector2(0, 240), new Vector2(750, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent, "SLAB FRAME  —  FRAME",
                28, FontStyle.Bold, new Vector2(0, 207), new Vector2(750, 38),
                TextAnchor.MiddleLeft, Ink);
            CreateText(slabPreviewContent,
                "Wire skeleton only  •  selected buckets become tick-blocks  •  no charts",
                14, FontStyle.Normal, new Vector2(0, 174), new Vector2(750, 25),
                TextAnchor.MiddleLeft, Green);

            string[] timeLabels = { "before", "during", "after" };
            string[] depthLabels = { "surface", "middle", "deep" };
            const float cellWidth = 165.0f;
            const float cellHeight = 86.0f;
            Vector2 gridCenter = new Vector2(55, 18);

            for (int column = 0; column < 3; column++)
            {
                CreateText(slabPreviewContent, timeLabels[column], 15, FontStyle.Bold,
                    gridCenter + new Vector2((column - 1) * cellWidth, 145),
                    new Vector2(cellWidth - 10, 24), TextAnchor.MiddleCenter, TimeColor);
            }

            for (int row = 0; row < 3; row++)
            {
                CreateText(slabPreviewContent, depthLabels[row], 14, FontStyle.Bold,
                    gridCenter + new Vector2(-cellWidth * 2.0f, (1 - row) * cellHeight),
                    new Vector2(135, 28), TextAnchor.MiddleRight, DepthColor);
                for (int column = 0; column < 3; column++)
                {
                    GameObject cellObject = new GameObject(
                        depthLabels[row] + " x " + timeLabels[column] + " skeleton cell",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(slabPreviewContent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 10, cellHeight - 10);
                    cell.anchoredPosition = gridCenter + new Vector2(
                        (column - 1) * cellWidth, (1 - row) * cellHeight);
                    Image skeletonBackground = cellObject.AddComponent<Image>();
                    skeletonBackground.sprite = RoundedUiSprite();
                    skeletonBackground.type = Image.Type.Sliced;
                    skeletonBackground.color = new Color(0.018f, 0.045f, 0.062f, 0.96f);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.42f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);
                    CreateText(cell,
                        intentConfigured
                            ? intentMode.Replace("CHARACTERIZE ", string.Empty) + "\nSPEC READY"
                            : depthLabels[row] + " × " + timeLabels[column] + "\nPENDING INTENT",
                        10, FontStyle.Bold, Vector2.zero,
                        new Vector2(cellWidth - 22, 42), TextAnchor.MiddleCenter,
                        intentConfigured ? Purple : new Color(Muted.r, Muted.g, Muted.b, 0.82f));
                }
            }

            CreateText(slabPreviewContent,
                "TIME  FACETED  ×  DEPTH  FACETED", 13, FontStyle.Bold,
                new Vector2(-205, -194), new Vector2(340, 25),
                TextAnchor.MiddleLeft, Color.Lerp(TimeColor, DepthColor, 0.5f));
            CreateText(slabPreviewContent,
                "HORIZONTAL  MAPPED   •   VARIABLE  FIXED: " +
                (selectedDataset != null ? selectedDataset.Name : "—"),
                12, FontStyle.Normal, new Vector2(-160, -220), new Vector2(430, 24),
                TextAnchor.MiddleLeft, Muted);
            CreateText(slabPreviewContent,
                intentConfigured ? "GRID TASK  /  " + intentMode : "GRID TASK  /  WAITING FOR INTENT",
                11, FontStyle.Bold, new Vector2(-160, -244), new Vector2(430, 22),
                TextAnchor.MiddleLeft, intentConfigured ? Purple : Amber);
            CreateButton(slabPreviewContent, "REGENERATE FRAME",
                new Vector2(265, -205), new Vector2(225, 46), Green, PreviewSlab);
            CreateButton(slabPreviewContent, "CLOSE",
                new Vector2(315, 215), new Vector2(105, 36), Card,
                () => slabPreviewCanvas.gameObject.SetActive(false));
        }

        private void BuildFacetGridPanel()
        {
            if (facetGridContent == null)
                return;
            ClearChildren(facetGridContent);
            facetGridProgressText = null;
            facetGridProgressStageText = null;
            facetGridValidatedText = null;
            facetGridProgressFill = null;
            Array.Clear(facetGridCellImages, 0, facetGridCellImages.Length);
            Array.Clear(facetGridCellStateLabels, 0, facetGridCellStateLabels.Length);
            Array.Clear(facetGridCellPlaceholders, 0,
                facetGridCellPlaceholders.Length);
            CreateText(facetGridContent,
                FacetAxisSummary().ToUpperInvariant() +
                    (gridStale ? "  /  STALE" : string.Empty), 33,
                FontStyle.Bold, new Vector2(-335, 350), new Vector2(700, 46),
                TextAnchor.MiddleLeft, gridStale ? Amber : Ink);

            if (placementConfirmed && s4dGridImage != null)
            {
                CreateButton(facetGridContent, facetGridLayered ? "2D GRID" : "3D LAYERS",
                    new Vector2(338, 330), new Vector2(180, 42),
                    facetGridLayered ? Purple : Card, ToggleFacetGridViewMode);
                if (facetGridLayered)
                {
                    CreateButton(facetGridContent, "PEEL +",
                        new Vector2(500, 330), new Vector2(125, 42), Amber,
                        PeelFacetGridLayer);
                    CreateButton(facetGridContent, "RESET",
                        new Vector2(625, 330), new Vector2(110, 42), Card,
                        ResetFacetGridPeel);
                }
            }

            CreatePanelCard(facetGridContent, new Vector2(0, 266), new Vector2(1370, 72), Cyan);
            // Once an operation is confirmed the result panel becomes a
            // dedicated materialization view.  Do not leave the old result
            // atlas or Pivot / Drill / Roll-up selection overlay underneath
            // the progress UI: it makes the target matrix look like another
            // editable draft and, for expanded grids, causes severe overlap.
            // Network callbacks may advance the semantic workflow state while
            // the current MatPlot job is still streaming.  The visual state is
            // therefore keyed by jobRunning as well, so the source draft can
            // never reappear underneath the progress view.
            VolumeSTCubeSliceDataset gridDataset = selectedDataset;
            if (materializedLayerAtlases.Count > 0 &&
                materializationVariableIndices.Count >= materializedLayerAtlases.Count)
            {
                int gridVariableIndex = materializationVariableIndices[
                    materializedLayerAtlases.Count - 1];
                if (gridVariableIndex >= 0 && gridVariableIndex < datasets.Count)
                    gridDataset = datasets[gridVariableIndex];
            }
            CreateText(facetGridContent,
                gridDataset == null
                    ? "Variable  —     " + FacetAxisSummary() +
                        "     Mapped  Horizontal"
                    : gridDataset.Name.ToUpperInvariant() + "  ·  " +
                        FacetAxisSummary(),
                17, FontStyle.Bold, new Vector2(0, 279), new Vector2(1300, 24),
                TextAnchor.MiddleLeft, Ink);
            bool materializingView = jobRunning ||
                spatialWorkflowStep == SpatialWorkflowStep.Materializing;
            if (materializingView)
            {
                CreateFacetPreviewCells(facetGridContent, new Vector2(-230, -20),
                    new Vector2(900, 440), true);
                BuildFacetGenerationOverlay();
            }
            else if (!placementConfirmed)
            {
                CreateFacetPreviewCells(facetGridContent, new Vector2(-230, -20),
                    new Vector2(900, 440), false);
                BuildFacetSelectionCard(true);
            }
            else if (s4dGridImage == null)
            {
                CreateMaterializedFacetCells(facetGridContent, null,
                    new Vector2(-230, -20), new Vector2(900, 440));
                if (jobRunning)
                    BuildFacetGenerationOverlay();
                else
                {
                    CreatePanelCard(facetGridContent, new Vector2(500, 10),
                        new Vector2(330, 390), Amber);
                    CreateText(facetGridContent, "FAILED", 48, FontStyle.Bold,
                        new Vector2(500, 78), new Vector2(270, 70),
                        TextAnchor.MiddleCenter, Amber);
                    CreateText(facetGridContent, "NO MATPLOT GRID COMMITTED",
                        18, FontStyle.Bold, new Vector2(500, 25),
                        new Vector2(270, 58), TextAnchor.MiddleCenter, Ink);
                    CreateText(facetGridContent,
                        !string.IsNullOrWhiteSpace(s4dGridFailure)
                            ? s4dGridFailure
                            : intentConfigured
                                ? "FULL MATRIX NOT STARTED\nSelect Retry MatPlotAgent to submit the resolved intent."
                                : "INTENT REQUIRED\nResolve the MatPlot intent before generating the Grid.",
                        14, FontStyle.Normal, new Vector2(500, -58),
                        new Vector2(270, 72), TextAnchor.MiddleCenter, Muted);
                }
            }
            else
            {
                Vector2 materializedGridPosition = draftOperation == DraftOperation.None
                    ? new Vector2(-235, -20)
                    : new Vector2(-165, -85);
                CreateMaterializedFacetCells(facetGridContent, s4dGridImage,
                    materializedGridPosition, new Vector2(900, 440));
                if (draftOperation == DraftOperation.None)
                {
                    BuildSharedColorScale(facetGridContent);
                    BuildFacetSelectionCard(false);
                }
                else
                {
                    BuildDraftGridInteractionOverlay();
                }
                if (jobRunning)
                    BuildFacetGenerationOverlay();
            }

            // Draft mode owns the footer. Previously the normal result footer
            // was created after the draft controls, covering APPLY and leaving
            // Pivot / Drill / Roll-up looking like an empty result page.
            if (materializingView)
            {
                CreateButton(facetGridContent, "CANCEL GENERATION",
                    new Vector2(500, -238), new Vector2(300, 44), Amber,
                    CancelS4DGridJob);
            }
            else if (draftOperation != DraftOperation.None)
            {
                bool ready = DraftSelectionReady(out _);
                string confirmLabel = "CONFIRM " +
                    (draftOperation == DraftOperation.Pivot ? "PIVOT" :
                    draftOperation == DraftOperation.Drill ? "DRILL" :
                    "ROLL-UP") + "  >  GENERATE";
                CreateButton(facetGridContent, "CANCEL", new Vector2(-190, -365),
                    new Vector2(280, 56), Card, CancelDraft);
                CreateButton(facetGridContent, confirmLabel,
                    new Vector2(190, -365), new Vector2(420, 56),
                    ready ? OperationColor(draftOperation) : Card,
                    ConfirmDraftAndGenerate);
            }
            else if (!placementConfirmed)
            {
                CreateButton(facetGridContent, "Back to Slab", new Vector2(-205, -350),
                    new Vector2(330, 56), Card, ReturnToSlabFromPlacement);
                CreateButton(facetGridContent, "Confirm Placement", new Vector2(205, -350),
                    new Vector2(380, 56), Cyan, ConfirmGridPlacement);
            }
            else if (jobRunning)
            {
                CreateButton(facetGridContent, "Cancel Generation", new Vector2(-170, -350),
                    new Vector2(340, 56), Amber, CancelS4DGridJob);
                CreateButton(facetGridContent, "Keep Working", new Vector2(205, -350),
                    new Vector2(300, 56), Card, ToggleFacetGrid);
            }
            else if (s4dGridImage == null)
            {
                CreateButton(facetGridContent, "Retry MatPlotAgent", new Vector2(-170, -350),
                    new Vector2(360, 56), Purple, RematerializeS4DGrid);
                CreateButton(facetGridContent, "Close Grid", new Vector2(235, -350),
                    new Vector2(260, 56), Card, ToggleFacetGrid);
            }
            else
            {
                CreateButton(facetGridContent, "Re-materialize", new Vector2(-330, -350),
                    new Vector2(310, 56), gridStale ? Amber : Card, RematerializeS4DGrid);
                CreateButton(facetGridContent, "Ground Selected", new Vector2(40, -350),
                    new Vector2(300, 56), gridCellSelected ? Cyan : Card, OpenGroundFromGrid);
                CreateButton(facetGridContent, "Close Grid", new Vector2(350, -350),
                    new Vector2(220, 56), Card, ToggleFacetGrid);
            }
            AnimatePanelRefresh(facetGridCanvasGroup,
                ref facetGridRefreshAnimation);
            RefreshMaterializedLayerVisibility();
        }

        private void ToggleFacetGridViewMode()
        {
            int selectedTimeIndex = DisplayTimeBucketIndex(
                selectedGridColumn, selectedGridRow);
            int selectedDepthIndex = DisplayDepthBucketIndex(
                selectedGridColumn, selectedGridRow);
            if (!facetGridLayered)
            {
                facetGridPreviousTransposed = activeGridTransposed;
                facetGridPreviousColumns = activeGridColumns;
                facetGridPreviousRows = activeGridRows;
                facetGridLayered = true;
                activeGridTransposed = false;
                activeGridColumns = Mathf.Max(1,
                    activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
                activeGridRows = Mathf.Max(1,
                    activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
                selectedGridColumn = Mathf.Clamp(selectedTimeIndex, 0,
                    activeGridColumns - 1);
                selectedGridRow = Mathf.Clamp(selectedDepthIndex, 0,
                    activeGridRows - 1);
            }
            else
            {
                facetGridLayered = false;
                activeGridTransposed = facetGridPreviousTransposed;
                activeGridColumns = facetGridPreviousColumns;
                activeGridRows = facetGridPreviousRows;
                selectedGridColumn = activeGridTransposed
                    ? Mathf.Clamp(selectedDepthIndex, 0, activeGridColumns - 1)
                    : Mathf.Clamp(selectedTimeIndex, 0, activeGridColumns - 1);
                selectedGridRow = activeGridTransposed
                    ? Mathf.Clamp(selectedTimeIndex, 0, activeGridRows - 1)
                    : Mathf.Clamp(selectedDepthIndex, 0, activeGridRows - 1);
            }
            facetGridPeeledLayers = 0;
            SetStatus(facetGridLayered
                ? "Facet Grid switched to 3D depth layers. Use PEEL to reveal deeper evidence."
                : "Facet Grid returned to the flat 2D comparison layout.");
            BuildFacetGridPanel();
        }

        private void PeelFacetGridLayer()
        {
            int rowCount = Mathf.Clamp(activeGridRows, 1, MaxFacetAxisBuckets);
            facetGridPeeledLayers = (facetGridPeeledLayers + 1) % rowCount;
            SetStatus(facetGridPeeledLayers == 0
                ? "Layer peel reset; all depth layers are visible."
                : facetGridPeeledLayers + " front depth layer(s) peeled away.");
            BuildFacetGridPanel();
        }

        private void ResetFacetGridPeel()
        {
            facetGridPeeledLayers = 0;
            SetStatus("Layer peel reset; surface, middle, and deep are visible.");
            BuildFacetGridPanel();
        }

        private void BuildFacetSelectionCard(bool placementPreview)
        {
            Color accent = gridCellSelected
                ? (selectedCellPinned ? Amber : Purple)
                : Cyan;
            CreatePanelCard(facetGridContent, new Vector2(515, 5),
                new Vector2(340, 500), accent);
            BuildFacetAxisGizmo();

            if (!gridCellSelected)
            {
                if (placementPreview)
                {
                    CreateText(facetGridContent, "READY TO ANCHOR",
                        21, FontStyle.Bold, new Vector2(515, 198),
                        new Vector2(280, 32), TextAnchor.MiddleLeft, Cyan);
                    CreateText(facetGridContent,
                        (activeGridColumns * activeGridRows) +
                            " SOURCE PREVIEW CELLS",
                        12, FontStyle.Bold,
                        new Vector2(515, 166), new Vector2(280, 22),
                        TextAnchor.MiddleLeft, Muted);
                    CreateText(facetGridContent,
                        IntentDisplayLabel() +
                        "\n\nShared color scale\nIdentical encoding" +
                        "\nTime x Depth ordering\nReal interval means",
                        15, FontStyle.Bold, new Vector2(515, 45),
                        new Vector2(280, 210), TextAnchor.UpperLeft, Ink);
                }
                else
                {
                    FindDigestExtremes(out int minimumIndex,
                        out int maximumIndex, out int widestIndex);
                    CreateText(facetGridContent, "STAT EVIDENCE",
                        24, FontStyle.Bold, new Vector2(515, 207),
                        new Vector2(280, 40), TextAnchor.UpperLeft,
                        Cyan);
                    CreateText(facetGridContent,
                        "Calculated evidence. Select a row to locate its source cell.",
                        15, FontStyle.Normal, new Vector2(515, 164),
                        new Vector2(280, 52), TextAnchor.UpperLeft, Muted);
                    CreateButton(facetGridContent, "HIGHEST AVERAGE",
                        new Vector2(515, 110), new Vector2(280, 40), Amber,
                        () => SelectFindingSourceCell(maximumIndex));
                    CreateButton(facetGridContent, "LOWEST AVERAGE",
                        new Vector2(515, 62), new Vector2(280, 40), Cyan,
                        () => SelectFindingSourceCell(minimumIndex));
                    CreateButton(facetGridContent, "WIDEST RANGE",
                        new Vector2(515, 14), new Vector2(280, 40), Purple,
                        () => SelectFindingSourceCell(widestIndex));
                    CreateButton(facetGridContent,
                        digestRunning ? "AI FINDINGS PREPARING..." :
                        "OPEN AI FINDINGS",
                        new Vector2(515, -48), new Vector2(280, 52), Green,
                        OpenAiFindingsPanel);
                    CreateText(facetGridContent,
                        "Statistics support the interpretation; they are not the finding.",
                        12, FontStyle.Normal, new Vector2(515, -94),
                        new Vector2(280, 42), TextAnchor.UpperLeft, Muted);
                }
                BuildGridSnapshotActions();
                return;
            }

            int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            string time = selectedDataset != null && matrixTimes.Length > timeIndex
                ? selectedDataset.GetTimeLabel(matrixTimes[timeIndex])
                : "-";
            string variable = selectedDataset != null ? selectedDataset.Name : "-";
            CreateText(facetGridContent, "CELL EVIDENCE", 25, FontStyle.Bold,
                new Vector2(515, 204), new Vector2(280, 32),
                TextAnchor.MiddleLeft, Cyan);
            CreateText(facetGridContent,
                CellLabel(selectedGridColumn, selectedGridRow),
                20, FontStyle.Bold, new Vector2(515, 168),
                new Vector2(280, 26), TextAnchor.MiddleLeft, Ink);
            FindDigestExtremes(out int cellMinimumIndex,
                out int cellMaximumIndex, out int cellWidestIndex);
            string findingRole = !matrixHasData[index]
                ? "STATISTICS UNAVAILABLE"
                : index == cellMaximumIndex
                    ? "HIGHEST CELL AVERAGE"
                    : index == cellMinimumIndex
                        ? "LOWEST CELL AVERAGE"
                        : index == cellWidestIndex
                            ? "BIGGEST WITHIN-CELL SPREAD"
                            : "SELECTED CELL";
            CreateText(facetGridContent, findingRole,
                18, FontStyle.Bold, new Vector2(515, 139),
                new Vector2(280, 24), TextAnchor.MiddleLeft,
                index == cellMaximumIndex ? Amber :
                index == cellMinimumIndex ? Cyan : Purple);
            string statisticSummary = matrixHasData[index]
                ? "CELL AVERAGE  " + matrixMeans[index].ToString("0.###") +
                    FindingUnitSuffix() +
                    "\nMIN-MAX  " + matrixMinimums[index].ToString("0.###") +
                    " - " + matrixMaximums[index].ToString("0.###") +
                    FindingUnitSuffix() +
                    "\nVALID DATA  " +
                    (matrixValidFractions[index] * 100.0f).ToString("0.0") + "%"
                : "LEGACY RESULT\nRe-materialize to compute statistics.";
            CreateText(facetGridContent, statisticSummary,
                20, FontStyle.Bold, new Vector2(515, 61),
                new Vector2(280, 82), TextAnchor.UpperLeft,
                matrixHasData[index] ? Ink : Amber);
            string groundSummary = !float.IsNaN(groundSnapshotCellMean) &&
                !float.IsNaN(groundReconstructedCellMean)
                    ? "GROUND VERIFIED  Δ " + Mathf.Abs(
                        groundSnapshotCellMean -
                        groundReconstructedCellMean).ToString("0.###")
                    : "GROUND NOT CHECKED";
            // Use an ASCII label so the Quest font fallback cannot corrupt the
            // delta marker inherited from an older source encoding.
            if (!float.IsNaN(groundSnapshotCellMean) &&
                !float.IsNaN(groundReconstructedCellMean))
                groundSummary = "GROUND VERIFIED  DELTA " + Mathf.Abs(
                    groundSnapshotCellMean -
                    groundReconstructedCellMean).ToString("0.###");
            CreateText(facetGridContent, groundSummary,
                12, FontStyle.Bold, new Vector2(515, -15),
                new Vector2(280, 22), TextAnchor.MiddleLeft,
                groundSummary.StartsWith("GROUND VERIFIED") ? Green : Cyan);
            int sourceTimeFirst;
            int sourceTimeLast;
            int sourceDepthFirst;
            int sourceDepthLast;
            bool hasSourceFootprint = TryGetGroundBucketRanges(
                out sourceTimeFirst, out sourceTimeLast,
                out sourceDepthFirst, out sourceDepthLast);
            string sourceSummary = selectedCellPinned
                ? "PINNED  /  source remains attached to this snapshot"
                : hasSourceFootprint && selectedDataset != null
                    ? variable + "  /  " +
                        selectedDataset.GetTimeLabel(sourceTimeFirst) +
                        " - " + selectedDataset.GetTimeLabel(sourceTimeLast) +
                        "  /  z " + sourceDepthFirst + "-" + sourceDepthLast
                    : "Source footprint unavailable for this cell.";
            CreateText(facetGridContent, sourceSummary,
                12, FontStyle.Normal, new Vector2(515, -52),
                new Vector2(280, 52), TextAnchor.UpperLeft,
                selectedCellPinned ? Amber : Green);
            CreateButton(facetGridContent,
                selectedCellPinned ? "UNPIN" : "PIN CELL",
                new Vector2(445, -170), new Vector2(135, 44),
                selectedCellPinned ? Amber : Card, ToggleSelectedCellPin);
            CreateButton(facetGridContent, "DISMISS",
                new Vector2(595, -170), new Vector2(135, 44),
                Card, DismissSelectedCell);
            BuildGridSnapshotActions();
        }

        private void BuildFacetAxisGizmo()
        {
            CreateText(facetGridContent, "AXIS GIZMO", 9, FontStyle.Bold,
                new Vector2(608, 218), new Vector2(90, 16),
                TextAnchor.MiddleRight, Muted);
            CreateUiRule(facetGridContent, new Vector2(630, 198),
                new Vector2(42, 4), TimeColor, 0.0f);
            CreateUiRule(facetGridContent, new Vector2(609, 177),
                new Vector2(42, 4), VariableColor, 90.0f);
            CreateUiRule(facetGridContent, new Vector2(614, 185),
                new Vector2(34, 4), DepthAxisColor, 42.0f);
            CreateText(facetGridContent, "X", 9, FontStyle.Bold,
                new Vector2(655, 198), new Vector2(14, 14),
                TextAnchor.MiddleCenter, TimeColor);
            CreateText(facetGridContent, "Y", 9, FontStyle.Bold,
                new Vector2(609, 153), new Vector2(14, 14),
                TextAnchor.MiddleCenter, VariableColor);
            CreateText(facetGridContent, "Z", 9, FontStyle.Bold,
                new Vector2(638, 165), new Vector2(14, 14),
                TextAnchor.MiddleCenter, DepthAxisColor);
        }

        private static void CreateUiRule(RectTransform parent, Vector2 position,
            Vector2 size, Color color, float angle)
        {
            GameObject ruleObject = new GameObject("Axis rule",
                typeof(RectTransform), typeof(Image));
            ruleObject.layer = 5;
            ruleObject.transform.SetParent(parent, false);
            RectTransform rule = ruleObject.GetComponent<RectTransform>();
            rule.sizeDelta = size;
            rule.anchoredPosition = position;
            rule.localRotation = Quaternion.Euler(0.0f, 0.0f, angle);
            ruleObject.GetComponent<Image>().color = color;
        }

        private void BuildGridSnapshotActions()
        {
            bool pinned = currentAnalysisNode != null &&
                currentAnalysisNode.pinned;
            bool confirmingDelete = currentAnalysisNode != null &&
                pendingDeleteNodeId == currentAnalysisNode.nodeId;
            CreateButton(facetGridContent, pinned ? "UNPIN GRID" : "PIN GRID",
                new Vector2(420, -221), new Vector2(94, 34),
                pinned ? Amber : Card, ToggleCurrentGridPin);
            CreateButton(facetGridContent, "DISMISS",
                new Vector2(520, -221), new Vector2(94, 34),
                Card, DismissCurrentGrid);
            CreateButton(facetGridContent,
                confirmingDelete ? "CONFIRM" : "DELETE",
                new Vector2(620, -221), new Vector2(94, 34),
                confirmingDelete ? Danger : Card, DeleteCurrentGridLeaf);
        }

        private void ToggleCurrentGridPin()
        {
            if (currentAnalysisNode == null)
                return;
            currentAnalysisNode.pinned = !currentAnalysisNode.pinned;
            SetStatus(currentAnalysisNode.pinned
                ? currentAnalysisNode.nodeId +
                    " pinned in the workspace and SlabTrail."
                : currentAnalysisNode.nodeId + " grid pin released.");
            BuildFacetGridPanel();
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private void DismissCurrentGrid()
        {
            if (currentAnalysisNode != null)
                currentAnalysisNode.dismissed = true;
            SetFacetSelectionEvidencePreview(false);
            if (facetGridCanvas != null)
                facetGridCanvas.gameObject.SetActive(false);
            ShowPrimaryTool(trailCanvas);
            BuildTrailPanel();
            SetStatus("Grid dismissed from the workspace. Select its SlabTrail " +
                "node to restore the immutable snapshot.");
        }

        private void DeleteCurrentGridLeaf()
        {
            if (currentAnalysisNode == null)
                return;
            AnalysisNodeState target = currentAnalysisNode;
            DeleteAnalysisLeaf(target);
            if (currentAnalysisNode == target &&
                facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void SelectFacetPreviewCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0, Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0, Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            int selectedIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            selectedCellPinned = facetCellPinned[Mathf.Clamp(
                selectedIndex, 0, facetCellPinned.Length - 1)];
            BuildMatrixBucketSelections();
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            selectedTime = matrixTimes[Mathf.Clamp(timeIndex, 0, matrixTimes.Length - 1)];
            selectedZ = matrixDepths[Mathf.Clamp(depthIndex, 0, matrixDepths.Length - 1)];
            slabNormalized = selectedDataset != null && selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            int textureIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            if (matrixTextures != null && textureIndex < matrixTextures.Length &&
                matrixTextures[textureIndex] != null)
            {
                slabTexture = matrixTextures[textureIndex];
                if (slabPreviewMaterial != null)
                    slabPreviewMaterial.mainTexture = slabTexture;
            }
            RebuildTimeMarkers();
            SetStatus("Selected " + CellLabel(selectedGridColumn, selectedGridRow) +
                ". Its source time range is highlighted in the XYT STC.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
            SetFacetSelectionEvidencePreview(true);
        }

        private void SetFacetSelectionEvidencePreview(bool visible)
        {
            bool show = visible && gridCellSelected && !groundDocked &&
                facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf;
            // A MatPlot-card preview is grounded in the independent XYT STC,
            // not in the legacy animated Field. Ground mode later in the
            // workflow still uses the original evidence visuals.
            SetGroundEvidenceVisuals(false);
            if (!show)
            {
                VolumeSTCubeForVrXytCompanion companion =
                    FindObjectOfType<VolumeSTCubeForVrXytCompanion>();
                if (companion != null)
                    companion.HideMatPlotSourceRange();
            }
            if (groundLink == null)
                return;
            groundLink.gameObject.SetActive(false);
            SetMatPlotStcDashedLinkVisible(show);
            if (show)
                UpdateGroundEvidenceLink();
        }

        private void ToggleSelectedCellPin()
        {
            if (!gridCellSelected)
                return;
            selectedCellPinned = !selectedCellPinned;
            int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
            facetCellPinned[Mathf.Clamp(index, 0, facetCellPinned.Length - 1)] =
                selectedCellPinned;
            if (currentAnalysisNode != null)
            {
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.pinnedCells[Mathf.Clamp(index, 0,
                    currentAnalysisNode.pinnedCells.Length - 1)] =
                    selectedCellPinned;
            }
            SetStatus(selectedCellPinned
                ? CellLabel(selectedGridColumn, selectedGridRow) + " pinned to this Grid snapshot."
                : "Cell pin released; the selection remains active.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
        }

        private void SelectFindingSourceCell(int sourceIndex)
        {
            if (sourceIndex < 0)
                return;
            for (int row = 0; row < activeGridRows; row++)
            {
                for (int column = 0; column < activeGridColumns; column++)
                {
                    if (SourceCellIndex(column, row) != sourceIndex)
                        continue;
                    SelectFacetPreviewCell(column, row);
                    SetStatus("Finding selected: " + CellLabel(column, row) +
                        ". Inspect its source footprint or send it to Ground.");
                    return;
                }
            }
        }

        private void DismissSelectedCell()
        {
            gridCellSelected = false;
            selectedCellPinned = false;
            SetFacetSelectionEvidencePreview(false);
            SetStatus("Findings closed. The Full Matrix remains anchored.");
            if (stage == Stage.Matrix && panelCanvas != null &&
                panelCanvas.gameObject.activeSelf)
                BuildStage();
            BuildFacetGridPanel();
        }

        private void ReturnToSlabFromPlacement()
        {
            SetFacetSelectionEvidencePreview(false);
            facetGridCanvas.gameObject.SetActive(false);
            ReturnToSpatialWorkflow();
            Navigate(Stage.Slab);
        }

        private void OpenDraftFromGrid()
        {
            SetFacetSelectionEvidencePreview(false);
            BeginDraft(DraftOperation.Pivot);
        }

        private void OpenGroundFromGrid()
        {
            if (!gridCellSelected)
            {
                SetStatus("Select one Time x Depth cell before opening Ground.");
                BuildFacetGridPanel();
                return;
            }
            SelectS4DGridCell(selectedGridColumn, selectedGridRow);
        }

        private void ReturnToFacetGrid()
        {
            SetGroundDock(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                SetFacetSelectionEvidencePreview(gridCellSelected);
            }
            stage = Stage.Matrix;
            SetStatus("Returned to the anchored Facet Grid. Ground evidence remains linked to its snapshot.");
        }

        private void SetGroundDock(bool active)
        {
            if (panelCanvas == null)
                return;
            if (active && !groundDocked)
            {
                panelPreGroundPosition = panelCanvas.transform.localPosition;
                panelPreGroundRotation = panelCanvas.transform.localRotation;
                panelPreGroundScale = panelCanvas.transform.localScale;
                groundDocked = true;
                // Ground is the next workflow stage, so it temporarily replaces the
                // immutable grid instead of competing with it for space and pointer
                // hits. Return to Grid restores the snapshot at its anchored dock.
                panelCanvas.transform.localPosition = PrimaryToolDockPosition;
                panelCanvas.transform.localScale = Vector3.one * 0.00062f;
                FacePanelTowardViewer(panelCanvas.transform);
            }
            else if (!active && groundDocked)
            {
                StopGroundPlayback();
                groundDocked = false;
                panelCanvas.transform.localPosition = panelPreGroundPosition;
                panelCanvas.transform.localRotation = panelPreGroundRotation;
                panelCanvas.transform.localScale = panelPreGroundScale;
            }

            SetGroundEvidenceVisuals(active);
            if (groundLink != null)
            {
                SetMatPlotStcDashedLinkVisible(false);
                groundLink.gameObject.SetActive(active);
                if (active)
                    UpdateGroundEvidenceLink();
            }
        }

        private void UpdateGroundEvidenceLink()
        {
            bool gridPreview = !groundDocked && gridCellSelected &&
                facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf;
            if ((!groundDocked && !gridPreview) || groundLink == null)
                return;
            Vector3 source = groundDepthBand != null
                ? groundDepthBand.transform.position
                : slabObject != null
                    ? slabObject.transform.position
                    : spatialRoot.transform.position;
            if (gridPreview && TryGetGroundBucketRanges(
                out int timeFirst, out int timeLast,
                out int ignoredDepthFirst, out int ignoredDepthLast))
            {
                VolumeSTCubeForVrXytCompanion companion =
                    FindObjectOfType<VolumeSTCubeForVrXytCompanion>();
                Vector3 stcSource;
                if (companion != null && companion.ShowMatPlotSourceRange(
                    timeFirst, timeLast,
                    selectedDataset != null ? selectedDataset.Name : string.Empty,
                    out stcSource))
                    source = stcSource;
            }
            Vector3 target = selectedFacetCellAnchor != null
                ? selectedFacetCellAnchor.TransformPoint(Vector3.zero)
                : facetGridCanvas != null
                    ? facetGridCanvas.transform.position
                    : panelCanvas.transform.position;
            if (gridPreview)
            {
                groundLink.gameObject.SetActive(false);
                SetMatPlotStcDashedLink(source, target);
            }
            else
            {
                SetMatPlotStcDashedLinkVisible(false);
                groundLink.gameObject.SetActive(true);
                groundLink.SetPosition(0, source);
                groundLink.SetPosition(1, target);
            }
        }

        private void SetMatPlotStcDashedLink(Vector3 source, Vector3 target)
        {
            Vector3 delta = target - source;
            int segmentCount = matPlotStcLinkSegments.Length;
            for (int index = 0; index < segmentCount; index++)
            {
                LineRenderer segment = matPlotStcLinkSegments[index];
                if (segment == null)
                    continue;
                float start = index / (float)segmentCount;
                float end = (index + 0.58f) / segmentCount;
                segment.SetPosition(0, source + delta * start);
                segment.SetPosition(1, source + delta * Mathf.Min(1.0f, end));
                segment.gameObject.SetActive(true);
            }
        }

        private void SetMatPlotStcDashedLinkVisible(bool visible)
        {
            for (int index = 0; index < matPlotStcLinkSegments.Length; index++)
                if (matPlotStcLinkSegments[index] != null)
                    matPlotStcLinkSegments[index].gameObject.SetActive(visible);
        }

        private void SetGroundEvidenceVisuals(bool visible)
        {
            if (!visible)
            {
                SetGroundAggregateVisible(false);
                if (currentView != null)
                {
                    currentView.SetVisible(cubeVisible);
                    currentView.ApplyOpacity(FieldOpacity);
                }
            }
            int timeFirst = 0;
            int timeLast = 0;
            int depthFirst = 0;
            int depthLast = 0;
            bool show = false;
            if (visible && selectedDataset != null && gridCellSelected)
                show = TryGetGroundBucketRanges(
                    out timeFirst, out timeLast, out depthFirst, out depthLast);
            if (groundTimeRangeLine != null)
                groundTimeRangeLine.gameObject.SetActive(show);
            if (groundTimeRangeLabel != null)
                groundTimeRangeLabel.gameObject.SetActive(show);
            // The immutable footprint is represented by two light boundary
            // slices. Never show the filled range volume: Quest renders it as
            // an opaque wall that hides the actual STC field.
            bool showFilledDepthEvidence = false;
            if (groundDepthBand != null)
                groundDepthBand.SetActive(showFilledDepthEvidence);
            for (int index = 0; index < groundDepthRangePlanes.Length; index++)
                if (groundDepthRangePlanes[index] != null)
                    groundDepthRangePlanes[index].SetActive(show);
            if (groundDepthRangeLabel != null)
                groundDepthRangeLabel.gameObject.SetActive(show);
            bool boundaryEditorVisible = boundaryCanvas != null &&
                boundaryCanvas.gameObject.activeSelf;
            // Boundary handles belong exclusively to Author Boundary.  A
            // stale result may mention that its source changed, but selecting
            // Highest/Lowest must never open those editable cyan/white planes.
            if (boundaryEditorVisible)
            {
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                SetTimeBoundaryHandleVisibility(true);
                SetDepthBoundaryVisibility(true);
            }
            else
            {
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
            }
            if (!show)
                return;

            float timeDenominator = Mathf.Max(1, selectedDataset.TimeCount - 1);
            float timeX0 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                timeFirst / timeDenominator);
            float timeX1 = Mathf.Lerp(-TimeRailHalfWidth, TimeRailHalfWidth,
                timeLast / timeDenominator);
            SetLine(groundTimeRangeLine,
                new Vector3(timeX0, 0.018f, -0.006f),
                new Vector3(timeX1, 0.018f, -0.006f));
            groundTimeRangeLabel.text =
                "SELECTED  " + selectedDataset.GetTimeLabel(timeFirst) + "–" +
                selectedDataset.GetTimeLabel(timeLast);
            // Always replace any legacy encoded separator with readable ASCII.
            groundTimeRangeLabel.text =
                "SELECTED  " + selectedDataset.GetTimeLabel(timeFirst) + " - " +
                selectedDataset.GetTimeLabel(timeLast);
            groundTimeRangeLabel.transform.localPosition =
                new Vector3((timeX0 + timeX1) * 0.5f, 0.165f, 0.0f);

            float depthDenominator = Mathf.Max(1, selectedDataset.DimZ - 1);
            float depthY0 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthFirst / depthDenominator);
            float depthY1 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthLast / depthDenominator);
            float lowerY = Mathf.Min(depthY0, depthY1);
            float upperY = Mathf.Max(depthY0, depthY1);
            if (groundDepthBand != null)
            {
                groundDepthBand.transform.localPosition =
                    new Vector3(0.0f, (lowerY + upperY) * 0.5f, 0.0f);
                groundDepthBand.transform.localScale = new Vector3(
                    FieldHalfWidth * 1.94f, Mathf.Max(0.025f, upperY - lowerY),
                    FieldHalfDepth * 1.94f);
            }
            if (groundDepthRangePlanes[0] != null)
                groundDepthRangePlanes[0].transform.localPosition =
                    new Vector3(0.0f, lowerY, 0.0f);
            if (groundDepthRangePlanes[1] != null)
                groundDepthRangePlanes[1].transform.localPosition =
                    new Vector3(0.0f, upperY, 0.0f);
            groundDepthRangeLabel.text =
                "DEPTH  z=" + depthFirst + " - " + depthLast;
            // Keep the annotation inside the cube and just above the upper cut.
            // This avoids colliding with the analysis panel or floating outside
            // the field when the user views it from an oblique Quest angle.
            groundDepthRangeLabel.anchor = TextAnchor.MiddleRight;
            groundDepthRangeLabel.alignment = TextAlignment.Right;
            groundDepthRangeLabel.transform.localPosition =
                new Vector3(FieldHalfWidth - 0.045f,
                    Mathf.Min(volumeLocalMaxY - 0.035f, upperY + 0.065f),
                    -FieldHalfDepth * 0.91f);
            RebuildTimeMarkers();
        }

        private bool TryGetGroundBucketRanges(
            out int timeFirst, out int timeLast, out int depthFirst, out int depthLast)
        {
            timeFirst = timeLast = selectedTime;
            depthFirst = depthLast = selectedZ;
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || timeIndex < 0 ||
                timeIndex >= activeTimeBuckets.Length ||
                activeDepthBuckets == null || depthIndex < 0 ||
                depthIndex >= activeDepthBuckets.Length)
                return false;
            if (!TryGetIndexRange(activeTimeBuckets[timeIndex], out timeFirst, out timeLast) ||
                !TryGetIndexRange(activeDepthBuckets[depthIndex], out depthFirst, out depthLast))
                return false;
            timeFirst = Mathf.Clamp(timeFirst, 0, selectedDataset.TimeCount - 1);
            timeLast = Mathf.Clamp(timeLast, timeFirst, selectedDataset.TimeCount - 1);
            depthFirst = Mathf.Clamp(depthFirst, 0, selectedDataset.DimZ - 1);
            depthLast = Mathf.Clamp(depthLast, depthFirst, selectedDataset.DimZ - 1);
            return true;
        }

        private static bool TryGetIndexRange(
            S4DIndexBucketRequest bucket, out int first, out int last)
        {
            first = last = 0;
            if (bucket == null || bucket.indices == null || bucket.indices.Length == 0)
                return false;
            first = int.MaxValue;
            last = int.MinValue;
            for (int index = 0; index < bucket.indices.Length; index++)
            {
                first = Mathf.Min(first, bucket.indices[index]);
                last = Mathf.Max(last, bucket.indices[index]);
            }
            return true;
        }

        private void ToggleTrailPanel()
        {
            if (trailCanvas == null)
                return;
            bool next = !trailCanvas.gameObject.activeSelf;
            if (next)
            {
                ShowPrimaryTool(trailCanvas);
                BuildTrailPanel();
            }
            else
            {
                trailCanvas.gameObject.SetActive(false);
            }
        }

        private void BuildTrailPanel()
        {
            if (trailContent == null)
                return;
            ClearChildren(trailContent);
            CreateText(trailContent, "QUESTION + EVIDENCE HISTORY", 28, FontStyle.Bold,
                new Vector2(0, 270), new Vector2(840, 44), TextAnchor.MiddleLeft, Ink);
            CreateText(trailContent, analysisQuestion,
                15, FontStyle.Normal, new Vector2(0, 235), new Vector2(840, 28),
                TextAnchor.MiddleLeft, Muted);

            Vector2 rootPosition = new Vector2(-315, 55);
            CreateTrailNode(trailContent, "FIELD", "ROOT", rootPosition,
                Cyan, () => NavigateFromTrail(Stage.Field));

            Dictionary<string, Vector2> positions = new Dictionary<string, Vector2>();
            List<AnalysisNodeState> visibleNodes = VisibleTrailNodes();
            Dictionary<int, int> nodesAtDepth = new Dictionary<int, int>();
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                int depth = Mathf.Clamp(AnalysisNodeDepth(node), 1, 3);
                nodesAtDepth[depth] = nodesAtDepth.TryGetValue(depth, out int count)
                    ? count + 1
                    : 1;
            }
            Dictionary<int, int> placedAtDepth = new Dictionary<int, int>();
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                int depth = Mathf.Clamp(AnalysisNodeDepth(node), 1, 3);
                int lane = placedAtDepth.TryGetValue(depth, out int placed)
                    ? placed
                    : 0;
                placedAtDepth[depth] = lane + 1;
                int laneCount = nodesAtDepth[depth];
                float y = laneCount == 1
                    ? 55
                    : 165 - lane * (300.0f / (laneCount - 1));
                Vector2 nodePosition = new Vector2(-315 + depth * 225, y);
                positions[node.nodeId] = nodePosition;
            }
            for (int index = 0; index < visibleNodes.Count; index++)
            {
                AnalysisNodeState node = visibleNodes[index];
                Vector2 nodePosition = positions[node.nodeId];
                Vector2 parentPosition = rootPosition;
                if (!string.IsNullOrEmpty(node.parentNodeId) &&
                    positions.TryGetValue(node.parentNodeId, out Vector2 visibleParent))
                    parentPosition = visibleParent;
                CreateTrailLink(parentPosition + Vector2.right * 105,
                    nodePosition - Vector2.right * 105, OperationLabel(node.bornFrom));
                Color accent = node.stale || node.boundarySuspect ? Amber :
                    node == currentAnalysisNode ? Green : OperationColor(node.bornFrom);
                AnalysisNodeState selectedNode = node;
                CreateTrailNode(trailContent, node.title,
                    TrailNodeSubtitle(node), nodePosition, accent,
                    () => NavigateToAnalysisNode(selectedNode));
                if (IsLeafNode(node.nodeId))
                {
                    bool confirmingDelete = pendingDeleteNodeId == node.nodeId;
                    CreateButton(trailContent,
                        confirmingDelete ? "CONFIRM" : "DELETE",
                        nodePosition + new Vector2(62, -62), new Vector2(92, 26),
                        Danger, () => DeleteAnalysisLeaf(selectedNode));
                }
            }

            CreateText(trailContent,
                analysisNodes.Count == 0
                    ? "No committed analysis yet. Drafts do not enter SlabTrail."
                    : analysisNodes.Count + " COMMITTED  /  " + LeafNodeCount() +
                        " LEAF  /  Latest: " +
                        analysisNodes[analysisNodes.Count - 1].nodeId + "  " +
                        OperationLabel(analysisNodes[analysisNodes.Count - 1].bornFrom),
                14, FontStyle.Bold, new Vector2(0, -190), new Vector2(820, 28),
                TextAnchor.MiddleLeft, inspected ? Green : boundarySuspect ? Amber : Muted);
            CreateText(trailContent, RecentTrailEventSummary(),
                10, FontStyle.Bold, new Vector2(0, -238),
                new Vector2(820, 88), TextAnchor.MiddleLeft, Purple);
            CreateButton(trailContent, "Arrange Workspace", new Vector2(-190, -304),
                new Vector2(330, 56), Cyan, ArrangeWorkspace);
            CreateButton(trailContent, "Close", new Vector2(230, -304),
                new Vector2(220, 56), Card, ToggleTrailPanel);
        }

        private string RecentTrailEventSummary()
        {
            if (trailEvents.Count == 0)
                return "EVENT LOG  /  Variable, role, boundary, intent, Grid and Ground actions appear here.";
            int start = Mathf.Max(0, trailEvents.Count - 2);
            List<string> labels = new List<string>();
            for (int index = start; index < trailEvents.Count; index++)
            {
                TrailEventState item = trailEvents[index];
                labels.Add("#" + item.sequence + " " + item.nodeId + " " +
                    item.kind + "  " + item.detail);
            }
            return string.Join("\n", labels.ToArray());
        }

        private void RecordTrailEvent(string kind, string detail,
            AnalysisNodeState node = null)
        {
            AnalysisNodeState owner = node ?? currentAnalysisNode;
            trailEvents.Add(new TrailEventState
            {
                sequence = nextTrailEventSequence++,
                nodeId = owner != null ? owner.nodeId : "ROOT",
                kind = kind,
                detail = detail
            });
            if (trailEvents.Count > 48)
                trailEvents.RemoveAt(0);
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private List<AnalysisNodeState> VisibleTrailNodes()
        {
            const int recentNodeCount = 6;
            HashSet<string> visibleIds = new HashSet<string>();
            int recentStart = Mathf.Max(0, analysisNodes.Count - recentNodeCount);
            for (int index = recentStart; index < analysisNodes.Count; index++)
            {
                visibleIds.Add(analysisNodes[index].nodeId);
                AnalysisNodeState ancestor = FindAnalysisNode(
                    analysisNodes[index].parentNodeId);
                int ancestorGuard = 0;
                while (ancestor != null && ancestorGuard++ <= analysisNodes.Count)
                {
                    visibleIds.Add(ancestor.nodeId);
                    ancestor = FindAnalysisNode(ancestor.parentNodeId);
                }
            }

            AnalysisNodeState cursor = currentAnalysisNode ??
                (analysisNodes.Count > 0 ? analysisNodes[analysisNodes.Count - 1] : null);
            int guard = 0;
            while (cursor != null && guard++ <= analysisNodes.Count)
            {
                visibleIds.Add(cursor.nodeId);
                cursor = FindAnalysisNode(cursor.parentNodeId);
            }

            List<AnalysisNodeState> result = new List<AnalysisNodeState>();
            for (int index = 0; index < analysisNodes.Count; index++)
                if (visibleIds.Contains(analysisNodes[index].nodeId))
                    result.Add(analysisNodes[index]);
            return result;
        }

        private string TrailNodeSubtitle(AnalysisNodeState node)
        {
            string parent = string.IsNullOrWhiteSpace(node.parentNodeId)
                ? "FIELD"
                : node.parentNodeId;
            string state = node.stale ? "STALE" :
                node.boundarySuspect ? "SUSPECT" :
                node.localizedCells != null && Array.Exists(node.localizedCells, value => value)
                    ? "LOCAL" :
                node.inspected ? "VERIFIED" :
                node.pinned ? "PINNED" : "READY";
            string digestState = node.digestPending ? "  /  DIGEST..." :
                node.digest != null ? "  /  DIGEST READY" :
                !string.IsNullOrWhiteSpace(node.digestError)
                    ? "  /  DIGEST FALLBACK" : string.Empty;
            string task = string.IsNullOrWhiteSpace(node.analyticTask)
                ? "ANALYSIS" : node.analyticTask.Replace("_", " ").ToUpperInvariant();
            return node.nodeId + "  /  " + task +
                "\nFROM " + parent + "  /  " + state + digestState;
        }

        private static string OperationLabel(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return "Pivot";
                case DraftOperation.Drill: return "Drill";
                case DraftOperation.RollUp: return "Roll-up";
                default: return "Full Matrix";
            }
        }

        private static Color OperationColor(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return Purple;
                case DraftOperation.Drill: return TimeColor;
                case DraftOperation.RollUp: return Green;
                default: return Cyan;
            }
        }

        private bool IsLeafNode(string nodeId)
        {
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].parentNodeId == nodeId)
                    return false;
            return true;
        }

        private int AnalysisNodeDepth(AnalysisNodeState node)
        {
            int depth = 1;
            string parentId = node != null ? node.parentNodeId : string.Empty;
            int guard = 0;
            while (!string.IsNullOrEmpty(parentId) && guard++ < analysisNodes.Count)
            {
                AnalysisNodeState parent = FindAnalysisNode(parentId);
                if (parent == null)
                    break;
                depth++;
                parentId = parent.parentNodeId;
            }
            return depth;
        }

        private int LeafNodeCount()
        {
            int count = 0;
            for (int index = 0; index < analysisNodes.Count; index++)
                if (IsLeafNode(analysisNodes[index].nodeId))
                    count++;
            return count;
        }

        private void CreateTrailNode(RectTransform parent, string title, string subtitle,
            Vector2 position, Color accent, Action action)
        {
            GameObject nodeObject = new GameObject(title, typeof(RectTransform));
            nodeObject.layer = 5;
            nodeObject.transform.SetParent(parent, false);
            RectTransform node = nodeObject.GetComponent<RectTransform>();
            node.sizeDelta = new Vector2(230, 104);
            node.anchoredPosition = position;
            nodeObject.AddComponent<Image>().color = Card;
            Outline outline = nodeObject.AddComponent<Outline>();
            outline.effectColor = accent;
            outline.effectDistance = new Vector2(3, -3);
            CreateText(node, title, 16, FontStyle.Bold, new Vector2(0, 20),
                new Vector2(200, 30), TextAnchor.MiddleCenter, Ink);
            CreateText(node, subtitle, 10, FontStyle.Bold, new Vector2(0, -22),
                new Vector2(204, 44), TextAnchor.MiddleCenter, accent);
            BoxCollider collider = nodeObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(230, 104, 12);
            nodeObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private void CreateTrailLink(Vector2 from, Vector2 to, string label)
        {
            Vector2 delta = to - from;
            GameObject linkObject = new GameObject(label, typeof(RectTransform));
            linkObject.transform.SetParent(trailContent, false);
            RectTransform link = linkObject.GetComponent<RectTransform>();
            link.sizeDelta = new Vector2(delta.magnitude, 3);
            link.anchoredPosition = (from + to) * 0.5f;
            link.localRotation = Quaternion.Euler(0, 0,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            linkObject.AddComponent<Image>().color = new Color(Muted.r, Muted.g, Muted.b, 0.72f);
            CreateText(trailContent, label, 11, FontStyle.Bold, (from + to) * 0.5f + Vector2.up * 14,
                new Vector2(100, 20), TextAnchor.MiddleCenter, Muted);
        }

        private void CreatePanel()
        {
            GameObject panelObject = new GameObject("Slab Lab Spatial Console", typeof(RectTransform));
            panelObject.layer = 5;
            panelObject.transform.SetParent(transform, false);
            panelObject.transform.localPosition = PrimaryToolDockPosition;
            panelObject.transform.localRotation = Quaternion.identity;
            // About 69 cm wide at the default Quest placement: comfortably readable
            // without requiring the user to lean in, while remaining a hand-scale tool.
            panelObject.transform.localScale = Vector3.one * 0.00070f;
            panelCanvas = panelObject.AddComponent<Canvas>();
            panelCanvasGroup = panelObject.AddComponent<CanvasGroup>();
            panelCanvas.renderMode = UnityEngine.RenderMode.WorldSpace;
            panelCanvas.worldCamera = xrCamera;
            panelCanvas.sortingOrder = 100;
            CanvasScaler scaler = panelObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit =
#if UNITY_EDITOR || SLABLAB_FLAT
                VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled ? 48.0f :
#endif
                24.0f;
            scaler.referencePixelsPerUnit = 100.0f;
            panelObject.AddComponent<GraphicRaycaster>();

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1120.0f, 840.0f);
            Image panelBackground = panelObject.AddComponent<Image>();
            panelBackground.color = Panel;
            panelBackground.sprite = RoundedUiSprite();
            panelBackground.type = Image.Type.Sliced;
            Shadow panelShadow = panelObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.70f);
            panelShadow.effectDistance = new Vector2(14.0f, -16.0f);
            Outline panelOutline = panelObject.AddComponent<Outline>();
            panelOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.56f);
            panelOutline.effectDistance = new Vector2(2, -2);
            BoxCollider panelCollider = panelObject.AddComponent<BoxCollider>();
            panelCollider.isTrigger = true;
            panelCollider.center = new Vector3(0.0f, 0.0f, 14.0f);
            panelCollider.size = new Vector3(1120.0f, 840.0f, 8.0f);
            panelObject.AddComponent<VolumeSTCubeQuestPanelHandle>().accent = Cyan;

            GameObject headerWash = new GameObject("Console header wash", typeof(RectTransform));
            headerWash.transform.SetParent(rect, false);
            RectTransform headerWashRect = headerWash.GetComponent<RectTransform>();
            headerWashRect.anchorMin = new Vector2(0, 1);
            headerWashRect.anchorMax = new Vector2(1, 1);
            headerWashRect.pivot = new Vector2(0.5f, 1);
            headerWashRect.anchoredPosition = new Vector2(0, -3);
            headerWashRect.sizeDelta = new Vector2(-6, 132);
            Image headerWashImage = headerWash.AddComponent<Image>();
            headerWashImage.sprite = RoundedUiSprite();
            headerWashImage.type = Image.Type.Sliced;
            headerWashImage.color = new Color(0.02f, 0.12f, 0.16f, 0.28f);
            headerWashImage.raycastTarget = false;

            GameObject topAccent = new GameObject("Console top accent", typeof(RectTransform));
            topAccent.transform.SetParent(rect, false);
            RectTransform topAccentRect = topAccent.GetComponent<RectTransform>();
            topAccentRect.anchorMin = new Vector2(0, 1);
            topAccentRect.anchorMax = new Vector2(1, 1);
            topAccentRect.pivot = new Vector2(0.5f, 1);
            topAccentRect.anchoredPosition = Vector2.zero;
            topAccentRect.sizeDelta = new Vector2(0, 6);
            Image topAccentImage = topAccent.AddComponent<Image>();
            topAccentImage.color = Cyan;
            topAccentImage.raycastTarget = false;

            CreateDecorativeSurface(rect, "Navigation dock", new Vector2(0, 298),
                new Vector2(1070, 58), new Color(0.018f, 0.043f, 0.064f, 0.94f));
            Image contentSurface = CreateDecorativeSurface(rect, "Stage surface",
                new Vector2(0, -50), new Vector2(1070, 620),
                new Color(0.014f, 0.030f, 0.047f, 0.82f));
            Shadow contentShadow = contentSurface.gameObject.AddComponent<Shadow>();
            contentShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.26f);
            contentShadow.effectDistance = new Vector2(0, -7);

            panelBrandText = CreateText(rect, "S4D CANVAS", 15, FontStyle.Bold,
                new Vector2(0, 399), new Vector2(1060, 24), TextAnchor.MiddleLeft, Muted);
            panelGripHintText = CreateText(rect, "GRIP TO MOVE  /  RELEASE TO PIN", 11, FontStyle.Bold,
                new Vector2(0, 399), new Vector2(1060, 24), TextAnchor.MiddleRight, Cyan);
            panelTitleText = CreateText(rect, "DATASET IMPORT", 31, FontStyle.Bold,
                new Vector2(0, 367), new Vector2(1060, 42), TextAnchor.MiddleLeft, Ink);
            panelTitleCrispText = AddCrispTextOverlay(panelTitleText,
                panelTitleText.text, 64.0f, 34.0f, true);
            panelFlowText = CreateText(rect, "Upload -> Detect variables -> Confirm -> Continuous Field", 16,
                FontStyle.Normal, new Vector2(0, 336), new Vector2(1060, 26), TextAnchor.MiddleLeft, Cyan);

            string[] steps = { "DATA", "SLAB", "FACET GRID", "GROUND", "FINDING" };
            for (int i = 0; i < steps.Length; i++)
            {
                int index = i;
                Button button = CreateButton(rect, steps[i], new Vector2(-432 + i * 216, 298), new Vector2(196, 43),
                    Card, () => Navigate((Stage)(index + 1)));
                button.gameObject.name = "Spatial step " + i;
            }

            GameObject content = new GameObject("Spatial stage content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            panelContent = content.GetComponent<RectTransform>();
            panelContent.sizeDelta = new Vector2(1070, 595);
            panelContent.anchoredPosition = new Vector2(0, -34);
            statusText = CreateText(rect, "Starting...", 17, FontStyle.Normal,
                new Vector2(0, -396), new Vector2(1060, 32), TextAnchor.MiddleLeft, Ink);
            statusCrispText = AddCrispTextOverlay(statusText,
                statusText.text, 34.0f, 18.0f, true);
            if (statusCrispText != null)
                statusCrispText.enableWordWrapping = true;
        }

        private void RefreshDatasets()
        {
            RefreshDatasets(null);
        }

        private void RefreshDatasets(string requestedRoot)
        {
            string previousRoot = dataRoot;
            dataRoot = string.IsNullOrWhiteSpace(requestedRoot)
                ? ResolveDataRoot()
                : Path.GetFullPath(requestedRoot);
            if (!string.IsNullOrWhiteSpace(previousRoot) &&
                !string.Equals(previousRoot, dataRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                spatialAxisRigStates.Clear();
                selectedDataset = null;
                importSelectedVariableIndex = -1;
            }
            datasets.Clear();
            try
            {
                datasets.AddRange(VolumeSTCubeRawSliceReader.DiscoverDatasets(dataRoot));
                if (datasets.Count == 0 &&
                    VolumeSTCubeRawSliceReader.TryOpenDataset(
                        dataRoot, out VolumeSTCubeSliceDataset directDataset, out _))
                {
                    datasets.Add(directDataset);
                }
                SetStatus(datasets.Count > 0
                    ? "Detected " + datasets.Count + " variable" +
                      (datasets.Count == 1 ? "" : "s") + " in " + dataRoot + "."
                    : "No RAW datasets found at " + dataRoot);
            }
            catch (Exception exception)
            {
                SetStatus("Dataset discovery failed: " + exception.Message);
            }
            if (datasets.Count == 1 && importSelectedVariableIndex < 0)
                importSelectedVariableIndex = 0;
            else if (importSelectedVariableIndex >= datasets.Count)
                importSelectedVariableIndex = -1;
            RefreshFieldDatasetSelector();
            RefreshSpatialAxisControllers();
            BuildStage();
        }

        private string ResolveDataRoot()
        {
#if UNITY_EDITOR || SLABLAB_FLAT
            string editorForVrRoot = Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "For_VR", "UnityRaw"));
            if (LooksLikeDatasetLocation(editorForVrRoot))
                return editorForVrRoot;
#endif
            string savedRoot = PlayerPrefs.GetString("VolumeSTCube.Quest.DatasetRoot", string.Empty);
            // A previously selected folder can remain in PlayerPrefs after its files
            // have been moved or after the APK has been reinstalled.  Merely checking
            // Directory.Exists then traps the import screen on an empty, stale folder.
            if (LooksLikeDatasetLocation(savedRoot))
                return Path.GetFullPath(savedRoot);
            if (Application.platform == RuntimePlatform.Android)
            {
                string folderName = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled
                    ? "Datasets"
                    : "OneDrive_1_4-30-2026";
                string privateRoot = Path.Combine("/data/user/0", Application.identifier, "files", folderName);
                string persistentRoot = Path.Combine(Application.persistentDataPath, folderName);
                string externalRoot = Path.Combine("/sdcard/Android/data", Application.identifier,
                    "files", folderName);
                string[] candidates = { persistentRoot, externalRoot, privateRoot };
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (LooksLikeDatasetLocation(candidates[i]))
                        return Path.GetFullPath(candidates[i]);
                }

                // Keep the normal Unity persistent location as the actionable path in
                // the empty-state message, even when no candidate is populated yet.
                return persistentRoot;
            }
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                return Path.Combine(Application.persistentDataPath, "Datasets");
            return Path.GetFullPath(Path.Combine(
                Application.dataPath, "..", "..", "OneDrive_1_4-30-2026"));
        }

        private static bool LooksLikeDatasetLocation(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return false;
            try
            {
                // Accept both a root containing variable directories and a directly
                // selected variable directory. Checking only filenames avoids parsing
                // every RAW metadata file during startup on Quest.
                if (Directory.GetFiles(root, "*.raw", SearchOption.TopDirectoryOnly).Length > 0)
                    return true;
                string[] directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < directories.Length; i++)
                {
                    if (Directory.GetFiles(directories[i], "*.raw", SearchOption.TopDirectoryOnly).Length > 0)
                        return true;
                }
            }
            catch (Exception)
            {
                // Android can expose a directory entry before the app can enumerate it;
                // continue probing the remaining candidate roots in that case.
            }
            return false;
        }

        private void Navigate(Stage next)
        {
            if (jobRunning)
                return;
            if (!datasetImportConfirmed && next != Stage.DatasetImport)
            {
                SetStatus("Import and confirm a compatible dataset first.");
                stage = Stage.DatasetImport;
                BuildStage();
                return;
            }
            if (selectedDataset == null && next != Stage.Field)
            {
                SetStatus("Choose a field first.");
                return;
            }
            if (next == Stage.Slab && !mainWorkspaceEntered &&
                !authorBoundaryConfirmed &&
                draftOperation == DraftOperation.None)
            {
                OpenInitialAuthorBoundary();
                return;
            }
            if (next == Stage.Result && s4dGridImage == null)
            {
                SetStatus("Finding is available after a Full Matrix is committed.");
                return;
            }
            if (next != Stage.Analyze && next != Stage.Result)
                SetGroundDock(false);
            stage = next;
            BuildStage();
            if (next == Stage.Slab && slabPreviewBuilt && slabPreviewCanvas != null)
            {
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
        }

        private void BuildStage()
        {
            if (panelContent == null)
                return;
            ConfigureDesktopConsoleShell(
                VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled &&
                stage == Stage.Field);
            ClearChildren(panelContent);
            RefreshStepColors();
            switch (stage)
            {
                case Stage.DatasetImport: BuildDatasetImportStage(); break;
                case Stage.Field: BuildFieldStage(); break;
                case Stage.Slab: BuildSlabStage(); break;
                case Stage.Matrix: BuildMatrixStage(); break;
                case Stage.Analyze: BuildAnalyzeStage(); break;
                case Stage.Result: BuildResultStage(); break;
            }
            // Apply the same SDF typography path at every workflow step;
            // later MatPlot and findings panels must not fall back to tiny
            // dynamic-font labels.
            UpgradeCanvasLabelsToCrispText(panelContent, null);
            AnimatePanelRefresh(panelCanvasGroup, ref panelRefreshAnimation);
        }

        private void ConfigureDesktopConsoleShell(bool compact)
        {
            if (panelCanvas == null || panelContent == null)
                return;
            RectTransform panelRect = panelCanvas.GetComponent<RectTransform>();
            if (panelRect == null)
                return;
            panelRect.sizeDelta = compact
                ? new Vector2(1120.0f, 150.0f)
                : new Vector2(1120.0f, 840.0f);
            panelContent.sizeDelta = compact
                ? new Vector2(1070.0f, 132.0f)
                : new Vector2(1070.0f, 595.0f);
            panelContent.anchoredPosition = compact
                ? Vector2.zero
                : new Vector2(0.0f, -34.0f);
            BoxCollider collider = panelCanvas.GetComponent<BoxCollider>();
            if (collider != null)
                collider.size = new Vector3(1120.0f,
                    compact ? 150.0f : 840.0f, 8.0f);
            for (int index = 0; index < panelRect.childCount; index++)
            {
                Transform child = panelRect.GetChild(index);
                if (child == panelContent)
                    continue;
                child.gameObject.SetActive(!compact);
            }
        }

        private void AnimatePanelRefresh(CanvasGroup group, ref Coroutine running)
        {
            if (group == null || !group.gameObject.activeInHierarchy)
                return;
            if (running != null)
                StopCoroutine(running);
            // Keep the surface visually stable while a local value changes.
            // The old whole-panel fade looked like flicker in a headset.
            group.alpha = 1.0f;
            running = null;
        }

        private IEnumerator FadePanelRefresh(CanvasGroup group)
        {
            group.alpha = 0.72f;
            float elapsed = 0.0f;
            const float duration = 0.16f;
            while (group != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                group.alpha = Mathf.SmoothStep(0.72f, 1.0f, t);
                yield return null;
            }
            if (group != null)
                group.alpha = 1.0f;
        }

        private void RefreshStepColors()
        {
            bool importOnly = stage == Stage.DatasetImport;
            bool compactField = stage == Stage.Field && preconfigurationActive;
            bool hideWorkflowChrome = importOnly || compactField;
            if (panelCanvas != null)
            {
                RectTransform panelRect = panelCanvas.GetComponent<RectTransform>();
                if (panelRect != null)
                    panelRect.sizeDelta = compactField
                        ? new Vector2(980.0f, 620.0f)
                        : new Vector2(1120.0f, 840.0f);
                BoxCollider panelCollider = panelCanvas.GetComponent<BoxCollider>();
                if (panelCollider != null)
                    panelCollider.size = compactField
                        ? new Vector3(980.0f, 620.0f, 8.0f)
                        : new Vector3(1120.0f, 840.0f, 8.0f);
                if (panelContent != null)
                {
                    panelContent.sizeDelta = compactField
                        ? new Vector2(930.0f, 540.0f)
                        : new Vector2(1070.0f, 595.0f);
                    panelContent.anchoredPosition = compactField
                        ? new Vector2(0.0f, -2.0f)
                        : new Vector2(0.0f, -34.0f);
                }
                Transform[] chrome = panelCanvas.GetComponentsInChildren<Transform>(true);
                for (int index = 0; index < chrome.Length; index++)
                {
                    string objectName = chrome[index].gameObject.name;
                    if (objectName == "Navigation dock" ||
                        objectName.StartsWith("Spatial step ",
                            StringComparison.Ordinal))
                        chrome[index].gameObject.SetActive(!hideWorkflowChrome);
                    else if (objectName == "Console header wash")
                        chrome[index].gameObject.SetActive(!compactField);
                    else if (objectName == "Stage surface")
                        chrome[index].gameObject.SetActive(!compactField);
                }
            }
            if (panelFlowText != null)
                panelFlowText.gameObject.SetActive(!hideWorkflowChrome);
            if (statusText != null)
            {
                statusText.gameObject.SetActive(!importOnly);
                statusText.rectTransform.anchoredPosition = compactField
                    ? new Vector2(0.0f, -276.0f)
                    : new Vector2(0.0f, -396.0f);
                statusText.rectTransform.sizeDelta = compactField
                    ? new Vector2(910.0f, 48.0f)
                    : new Vector2(1060.0f, 32.0f);
                statusText.alignment = compactField
                    ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                statusText.fontSize = Mathf.RoundToInt(
                    (compactField ? 14 : 17) * ActiveUiFontScale);
                if (statusCrispText != null)
                {
                    statusCrispText.fontSizeMin = compactField ? 20.0f : 16.0f;
                    statusCrispText.fontSizeMax = compactField ? 32.0f : 26.0f;
                }
            }
            if (panelBrandText != null)
                panelBrandText.gameObject.SetActive(!hideWorkflowChrome);
            if (panelGripHintText != null)
                panelGripHintText.gameObject.SetActive(!hideWorkflowChrome);

            for (int i = 0; i < 5; i++)
            {
                GameObject item = GameObject.Find("Spatial step " + i);
                if (item != null)
                {
                    bool active = datasetImportConfirmed && i == (int)stage - 1;
                    Color accent = active ? Cyan : Card;
                    Image image = item.GetComponent<Image>();
                    if (image != null)
                        image.color = ThemedButtonFill(accent);
                    Transform accentTransform = item.transform.Find("Button accent");
                    if (accentTransform != null)
                    {
                        Image accentImage = accentTransform.GetComponent<Image>();
                        if (accentImage != null)
                            accentImage.color = active
                                ? new Color(Cyan.r, Cyan.g, Cyan.b, 0.96f)
                                : new Color(Muted.r, Muted.g, Muted.b, 0.30f);
                    }
                    Text label = item.GetComponentInChildren<Text>();
                    if (label != null)
                        label.color = IdealButtonLabel(ThemedButtonFill(accent));
                }
            }
            if (panelTitleText != null)
            {
                panelTitleText.gameObject.SetActive(!compactField);
                panelTitleText.text = stage == Stage.DatasetImport
                    ? "OPEN A DATASET"
                    : stage == Stage.Field
                        ? "FIELD"
                        : "SLAB FRAME";
                panelTitleText.alignment = importOnly
                    ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
                panelTitleText.rectTransform.anchoredPosition = importOnly
                    ? new Vector2(0, 255) : new Vector2(0, 367);
                panelTitleText.rectTransform.sizeDelta = importOnly
                    ? new Vector2(900, 58) : new Vector2(1060, 42);
                panelTitleText.fontSize = Mathf.RoundToInt(
                    (importOnly ? 38 : 31) * ActiveUiFontScale);
                if (panelTitleCrispText != null)
                {
                    panelTitleCrispText.text = panelTitleText.text;
                    panelTitleCrispText.alignment = importOnly
                        ? TMPro.TextAlignmentOptions.Center
                        : TMPro.TextAlignmentOptions.Left;
                    panelTitleCrispText.fontSizeMin = importOnly ? 38.0f : 30.0f;
                    panelTitleCrispText.fontSizeMax = importOnly ? 64.0f : 54.0f;
                }
            }
            if (panelFlowText != null)
                panelFlowText.text = stage == Stage.DatasetImport
                    ? "Upload -> Detect variables -> Confirm -> Continuous Field"
                    : "Author Boundary -> Configure Slab -> MatPlot Grid -> Ground -> Re-materialize";
        }

        private void BuildDatasetImportStage()
        {
            // Dataset import is intentionally a single, focused decision.
            // Research-question authoring belongs to the analysis workspace;
            // keeping it off this first screen makes opening data much faster
            // and avoids presenting two unrelated decisions at once in VR.
            CreateText(panelContent, "LOAD DATASET", 20, FontStyle.Bold,
                new Vector2(0, 150), new Vector2(780, 34),
                TextAnchor.MiddleCenter, Ink);
            CreateButton(panelContent, "CHOOSE DATASET FOLDER",
                new Vector2(-105, 92), new Vector2(500, 58), Purple,
                ChooseDatasetFolder);
            CreateButton(panelContent, "RESCAN", new Vector2(280, 92),
                new Vector2(230, 58), Card,
                () => RefreshDatasets(dataRoot));

            int visibleVariables = datasets.Count;
            float buttonWidth = visibleVariables > 0
                ? Mathf.Min(260.0f, 780.0f / visibleVariables) : 240.0f;
            float start = -(visibleVariables - 1) * (buttonWidth + 16.0f) * 0.5f;
            for (int index = 0; index < visibleVariables; index++)
            {
                int captured = index;
                CreateButton(panelContent, datasets[index].Name.ToUpperInvariant(),
                    new Vector2(start + index * (buttonWidth + 16.0f), 5),
                    new Vector2(buttonWidth, 64),
                    importSelectedVariableIndex == index ? Amber : Card,
                    () => SelectImportVariable(captured));
            }
            CreateButton(panelContent,
                 importSelectedVariableIndex >= 0
                     ? "CONTINUE WITH " + datasets[importSelectedVariableIndex].Name.ToUpperInvariant()
                     : datasets.Count > 0 ? "SELECT A VARIABLE" : "WAITING FOR DATASET",
                new Vector2(0, -94), new Vector2(700, 72),
                importSelectedVariableIndex >= 0 ? Cyan : Card,
                ConfirmDatasetImport);

            // The opening screen is the user's first readability checkpoint.
            // Use the largest SDF glyphs that fit the existing content and
            // button rectangles; no panel or control geometry is changed.
            UpgradeCanvasLabelsToCrispText(panelContent, null);
        }

        private void SelectAnalysisTask(AnalysisTaskMode mode)
        {
            analysisTaskMode = mode;
            string variable = importSelectedVariableIndex >= 0 &&
                importSelectedVariableIndex < datasets.Count
                    ? datasets[importSelectedVariableIndex].Name
                    : "the selected variable";
            switch (mode)
            {
                case AnalysisTaskMode.Compare:
                    analysisQuestion = "How does " + variable +
                        " differ across the selected time and depth regions?";
                    prompt = "Compare the selected time and depth regions and explain the strongest differences.";
                    intentTask = "determine_range";
                    break;
                case AnalysisTaskMode.Relationship:
                    analysisQuestion = "Which time and depth patterns in " +
                        variable + " move together?";
                    prompt = "Identify relationships and consistent patterns across the selected time and depth regions.";
                    intentTask = "correlate";
                    break;
                case AnalysisTaskMode.Distribution:
                    analysisQuestion = "How is " + variable +
                        " distributed across time and depth?";
                    prompt = "Characterize the distribution across the selected time and depth regions.";
                    intentTask = "characterize_distribution";
                    break;
                default:
                    analysisQuestion = "Where and when does " + variable +
                        " show the strongest anomaly?";
                    prompt = "Find the strongest anomalies across the selected time and depth regions.";
                    intentTask = "find_anomalies";
                    break;
            }
            intentConfigured = false;
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            RecordTrailEvent("QUESTION", analysisQuestion);
            BuildStage();
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

        private void SelectImportVariable(int index)
        {
            if (index < 0 || index >= datasets.Count)
                return;
            importSelectedVariableIndex = index;
            SelectAnalysisTask(analysisTaskMode);
            SetStatus(datasets[index].Name +
                " selected. Continue to define Time and Depth.");
        }

        private string DatasetCompatibilitySummary()
        {
            if (datasets.Count == 0)
                return "no layout";
            VolumeSTCubeSliceDataset first = datasets[0];
            for (int i = 1; i < datasets.Count; i++)
            {
                VolumeSTCubeSliceDataset current = datasets[i];
                if (current.DimX != first.DimX || current.DimY != first.DimY ||
                    current.DimZ != first.DimZ || current.TimeCount != first.TimeCount)
                    return "mixed layouts (usable individually)";
            }
            return "common layout  " + first.TimeCount + "T × " + first.DimZ + "Z";
        }

        private string DatasetNamesSummary()
        {
            if (datasets.Count == 0)
                return "no variables";
            string[] names = new string[datasets.Count];
            for (int i = 0; i < datasets.Count; i++)
                names[i] = datasets[i].Name;
            return string.Join(" / ", names);
        }

        private void ChooseDatasetFolder()
        {
            if (Application.platform == RuntimePlatform.Android ||
                Application.platform == RuntimePlatform.IPhonePlayer)
            {
                string device = VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled
                    ? "Tablet"
                    : "Quest";
                SetStatus(device + ": copy the dataset root to " + ResolveDataRoot() +
                    ", then choose RESCAN CURRENT FOLDER.");
                return;
            }
            RuntimeFileBrowser.ShowOpenDirectoryDialog(OnDatasetFolderSelected, dataRoot);
        }

        private void OnDatasetFolderSelected(RuntimeFileBrowser.DialogResult result)
        {
            if (result.cancelled || string.IsNullOrWhiteSpace(result.path))
            {
                SetStatus("Dataset selection cancelled.");
                return;
            }
            if (!Directory.Exists(result.path))
            {
                SetStatus("Dataset folder does not exist: " + result.path);
                return;
            }

            if (currentView != null)
            {
                VolumeSTCubeAPI.DestroyView(currentView.viewId);
                currentView = null;
            }
            ClearPairedVariableVolumes();
            selectedDataset = null;
            datasetImportConfirmed = false;
            importSelectedVariableIndex = -1;
            preconfigurationActive = false;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            ResetAxisBucketSelection();
            stage = Stage.DatasetImport;
#if !UNITY_EDITOR && !SLABLAB_FLAT
            questImportHeadLocked = true;
            UpdateQuestImportHeadLock(true);
#endif
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            PlayerPrefs.SetString("VolumeSTCube.Quest.DatasetRoot", Path.GetFullPath(result.path));
            PlayerPrefs.Save();
            RefreshDatasets(result.path);
        }

        private void ConfirmDatasetImport()
        {
            if (datasets.Count == 0)
            {
                SetStatus("Choose a folder containing variable subfolders with RAW + .raw.ini files.");
                return;
            }
            if (importSelectedVariableIndex < 0 ||
                importSelectedVariableIndex >= datasets.Count)
            {
                SetStatus("Select a variable before entering the Field setup.");
                BuildStage();
                return;
            }
            datasetImportConfirmed = true;
            preconfigurationActive = true;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            ResetAxisBucketSelection();
            stage = Stage.Field;
#if !UNITY_EDITOR && !SLABLAB_FLAT
            questImportHeadLocked = false;
            if (xrCamera != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    xrCamera.transform.forward, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.01f)
                    forward = transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                PlaceQuestAnalysisWorkspace(xrCamera.transform.position, forward, right);
            }
#endif
            if (spatialRoot != null)
                spatialRoot.SetActive(true);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(false);
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(false);
            if (variablePaletteRoot != null)
                variablePaletteRoot.SetActive(false);
            legacyPanelVisible = true;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(true);
            LoadDataset(importSelectedVariableIndex);
            SetStatus("Preparing the selected variable for Time and Depth setup.");
            BuildStage();
        }

        private void BuildFieldStage()
        {
            bool surfaceDataset = IsForVrSurfaceDataset;
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                AlignDesktopVisualization();
                if (variableLoadRunning || selectedDataset == null)
                {
                    CreateText(panelContent, "LOADING FIELD", 24,
                        FontStyle.Bold, Vector2.zero, new Vector2(520, 60),
                        TextAnchor.MiddleCenter, Cyan);
                    return;
                }
                CreateButton(panelContent,
                    authorBoundaryConfirmed
                        ? "ENTER MAIN WORKSPACE"
                        : surfaceDataset ? "SET TIME RANGE" : "SET TIME + DEPTH",
                    Vector2.zero, new Vector2(470, 68),
                    authorBoundaryConfirmed ? Cyan : Amber,
                    () =>
                    {
                        if (authorBoundaryConfirmed)
                            EnterMainWorkspace();
                        else
                            OpenInitialAuthorBoundary();
                    });
                return;
            }
            CreateText(panelContent, surfaceDataset ? "SURFACE + TIME SETUP" : "FIELD SETUP", 27, FontStyle.Bold,
                new Vector2(-105, 216), new Vector2(680, 42), TextAnchor.MiddleLeft, Ink);
            CreateButton(panelContent, "CHANGE DATASET", new Vector2(360, 216),
                new Vector2(190, 42), Card, OpenDatasetImportStage);
            if (variableLoadRunning)
            {
                CreatePanelCard(panelContent, new Vector2(0, -30),
                    new Vector2(900, 210), Cyan);
                CreateText(panelContent, "LOADING FIELD", 25,
                    FontStyle.Bold, new Vector2(0, 35), new Vector2(820, 40),
                    TextAnchor.MiddleCenter, Cyan);
                CreateText(panelContent, "Preparing the selected STC volume...",
                    17, FontStyle.Normal, new Vector2(0, -22),
                    new Vector2(780, 36), TextAnchor.MiddleCenter, Muted);
                UpgradeCanvasLabelsToCrispText(panelContent,
                    "LOADING FIELD");
                return;
            }

            if (selectedDataset == null)
            {
                CreateText(panelContent, "PREPARING 3D FIELD...", 22,
                    FontStyle.Bold, new Vector2(0, -10), new Vector2(900, 90),
                    TextAnchor.MiddleCenter, Cyan);
                UpgradeCanvasLabelsToCrispText(panelContent,
                    "PREPARING 3D FIELD...");
                return;
            }

            CreatePanelCard(panelContent, new Vector2(0, 44),
                new Vector2(860, 166), Cyan);
            CreateText(panelContent, selectedDataset.Name.ToUpperInvariant(), 28,
                FontStyle.Bold, new Vector2(0, 82), new Vector2(820, 40),
                TextAnchor.MiddleCenter, Ink);
            fieldTimeSummaryText = CreateText(panelContent,
                surfaceDataset
                    ? "TIME  " + TimeRangeSummary() +
                        "\nGEOMETRY  HONG KONG WATER SURFACE (NO DEPTH AXIS)"
                    : "TIME  " + TimeRangeSummary() + "\nDEPTH  " + DepthRangeSummary(),
                18, FontStyle.Bold, new Vector2(0, 16), new Vector2(820, 76),
                TextAnchor.MiddleCenter, Cyan);
            CreateButton(panelContent,
                authorBoundaryConfirmed
                    ? "ENTER MAIN WORKSPACE"
                    : surfaceDataset ? "SET TIME RANGE" : "SET TIME + DEPTH",
                new Vector2(0, -185), new Vector2(460, 58),
                authorBoundaryConfirmed ? Cyan : Amber,
                () =>
                {
                    if (authorBoundaryConfirmed)
                        EnterMainWorkspace();
                    else
                        OpenInitialAuthorBoundary();
                });
            UpgradeCanvasLabelsToCrispText(panelContent,
                selectedDataset.Name.ToUpperInvariant());
        }

        private void AlignDesktopVisualization()
        {
            if (spatialRoot == null || xrCamera == null)
                return;
            if (!desktopVisualizationAligned)
            {
                // First establish the position where the independent STC is
                // centred by itself. The overview position then centres the
                // midpoint between the surface Field and STC Field.
                spatialRoot.transform.position +=
                    xrCamera.transform.right * 0.74f;
                // This anchored position centres the midpoint of the compact
                // desktop pair. Boundary editing then moves the pair left by
                // half its separation so the remaining STC is centred alone.
                desktopOverviewFieldPosition = spatialRoot.transform.position;
                desktopBoundaryFieldPosition = desktopOverviewFieldPosition -
                    spatialRoot.transform.right *
                    (VolumeSTCubeForVrFieldSwapLayout.ActiveSeparation * 0.5f);
                desktopFieldScale = spatialRoot.transform.localScale;
                desktopVisualizationAligned = true;
            }
            spatialRoot.transform.position = desktopOverviewFieldPosition;
            spatialRoot.transform.localScale = desktopFieldScale * 0.78f;
            VolumeSTCubeForVrFieldSwapLayout swapLayout =
                spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout != null)
                swapLayout.KeepCurrentShiftedPosition();
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.SetSurfaceContextVisible(true);
        }

        private void FocusDesktopStcVisualization()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled ||
                !desktopVisualizationAligned || spatialRoot == null)
                return;
            spatialRoot.transform.position = desktopBoundaryFieldPosition;
            spatialRoot.transform.localScale = desktopFieldScale;
            VolumeSTCubeForVrFieldSwapLayout swapLayout =
                spatialRoot.GetComponent<VolumeSTCubeForVrFieldSwapLayout>();
            if (swapLayout != null)
                swapLayout.KeepCurrentShiftedPosition();
        }

        private bool HasSavedAuthorBoundaries()
        {
            return authorBoundaryConfirmed &&
                authoredTimeBuckets != null && authoredTimeBuckets.Length > 0 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0;
        }

        private void EnsureSavedAuthorBoundaries()
        {
            if (HasSavedAuthorBoundaries() || selectedDataset == null)
                return;
            // The numeric cuts/selected values were committed in the Field
            // setup. Rebuild only their request buckets if a later variable or
            // axis refresh discarded the cached arrays; never reopen authoring.
            CommitAuthorBoundaryBuckets();
            authorBoundaryConfirmed = authoredTimeBuckets != null &&
                authoredTimeBuckets.Length > 0 &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0;
        }

        private void OpenInitialAuthorBoundary()
        {
            if (selectedDataset == null || boundaryCanvas == null)
            {
                SetStatus("Choose a variable before defining Author Boundary buckets.");
                return;
            }
            if (IsForVrSurfaceDataset && forVrSurfacePlayer != null)
            {
                OpenForVrCombinedTimeBoundaryEditor(true);
                return;
            }
            if (mainWorkspaceEntered)
            {
                EnsureSavedAuthorBoundaries();
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                slabPreviewBuilt = true;
                boundaryCanvas.gameObject.SetActive(false);
                SetTimeBoundaryHandleVisibility(false);
                SetDepthBoundaryVisibility(false);
                SetStatus("Time and Depth are already saved. Open MatPlot Intent.");
                BuildWorkflowToolbar();
                return;
            }
            PrepareBoundaryVariableQueue();
            ActivateBoundaryVariableQueueEntry();
            initialBoundarySetupActive = true;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            spatialWorkflowStep = SpatialWorkflowStep.BoundaryAuthoring;
            BeginBoundaryEditSession(BoundaryDimension.Time);
            SetStatus(
                roles[0] == DimensionRole.Fixed
                    ? "Slab step 1 of 2: choose the single fixed Time value."
                    : "Slab step 1 of 2: place two Time cuts for Before, During, and After.");
        }

        private void OpenForVrCombinedTimeBoundaryEditor(bool initialSetup)
        {
            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Fixed;
            roles[2] = DimensionRole.Mapped;
            boundaryReturnStage = stage;
            boundaryDimension = BoundaryDimension.Time;
            savedTimeBoundaryStart = timeBoundaryStart;
            savedTimeBoundaryEnd = timeBoundaryEnd;
            savedSelectedTime = selectedTime;
            initialBoundarySetupActive = initialSetup;
            initialTimeBoundaryComplete = false;
            initialDepthBoundaryComplete = false;
            boundaryEditActive = true;
            spatialWorkflowStep = SpatialWorkflowStep.BoundaryAuthoring;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            legacyPanelVisible = false;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (boundaryCanvas != null)
            {
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
                ShowPrimaryTool(boundaryCanvas);
                BuildBoundaryPanel();
            }
            // The two evidence-bearing planes now live inside the combined
            // STC. Suppress the superseded rail handles behind the orange UI.
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            forVrSurfacePlayer.OpenCombinedXytTimeSelection();
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                FocusDesktopStcVisualization();
                forVrSurfacePlayer.SetSurfaceContextVisible(false);
            }
            RefreshSpatialAxisControllers();
            SetStatus("Use CUT A and CUT B in the STC to define Before, During, and After. The orange panel reports the current ranges.");
        }

        private void EnterMainWorkspace()
        {
            if (!authorBoundaryConfirmed || selectedDataset == null)
            {
                OpenInitialAuthorBoundary();
                return;
            }
            preconfigurationActive = false;
            mainWorkspaceEntered = true;
            EnsureSavedAuthorBoundaries();
            stage = Stage.Slab;
            legacyPanelVisible = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            if (spatialAxisComposerRoot != null)
                spatialAxisComposerRoot.SetActive(true);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(true);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            RefreshSpatialAxisControllers();
            UpdateVariablePaletteFollow(true);
            SetStatus("Ranges saved. Drag one or more variables, Time, and Depth onto the tri-axis controller.");
            BuildWorkflowToolbar();
            BuildStage();
        }

        private void PrepareBoundaryVariableQueue()
        {
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            List<int> boundVariables = BoundVariableIndices();
            bool customWorkspace = roles[3] == DimensionRole.Faceted &&
                boundVariables.Count > 1;
            for (int index = 0; customWorkspace &&
                index < boundVariables.Count; index++)
            {
                SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                    item.boundVariable == boundVariables[index]);
                customWorkspace = state != null && !state.usesSharedBoundaries;
            }
            if (customWorkspace)
                boundaryVariableQueue.AddRange(boundVariables);
            else
            {
                int selected = datasets.IndexOf(selectedDataset);
                if (selected >= 0)
                    boundaryVariableQueue.Add(selected);
            }
        }

        private void ActivateBoundaryVariableQueueEntry()
        {
            if (boundaryVariableQueue.Count == 0 ||
                boundaryVariableQueueIndex < 0 ||
                boundaryVariableQueueIndex >= boundaryVariableQueue.Count)
                return;
            int variable = boundaryVariableQueue[boundaryVariableQueueIndex];
            if (variable < 0 || variable >= datasets.Count)
                return;
            selectedDataset = datasets[variable];
            Vector3 fieldCenter = ActiveBoundaryFieldCenter();
            if (timeRail != null)
                timeRail.localPosition = fieldCenter + new Vector3(0.0f,
                    -FieldHalfHeight + 0.095f,
                    FieldHalfDepth * 0.88f);
            SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                item.boundVariable == variable);
            LoadEffectiveBoundaryValues(state);
            RefreshSlabTexture();
            RebuildTimeMarkers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateSlabVisual(false);
        }

        private Vector3 ActiveBoundaryFieldCenter()
        {
            List<int> boundVariables = BoundVariableIndices();
            if (roles[3] != DimensionRole.Faceted ||
                boundVariables.Count <= 1 || selectedDataset == null)
                return Vector3.zero;
            int selected = datasets.IndexOf(selectedDataset);
            int fieldIndex = boundVariables.IndexOf(selected);
            return PairedFieldCenter(Mathf.Max(0, fieldIndex),
                boundVariables.Count);
        }

        private void ResetBoundaryInteractionFieldCenter()
        {
            if (timeRail != null)
                timeRail.localPosition = new Vector3(0.0f,
                    -FieldHalfHeight + 0.095f,
                    FieldHalfDepth * 0.88f);
        }

        private string BoundaryVariableProgressLabel()
        {
            string variable = selectedDataset != null
                ? selectedDataset.Name.ToUpperInvariant() : "VARIABLE";
            return boundaryVariableQueue.Count > 1
                ? (boundaryVariableQueueIndex + 1) + "/" +
                  boundaryVariableQueue.Count + "  " + variable
                : variable;
        }

        private void OpenDatasetImportStage()
        {
            datasetImportConfirmed = false;
            preconfigurationActive = false;
            mainWorkspaceEntered = false;
            authorBoundaryConfirmed = false;
            authoredTimeBuckets = null;
            authoredDepthBuckets = null;
            importSelectedVariableIndex = selectedDataset != null
                ? datasets.IndexOf(selectedDataset) : -1;
            stage = Stage.DatasetImport;
#if !UNITY_EDITOR && !SLABLAB_FLAT
            questImportHeadLocked = true;
            UpdateQuestImportHeadLock(true);
#endif
            if (spatialRoot != null)
                spatialRoot.SetActive(false);
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(false);
            legacyPanelVisible = true;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(true);
            if (currentView != null)
                currentView.SetVisible(false);
            SetStatus("Choose another dataset folder or reconfirm the detected variables.");
            BuildStage();
        }

        private void BuildSlabStage()
        {
            CreateText(panelContent,
                draftOperation == DraftOperation.None ? "CONFIGURE ANALYSIS" : draftOperation.ToString().ToUpperInvariant() + " DRAFT",
                27, FontStyle.Bold, new Vector2(0, 266), new Vector2(1030, 38),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                draftOperation == DraftOperation.None
                    ? "Assign one role to every dimension. Fixed/Faceted select buckets; Mapped stays continuous."
                    : DraftInstruction(),
                16, FontStyle.Normal, new Vector2(0, 234), new Vector2(1030, 28),
                TextAnchor.MiddleLeft, draftOperation == DraftOperation.None ? Muted : Amber);

            string[] names = { "TIME", "DEPTH", "HORIZONTAL", "VARIABLE" };
            string[] summaries =
            {
                roles[0] == DimensionRole.Faceted
                    ? TimeRangeSummary()
                    : selectedDataset.GetTimeLabel(selectedTime),
                roles[1] == DimensionRole.Faceted
                    ? DepthRangeSummary()
                    : "z " + selectedZ,
                roles[2] == DimensionRole.Mapped ? "continuous XY map" : "full basin",
                roles[3] == DimensionRole.Fixed ? selectedDataset.Name : DatasetNamesSummary()
            };
            Color[] colors = { TimeColor, DepthColor, HorizontalColor, VariableColor };
            for (int i = 0; i < names.Length; i++)
                CreateDimensionRow(i, names[i], summaries[i], colors[i], 174 - i * 55);

            if (draftOperation == DraftOperation.Pivot)
            {
                BuildPivotAxisControls();
            }
            else if (draftOperation == DraftOperation.Drill ||
                draftOperation == DraftOperation.RollUp)
            {
                BuildDraftTickBlockControls();
            }
            else
            {
                if (roles[0] == DimensionRole.Fixed)
                    CreateFixedValueGroup(panelContent, -260, -94, "FIXED TIME",
                        selectedDataset.GetTimeLabel(selectedTime), TimeColor,
                        () => NudgeFixedTime(-1), () => NudgeFixedTime(1));
                else
                    CreateBucketSummaryGroup(panelContent, -260, -94, "TIME RANGES",
                        TimeBucketButtonLabels(), TimeColor,
                        () => OpenBoundaryFromSlab(BoundaryDimension.Time));
                if (roles[1] == DimensionRole.Fixed)
                    CreateFixedValueGroup(panelContent, 260, -94, "FIXED DEPTH",
                        "z=" + selectedZ, DepthColor,
                        () => NudgeFixedDepth(-1), () => NudgeFixedDepth(1));
                else
                    CreateBucketSummaryGroup(panelContent, 260, -94, "DEPTH RANGES",
                        DepthBucketButtonLabels(), DepthColor,
                        () => OpenBoundaryFromSlab(BoundaryDimension.Depth));
            }

            CreateButton(panelContent, "MATPLOT INTENT", new Vector2(-185, -206),
                new Vector2(330, 52),
                AreSpatialAxisBindingsComplete(out _) && authorBoundaryConfirmed
                    ? Purple : Card,
                OpenIntentEditor);
            CreateButton(panelContent, "FULL MATRIX", new Vector2(185, -206),
                new Vector2(330, 50),
                intentConfigured ? Amber : Card, BeginGridPlacement);

            CreateText(panelContent, "1  DESCRIBE", 11, FontStyle.Bold,
                new Vector2(-185, -169), new Vector2(330, 20), TextAnchor.MiddleCenter, Purple);
            CreateText(panelContent, "2  MATERIALIZE", 11, FontStyle.Bold,
                new Vector2(185, -169), new Vector2(330, 20), TextAnchor.MiddleCenter, Amber);

            CreateButton(panelContent, draftOperation == DraftOperation.Pivot ? "PIVOT DRAFT" : "PIVOT",
                new Vector2(-315, -276), new Vector2(210, 42),
                draftOperation == DraftOperation.Pivot ? Purple : Card,
                () => BeginDraft(DraftOperation.Pivot));
            CreateButton(panelContent, draftOperation == DraftOperation.Drill ? "DRILL DRAFT" : "DRILL",
                new Vector2(-75, -276), new Vector2(210, 42),
                draftOperation == DraftOperation.Drill ? TimeColor : Card,
                () => BeginDraft(DraftOperation.Drill));
            CreateButton(panelContent, draftOperation == DraftOperation.RollUp ? "ROLL-UP DRAFT" : "ROLL-UP",
                new Vector2(165, -276), new Vector2(210, 42),
                draftOperation == DraftOperation.RollUp ? Green : Card,
                () => BeginDraft(DraftOperation.RollUp));
            if (draftOperation != DraftOperation.None)
                CreateButton(panelContent, "CANCEL DRAFT", new Vector2(410, -276),
                    new Vector2(210, 42), Danger, CancelDraft);
        }

        private void BuildMatrixStage()
        {
            CreateText(panelContent,
                "FACET GRID  /  " + FacetAxisSummary().ToUpperInvariant(),
                27, FontStyle.Bold,
                new Vector2(0, 206), new Vector2(1030, 38), TextAnchor.MiddleLeft, Ink);

            if (!placementConfirmed && s4dGridImage == null)
            {
                CreateText(panelContent, "PLACE GRID", 15, FontStyle.Bold,
                    new Vector2(0, 160), new Vector2(1030, 24), TextAnchor.MiddleLeft, Amber);
                CreateText(panelContent,
                    "A translucent " + activeGridColumns + " x " + activeGridRows +
                        " preview appears 1.5 m ahead. Grip adjusts pose; trigger confirms.",
                    17, FontStyle.Normal, new Vector2(0, 129), new Vector2(980, 30),
                    TextAnchor.MiddleLeft, Muted);
                CreateWireGrid(panelContent, new Vector2(0, -5), new Vector2(720, 260),
                    Mathf.Max(1, activeGridColumns), Mathf.Max(1, activeGridRows), Cyan);
                CreateText(panelContent, "No collision  /  shared scale  /  " +
                    Mathf.Max(1, activeGridColumns * activeGridRows) + " cells",
                    15, FontStyle.Bold, new Vector2(0, -154), new Vector2(720, 24),
                    TextAnchor.MiddleCenter, Green);
                CreateButton(panelContent, "Back to Slab", new Vector2(-205, -207),
                    new Vector2(300, 52), Card, () => Navigate(Stage.Slab));
                CreateButton(panelContent, "Confirm Placement", new Vector2(205, -207),
                    new Vector2(360, 52), Amber, ConfirmGridPlacement);
                return;
            }

            CreateTransformSummary();

            if (jobRunning && s4dGridImage == null)
            {
                CreateText(panelContent,
                    "Materializing immutable snapshot",
                    21, FontStyle.Bold, new Vector2(0, 70), new Vector2(920, 34),
                    TextAnchor.MiddleCenter, Ink);
                int progressColumns = Mathf.Clamp(activeGridColumns, 1,
                    MaxFacetAxisBuckets);
                int progressRows = Mathf.Clamp(activeGridRows, 1,
                    MaxFacetAxisBuckets);
                CreateWireGrid(panelContent, new Vector2(0, -52), new Vector2(720, 220),
                    progressColumns, progressRows, Muted);
                for (int row = 0; row < progressRows; row++)
                    for (int column = 0; column < progressColumns; column++)
                    {
                        int cellNumber = column + row * progressColumns;
                        int cellCount = progressColumns * progressRows;
                        CreateText(panelContent,
                            (cellNumber + 1) / (float)cellCount <= progress
                                ? "READY" : "COMPUTING",
                            Mathf.Clamp(18 - progressColumns, 9, 12),
                            FontStyle.Bold,
                            new Vector2(
                                progressColumns == 1 ? 0 :
                                    -310 + column * (620.0f / (progressColumns - 1)),
                                progressRows == 1 ? -52 :
                                    16 - row * (146.0f / (progressRows - 1))),
                            new Vector2(680.0f / progressColumns, 20),
                            TextAnchor.MiddleCenter,
                            (cellNumber + 1) / (float)cellCount <= progress
                                ? Green : Amber);
                    }
                CreateText(panelContent, Mathf.RoundToInt(progress * 100) + "%  /  " +
                    MaterializationStageLabel(), 17, FontStyle.Bold,
                    new Vector2(0, -185), new Vector2(700, 30),
                    TextAnchor.MiddleCenter, Amber);
                CreateButton(panelContent, "Cancel Job", new Vector2(0, -225),
                    new Vector2(250, 42), Card, CancelS4DGridJob);
                return;
            }

            if (s4dGridImage == null)
            {
                CreateText(panelContent,
                    "The grid could not be materialized. The Slab configuration is still available.",
                    20, FontStyle.Normal, new Vector2(0, 42), new Vector2(900, 70),
                    TextAnchor.MiddleCenter, Ink);
                CreateButton(panelContent, "Retry Full Matrix",
                    new Vector2(0, -62), new Vector2(420, 58), Amber,
                    RetryS4DGridJob);
                return;
            }

            if (gridStale)
            {
                CreatePanelCard(panelContent, new Vector2(0, 108), new Vector2(1010, 38), Amber);
                CreateText(panelContent,
                    "STALE  /  Boundary changed since this snapshot was generated",
                    15, FontStyle.Bold, new Vector2(-90, 108), new Vector2(790, 26),
                    TextAnchor.MiddleLeft, Amber);
                CreateButton(panelContent, "Re-materialize", new Vector2(400, 108),
                    new Vector2(195, 32), Amber, RematerializeS4DGrid);
            }

            // Every Matrix result is a real UI panel, not one atlas-shaped button.
            // Each cell owns its cropped chart, border, label and click target.
            CreateMaterializedFacetCells(panelContent, s4dGridImage,
                new Vector2(-112, -55), new Vector2(760, 332));
            BuildDigestCard();
            if (jobRunning)
                CreateText(panelContent, "UPDATING - old snapshot remains visible",
                    15, FontStyle.Bold, new Vector2(-112, -226), new Vector2(700, 28),
                    TextAnchor.MiddleCenter, Amber);
            else
                CreateText(panelContent, "Point to a cell to inspect its generation footprint.",
                    14, FontStyle.Bold, new Vector2(-112, -226), new Vector2(700, 28),
                    TextAnchor.MiddleCenter, Cyan);
        }

        private void SelectS4DGridCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0, Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0, Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            BuildMatrixBucketSelections();
            SelectMatrixCell(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow),
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow));
            groundMode = GroundMode.Aggregate;
            stage = Stage.Analyze;
            if (!cubeVisible)
                ToggleDataVisibility();
            if (facetGridCanvas != null)
                facetGridCanvas.gameObject.SetActive(false);
            ReturnToSpatialWorkflow();
            SetGroundDock(true);
            LoadGroundAggregateVolume();
            SetStatus("Ground opened for " + CellLabel(column, row) +
                " using the snapshot footprint.");
            BuildStage();
        }

        private string CellLabel(int column, int row)
        {
            int timeIndex = DisplayTimeBucketIndex(column, row);
            int depthIndex = DisplayDepthBucketIndex(column, row);
            string columnLabel = ActiveBucketLabel(activeTimeBuckets, timeIndex,
                new[] { "BEFORE", "DURING", "AFTER" });
            string rowLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                new[] { "SURFACE", "MID", "SEAFLOOR" });
            return columnLabel.ToUpperInvariant() + " x " + rowLabel.ToUpperInvariant();
        }

        private void BuildAnalyzeStage()
        {
            CreateText(panelContent, "GROUND TO CONTINUOUS EVIDENCE", 27, FontStyle.Bold,
                new Vector2(0, 206), new Vector2(1030, 38), TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent, GroundSelectionHeadline(), 17, FontStyle.Bold,
                new Vector2(0, 174), new Vector2(1030, 28), TextAnchor.MiddleLeft, Cyan);

            CreateButton(panelContent, "AGGREGATE", new Vector2(-365, 122),
                new Vector2(285, 48), groundMode == GroundMode.Aggregate ? Green : Card,
                () => SetGroundMode(GroundMode.Aggregate));
            CreateButton(panelContent, "PLAYBACK", new Vector2(-58, 122),
                new Vector2(285, 48), groundMode == GroundMode.Playback ? TimeColor : Card,
                () => SetGroundMode(GroundMode.Playback));
            CreateButton(panelContent, "Return to Grid", new Vector2(337, 122),
                new Vector2(310, 48), Card, ReturnToFacetGrid);

            CreatePanelCard(panelContent, new Vector2(-242, -3), new Vector2(540, 190),
                groundMode == GroundMode.Aggregate ? Green : TimeColor);
            if (s4dGridImage != null)
            {
                RawImage selectedPreview = CreateRawImage(panelContent, s4dGridImage,
                    new Vector2(-395, -4), new Vector2(205, 148));
                selectedPreview.uvRect = SelectedGridCellUv();
            }
            CreateText(panelContent,
                groundMode == GroundMode.Aggregate ? "AGGREGATE VOLUME" : "SOURCE FRAME PLAYBACK",
                17, FontStyle.Bold, new Vector2(-105, 57), new Vector2(255, 28),
                TextAnchor.MiddleLeft, groundMode == GroundMode.Aggregate ? Green : TimeColor);
            CreateText(panelContent,
                groundMode == GroundMode.Aggregate
                    ? "The selected MatPlot cell is placed on the representative slab; the full Time x Depth footprint is highlighted in the Cube."
                    : "The orange cursor advances only through source frames inside this bucket.",
                14, FontStyle.Normal, new Vector2(-105, 10), new Vector2(255, 66),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent, GroundFootprintSummary(), 14, FontStyle.Bold,
                new Vector2(-105, -59), new Vector2(255, 40), TextAnchor.MiddleLeft, Muted);

            CreatePanelCard(panelContent, new Vector2(328, -3), new Vector2(480, 190),
                gridStale ? Amber : Cyan);
            CreateText(panelContent, "EVIDENCE CHECK", 17, FontStyle.Bold,
                new Vector2(328, 57), new Vector2(420, 28), TextAnchor.MiddleLeft,
                gridStale ? Amber : Cyan);
            CreateText(panelContent,
                gridStale
                    ? "The source footprint changed. Re-materialize before judging the finding."
                    : GroundEvidenceComparison(),
                15, FontStyle.Normal, new Vector2(328, 0), new Vector2(420, 82),
                TextAnchor.MiddleLeft, Ink);
            if (gridStale)
                CreateButton(panelContent, "Re-materialize Grid", new Vector2(328, -67),
                    new Vector2(360, 38), Amber, RematerializeS4DGrid);

            CreateText(panelContent, "CONCLUSION", 14, FontStyle.Bold,
                new Vector2(0, -123), new Vector2(1010, 22), TextAnchor.MiddleLeft, Muted);
            CreateButton(panelContent, "SUPPORTED", new Vector2(-330, -172),
                new Vector2(300, 54), Green, AcceptBoundary);
            CreateButton(panelContent, "LOCAL ONLY", new Vector2(0, -172),
                new Vector2(300, 54), Purple, MarkEvidenceLocalized);
            CreateButton(panelContent, "RECHECK BOUNDARY", new Vector2(330, -172),
                new Vector2(300, 54), Amber, MarkBoundarySuspect);
            CreateText(panelContent,
                "Your decision is saved with the finding and source footprint.",
                13, FontStyle.Normal, new Vector2(0, -222), new Vector2(1010, 24),
                TextAnchor.MiddleCenter, Muted);
        }

        private void BuildResultStage()
        {
            FindDigestExtremes(out int minimumIndex, out int maximumIndex,
                out int widestIndex);
            int columnPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridColumns / 3.0f));
            int rowPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridRows / 3.0f));
            digestColumnPage = Mathf.Clamp(digestColumnPage, 0,
                columnPageCount - 1);
            digestRowPage = Mathf.Clamp(digestRowPage, 0,
                rowPageCount - 1);
            CreateText(panelContent, "FINDINGS", 32, FontStyle.Bold,
                new Vector2(0, 214), new Vector2(1030, 36),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                (activeGridColumns * activeGridRows) +
                " PANELS  ·  " +
                IntentDisplayLabel(),
                16, FontStyle.Bold, new Vector2(-170, 181),
                new Vector2(690, 24),
                TextAnchor.MiddleLeft, Muted);
            if (columnPageCount > 1 || rowPageCount > 1)
            {
                CreateButton(panelContent, "PREV PAGE",
                    new Vector2(315, 184), new Vector2(145, 30), Card,
                    () => ChangeDigestPage(-1));
                CreateButton(panelContent, "NEXT PAGE",
                    new Vector2(475, 184), new Vector2(145, 30), Cyan,
                    () => ChangeDigestPage(1));
            }

            CreatePanelCard(panelContent, new Vector2(-242, 12),
                new Vector2(570, 318), Cyan);
            CreateText(panelContent,
                FacetAxisSummary().ToUpperInvariant() + "  /  PAGE " +
                (digestColumnPage + 1) + " of " + columnPageCount,
                15,
                FontStyle.Bold, new Vector2(-242, 145), new Vector2(530, 22),
                TextAnchor.MiddleLeft, Cyan);
            float cellWidth = 170.0f;
            float cellHeight = 78.0f;
            for (int localRow = 0; localRow < 3; localRow++)
            {
                int row = digestRowPage * 3 + localRow;
                if (row >= activeGridRows)
                    continue;
                for (int localColumn = 0; localColumn < 3; localColumn++)
                {
                    int column = digestColumnPage * 3 + localColumn;
                    if (column >= activeGridColumns)
                        continue;
                    int selectedColumn = column;
                    int selectedRow = row;
                    int sourceIndex = SourceCellIndex(column, row);
                    string tag = DigestTaskTag(sourceIndex, minimumIndex,
                        maximumIndex, widestIndex);
                    Color accent = DigestTaskColor(sourceIndex, minimumIndex,
                        maximumIndex, widestIndex);
                    Vector2 cellPosition = new Vector2(
                        -412 + localColumn * cellWidth,
                        91 - localRow * cellHeight);
                    CreateButton(panelContent, string.Empty, cellPosition,
                        new Vector2(cellWidth - 10, cellHeight - 10), accent,
                        () => SelectDigestCell(selectedColumn, selectedRow));
                    CreateText(panelContent,
                        CellLabel(column, row).ToUpperInvariant(),
                        13, FontStyle.Bold, cellPosition + new Vector2(0, 14),
                        new Vector2(cellWidth - 28, 22),
                        TextAnchor.MiddleLeft, Ink).raycastTarget = false;
                    CreateText(panelContent,
                        "MEAN " + matrixMeans[sourceIndex].ToString("0.##") +
                        "   " + tag,
                        12, FontStyle.Bold, cellPosition + new Vector2(0, -13),
                        new Vector2(cellWidth - 28, 20),
                        TextAnchor.MiddleLeft, accent).raycastTarget = false;
                }
            }

            int selectedIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            CreatePanelCard(panelContent, new Vector2(360, 65),
                new Vector2(360, 180), Purple);
            CreateText(panelContent, "SELECTED CELL", 16, FontStyle.Bold,
                new Vector2(360, 132), new Vector2(314, 22),
                TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                CellLabel(selectedGridColumn, selectedGridRow).ToUpperInvariant(),
                20, FontStyle.Bold, new Vector2(360, 102),
                new Vector2(314, 30), TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                "minimum   " + matrixMinimums[selectedIndex].ToString("0.##") +
                "\nmean       " + matrixMeans[selectedIndex].ToString("0.##") +
                "\nmaximum   " + matrixMaximums[selectedIndex].ToString("0.##"),
                15, FontStyle.Bold, new Vector2(360, 52),
                new Vector2(314, 60), TextAnchor.UpperLeft, Ink);
            CreateText(panelContent,
                facetCellStale[selectedIndex] ? "STALE / RE-MATERIALIZE" :
                facetCellBoundarySuspect[selectedIndex] ? "RECHECK BOUNDARY" :
                facetCellLocalized[selectedIndex] ? "LOCAL PATTERN" :
                facetCellInspected[selectedIndex] ? "SUPPORTED" :
                DigestTaskTag(selectedIndex, minimumIndex, maximumIndex, widestIndex),
                13, FontStyle.Bold, new Vector2(360, 17),
                new Vector2(314, 22), TextAnchor.MiddleLeft,
                DigestTaskColor(selectedIndex, minimumIndex, maximumIndex,
                    widestIndex));
            CreateButton(panelContent, "GROUND SELECTED",
                new Vector2(360, -5), new Vector2(314, 34), Green,
                () =>
                {
                    gridCellSelected = true;
                    SelectS4DGridCell(selectedGridColumn, selectedGridRow);
                });

            CreatePanelCard(panelContent, new Vector2(360, -112),
                new Vector2(360, 154), Amber);
            CreateText(panelContent, ActiveDigestHeadline(), 15,
                FontStyle.Bold, new Vector2(360, -52),
                new Vector2(314, 20), TextAnchor.MiddleLeft,
                currentDigest != null ? Green :
                digestRunning ? Cyan : Amber);
            CreateText(panelContent,
                ActiveDigestNarrative(minimumIndex, maximumIndex, widestIndex),
                13, FontStyle.Normal, new Vector2(360, -124),
                new Vector2(314, 112), TextAnchor.UpperLeft, Ink);

            CreateButton(panelContent, "BACK TO GRID", new Vector2(-315, -216),
                new Vector2(260, 40), Cyan, ReturnToFacetGrid);
            CreateButton(panelContent, "SLABTRAIL", new Vector2(-30, -216),
                new Vector2(260, 40), Purple, ToggleTrailPanel);
            CreateButton(panelContent, "GROUND", new Vector2(255, -216),
                new Vector2(260, 40), Card, () => Navigate(Stage.Analyze));
        }

        private string ActiveDigestHeadline()
        {
            if (currentDigest != null &&
                !string.IsNullOrWhiteSpace(currentDigest.headline))
                return (currentDigest.generatedBy != null &&
                    currentDigest.generatedBy.StartsWith("llm:",
                        StringComparison.OrdinalIgnoreCase)
                        ? "AI GRID SUMMARY  /  "
                        : "EVIDENCE SUMMARY  /  ") + currentDigest.headline;
            if (digestRunning)
                return "AI COMPARING ALL " +
                    Mathf.Max(1, activeGridColumns * activeGridRows) +
                    " MATPLOT PANELS...";
            if (!string.IsNullOrWhiteSpace(digestError))
                return "DETERMINISTIC FALLBACK  /  DIGEST SERVICE ERROR";
            return "DETERMINISTIC GRID COMPARISON";
        }

        private string ActiveDigestSummary(int minimumIndex, int maximumIndex,
            int widestIndex)
        {
            if (currentDigest != null &&
                !string.IsNullOrWhiteSpace(currentDigest.summary))
                return HumanizeDigestCellIds(currentDigest.summary);
            string fallback = DigestComparativeSummary(minimumIndex,
                maximumIndex, widestIndex);
            if (digestRunning)
                return fallback + "\nSnapshot statistics remain visible while the " +
                    "asynchronous Digest is prepared.";
            if (!string.IsNullOrWhiteSpace(digestError))
                return fallback + "\nThe comparison remains evidence-only.";
            return fallback;
        }

        private string ActiveDigestNarrative(int minimumIndex, int maximumIndex,
            int widestIndex)
        {
            string narrative = ActiveDigestSummary(minimumIndex, maximumIndex,
                widestIndex);
            if (currentDigest == null || currentDigest.findings == null)
                return narrative;
            int count = Mathf.Min(2, currentDigest.findings.Length);
            for (int index = 0; index < count; index++)
            {
                if (!string.IsNullOrWhiteSpace(currentDigest.findings[index]))
                    narrative += "\n- " +
                        HumanizeDigestCellIds(currentDigest.findings[index]);
            }
            return narrative;
        }

        private void OpenAiFindingsPanel()
        {
            if (aiFindingsCanvas == null)
                return;
            if (facetGridCanvas != null)
            {
                aiFindingsCanvas.transform.position =
                    facetGridCanvas.transform.position;
                aiFindingsCanvas.transform.rotation =
                    facetGridCanvas.transform.rotation;
            }
            HidePrimaryToolsExcept(aiFindingsCanvas);
            aiFindingsCanvas.gameObject.SetActive(true);
            BuildAiFindingsPanel();
        }

        private void CloseAiFindingsPanel()
        {
            if (aiFindingsCanvas != null)
                aiFindingsCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
        }

        private void BuildAiFindingsPanel()
        {
            if (aiFindingsContent == null)
                return;
            ClearChildren(aiFindingsContent);
            CreateText(aiFindingsContent, "AI FINDINGS", 38, FontStyle.Bold,
                new Vector2(-565, 420), new Vector2(820, 54),
                TextAnchor.MiddleLeft, Green);
            CreateButton(aiFindingsContent, "BACK TO MATRIX",
                new Vector2(520, 420), new Vector2(240, 46), Card,
                CloseAiFindingsPanel);

            if (digestRunning)
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, 32),
                    new Vector2(1080, 430), Cyan);
                CreateText(aiFindingsContent, "AI IS COMPARING THE MATRIX",
                    31, FontStyle.Bold, new Vector2(0, 92),
                    new Vector2(960, 54), TextAnchor.MiddleCenter, Cyan);
                CreateText(aiFindingsContent,
                    "The charts remain available as evidence. This panel will update when the interpretation is ready.",
                    23, FontStyle.Normal, new Vector2(0, 22),
                    new Vector2(900, 100), TextAnchor.UpperCenter, Ink);
                return;
            }

            if (currentDigest == null)
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, 32),
                    new Vector2(1080, 430), Amber);
                CreateText(aiFindingsContent, "AI INTERPRETATION UNAVAILABLE",
                    31, FontStyle.Bold, new Vector2(0, 92),
                    new Vector2(960, 54), TextAnchor.MiddleCenter, Amber);
                CreateText(aiFindingsContent,
                    string.IsNullOrWhiteSpace(digestError)
                        ? "No AI interpretation has been returned for this matrix yet."
                        : CompactAiFinding(digestError, 260),
                    22, FontStyle.Normal, new Vector2(0, 12),
                    new Vector2(900, 120), TextAnchor.UpperCenter, Ink);
                return;
            }

            string headline = string.IsNullOrWhiteSpace(currentDigest.headline)
                ? "What this matrix suggests"
                : HumanizeDigestCellIds(currentDigest.headline);
            CreatePanelCard(aiFindingsContent, new Vector2(0, 304),
                new Vector2(1200, 170), Green);
            Text headlineText = CreateText(aiFindingsContent,
                WrapAiFinding(headline, 68),
                28, FontStyle.Bold, new Vector2(0, 338),
                new Vector2(1110, 58), TextAnchor.MiddleLeft, Ink);
            headlineText.resizeTextForBestFit = false;
            Text summaryText = CreateText(aiFindingsContent,
                WrapAiFinding(
                    HumanizeDigestCellIds(currentDigest.summary), 96),
                19, FontStyle.Normal, new Vector2(0, 274),
                new Vector2(1110, 82), TextAnchor.UpperLeft, Muted);
            summaryText.resizeTextForBestFit = false;

            string[] findings = currentDigest.findings ?? new string[0];
            int findingCount = Mathf.Min(5, findings.Length);
            if (findingCount == 0)
            {
                CreatePanelCard(aiFindingsContent, new Vector2(0, -38),
                    new Vector2(1100, 330), Purple);
                CreateText(aiFindingsContent,
                    "No additional AI conclusions were returned. Use the evidence controls in the matrix to inspect individual cells.",
                    24, FontStyle.Normal, new Vector2(0, -16),
                    new Vector2(990, 150), TextAnchor.UpperLeft, Ink);
            }
            else
            {
                for (int index = 0; index < findingCount; index++)
                {
                    int column = index % 2;
                    int row = index / 2;
                    Vector2 position = new Vector2(
                        findingCount == 5 && index == 4
                            ? 0 : (column == 0 ? -305 : 305),
                        125 - row * 190);
                    CreatePanelCard(aiFindingsContent, position,
                        new Vector2(580, 174), Purple);
                    CreateText(aiFindingsContent, "0" + (index + 1),
                        21, FontStyle.Bold, position + new Vector2(-242, 56),
                        new Vector2(54, 34), TextAnchor.MiddleLeft, Purple);
                    Text findingText = CreateText(aiFindingsContent,
                        WrapAiFinding(
                            HumanizeDigestCellIds(findings[index]), 48),
                        20, FontStyle.Normal, position + new Vector2(28, -3),
                        new Vector2(468, 132), TextAnchor.UpperLeft, Ink);
                    // Do not let Unity shrink an entire finding to make one
                    // long line fit. Word-wrap it into the available card and
                    // preserve a consistent, readable evidence-text size.
                    findingText.resizeTextForBestFit = false;
                }
            }

            CreateText(aiFindingsContent,
                "AI INTERPRETATION  /  Verify each claim against the matrix and its source cells.",
                15, FontStyle.Bold, new Vector2(0, -458),
                new Vector2(1200, 28), TextAnchor.MiddleCenter, Muted);
        }

        private static string CompactAiFinding(string value, int maximumLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "No interpretation was returned.";
            string compact = value.Replace("\r", " ").Replace("\n", " ");
            while (compact.Contains("  "))
                compact = compact.Replace("  ", " ");
            compact = compact.Trim();
            if (compact.Length <= maximumLength)
                return compact;
            int sentenceEnd = compact.LastIndexOf('.', maximumLength - 1);
            if (sentenceEnd >= maximumLength / 2)
                return compact.Substring(0, sentenceEnd + 1);
            return compact.Substring(0, maximumLength - 3).TrimEnd() + "...";
        }

        private static string WrapAiFinding(string value, int lineLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "No interpretation was returned.";
            string compact = value.Replace("\r", " ").Replace("\n", " ");
            while (compact.Contains("  "))
                compact = compact.Replace("  ", " ");
            string[] words = compact.Trim().Split(' ');
            StringBuilder wrapped = new StringBuilder(compact.Length + 8);
            int currentLineLength = 0;
            for (int index = 0; index < words.Length; index++)
            {
                string word = words[index];
                if (currentLineLength > 0 &&
                    currentLineLength + 1 + word.Length > lineLength)
                {
                    wrapped.Append('\n');
                    currentLineLength = 0;
                }
                else if (currentLineLength > 0)
                {
                    wrapped.Append(' ');
                    currentLineLength++;
                }
                wrapped.Append(word);
                currentLineLength += word.Length;
            }
            return wrapped.ToString();
        }

        private string HumanizeDigestCellIds(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                activeTimeBuckets == null || activeDepthBuckets == null)
                return value;
            string result = value;
            for (int time = 0; time < activeTimeBuckets.Length; time++)
            {
                for (int depth = 0; depth < activeDepthBuckets.Length; depth++)
                {
                    string cellId = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    string label = activeTimeBuckets[time].label + " / " +
                        activeDepthBuckets[depth].label;
                    result = result.Replace(cellId, label);
                }
            }
            return result;
        }

        private void SelectDigestCell(int column, int row)
        {
            selectedGridColumn = Mathf.Clamp(column, 0,
                Mathf.Max(0, activeGridColumns - 1));
            selectedGridRow = Mathf.Clamp(row, 0,
                Mathf.Max(0, activeGridRows - 1));
            gridCellSelected = true;
            int sourceIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            selectedCellPinned = facetCellPinned[Mathf.Clamp(sourceIndex, 0,
                facetCellPinned.Length - 1)];
            BuildStage();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void ChangeDigestPage(int direction)
        {
            int columnPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridColumns / 3.0f));
            int rowPageCount = Mathf.Max(1,
                Mathf.CeilToInt(activeGridRows / 3.0f));
            int flatPage = digestRowPage * columnPageCount +
                digestColumnPage;
            int pageCount = columnPageCount * rowPageCount;
            flatPage = (flatPage + direction + pageCount) % pageCount;
            digestColumnPage = flatPage % columnPageCount;
            digestRowPage = flatPage / columnPageCount;
            BuildStage();
        }

        private void FocusDigestPageOnSelection()
        {
            digestColumnPage = Mathf.Max(0, selectedGridColumn / 3);
            digestRowPage = Mathf.Max(0, selectedGridRow / 3);
        }

        private void FindDigestExtremes(out int minimumIndex,
            out int maximumIndex, out int widestIndex)
        {
            // Work from the displayed grid rather than assuming that the
            // active cells occupy the first N source slots.  Filtering,
            // Pivot and transposition can all make that assumption false.
            List<int> visibleSourceIndices = new List<int>();
            for (int row = 0; row < Mathf.Max(1, activeGridRows); row++)
            {
                for (int column = 0; column < Mathf.Max(1, activeGridColumns);
                    column++)
                {
                    int sourceIndex = SourceCellIndex(column, row);
                    if (!visibleSourceIndices.Contains(sourceIndex))
                        visibleSourceIndices.Add(sourceIndex);
                }
            }

            if (visibleSourceIndices.Count == 0)
                visibleSourceIndices.Add(0);

            minimumIndex = visibleSourceIndices[0];
            maximumIndex = visibleSourceIndices[0];
            widestIndex = visibleSourceIndices[0];

            // Extreme-cell navigation is always derived from the immutable
            // numeric matrix loaded by Unity.  The LLM may summarize the
            // evidence, but it must never decide which cell a metric button
            // opens.  This also protects an active Unity session from stale
            // digest responses produced by an older backend process.
            //
            // The interval mean is the primary comparison.  Older snapshots
            // can contain identical/default means even though their ranges
            // differ, so fall back to the range midpoint and then maximum.
            // This keeps Highest and Lowest tied to meaningful, distinct
            // visible panels instead of both resolving to source slot zero.
            float meanSpread = StatisticSpread(visibleSourceIndices,
                matrixMeans);
            float midpointSpread = MidpointSpread(visibleSourceIndices);
            Func<int, float> score = meanSpread > 0.000001f
                ? new Func<int, float>(index => SafeStatistic(matrixMeans[index]))
                : midpointSpread > 0.000001f
                    ? new Func<int, float>(index =>
                        SafeStatistic((matrixMinimums[index] +
                            matrixMaximums[index]) * 0.5f))
                    : new Func<int, float>(index =>
                        SafeStatistic(matrixMaximums[index]));

            float minimumScore = score(minimumIndex);
            float maximumScore = score(maximumIndex);
            float widestRange = float.MinValue;
            for (int candidate = 0; candidate < visibleSourceIndices.Count;
                candidate++)
            {
                int index = visibleSourceIndices[candidate];
                float candidateScore = score(index);
                if (candidateScore < minimumScore)
                {
                    minimumScore = candidateScore;
                    minimumIndex = index;
                }
                if (candidateScore > maximumScore)
                {
                    maximumScore = candidateScore;
                    maximumIndex = index;
                }
                float range = SafeStatistic(matrixMaximums[index]) -
                    SafeStatistic(matrixMinimums[index]);
                if (range > widestRange)
                {
                    widestRange = range;
                    widestIndex = index;
                }
            }
        }

        private bool TrySourceIndexForCellId(string cellId, out int sourceIndex)
        {
            sourceIndex = 0;
            if (string.IsNullOrWhiteSpace(cellId) || activeTimeBuckets == null ||
                activeDepthBuckets == null)
                return false;
            for (int depth = 0; depth < activeDepthBuckets.Length; depth++)
            {
                for (int time = 0; time < activeTimeBuckets.Length; time++)
                {
                    string candidate = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    if (!string.Equals(candidate, cellId,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    sourceIndex = Mathf.Clamp(
                        depth * activeTimeBuckets.Length + time, 0,
                        matrixMeans.Length - 1);
                    return true;
                }
            }
            return false;
        }

        private void ApplyAuthoritativeCellStatistics(S4DFacetGridResult result)
        {
            if (result == null || result.CellStatistics == null)
                return;
            Array.Clear(matrixHasData, 0, matrixHasData.Length);
            Array.Clear(matrixValidFractions, 0, matrixValidFractions.Length);
            for (int item = 0; item < result.CellStatistics.Length; item++)
            {
                S4DCellStatistic statistic = result.CellStatistics[item];
                if (statistic == null ||
                    !TrySourceIndexForCellId(statistic.cellId, out int index))
                    continue;
                matrixMinimums[index] = statistic.minimum;
                matrixMeans[index] = statistic.mean;
                matrixMaximums[index] = statistic.maximum;
                // Coverage by itself is not proof that min/mean/max exist.
                // The service marks legacy coverage-only snapshots as empty.
                matrixHasData[index] = statistic.hasData ||
                    statistic.validCount > 0;
                matrixValidFractions[index] = statistic.validFraction;
            }
        }

        private static float SafeStatistic(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0.0f : value;
        }

        private static float StatisticSpread(List<int> indices, float[] values)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int candidate = 0; candidate < indices.Count; candidate++)
            {
                int index = Mathf.Clamp(indices[candidate], 0, values.Length - 1);
                float value = SafeStatistic(values[index]);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }
            return maximum - minimum;
        }

        private float MidpointSpread(List<int> indices)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            for (int candidate = 0; candidate < indices.Count; candidate++)
            {
                int index = Mathf.Clamp(indices[candidate], 0,
                    matrixMinimums.Length - 1);
                float value = SafeStatistic((matrixMinimums[index] +
                    matrixMaximums[index]) * 0.5f);
                minimum = Mathf.Min(minimum, value);
                maximum = Mathf.Max(maximum, value);
            }
            return maximum - minimum;
        }

        private void BuildFacetGenerationOverlay()
        {
            CreatePanelCard(facetGridContent, new Vector2(500, -20),
                new Vector2(350, 350), Amber);
            CreateText(facetGridContent, "MATPLOTAGENT  /  RE-MATERIALIZE",
                13, FontStyle.Bold, new Vector2(500, 126),
                new Vector2(305, 24), TextAnchor.MiddleCenter, Amber);
            facetGridProgressText = CreateText(facetGridContent,
                Mathf.RoundToInt(displayedGridProgress * 100.0f) + "%",
                46, FontStyle.Bold, new Vector2(500, 74),
                new Vector2(300, 72), TextAnchor.MiddleCenter, Ink);
            facetGridProgressStageText = CreateText(facetGridContent,
                MaterializationStageLabel().ToUpperInvariant(), 15,
                FontStyle.Bold, new Vector2(500, 29),
                new Vector2(300, 34), TextAnchor.MiddleCenter, Muted);

            GameObject trackObject = new GameObject("Generation progress track",
                typeof(RectTransform));
            trackObject.transform.SetParent(facetGridContent, false);
            RectTransform track = trackObject.GetComponent<RectTransform>();
            track.sizeDelta = new Vector2(290, 30);
            track.anchoredPosition = new Vector2(500, -12);
            Image trackImage = trackObject.AddComponent<Image>();
            trackImage.sprite = RoundedUiSprite();
            trackImage.type = Image.Type.Sliced;
            trackImage.color = new Color(0.01f, 0.025f, 0.04f, 1.0f);
            trackImage.raycastTarget = false;

            GameObject fillObject = new GameObject("Generation progress fill",
                typeof(RectTransform));
            fillObject.transform.SetParent(track, false);
            RectTransform fill = fillObject.GetComponent<RectTransform>();
            fill.anchorMin = new Vector2(0, 0);
            fill.anchorMax = new Vector2(1, 1);
            fill.offsetMin = new Vector2(4, 4);
            fill.offsetMax = new Vector2(-4, -4);
            facetGridProgressFill = fillObject.AddComponent<Image>();
            facetGridProgressFill.sprite = RoundedUiSprite();
            facetGridProgressFill.type = Image.Type.Filled;
            facetGridProgressFill.fillMethod = Image.FillMethod.Horizontal;
            facetGridProgressFill.fillOrigin = 0;
            facetGridProgressFill.fillAmount = displayedGridProgress;
            facetGridProgressFill.color = Amber;
            facetGridProgressFill.raycastTarget = false;

            int ready = 0;
            for (int i = 0; i < streamingCellTextures.Length; i++)
                if (streamingCellTextures[i] != null)
                    ready++;
            facetGridValidatedText = CreateText(facetGridContent, ready + " / " +
                Mathf.Max(1, activeGridColumns * activeGridRows) +
                " PANELS READY",
                13, FontStyle.Normal, new Vector2(500, -76),
                new Vector2(300, 44), TextAnchor.MiddleCenter, Ink);
            CreateText(facetGridContent,
                "Completed images appear in the empty slots.",
                12, FontStyle.Normal, new Vector2(500, -137),
                new Vector2(300, 46), TextAnchor.MiddleCenter, Muted);
            UpdateFacetGenerationProgress();
        }

        private void UpdateFacetGenerationProgress()
        {
            if (facetGridProgressText != null)
                facetGridProgressText.text =
                    Mathf.RoundToInt(displayedGridProgress * 100.0f) + "%";
            if (facetGridProgressStageText != null)
                facetGridProgressStageText.text =
                    MaterializationStageLabel().ToUpperInvariant();
            if (facetGridProgressFill != null)
                facetGridProgressFill.fillAmount = displayedGridProgress;
        }

        private IEnumerator AnimateGridProgress()
        {
            while (displayedGridProgress + 0.001f < targetGridProgress)
            {
                displayedGridProgress = Mathf.MoveTowards(displayedGridProgress,
                    targetGridProgress, Time.unscaledDeltaTime * 0.42f);
                UpdateFacetGenerationProgress();
                yield return null;
            }
            displayedGridProgress = targetGridProgress;
            UpdateFacetGenerationProgress();
            gridProgressAnimation = null;
        }

        private string DigestTaskTag(int sourceIndex, int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            sourceIndex = Mathf.Clamp(sourceIndex, 0, facetCellStale.Length - 1);
            if (facetCellStale[sourceIndex])
                return "STALE";
            if (facetCellBoundarySuspect[sourceIndex])
                return "SUSPECT";
            if (facetCellInspected[sourceIndex])
                return "VERIFIED";
            string task = (intentTask ?? string.Empty).ToLowerInvariant();
            if (task.Contains("anomal") && sourceIndex == widestIndex)
                return "ANOMALY";
            if (task.Contains("range"))
            {
                if (sourceIndex == maximumIndex)
                    return "HIGHEST";
                if (sourceIndex == minimumIndex)
                    return "LOWEST";
            }
            return sourceIndex == maximumIndex ? "HIGH" :
                sourceIndex == minimumIndex ? "LOW" : "COMPARED";
        }

        private Color DigestTaskColor(int sourceIndex, int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            sourceIndex = Mathf.Clamp(sourceIndex, 0, facetCellStale.Length - 1);
            if (facetCellStale[sourceIndex] ||
                facetCellBoundarySuspect[sourceIndex])
                return Amber;
            if (facetCellInspected[sourceIndex])
                return Green;
            string task = (intentTask ?? string.Empty).ToLowerInvariant();
            if (task.Contains("anomal") && sourceIndex == widestIndex)
                return Purple;
            if (sourceIndex == maximumIndex)
                return TimeColor;
            if (sourceIndex == minimumIndex)
                return DepthColor;
            return Card;
        }

        private string DigestComparativeSummary(int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            return "Highest mean: " + DigestSourceCellLabel(maximumIndex) +
                "; lowest: " + DigestSourceCellLabel(minimumIndex) + ".\n" +
                "Widest range: " + DigestSourceCellLabel(widestIndex) +
                "; all cells use the same scale.";
        }

        private string DigestMetricSummary(int minimumIndex,
            int maximumIndex, int widestIndex)
        {
            minimumIndex = Mathf.Clamp(minimumIndex, 0,
                matrixMeans.Length - 1);
            maximumIndex = Mathf.Clamp(maximumIndex, 0,
                matrixMeans.Length - 1);
            widestIndex = Mathf.Clamp(widestIndex, 0,
                matrixMeans.Length - 1);
            float widestSpread = Mathf.Max(0.0f,
                matrixMaximums[widestIndex] - matrixMinimums[widestIndex]);
            return "HIGHEST CELL AVERAGE  " +
                matrixMeans[maximumIndex].ToString("0.###") +
                FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(maximumIndex).ToUpperInvariant() +
                "\n\nLOWEST CELL AVERAGE  " +
                matrixMeans[minimumIndex].ToString("0.###") +
                FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(minimumIndex).ToUpperInvariant() +
                "\n\nBIGGEST MIN-MAX SPREAD  " +
                widestSpread.ToString("0.###") + FindingUnitSuffix() +
                "\n" + DigestSourceCellLabel(widestIndex).ToUpperInvariant();
        }

        private string FindingUnitSuffix()
        {
            return selectedDataset != null &&
                !string.IsNullOrWhiteSpace(selectedDataset.Unit)
                    ? " " + selectedDataset.Unit
                    : " dataset-value";
        }

        private string DigestSourceCellLabel(int sourceIndex)
        {
            int sourceColumns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
            int timeIndex = Mathf.Clamp(sourceIndex % sourceColumns, 0,
                Mathf.Max(0, sourceColumns - 1));
            int sourceRows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
            int depthIndex = Mathf.Clamp(sourceIndex / sourceColumns, 0,
                Mathf.Max(0, sourceRows - 1));
            int column = activeGridTransposed ? depthIndex : timeIndex;
            int row = activeGridTransposed ? timeIndex : depthIndex;
            return CellLabel(column, row);
        }

        private void CreatePanelCard(RectTransform parent, Vector2 position, Vector2 size, Color accent)
        {
            GameObject cardObject = new GameObject("Panel card", typeof(RectTransform));
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = size;
            card.anchoredPosition = position;
            Image background = cardObject.AddComponent<Image>();
            background.sprite = RoundedUiSprite();
            background.type = Image.Type.Sliced;
            background.color = Color.Lerp(Card,
                new Color(accent.r * 0.25f, accent.g * 0.25f, accent.b * 0.25f, 1.0f), 0.18f);
            background.raycastTarget = false;
            Shadow shadow = cardObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.38f);
            shadow.effectDistance = new Vector2(4.0f, -5.0f);
            Outline outline = cardObject.AddComponent<Outline>();
            outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.28f);
            outline.effectDistance = new Vector2(1, -1);

            GameObject railObject = new GameObject("Card accent rail", typeof(RectTransform));
            railObject.transform.SetParent(card, false);
            RectTransform rail = railObject.GetComponent<RectTransform>();
            rail.anchorMin = new Vector2(0, 0);
            rail.anchorMax = new Vector2(0, 1);
            rail.pivot = new Vector2(0, 0.5f);
            rail.anchoredPosition = new Vector2(0, 0);
            rail.sizeDelta = new Vector2(5, -4);
            Image railImage = railObject.AddComponent<Image>();
            railImage.color = new Color(accent.r, accent.g, accent.b, 0.86f);
            railImage.raycastTarget = false;

            GameObject sheenObject = new GameObject("Card top sheen", typeof(RectTransform));
            sheenObject.transform.SetParent(card, false);
            RectTransform sheen = sheenObject.GetComponent<RectTransform>();
            sheen.anchorMin = new Vector2(0, 1);
            sheen.anchorMax = new Vector2(1, 1);
            sheen.pivot = new Vector2(0.5f, 1);
            sheen.anchoredPosition = new Vector2(0, -1);
            sheen.sizeDelta = new Vector2(-8, 2);
            Image sheenImage = sheenObject.AddComponent<Image>();
            sheenImage.color = new Color(accent.r, accent.g, accent.b, 0.24f);
            sheenImage.raycastTarget = false;
        }

        private void CreateDimensionRow(int index, string name, string summary, Color color, float y)
        {
            CreatePanelCard(panelContent, new Vector2(0, y), new Vector2(1010, 48), color);
            CreateText(panelContent, name, 15, FontStyle.Bold,
                new Vector2(-420, y), new Vector2(170, 28), TextAnchor.MiddleLeft, color);
            CreateButton(panelContent,
                index == 2 ? "MAPPED / EDIT" : RoleLabel(roles[index]),
                new Vector2(-210, y), new Vector2(180, 36),
                RoleColor(roles[index], color), () => CycleDimensionRole(index));
            CreateText(panelContent, summary, 13, FontStyle.Bold,
                new Vector2(88, y), new Vector2(390, 28), TextAnchor.MiddleLeft,
                roles[index] == DimensionRole.Mapped ? color : Ink);
            CreateText(panelContent,
                index == 2 ? "EDIT XY REGION" :
                roles[index] == DimensionRole.Fixed ? "LOCKED" :
                roles[index] == DimensionRole.Faceted ? "GRID AXIS" : "PANEL AXIS",
                12, FontStyle.Bold, new Vector2(402, y), new Vector2(130, 24),
                TextAnchor.MiddleRight, Muted);
        }

        private static string RoleLabel(DimensionRole role)
        {
            return role == DimensionRole.Fixed ? "FIXED" :
                role == DimensionRole.Faceted ? "FACETED" : "MAPPED";
        }

        private string FacetAxisSummary()
        {
            string[] names = { "Time", "Depth", "Horizontal", "Variable" };
            List<string> facets = new List<string>();
            for (int index = 0; index < roles.Length; index++)
                if (roles[index] == DimensionRole.Faceted)
                    facets.Add(names[index]);
            return facets.Count == 0
                ? "single panel (all non-spatial dimensions Fixed)"
                : string.Join(" x ", facets.ToArray()) + " facets";
        }

        private static Color RoleColor(DimensionRole role, Color dimensionColor)
        {
            return role == DimensionRole.Fixed
                ? new Color(dimensionColor.r * 0.45f, dimensionColor.g * 0.45f,
                    dimensionColor.b * 0.45f, 1.0f)
                : role == DimensionRole.Faceted ? dimensionColor :
                new Color(dimensionColor.r, dimensionColor.g, dimensionColor.b, 0.72f);
        }

        private void CycleDimensionRole(int index)
        {
            if (index < 0 || index >= roles.Length || jobRunning)
                return;
            if (index == 2)
            {
                OpenBoundaryFromSlab(BoundaryDimension.Horizontal);
                SetStatus("Horizontal remains the mapped XY panel axis. Draw or reset its green analysis region here.");
                return;
            }

            // The current MatPlot contract is a 2D Facet Grid with Horizontal
            // mapped inside every panel. Time, Depth and Variable therefore
            // switch between Fixed and Faceted; at most two may be grid axes.
            roles[index] = roles[index] == DimensionRole.Faceted
                ? DimensionRole.Fixed
                : DimensionRole.Faceted;
            if (roles[index] == DimensionRole.Faceted)
            {
                int faceted = 0;
                for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                    if (roles[roleIndex] == DimensionRole.Faceted)
                        faceted++;
                if (faceted > 2)
                {
                    int demote = index == 3 ? 1 :
                        roles[3] == DimensionRole.Faceted ? 3 :
                        index == 1 ? 0 : 1;
                    if (demote != index)
                        roles[demote] = DimensionRole.Fixed;
                }
            }
            slabPreviewBuilt = false;
            intentConfigured = false;
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            UpdateAnalysisAxisLabels();
            RefreshVariableFacetStacks();
            RecordTrailEvent("ROLE", new[] { "Time", "Depth", "Horizontal", "Variable" }[index] +
                " -> " + RoleLabel(roles[index]));
            SetStatus("Role changed: " + FacetAxisSummary() +
                ". Generate the Slab Frame again before Full Matrix.");
            BuildStage();
        }

        private void PreviewSlab()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                SetStatus("Generate Slab locked: " + missing);
                BuildWorkflowToolbar();
                return;
            }
            if (selectedDataset == null)
            {
                SetStatus("Generate Slab locked: bind and select a variable first.");
                return;
            }
            gridCellSelected = false;
            selectedCellPinned = false;
            ClearSourcePreviewLayers();
            if (mainWorkspaceEntered)
                EnsureSavedAuthorBoundaries();
            bool reusePreconfiguredBoundaries = HasSavedAuthorBoundaries();
            intentConfigured = false;
            intentResolutionError = string.Empty;
            slabPreviewBuilt = true;
            spatialWorkflowStep = reusePreconfiguredBoundaries
                ? SpatialWorkflowStep.Intent
                : SpatialWorkflowStep.SlabSkeleton;
            // Build provisional dimensions only to draw the empty skeleton.
            // No aggregate or MatPlot request is made at this step.
            BuildS4DGridRequest();
            if (slabPreviewCanvas != null)
            {
                slabPreviewCanvas.transform.localPosition = SlabPreviewDockPosition;
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
            RecordTrailEvent("SLAB", "axis skeleton generated");
            BuildStage();
            if (reusePreconfiguredBoundaries)
            {
                SetStatus("Slab ready with the saved Time and Depth ranges. Open MatPlot Intent.");
                BuildWorkflowToolbar();
            }
            else if (!mainWorkspaceEntered)
            {
                OpenInitialAuthorBoundary();
            }
            else
            {
                SetStatus("Saved Time and Depth ranges could not be restored. Return to Field Setup.");
            }
        }

        private void OnAggregatePreviewComplete(Texture2D atlas, string error)
        {
            jobRunning = false;
            if (atlas == null)
            {
                SetStatus(string.IsNullOrWhiteSpace(error)
                    ? "Aggregate preview returned no image."
                    : error);
            }
            else
            {
                matrixPreviewAtlas = atlas;
                SetStatus(
                    "Preview ready: interval mean, missing values excluded, shared scale across all " +
                        Mathf.Max(1, activeGridColumns * activeGridRows) + " cells.");
            }
            if (slabPreviewCanvas != null && slabPreviewCanvas.gameObject.activeSelf)
                BuildSlabPreviewPanel();
            BuildStage();
        }

        private static int CountSelectedTicks(bool[] ticks)
        {
            int count = 0;
            if (ticks == null)
                return count;
            for (int i = 0; i < ticks.Length; i++)
                if (ticks[i])
                    count++;
            return count;
        }

        private static bool RollupGroupsAreValid(int[] groups, int count)
        {
            if (groups == null || count <= 0)
                return false;
            bool foundGroup = false;
            for (int group = 1; group <= 3; group++)
            {
                int first = -1;
                int last = -1;
                int groupCount = 0;
                for (int index = 0; index < count && index < groups.Length;
                    index++)
                {
                    if (groups[index] != group)
                        continue;
                    if (first < 0)
                        first = index;
                    last = index;
                    groupCount++;
                }
                if (groupCount == 0)
                    continue;
                foundGroup = true;
                if (groupCount < 2 || last - first + 1 != groupCount)
                    return false;
            }
            return foundGroup;
        }

        private void BuildDraftTickBlockControls()
        {
            bool drill = draftOperation == DraftOperation.Drill;
            Color accent = drill ? TimeColor : Green;
            CreatePanelCard(panelContent, new Vector2(0, -91),
                new Vector2(1010, 150), accent);
            CreateText(panelContent,
                drill ? "SELECT ONE OR MORE PARENT TICK-BLOCKS" :
                    "ASSIGN ADJACENT BLOCKS TO ROLL-UP GROUPS",
                13, FontStyle.Bold, new Vector2(-310, -39),
                new Vector2(390, 22),
                TextAnchor.MiddleLeft, accent);
            CreateButton(panelContent, "TIME", new Vector2(185, -39),
                new Vector2(150, 28),
                draftTargetDimension == 0 ? TimeColor : Card,
                () => SetDraftTargetDimension(0));
            CreateButton(panelContent, "DEPTH", new Vector2(355, -39),
                new Vector2(150, 28),
                draftTargetDimension == 1 ? DepthColor : Card,
                () => SetDraftTargetDimension(1));
            CreateTrackPreview(panelContent, new Vector2(0, -83),
                draftTargetDimension);
            if (drill)
            {
                CreateText(panelContent,
                    "Selected parents expand into three children each; unselected buckets remain unchanged.",
                    10, FontStyle.Normal, new Vector2(0, -132),
                    new Vector2(930, 20), TextAnchor.MiddleCenter, Muted);
            }
            else
            {
                CreateText(panelContent, "ACTIVE GROUP", 10, FontStyle.Bold,
                    new Vector2(-330, -132), new Vector2(150, 20),
                    TextAnchor.MiddleLeft, Muted);
                CreateButton(panelContent, "GROUP 1", new Vector2(-105, -132),
                    new Vector2(130, 26),
                    activeRollupGroup == 1 ? Green : Card,
                    () => SelectRollupGroup(1));
                CreateButton(panelContent, "GROUP 2", new Vector2(45, -132),
                    new Vector2(130, 26),
                    activeRollupGroup == 2 ? Purple : Card,
                    () => SelectRollupGroup(2));
                CreateButton(panelContent, "GROUP 3", new Vector2(195, -132),
                    new Vector2(130, 26),
                    activeRollupGroup == 3 ? Amber : Card,
                    () => SelectRollupGroup(3));
                CreateButton(panelContent, "ERASE", new Vector2(345, -132),
                    new Vector2(130, 26),
                    activeRollupGroup == 0 ? Danger : Card,
                    () => SelectRollupGroup(0));
            }
        }

        private void CreateTrackPreview(RectTransform parent, Vector2 position,
            int dimension)
        {
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            int count = DraftBucketCount(dimension);
            float width = Mathf.Min(104, 900.0f / count - 6);
            float step = width + 6;
            float start = -(count - 1) * step * 0.5f;
            bool[] selected = dimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            int[] groups = dimension == 0
                ? timeRollupGroups : depthRollupGroups;
            Color dimensionColor = dimension == 0 ? TimeColor : DepthColor;
            for (int index = 0; index < count; index++)
            {
                int tick = index;
                string label = buckets != null && index < buckets.Length &&
                    !string.IsNullOrWhiteSpace(buckets[index].label)
                        ? buckets[index].label
                        : "bucket " + (index + 1);
                Color color = draftOperation == DraftOperation.RollUp &&
                    groups[index] > 0
                        ? RollupGroupColor(groups[index])
                        : selected[index] ? dimensionColor : Card;
                string prefix = draftOperation == DraftOperation.RollUp &&
                    groups[index] > 0 ? "G" + groups[index] + "  " : string.Empty;
                CreateButton(parent, prefix + label,
                    position + new Vector2(start + index * step, 0),
                    new Vector2(width, 30), color,
                    () => ToggleDraftTick(dimension, tick));
            }
        }

        private static Color RollupGroupColor(int group)
        {
            return group == 1 ? Green : group == 2 ? Purple :
                group == 3 ? Amber : Card;
        }

        // Retained only so older saved drafts can still deserialize their
        // numeric groups. The current direct-manipulation UI intentionally
        // never exposes those implementation identifiers.
        private static bool LegacyRollupGroupUiVisible()
        {
            return false;
        }

        private void SelectRollupGroup(int group)
        {
            activeRollupGroup = Mathf.Clamp(group, 0, 3);
            SetStatus(activeRollupGroup == 0
                ? "Roll-up erase mode: select a tick-block to remove its group."
                : "Assign adjacent tick-blocks to Roll-up group " +
                    activeRollupGroup + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private List<int> ActiveBoundVariableIndices()
        {
            List<int> indices = new List<int>(datasets.Count);
            for (int rigIndex = 0; rigIndex < spatialAxisRigStates.Count;
                rigIndex++)
            {
                int variableIndex = spatialAxisRigStates[rigIndex].boundVariable;
                if (variableIndex >= 0 && variableIndex < datasets.Count &&
                    !indices.Contains(variableIndex))
                    indices.Add(variableIndex);
            }
            if (indices.Count == 0 && selectedDataset != null)
            {
                int selectedIndex = datasets.IndexOf(selectedDataset);
                if (selectedIndex >= 0)
                    indices.Add(selectedIndex);
            }
            return indices;
        }

        private string[] ActiveVariableNames()
        {
            List<int> indices = ActiveBoundVariableIndices();
            if (indices.Count == 0)
                return new[] { "variable" };
            string[] names = new string[indices.Count];
            for (int index = 0; index < indices.Count; index++)
            {
                int variableIndex = indices[index];
                names[index] = variableIndex >= 0 && variableIndex < datasets.Count
                    ? datasets[variableIndex].Name.ToLowerInvariant()
                    : "variable " + (index + 1);
            }
            return names;
        }

        private void BeginSourcePreviewGeneration()
        {
            if (!intentConfigured || !authorBoundaryConfirmed)
            {
                SetStatus("Source preview locked: confirm both boundaries and resolve the intent first.");
                return;
            }
            List<int> variables = ActiveBoundVariableIndices();
            if (variables.Count < 1)
            {
                SetStatus("Source preview locked: select at least one variable.");
                return;
            }

            ClearSourcePreviewLayers();
            sourcePreviewVariableIndices.AddRange(variables);
            sourcePreviewRequestCursor = 0;
            sourcePreviewRunning = true;
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            // Recompute the final 1/3 by 1/3 layout from the confirmed buckets.
            BuildS4DGridRequest();
            if (slabPreviewCanvas != null)
            {
                ShowComposerTool(slabPreviewCanvas);
                BuildSlabPreviewPanel();
            }
            BuildIntentPanel();
            BuildWorkflowToolbar();
            RequestNextSourcePreviewLayer();
        }

        private void RequestNextSourcePreviewLayer()
        {
            if (!sourcePreviewRunning)
                return;
            if (sourcePreviewRequestCursor >= sourcePreviewVariableIndices.Count)
            {
                sourcePreviewRunning = false;
                spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                BuildAllSourcePreviewLayers();
                BuildIntentPanel();
                BuildWorkflowToolbar();
                SetStatus("Raw interval-mean preview ready for " +
                    sourcePreviewLayerAtlases.Count + " variable layer" +
                    (sourcePreviewLayerAtlases.Count == 1 ? string.Empty : "s") +
                    ". MatPlotAgent has not run; Full Matrix is now enabled.");
                return;
            }

            int variableIndex =
                sourcePreviewVariableIndices[sourcePreviewRequestCursor];
            VolumeSTCubeSliceDataset dataset = datasets[variableIndex];
            SetStatus("Building raw interval-mean preview " +
                (sourcePreviewRequestCursor + 1) + "/" +
                sourcePreviewVariableIndices.Count + ": " + dataset.Name + "...");
            S4DFacetGridRequest request =
                BuildS4DGridRequestForVariable(variableIndex);
            request.datasetId = dataset.DatasetId;
            request.variableId = dataset.VariableId;
            VolumeSTCubeS4DAnalysisClient previewClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 60);
            StartCoroutine(previewClient.PreviewAtlas(request,
                (atlas, error) => OnSourcePreviewLayerComplete(
                    variableIndex, atlas, error)));
        }

        private void OnSourcePreviewLayerComplete(int variableIndex,
            Texture2D atlas, string error)
        {
            if (!sourcePreviewRunning)
            {
                if (atlas != null)
                    Destroy(atlas);
                return;
            }
            if (atlas == null)
            {
                sourcePreviewRunning = false;
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                intentResolutionError = string.IsNullOrWhiteSpace(error)
                    ? "Raw interval-mean preview returned no image."
                    : error;
                SetStatus(intentResolutionError + " Resolve & Apply to retry.");
                BuildIntentPanel();
                BuildWorkflowToolbar();
                return;
            }
            sourcePreviewLayerAtlases.Add(atlas);
            sourcePreviewRequestCursor++;
            RequestNextSourcePreviewLayer();
        }

        private void DestroySourcePreviewLayerCanvases()
        {
            for (int index = 0; index < sourcePreviewLayerCanvases.Count; index++)
                if (sourcePreviewLayerCanvases[index] != null)
                    Destroy(sourcePreviewLayerCanvases[index].gameObject);
            sourcePreviewLayerCanvases.Clear();
        }

        private void BuildAllSourcePreviewLayers()
        {
            DestroySourcePreviewLayerCanvases();
            if (sourcePreviewLayerAtlases.Count == 0 || slabPreviewCanvas == null)
                return;

            RectTransform baseContent = slabPreviewContent;
            Texture2D previousAtlas = matrixPreviewAtlas;
            matrixPreviewAtlas = sourcePreviewLayerAtlases[0];
            sourcePreviewRenderVariableIndex = sourcePreviewVariableIndices[0];
            ShowComposerTool(slabPreviewCanvas);
            BuildSlabPreviewPanel();
            if (sourcePreviewLayerAtlases.Count > 1)
                CreateButton(slabPreviewContent, "ACTIVE LAYER",
                    new Vector2(330, 270), new Vector2(190, 38),
                    VariableColor, () => FocusSourcePreviewLayer(0));

            for (int layer = 1; layer < sourcePreviewLayerAtlases.Count; layer++)
            {
                Canvas layerCanvas = CreateFloatingCanvas(
                    "Source Preview Layer " + (layer + 1),
                    SlabPreviewDockPosition, new Vector2(900, 610),
                    0.00066f, Green);
                layerCanvas.sortingOrder = slabPreviewCanvas.sortingOrder - layer;
                layerCanvas.transform.position = slabPreviewCanvas.transform.position +
                    slabPreviewCanvas.transform.forward * (0.055f * layer) +
                    slabPreviewCanvas.transform.right * (0.055f * layer) -
                    slabPreviewCanvas.transform.up * (0.035f * layer);
                layerCanvas.transform.rotation = slabPreviewCanvas.transform.rotation;
                layerCanvas.transform.localScale = slabPreviewCanvas.transform.localScale;
                sourcePreviewLayerCanvases.Add(layerCanvas);

                slabPreviewContent = layerCanvas.GetComponent<RectTransform>();
                matrixPreviewAtlas = sourcePreviewLayerAtlases[layer];
                sourcePreviewRenderVariableIndex =
                    sourcePreviewVariableIndices[layer];
                BuildSlabPreviewPanel();
                int capturedLayer = layer;
                CreateButton(slabPreviewContent, "BRING FORWARD",
                    new Vector2(330, 270), new Vector2(190, 38),
                    VariableColor,
                    () => FocusSourcePreviewLayer(capturedLayer));
            }

            slabPreviewContent = baseContent;
            matrixPreviewAtlas = sourcePreviewLayerAtlases[0];
            sourcePreviewRenderVariableIndex = sourcePreviewVariableIndices[0];
            if (previousAtlas != null &&
                !sourcePreviewLayerAtlases.Contains(previousAtlas))
                Destroy(previousAtlas);
        }

        private void FocusSourcePreviewLayer(int layer)
        {
            if (layer <= 0 || layer >= sourcePreviewLayerAtlases.Count)
                return;
            Texture2D atlas = sourcePreviewLayerAtlases[layer];
            sourcePreviewLayerAtlases.RemoveAt(layer);
            sourcePreviewLayerAtlases.Insert(0, atlas);
            int variable = sourcePreviewVariableIndices[layer];
            sourcePreviewVariableIndices.RemoveAt(layer);
            sourcePreviewVariableIndices.Insert(0, variable);
            BuildAllSourcePreviewLayers();
            SetStatus(datasets[variable].Name +
                " source preview moved to the inspection layer.");
        }

        private void DestroyMaterializedLayerCanvases()
        {
            for (int index = 0; index < materializedLayerCanvases.Count; index++)
                if (materializedLayerCanvases[index] != null)
                    Destroy(materializedLayerCanvases[index].gameObject);
            materializedLayerCanvases.Clear();
        }

        private void RefreshMaterializedLayerVisibility()
        {
            // Extra variable canvases are a 3D-layer affordance. Keeping them
            // alive in the normal 2D result view lets their opaque panel backs
            // sit in front of the interactive grid on some Quest/URP sorting
            // paths, which looks like an entirely empty result panel.
            bool visible = facetGridLayered && facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            for (int index = 0; index < materializedLayerCanvases.Count; index++)
            {
                Canvas layerCanvas = materializedLayerCanvases[index];
                if (layerCanvas != null)
                    layerCanvas.gameObject.SetActive(visible);
            }
        }

        private void BuildMaterializedVariableLayerPanels()
        {
            DestroyMaterializedLayerCanvases();
            if (materializedLayerAtlases.Count <= 1 || facetGridCanvas == null)
                return;
            // The normal Facet Grid remains the interactive front layer. Earlier
            // variables are shown as spatially offset, full-resolution layers
            // behind it and can be inspected by moving or peeling the front grid.
            int extraCount = materializedLayerAtlases.Count - 1;
            for (int layer = 0; layer < extraCount; layer++)
            {
                int variableIndex = layer < materializationVariableIndices.Count
                    ? materializationVariableIndices[layer] : -1;
                string variableName = variableIndex >= 0 &&
                    variableIndex < datasets.Count
                    ? datasets[variableIndex].Name : "variable " + (layer + 1);
                Canvas layerCanvas = CreateFloatingCanvas(
                    "MatPlot Full Matrix " + variableName,
                    Vector3.zero, new Vector2(1120, 650), 0.00058f,
                    VariableColor);
                layerCanvas.sortingOrder = facetGridCanvas.sortingOrder - layer - 1;
                layerCanvas.transform.position = facetGridCanvas.transform.position +
                    facetGridCanvas.transform.forward * (0.070f * (layer + 1)) +
                    facetGridCanvas.transform.right * (0.060f * (layer + 1)) -
                    facetGridCanvas.transform.up * (0.045f * (layer + 1));
                layerCanvas.transform.rotation = facetGridCanvas.transform.rotation;
                layerCanvas.transform.localScale = facetGridCanvas.transform.localScale;
                RectTransform content = layerCanvas.GetComponent<RectTransform>();
                CreateText(content, "MATPLOTAGENT  /  FULL MATRIX",
                    15, FontStyle.Bold, new Vector2(0, 280),
                    new Vector2(1030, 26), TextAnchor.MiddleLeft, Muted);
                CreateText(content, variableName.ToUpperInvariant(),
                    29, FontStyle.Bold, new Vector2(0, 242),
                    new Vector2(1030, 40), TextAnchor.MiddleLeft,
                    VariableColor);
                CreateMaterializedFacetCells(content,
                    materializedLayerAtlases[layer], new Vector2(0, -18),
                    new Vector2(940, 450));
                int capturedLayer = layer;
                CreateButton(content, "BRING FORWARD",
                    new Vector2(390, 242), new Vector2(220, 42),
                    VariableColor,
                    () => FocusMaterializedLayer(capturedLayer));
                // The flat result owns the screen by default. These canvases
                // become visible only after the explicit 3D LAYERS action.
                layerCanvas.gameObject.SetActive(facetGridLayered &&
                    facetGridCanvas.gameObject.activeSelf);
                materializedLayerCanvases.Add(layerCanvas);
            }
        }

        private void FocusMaterializedLayer(int layer)
        {
            int front = materializedLayerAtlases.Count - 1;
            if (layer < 0 || layer >= front ||
                materializationVariableIndices.Count <= front)
                return;
            Texture2D atlas = materializedLayerAtlases[layer];
            materializedLayerAtlases[layer] = materializedLayerAtlases[front];
            materializedLayerAtlases[front] = atlas;
            if (materializedLayerResults.Count > front)
            {
                S4DFacetGridResult result = materializedLayerResults[layer];
                materializedLayerResults[layer] = materializedLayerResults[front];
                materializedLayerResults[front] = result;
            }
            int variable = materializationVariableIndices[layer];
            materializationVariableIndices[layer] =
                materializationVariableIndices[front];
            materializationVariableIndices[front] = variable;
            s4dGridImage = atlas;
            if (materializedLayerResults.Count > front)
                ActivateMaterializedLayerResult(
                    materializedLayerResults[front], variable);
            BuildFacetGridPanel();
            BuildMaterializedVariableLayerPanels();
            SetStatus(datasets[variable].Name +
                " MatPlot layer moved forward for inspection.");
        }

        private void ActivateMaterializedLayerResult(
            S4DFacetGridResult result, int variableIndex)
        {
            if (result == null)
                return;
            if (variableIndex >= 0 && variableIndex < datasets.Count)
                selectedDataset = datasets[variableIndex];
            s4dGridImage = result.Panel;
            s4dChartResultJson = result.ChartResultJson;
            s4dSnapshotId = result.SnapshotId;
            s4dJobId = result.JobId;
            ApplyAuthoritativeCellStatistics(result);
            if (result.SharedScale != null)
            {
                s4dSharedMinimum = result.SharedScale.minimum;
                s4dSharedMaximum = result.SharedScale.maximum;
                s4dSharedUnit = result.SharedScale.unit;
            }

            // A multi-variable result is a stack of immutable 3 x 3 snapshots.
            // Refresh Findings for the layer that the user actually brought
            // forward instead of leaving the final variable's digest attached.
            currentDigest = null;
            digestError = string.Empty;
            string requestedJobId = result.JobId;
            S4DDigestResult cachedDigest;
            if (layerDigestCache.TryGetValue(requestedJobId, out cachedDigest))
            {
                currentDigest = cachedDigest;
                digestRunning = false;
                return;
            }
            digestRunning = true;
            VolumeSTCubeS4DAnalysisClient layerDigestClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(layerDigestClient.GenerateDigest(requestedJobId,
                (digest, error) =>
                {
                    if (!string.Equals(s4dJobId, requestedJobId,
                            StringComparison.Ordinal))
                        return;
                    currentDigest = digest;
                    if (digest != null)
                        layerDigestCache[requestedJobId] = digest;
                    digestError = error ?? string.Empty;
                    digestRunning = false;
                    if (facetGridCanvas != null &&
                        facetGridCanvas.gameObject.activeSelf)
                        BuildFacetGridPanel();
                    if (aiFindingsCanvas != null &&
                        aiFindingsCanvas.gameObject.activeSelf)
                        BuildAiFindingsPanel();
                }));
        }

        private void SetDraftTargetDimension(int dimension)
        {
            if (dimension < 0 || dimension > 1 ||
                roles[dimension] != DimensionRole.Faceted)
            {
                SetStatus("Drill and Roll-up are available only on a Faceted dimension.");
                return;
            }
            if (draftTargetDimension == dimension)
                return;
            draftTargetDimension = dimension;
            ClearDraftTickSelections();
            if (draftOperation == DraftOperation.Drill)
            {
                int count = DraftBucketCount(dimension);
                (dimension == 0 ? selectedTimeTicks : selectedDepthTicks)[
                    Mathf.Clamp(count / 2, 0, MaxFacetAxisBuckets - 1)] = true;
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                int[] groups = dimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                bool[] ticks = dimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                for (int index = 0;
                    index < Mathf.Min(2, DraftBucketCount(dimension)); index++)
                {
                    groups[index] = 1;
                    ticks[index] = true;
                }
            }
            SetStatus((dimension == 0 ? "Time" : "Depth") +
                " selected as the operation target.");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void CreateBucketSummaryGroup(RectTransform parent, float centerX, float centerY,
            string title, string[] labels, Color color, Action editAction)
        {
            CreatePanelCard(parent, new Vector2(centerX, centerY), new Vector2(490, 118), color);
            CreateText(parent, title, 13, FontStyle.Bold,
                new Vector2(centerX - 120, centerY + 40), new Vector2(220, 22),
                TextAnchor.MiddleLeft, color);
            CreateButton(parent, "EDIT CUTS", new Vector2(centerX + 155, centerY + 40),
                new Vector2(150, 28), new Color(color.r, color.g, color.b, 0.72f), editAction);

            for (int i = 0; i < 3; i++)
            {
                float x = centerX + (i - 1) * 150;
                CreatePanelCard(parent, new Vector2(x, centerY - 15),
                    new Vector2(136, 54),
                    new Color(color.r, color.g, color.b, 0.72f));
                CreateText(parent, labels[i], 12, FontStyle.Bold,
                    new Vector2(x, centerY - 15), new Vector2(128, 48),
                    TextAnchor.MiddleCenter, Ink);
            }
        }

        private string[] TimeBucketButtonLabels()
        {
            if (authorBoundaryConfirmed && authoredTimeBuckets != null &&
                authoredTimeBuckets.Length == 3)
                return AuthoredBucketButtonLabels(authoredTimeBuckets, true);
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int firstCut = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(timeBoundaryEnd + 1, firstCut + 1, count - 1);
            return new[]
            {
                "BEFORE\n1-" + firstCut,
                "DURING\n" + (firstCut + 1) + "-" + secondCut,
                "AFTER\n" + (secondCut + 1) + "-" + count
            };
        }

        private string TimeRangeSummary()
        {
            if (authorBoundaryConfirmed && authoredTimeBuckets != null &&
                authoredTimeBuckets.Length == 3)
                return AuthoredBucketSummary(authoredTimeBuckets, true);
            int count = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int firstCut = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(timeBoundaryEnd + 1, firstCut + 1, count - 1);
            return "before 1-" + firstCut + "  /  during " + (firstCut + 1) + "-" + secondCut +
                "  /  after " + (secondCut + 1) + "-" + count;
        }

        private string[] DepthBucketButtonLabels()
        {
            if (authorBoundaryConfirmed && authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketButtonLabels(authoredDepthBuckets, false);
            int count = selectedDataset != null ? selectedDataset.DimZ : 3;
            int firstCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * count),
                1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * count),
                firstCut + 1, count - 1);
            return new[]
            {
                "SURFACE\n0-" + (firstCut - 1),
                "MIDDLE\n" + firstCut + "-" + (secondCut - 1),
                "DEEP\n" + secondCut + "-" + (count - 1)
            };
        }

        private string DepthRangeSummary()
        {
            if (authorBoundaryConfirmed && authoredDepthBuckets != null &&
                authoredDepthBuckets.Length == 3)
                return AuthoredBucketSummary(authoredDepthBuckets, false);
            int count = selectedDataset != null ? selectedDataset.DimZ : 3;
            int firstCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * count),
                1, Mathf.Max(1, count - 2));
            int secondCut = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * count),
                firstCut + 1, count - 1);
            return "surface 0-" + (firstCut - 1) + "  /  middle " + firstCut + "-" +
                (secondCut - 1) + "  /  deep " + secondCut + "-" + (count - 1);
        }

        private static string[] AuthoredBucketButtonLabels(
            S4DIndexBucketRequest[] buckets, bool oneBased)
        {
            string[] labels = new string[buckets.Length];
            for (int index = 0; index < buckets.Length; index++)
            {
                S4DIndexBucketRequest bucket = buckets[index];
                int first = bucket.indices != null && bucket.indices.Length > 0
                    ? bucket.indices[0]
                    : 0;
                int last = bucket.indices != null && bucket.indices.Length > 0
                    ? bucket.indices[bucket.indices.Length - 1]
                    : first;
                if (oneBased)
                {
                    first++;
                    last++;
                }
                labels[index] = bucket.label.ToUpperInvariant() +
                    "\n" + first + "-" + last;
            }
            return labels;
        }

        private static string AuthoredBucketSummary(
            S4DIndexBucketRequest[] buckets, bool oneBased)
        {
            string[] labels = AuthoredBucketButtonLabels(buckets, oneBased);
            for (int index = 0; index < labels.Length; index++)
                labels[index] = labels[index].Replace("\n", " ").ToLowerInvariant();
            return string.Join("  /  ", labels);
        }

        private void OpenBoundaryFromSlab(BoundaryDimension dimension)
        {
            if (boundaryCanvas == null)
                return;
            if (dimension == BoundaryDimension.Time && IsForVrSurfaceDataset &&
                forVrSurfacePlayer != null)
            {
                OpenForVrCombinedTimeBoundaryEditor(false);
                return;
            }
            if (mainWorkspaceEntered &&
                (dimension == BoundaryDimension.Time ||
                 dimension == BoundaryDimension.Depth))
            {
                EnsureSavedAuthorBoundaries();
                SetStatus("Time and Depth are configured once in Field Setup and cannot be re-authored here.");
                return;
            }
            initialBoundarySetupActive = false;
            BeginBoundaryEditSession(dimension);
            SetStatus(dimension == BoundaryDimension.Time
                ? "Place CUT A and CUT B. They divide time into Before, During, and After."
                : "Place LOWER and UPPER. They divide depth into Surface, Middle, and Deep.");
        }

        private void ToggleDraftTick(int dimension, int tick)
        {
            if (tick < 0 || tick >= DraftBucketCount(dimension) ||
                dimension != draftTargetDimension)
                return;
            bool[] ticks = dimension == 0 ? selectedTimeTicks : selectedDepthTicks;
            bool[] otherTicks = dimension == 0 ? selectedDepthTicks : selectedTimeTicks;
            SetAllTicks(otherTicks, false);
            if (draftOperation == DraftOperation.RollUp)
            {
                int[] groups = dimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                if (groups[tick] > 0)
                {
                    groups[tick] = 0;
                }
                else
                {
                    int first = -1;
                    int last = -1;
                    for (int index = 0; index < DraftBucketCount(dimension); index++)
                    {
                        if (groups[index] <= 0)
                            continue;
                        if (first < 0)
                            first = index;
                        last = index;
                    }
                    if (first >= 0 && tick != first - 1 && tick != last + 1)
                    {
                        SetStatus("Roll-up only merges neighbouring buckets. Select next to the green outline.");
                        return;
                    }
                    groups[tick] = 1;
                }
                ticks[tick] = groups[tick] > 0;
            }
            else
            {
                ticks[tick] = !ticks[tick];
            }
            string operation = draftOperation == DraftOperation.Drill ? "drill" :
                draftOperation == DraftOperation.RollUp ? "roll-up" : "selection";
            string groupSuffix = string.Empty;
            SetStatus((ticks[tick] ? "Added to " : "Removed from ") + operation +
                groupSuffix + ": " + (dimension == 0 ? "Time" : "Depth") +
                " tick " + (tick + 1) + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private string IntentDisplayLabel()
        {
            if (!intentConfigured)
                return "WAITING FOR MATPLOTAGENT INTENT";
            return intentMode.Replace("CHARACTERIZE ", string.Empty)
                .Replace("DETERMINE ", string.Empty);
        }

        private void CreateFacetPreviewCells(RectTransform parent, Vector2 position,
            Vector2 size, bool showProgress)
        {
            int columnCount = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rowCount = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            int cellCount = Mathf.Max(1, columnCount * rowCount);
            float cellWidth = size.x / columnCount;
            float cellHeight = size.y / rowCount;

            for (int row = 0; row < rowCount; row++)
            {
                for (int column = 0; column < columnCount; column++)
                {
                    int timeIndex = activeGridTransposed ? row : column;
                    int depthIndex = activeGridTransposed ? column : row;
                    int index = SourceCellIndex(column, row);
                    bool ready = showProgress && index >= 0 &&
                        index < streamingCellTextures.Length &&
                        streamingCellTextures[index] != null;
                    bool selected = gridCellSelected &&
                        selectedGridColumn == column && selectedGridRow == row;
                    int cellSourceIndex = SourceCellIndex(column, row);
                    bool pinned = facetCellPinned[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellPinned.Length - 1)];
                    Color accent = selected
                        ? Purple
                        : pinned ? Amber
                        : showProgress
                            ? (ready ? Green : Amber)
                            : (intentConfigured ? Purple : Cyan);

                    string timeLabel = ActiveBucketLabel(activeTimeBuckets,
                        timeIndex, new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(activeDepthBuckets,
                        depthIndex, new[] { "surface", "middle", "deep" });
                    GameObject cellObject = new GameObject(
                        depthLabel + " x " + timeLabel + " pending panel",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 14, cellHeight - 14);
                    cell.anchoredPosition = position + new Vector2(
                        -size.x * 0.5f + cellWidth * (column + 0.5f),
                        size.y * 0.5f - cellHeight * (row + 0.5f));
                    Image pendingBackground = cellObject.AddComponent<Image>();
                    pendingBackground.sprite = RoundedUiSprite();
                    pendingBackground.type = Image.Type.Sliced;
                    pendingBackground.color = new Color(0.015f, 0.033f, 0.050f, 1.0f);
                    Shadow pendingShadow = cellObject.AddComponent<Shadow>();
                    pendingShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.42f);
                    pendingShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.52f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    Texture2D texture = showProgress
                        ? streamingCellTextures[index]
                        : matrixTextures != null && index < matrixTextures.Length
                            ? matrixTextures[index]
                            : null;
                    RawImage streamedImage = null;
                    GameObject waitingPlaceholder = null;
                    if (texture != null)
                    {
                        streamedImage = CreateRawImage(cell, texture, new Vector2(0, -2),
                            new Vector2(cellWidth - 30, cellHeight - 58));
                    }
                    else
                    {
                        // Keep a transparent RawImage in every generation slot
                        // so OnS4DCellReady can stream the completed panel into
                        // this exact card without rebuilding the whole Canvas.
                        if (showProgress)
                            streamedImage = CreateRawImage(cell, null,
                                new Vector2(0, -2),
                                new Vector2(cellWidth - 30, cellHeight - 58));
                        Text waiting = CreateText(cell, showProgress ? "WAITING" :
                            "XY SLICE\nUNAVAILABLE", 12, FontStyle.Bold,
                            new Vector2(0, -2), new Vector2(cellWidth - 30, cellHeight - 58),
                            TextAnchor.MiddleCenter, Muted);
                        waitingPlaceholder = waiting.gameObject;
                    }

                    CreateText(cell,
                        timeLabel.ToUpperInvariant() + "  /  " +
                            depthLabel.ToUpperInvariant(),
                        Mathf.Clamp(15 - columnCount, 9, 12), FontStyle.Bold,
                        new Vector2(0, cellHeight * 0.5f - 18),
                        new Vector2(cellWidth - 30, 24), TextAnchor.MiddleLeft,
                        Color.Lerp(TimeColor, DepthColor, 0.5f));
                    Text stateLabel = CreateText(cell,
                        showProgress
                            ? (ready ? "VALIDATED" : "GENERATING")
                            : IntentDisplayLabel(),
                        10, FontStyle.Bold, new Vector2(0, -cellHeight * 0.5f + 18),
                        new Vector2(cellWidth - 30, 22), TextAnchor.MiddleLeft, accent);

                    if (showProgress && index >= 0 &&
                        index < facetGridCellImages.Length)
                    {
                        facetGridCellImages[index] = streamedImage;
                        facetGridCellPlaceholders[index] = waitingPlaceholder;
                        facetGridCellStateLabels[index] = stateLabel;
                    }

                    int selectedColumn = column;
                    int selectedRow = row;
                    if (selected)
                        selectedFacetCellAnchor = cell;
                    BoxCollider collider = cellObject.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(cell.sizeDelta.x, cell.sizeDelta.y, 12);
                    bool draftInteraction = parent == facetGridContent &&
                        draftOperation != DraftOperation.None &&
                        spatialWorkflowStep != SpatialWorkflowStep.Materializing;
                    cellObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        draftInteraction
                            ? () => SelectDraftGridCell(selectedColumn, selectedRow)
                            : () => SelectFacetPreviewCell(selectedColumn, selectedRow);
                }
            }
        }

        private void CreateMaterializedFacetCells(RectTransform parent, Texture texture,
            Vector2 position, Vector2 size)
        {
            int columnCount = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rowCount = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);
            bool layered = facetGridLayered && parent == facetGridContent;
            bool draftInteraction = parent == facetGridContent &&
                draftOperation != DraftOperation.None;
            float cellWidth = size.x / columnCount;
            float cellHeight = layered
                ? Mathf.Min(142.0f, size.y / Mathf.Max(1, rowCount))
                : size.y / rowCount;
            int labelFontSize = columnCount > 6 ? 8 :
                columnCount > 3 ? 9 : 11;
            int stateFontSize = columnCount > 6 ? 7 :
                columnCount > 3 ? 8 : 10;
            if (draftInteraction)
            {
                labelFontSize += 2;
                stateFontSize += 2;
            }

            for (int row = 0; row < rowCount; row++)
            {
                if (layered && row < facetGridPeeledLayers)
                    continue;
                for (int column = 0; column < columnCount; column++)
                {
                    int selectedColumn = column;
                    int selectedRow = row;
                    bool selected = gridCellSelected &&
                        selectedGridColumn == column && selectedGridRow == row;
                    int cellSourceIndex = SourceCellIndex(column, row);
                    bool pinned = facetCellPinned[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellPinned.Length - 1)];
                    int timeIndex = DisplayTimeBucketIndex(column, row);
                    int depthIndex = DisplayDepthBucketIndex(column, row);
                    string timeLabel = ActiveBucketLabel(activeTimeBuckets, timeIndex,
                        new[] { "before", "during", "after" });
                    string depthLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                        new[] { "surface", "middle", "deep" });
                    GameObject cellObject = new GameObject(
                        depthLabel + " x " + timeLabel + " panel",
                        typeof(RectTransform));
                    cellObject.layer = 5;
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 12, cellHeight - 12);
                    if (layered)
                    {
                        int visibleLayer = row - facetGridPeeledLayers;
                        float layerX = visibleLayer * 28.0f;
                        float layerY = size.y * 0.31f - visibleLayer * 142.0f;
                        cell.anchoredPosition = position + new Vector2(
                            -size.x * 0.5f + cellWidth * (column + 0.5f) + layerX,
                            layerY);
                        Vector3 local = cell.localPosition;
                        local.z = visibleLayer * 24.0f;
                        cell.localPosition = local;
                        cell.localRotation = Quaternion.Euler(7.0f, -3.0f, 0.0f);
                    }
                    else
                    {
                        cell.anchoredPosition = position + new Vector2(
                            -size.x * 0.5f + cellWidth * (column + 0.5f),
                            size.y * 0.5f - cellHeight * (row + 0.5f));
                    }
                    Image materializedBackground = cellObject.AddComponent<Image>();
                    materializedBackground.sprite = RoundedUiSprite();
                    materializedBackground.type = Image.Type.Sliced;
                    materializedBackground.color =
                        new Color(0.015f, 0.030f, 0.046f, 1.0f);
                    Shadow materializedShadow = cellObject.AddComponent<Shadow>();
                    materializedShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.46f);
                    materializedShadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    bool stale = facetCellStale[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellStale.Length - 1)];
                    bool suspect = facetCellBoundarySuspect[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellBoundarySuspect.Length - 1)];
                    bool verified = facetCellInspected[Mathf.Clamp(
                        cellSourceIndex, 0, facetCellInspected.Length - 1)];
                    Color accent = stale || suspect ? Amber : verified ? Green : selected
                        ? Purple
                        : pinned ? Amber : Cyan;
                    outline.effectColor = new Color(accent.r, accent.g, accent.b,
                        selected ? 0.86f : 0.44f);
                    outline.effectDistance = new Vector2(selected ? 2.5f : 1.5f,
                        selected ? -2.5f : -1.5f);

                    Texture2D streamedTexture = texture == null &&
                        cellSourceIndex >= 0 &&
                        cellSourceIndex < streamingCellTextures.Length
                            ? streamingCellTextures[cellSourceIndex]
                            : null;
                    if (texture != null || streamedTexture != null)
                    {
                        GameObject imageObject = new GameObject(
                            "Cell chart", typeof(RectTransform));
                        imageObject.transform.SetParent(cell, false);
                        RectTransform imageRect =
                            imageObject.GetComponent<RectTransform>();
                        imageRect.sizeDelta = new Vector2(
                            Mathf.Max(28, cellWidth - 20),
                            Mathf.Max(24, cellHeight - 55));
                        imageRect.anchoredPosition = new Vector2(0, 3);
                        RawImage image = imageObject.AddComponent<RawImage>();
                        image.texture = texture != null ? texture : streamedTexture;
                        image.color = Color.white;
                        image.raycastTarget = false;
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellImages.Length)
                            facetGridCellImages[cellSourceIndex] = image;
                        if (texture != null)
                        {
                            int sourceColumns = Mathf.Max(1,
                                activeTimeBuckets != null
                                    ? activeTimeBuckets.Length : 3);
                            int sourceRows = Mathf.Max(1,
                                activeDepthBuckets != null
                                    ? activeDepthBuckets.Length : 3);
                            image.uvRect = new Rect(
                                timeIndex / (float)sourceColumns,
                                (sourceRows - 1 - depthIndex) /
                                    (float)sourceRows,
                                1.0f / sourceColumns,
                                1.0f / sourceRows);
                        }
                    }
                    else
                    {
                        GameObject imageObject = new GameObject(
                            "Cell chart placeholder", typeof(RectTransform));
                        imageObject.transform.SetParent(cell, false);
                        RectTransform imageRect =
                            imageObject.GetComponent<RectTransform>();
                        imageRect.sizeDelta = new Vector2(
                            Mathf.Max(28, cellWidth - 20),
                            Mathf.Max(24, cellHeight - 55));
                        imageRect.anchoredPosition = new Vector2(0, 3);
                        RawImage placeholder = imageObject.AddComponent<RawImage>();
                        placeholder.color = Color.clear;
                        placeholder.raycastTarget = false;
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellImages.Length)
                            facetGridCellImages[cellSourceIndex] = placeholder;
                        Text computingLabel = CreateText(cell, jobRunning
                                ? "MATPLOTAGENT\nCOMPUTING"
                                : "PANEL\nUNAVAILABLE",
                            stateFontSize, FontStyle.Bold, new Vector2(0, 3),
                            new Vector2(Mathf.Max(30, cellWidth - 16),
                                Mathf.Max(24, cellHeight - 55)),
                            TextAnchor.MiddleCenter,
                            jobRunning ? Amber : Muted);
                        if (cellSourceIndex >= 0 &&
                            cellSourceIndex < facetGridCellPlaceholders.Length)
                            facetGridCellPlaceholders[cellSourceIndex] =
                                computingLabel.gameObject;
                    }

                    CreateText(cell, depthLabel + "  x  " + timeLabel,
                        labelFontSize, FontStyle.Bold,
                        new Vector2(0, cellHeight * 0.5f - 14),
                        new Vector2(Mathf.Max(30, cellWidth - 16), 20),
                        TextAnchor.MiddleLeft,
                        Color.Lerp(TimeColor, DepthColor, 0.5f));
                    int draftTick = draftTargetDimension == 0
                        ? timeIndex : depthIndex;
                    bool draftTickMarked = draftInteraction &&
                        draftOperation != DraftOperation.Pivot &&
                        draftTick >= 0 && draftTick < MaxFacetAxisBuckets &&
                        (draftTargetDimension == 0
                            ? selectedTimeTicks[draftTick]
                            : selectedDepthTicks[draftTick]);
                    Text stateLabel = CreateText(cell,
                        texture == null && streamedTexture == null && jobRunning
                            ? "GENERATING" :
                        draftInteraction && draftOperation == DraftOperation.Pivot
                            ? "CURRENT LAYOUT" :
                        draftInteraction && draftOperation == DraftOperation.Drill &&
                            draftTickMarked ? "EXPAND INTO 3" :
                        draftInteraction && draftOperation == DraftOperation.Drill
                            ? "SELECT TO EXPAND" :
                        draftInteraction && draftOperation == DraftOperation.RollUp &&
                            draftTickMarked ? "MERGE SELECTED" :
                        draftInteraction && draftOperation == DraftOperation.RollUp
                            ? "SELECT NEIGHBOUR" :
                        stale ? "STALE / RE-MATERIALIZE" :
                        suspect ? "BOUNDARY SUSPECT" :
                        verified ? "BOUNDARY VERIFIED" :
                        selected ? "SELECTED" :
                        pinned ? "PINNED" :
                        "CLICK TO INSPECT",
                        stateFontSize, FontStyle.Bold,
                        new Vector2(0, -cellHeight * 0.5f + 17),
                        new Vector2(Mathf.Max(30, cellWidth - 16), 20),
                        TextAnchor.MiddleCenter, accent);
                    if (cellSourceIndex >= 0 &&
                        cellSourceIndex < facetGridCellStateLabels.Length)
                        facetGridCellStateLabels[cellSourceIndex] = stateLabel;
                    if (selected)
                        selectedFacetCellAnchor = cell;
                    BoxCollider collider = cellObject.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    collider.size = new Vector3(cell.sizeDelta.x, cell.sizeDelta.y, 12);
                    cellObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        draftInteraction
                            ? () => SelectDraftGridCell(selectedColumn, selectedRow)
                            : () => SelectFacetPreviewCell(selectedColumn, selectedRow);
                }

                if (layered)
                {
                    int visibleLayer = row - facetGridPeeledLayers;
                    int depthIndex = activeGridTransposed ? 0 : row;
                    string layerLabel = ActiveBucketLabel(activeDepthBuckets, depthIndex,
                        new[] { "surface", "middle", "deep" });
                    CreateText(parent,
                        "Z  " + layerLabel.ToUpperInvariant() +
                        "  /  LAYER " + (row + 1),
                        11, FontStyle.Bold,
                        position + new Vector2(-size.x * 0.5f + 18 + visibleLayer * 28.0f,
                            size.y * 0.31f - visibleLayer * 142.0f),
                        new Vector2(135, 30), TextAnchor.MiddleLeft, DepthAxisColor);
                }
            }
        }

        private void SelectDraftGridCell(int column, int row)
        {
            if (draftOperation == DraftOperation.None)
                return;
            if (draftOperation == DraftOperation.Pivot)
            {
                return;
            }
            int tick = draftTargetDimension == 0
                ? DisplayTimeBucketIndex(column, row)
                : DisplayDepthBucketIndex(column, row);
            ToggleDraftTick(draftTargetDimension, tick);
        }

        private void TogglePivotBucketVisibility(int dimension, int index)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            bool[] mask = dimension == 0 ? selectedTimeTicks : selectedDepthTicks;
            int count = DraftBucketCount(dimension);
            index = Mathf.Clamp(index, 0, Mathf.Max(0, count - 1));
            if (index >= mask.Length)
                return;
            if (mask[index] && CountSelectedTicks(mask) <= 1)
            {
                SetStatus("Pivot keeps at least one visible " +
                    (dimension == 0 ? "Time column." : "Depth row."));
                return;
            }
            mask[index] = !mask[index];
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void RotatePivotMatrix()
        {
            SetPivotOrientation(!pivotTransposed);
        }

        private void BuildPivotAxisVisibilityControls(Vector2 gridPosition,
            Vector2 gridSize, int columns, int rows)
        {
            // These controls sit on top of the *source* matrix.  Use the
            // source orientation here; pivotTransposed describes the target
            // preview.  Mixing the two made valid source columns look like
            // nonexistent target buckets and covered them with HIDDEN washes.
            bool columnsAreDepth = activeGridTransposed;
            int columnDimension = columnsAreDepth ? 1 : 0;
            int rowDimension = columnsAreDepth ? 0 : 1;
            bool[] columnMask = columnsAreDepth
                ? selectedDepthTicks : selectedTimeTicks;
            bool[] rowMask = columnsAreDepth
                ? selectedTimeTicks : selectedDepthTicks;
            S4DIndexBucketRequest[] columnBuckets =
                DraftSourceBuckets(columnDimension);
            S4DIndexBucketRequest[] rowBuckets = DraftSourceBuckets(rowDimension);
            string[] columnFallback = columnsAreDepth
                ? new[] { "surface", "middle", "deep" }
                : new[] { "before", "during", "after" };
            string[] rowFallback = columnsAreDepth
                ? new[] { "before", "during", "after" }
                : new[] { "surface", "middle", "deep" };
            Color columnColor = columnsAreDepth ? DepthColor : TimeColor;
            Color rowColor = columnsAreDepth ? TimeColor : DepthColor;
            float columnWidth = gridSize.x / Mathf.Max(1, columns);
            float rowHeight = gridSize.y / Mathf.Max(1, rows);

            for (int column = 0; column < columns; column++)
            {
                int captured = column;
                bool visible = column < columnMask.Length && columnMask[column];
                Vector2 center = gridPosition + new Vector2(
                    -gridSize.x * 0.5f + columnWidth * (column + 0.5f),
                    gridSize.y * 0.5f + 25.0f);
                CreateButton(facetGridContent,
                    ActiveBucketLabel(columnBuckets, column, columnFallback)
                        .ToUpperInvariant(), center,
                    new Vector2(Mathf.Min(190, columnWidth - 18), 38),
                    visible ? columnColor : Card,
                    () => TogglePivotBucketVisibility(columnDimension, captured));
                if (!visible)
                {
                    Vector2 cellCenter = gridPosition + new Vector2(
                        -gridSize.x * 0.5f + columnWidth * (column + 0.5f), 0);
                    CreatePivotHiddenWash(cellCenter,
                        new Vector2(columnWidth - 12, gridSize.y - 12));
                }
            }

            for (int row = 0; row < rows; row++)
            {
                int captured = row;
                bool visible = row < rowMask.Length && rowMask[row];
                Vector2 center = gridPosition + new Vector2(
                    -gridSize.x * 0.5f - 56.0f,
                    gridSize.y * 0.5f - rowHeight * (row + 0.5f));
                CreateButton(facetGridContent,
                    ActiveBucketLabel(rowBuckets, row, rowFallback)
                        .ToUpperInvariant(), center,
                    new Vector2(108, Mathf.Min(58, rowHeight - 14)),
                    visible ? rowColor : Card,
                    () => TogglePivotBucketVisibility(rowDimension, captured));
                if (!visible)
                {
                    Vector2 cellCenter = gridPosition + new Vector2(0,
                        gridSize.y * 0.5f - rowHeight * (row + 0.5f));
                    CreatePivotHiddenWash(cellCenter,
                        new Vector2(gridSize.x - 12, rowHeight - 12));
                }
            }
        }

        private void CreatePivotHiddenWash(Vector2 center, Vector2 size)
        {
            GameObject hiddenObject = new GameObject("Hidden pivot row or column",
                typeof(RectTransform), typeof(Image));
            hiddenObject.layer = 5;
            hiddenObject.transform.SetParent(facetGridContent, false);
            RectTransform rect = hiddenObject.GetComponent<RectTransform>();
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            Image image = hiddenObject.GetComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            image.color = new Color(0.005f, 0.012f, 0.020f, 0.90f);
            image.raycastTarget = false;
            CreateText(rect, "HIDDEN", 13, FontStyle.Bold, Vector2.zero,
                size, TextAnchor.MiddleCenter, Muted);
        }

        private void BeginDraftPivotPreviewDrag()
        {
            if (draftOperation != DraftOperation.Pivot ||
                draftPivotPreviewRoot == null || rayInteractor == null ||
                !TryGetFacetGridPointerLocal(out Vector2 pointer))
                return;
            Vector2 delta = pointer - draftPivotPreviewRoot.anchoredPosition;
            draftPivotPreviewStartPointerAngle = Mathf.Atan2(delta.y, delta.x) *
                Mathf.Rad2Deg;
            draftPivotPreviewVisualAngle = 0.0f;
            draftPivotPreviewDragging = true;
            SetStatus("Rotate the preview a quarter turn, then release to swap Time and Depth.");
        }

        private void UpdateDraftPivotPreviewDrag()
        {
            if (!draftPivotPreviewDragging)
                return;
            if (draftOperation != DraftOperation.Pivot ||
                draftPivotPreviewRoot == null || rayInteractor == null)
            {
                draftPivotPreviewDragging = false;
                return;
            }
            if (rayInteractor.TriggerHeld &&
                TryGetFacetGridPointerLocal(out Vector2 pointer))
            {
                Vector2 delta = pointer - draftPivotPreviewRoot.anchoredPosition;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                draftPivotPreviewVisualAngle = Mathf.Clamp(
                    Mathf.DeltaAngle(draftPivotPreviewStartPointerAngle, angle),
                    -95.0f, 95.0f);
                draftPivotPreviewRoot.localRotation = Quaternion.Euler(
                    0, 0, draftPivotPreviewVisualAngle);
                return;
            }
            if (!rayInteractor.TriggerReleased && rayInteractor.TriggerHeld)
                return;
            bool commit = Mathf.Abs(draftPivotPreviewVisualAngle) >= 32.0f;
            draftPivotPreviewDragging = false;
            if (commit)
            {
                pivotTransposed = !pivotTransposed;
                SetStatus(pivotTransposed
                    ? "Pivot ready: Depth across, Time down."
                    : "Pivot ready: Time across, Depth down.");
                BuildFacetGridPanel();
            }
            else if (draftPivotPreviewRoot != null)
            {
                draftPivotPreviewVisualAngle = 0.0f;
                draftPivotPreviewRoot.localRotation = Quaternion.identity;
            }
        }

        private bool TryGetFacetGridPointerLocal(out Vector2 localPoint)
        {
            localPoint = Vector2.zero;
            if (rayInteractor == null || facetGridCanvas == null ||
                facetGridContent == null)
                return false;
            Plane plane = new Plane(facetGridCanvas.transform.forward,
                facetGridCanvas.transform.position);
            if (!plane.Raycast(rayInteractor.PointerRay, out float distance))
                return false;
            Vector3 local = facetGridContent.InverseTransformPoint(
                rayInteractor.PointerRay.GetPoint(distance));
            localPoint = new Vector2(local.x, local.y);
            return true;
        }

        private void BuildDraftGridInteractionOverlay()
        {
            if (facetGridContent == null || draftOperation == DraftOperation.None)
                return;
            Color accent = OperationColor(draftOperation);
            // Keep one generous header band across Pivot, Drill and Roll-up.
            // The result matrix and its outcome preview share the same lower
            // visual baseline, leaving operation labels clear and readable.
            Vector2 gridPosition = new Vector2(-165, -85);
            Vector2 gridSize = new Vector2(900, 440);
            // Materialized cells leave a six-pixel gutter on every side of
            // their logical slot. Draft outlines must follow the visible card
            // edges rather than the larger slot bounds.
            Vector2 visibleGridSize = gridSize - new Vector2(12, 12);
            int columns = Mathf.Clamp(activeGridColumns, 1,
                MaxFacetAxisBuckets);
            int rows = Mathf.Clamp(activeGridRows, 1,
                MaxFacetAxisBuckets);

            string currentColumns = activeGridTransposed ? "DEPTH" : "TIME";
            string currentRows = activeGridTransposed ? "TIME" : "DEPTH";
            string nextColumns = pivotComparisonMode == 1 ? "DEPTH" :
                pivotComparisonMode == 3 ? "DEPTH" : "TIME";
            string nextRows = pivotComparisonMode >= 2 ? "VARIABLE" :
                pivotComparisonMode == 1 ? "TIME" : "DEPTH";
            string operationLabel = draftOperation == DraftOperation.Pivot
                ? "PIVOT   " + currentColumns + " COLUMNS × " + currentRows +
                    " ROWS   →   " + nextColumns + " COLUMNS × " + nextRows + " ROWS"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL  ·  SELECT BUCKETS"
                    : "ROLL-UP  ·  GROUP NEIGHBOURS";
            operationLabel = draftOperation == DraftOperation.Pivot
                ? "PIVOT   SHOW / HIDE ROWS AND COLUMNS"
                : draftOperation == DraftOperation.Drill
                    ? "DRILL   SELECT ROWS OR COLUMNS TO EXPAND"
                    : "ROLL-UP   SELECT ADJACENT ROWS OR COLUMNS";
            CreateText(facetGridContent, operationLabel,
                21, FontStyle.Bold, new Vector2(-285, 205),
                new Vector2(720, 32), TextAnchor.MiddleLeft, accent);

            if (draftOperation != DraftOperation.Pivot)
            {
                CreateButton(facetGridContent, "TIME", new Vector2(-90, 165),
                    new Vector2(160, 38),
                    draftTargetDimension == 0 ? TimeColor : Card,
                    () => SetDraftTargetDimension(0));
                CreateButton(facetGridContent, "DEPTH", new Vector2(90, 165),
                    new Vector2(160, 38),
                    draftTargetDimension == 1 ? DepthColor : Card,
                    () => SetDraftTargetDimension(1));
            }

            if (draftOperation == DraftOperation.Pivot)
            {
                CreateDashedRect(facetGridContent, gridPosition, visibleGridSize,
                    new Color(accent.r, accent.g, accent.b, 0.84f), 4.0f);
                BuildPivotAxisVisibilityControls(gridPosition, gridSize,
                    columns, rows);
            }
            else
            {
                bool[] selected = draftTargetDimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                int[] groups = draftTargetDimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                int count = Mathf.Min(DraftBucketCount(draftTargetDimension),
                    selected.Length);
                bool targetAcross = draftTargetDimension == 0
                    ? !activeGridTransposed : activeGridTransposed;
                for (int tick = 0; tick < count; tick++)
                {
                    bool marked = draftOperation == DraftOperation.RollUp
                        ? groups[tick] > 0
                        : selected[tick];
                    if (!marked)
                        continue;
                    Color markColor = draftOperation == DraftOperation.RollUp
                        ? RollupGroupColor(groups[tick]) : accent;
                    Vector2 center;
                    Vector2 size;
                    if (targetAcross)
                    {
                        float width = gridSize.x / columns;
                        center = gridPosition + new Vector2(
                            -gridSize.x * 0.5f + width * (tick + 0.5f), 0);
                        size = new Vector2(width - 12, visibleGridSize.y);
                    }
                    else
                    {
                        float height = gridSize.y / rows;
                        center = gridPosition + new Vector2(0,
                            gridSize.y * 0.5f - height * (tick + 0.5f));
                        size = new Vector2(visibleGridSize.x, height - 12);
                    }
                    CreateDraftSelectionWash(facetGridContent, center, size,
                        markColor);
                    CreateDashedRect(facetGridContent, center, size,
                        markColor, 4.0f);
                    string tag = draftOperation == DraftOperation.RollUp
                        ? "MERGE " + (char)('A' + groups[tick] - 1)
                        : "OPEN ×3";
                    if (LegacyRollupGroupUiVisible()) CreateText(facetGridContent, tag, 12, FontStyle.Bold,
                        center, new Vector2(Mathf.Min(160, size.x - 8), 25),
                        TextAnchor.MiddleCenter, markColor);
                }
            }

            CreateDraftOutcomePreview(accent, columns, rows);
        }

        private void CreateDraftOutcomePreview(Color accent, int oldColumns,
            int oldRows)
        {
            Vector2 cardCenter = new Vector2(515, -55);
            Vector2 cardSize = new Vector2(390, 440);
            CreatePanelCard(facetGridContent, cardCenter, cardSize, accent);
            CreateText(facetGridContent,
                draftOperation == DraftOperation.Pivot ? "PIVOT PREVIEW" :
                draftOperation == DraftOperation.Drill ? "EXPANDED RESULT" :
                "MERGED RESULT",
                22, FontStyle.Bold, cardCenter + new Vector2(0, 176),
                new Vector2(320, 34), TextAnchor.MiddleCenter, accent);
            if (LegacyRollupGroupUiVisible() &&
                draftOperation == DraftOperation.RollUp)
            {
                CreateButton(facetGridContent, "A", cardCenter + new Vector2(-86, 138),
                    new Vector2(48, 30), activeRollupGroup == 1 ? Green : Card,
                    () => SelectRollupGroup(1));
                CreateButton(facetGridContent, "B", cardCenter + new Vector2(-28, 138),
                    new Vector2(48, 30), activeRollupGroup == 2 ? Purple : Card,
                    () => SelectRollupGroup(2));
                CreateButton(facetGridContent, "C", cardCenter + new Vector2(30, 138),
                    new Vector2(48, 30), activeRollupGroup == 3 ? Amber : Card,
                    () => SelectRollupGroup(3));
                CreateButton(facetGridContent, "–", cardCenter + new Vector2(88, 138),
                    new Vector2(48, 30), activeRollupGroup == 0 ? Danger : Card,
                    () => SelectRollupGroup(0));
            }
            if (draftOperation == DraftOperation.RollUp)
                CreateText(facetGridContent,
                    "SELECT ADJACENT CELLS  >  MERGE INTO ONE",
                    14, FontStyle.Bold, cardCenter + new Vector2(0, 143),
                    new Vector2(330, 28), TextAnchor.MiddleCenter, Green);

            int previewColumns = oldColumns;
            int previewRows = oldRows;
            if (draftOperation == DraftOperation.Pivot)
            {
                int timeCount = activeTimeBuckets != null
                    ? Mathf.Max(1, activeTimeBuckets.Length)
                    : (activeGridTransposed ? oldRows : oldColumns);
                int depthCount = activeDepthBuckets != null
                    ? Mathf.Max(1, activeDepthBuckets.Length)
                    : (activeGridTransposed ? oldColumns : oldRows);
                int variableCount = Mathf.Max(1,
                    ActiveBoundVariableIndices().Count);
                timeCount = Mathf.Max(1, CountSelectedTicks(selectedTimeTicks));
                depthCount = Mathf.Max(1, CountSelectedTicks(selectedDepthTicks));
                previewColumns = pivotComparisonMode == 1 ||
                    pivotComparisonMode == 3 ? depthCount : timeCount;
                previewRows = pivotComparisonMode >= 2 ? variableCount :
                    pivotComparisonMode == 1 ? timeCount : depthCount;
            }
            else
            {
                S4DIndexBucketRequest[] time = DraftSourceBuckets(0);
                S4DIndexBucketRequest[] depth = DraftSourceBuckets(1);
                int nextTime = time != null ? time.Length : 1;
                int nextDepth = depth != null ? depth.Length : 1;
                if (draftOperation == DraftOperation.Drill)
                {
                    if (draftTargetDimension == 0 && time != null)
                        nextTime = ExpandSelectedBuckets(time,
                            selectedTimeTicks, "ghost_time").Length;
                    else if (draftTargetDimension == 1 && depth != null)
                        nextDepth = ExpandSelectedBuckets(depth,
                            selectedDepthTicks, "ghost_depth").Length;
                }
                else
                {
                    if (draftTargetDimension == 0 && time != null)
                        nextTime = MergeBucketGroups(time,
                            timeRollupGroups, "ghost_time").Length;
                    else if (draftTargetDimension == 1 && depth != null)
                        nextDepth = MergeBucketGroups(depth,
                            depthRollupGroups, "ghost_depth").Length;
                }
                previewColumns = pivotTransposed ? nextDepth : nextTime;
                previewRows = pivotTransposed ? nextTime : nextDepth;
            }
            previewColumns = Mathf.Clamp(previewColumns, 1, 9);
            previewRows = Mathf.Clamp(previewRows, 1, 9);

            Vector2 previewCenter = cardCenter + new Vector2(12, -7);
            Vector2 previewSize = new Vector2(258, 232);
            RectTransform previewParent = facetGridContent;
            Vector2 previewOrigin = previewCenter;
            if (draftOperation == DraftOperation.Pivot)
            {
                GameObject previewObject = new GameObject(
                    "Pivot matrix - grab and rotate", typeof(RectTransform),
                    typeof(Image));
                previewObject.layer = 5;
                previewObject.transform.SetParent(facetGridContent, false);
                draftPivotPreviewRoot = previewObject.GetComponent<RectTransform>();
                draftPivotPreviewRoot.anchoredPosition = previewCenter;
                draftPivotPreviewRoot.sizeDelta = previewSize + new Vector2(42, 46);
                Image previewHitArea = previewObject.GetComponent<Image>();
                previewHitArea.color = new Color(accent.r, accent.g, accent.b, 0.018f);
                previewHitArea.raycastTarget = false;
                previewParent = draftPivotPreviewRoot;
                previewOrigin = Vector2.zero;
            }
            float cellWidth = previewSize.x / previewColumns;
            float cellHeight = previewSize.y / previewRows;
            for (int row = 0; row < previewRows; row++)
            {
                for (int column = 0; column < previewColumns; column++)
                {
                    Vector2 center = previewOrigin + new Vector2(
                        -previewSize.x * 0.5f + cellWidth * (column + 0.5f),
                        previewSize.y * 0.5f - cellHeight * (row + 0.5f));
                    CreateDashedRect(previewParent, center,
                        new Vector2(cellWidth - 5, cellHeight - 5), accent,
                        2.2f);
                }
            }
            if (draftOperation == DraftOperation.Pivot)
            {
                string columnDimension = pivotComparisonMode == 1 ||
                    pivotComparisonMode == 3 ? "DEPTH" : "TIME";
                string rowDimension = pivotComparisonMode >= 2 ? "VARIABLE" :
                    pivotComparisonMode == 1 ? "TIME" : "DEPTH";
                Color columnColor = columnDimension == "DEPTH"
                    ? DepthColor : TimeColor;
                Color rowColor = rowDimension == "VARIABLE" ? VariableColor :
                    rowDimension == "TIME" ? TimeColor : DepthColor;
                CreateText(previewParent, columnDimension + "  COLUMNS",
                    15, FontStyle.Bold,
                    previewOrigin + new Vector2(0, previewSize.y * 0.5f + 19),
                    new Vector2(260, 26), TextAnchor.MiddleCenter, columnColor);
                Text previewRowLabel = CreateText(previewParent,
                    rowDimension + "  ROWS", 15, FontStyle.Bold,
                    previewOrigin + new Vector2(-previewSize.x * 0.5f - 17, 0),
                    new Vector2(190, 22), TextAnchor.MiddleCenter, rowColor);
                previewRowLabel.rectTransform.localRotation =
                    Quaternion.Euler(0, 0, 90.0f);

                bool depthAcross = columnDimension == "DEPTH";
                S4DIndexBucketRequest[] columnBuckets = depthAcross
                    ? DraftSourceBuckets(1) : DraftSourceBuckets(0);
                S4DIndexBucketRequest[] rowBuckets = rowDimension == "TIME"
                    ? DraftSourceBuckets(0) : rowDimension == "DEPTH"
                        ? DraftSourceBuckets(1) : null;
                string[] columnFallback = depthAcross
                    ? new[] { "surface", "middle", "deep" }
                    : new[] { "before", "during", "after" };
                string[] rowFallback = rowDimension == "TIME"
                    ? new[] { "before", "during", "after" }
                    : rowDimension == "DEPTH"
                        ? new[] { "surface", "middle", "deep" }
                        : ActiveVariableNames();
                for (int column = 0; column < previewColumns; column++)
                {
                    float x = -previewSize.x * 0.5f +
                        cellWidth * (column + 0.5f);
                    CreateText(previewParent,
                        ActiveBucketLabel(columnBuckets, column, columnFallback),
                        11, FontStyle.Bold,
                        previewOrigin + new Vector2(x,
                            previewSize.y * 0.5f - 12),
                        new Vector2(Mathf.Max(42, cellWidth - 8), 18),
                        TextAnchor.MiddleCenter, columnColor);
                }
                for (int row = 0; row < previewRows; row++)
                {
                    float y = previewSize.y * 0.5f -
                        cellHeight * (row + 0.5f);
                    CreateText(previewParent,
                        ActiveBucketLabel(rowBuckets, row, rowFallback),
                        11, FontStyle.Bold,
                        previewOrigin + new Vector2(-previewSize.x * 0.5f + 23, y),
                        new Vector2(54, 18), TextAnchor.MiddleCenter, rowColor);
                }
                if (pivotComparisonMode == 2 || pivotComparisonMode == 3)
                {
                    string fixedLabel = pivotComparisonMode == 2
                        ? "DEPTH FIXED  z=" + pivotFixedDepth
                        : "TIME FIXED  " +
                            (selectedDataset != null
                                ? selectedDataset.GetTimeLabel(pivotFixedTime)
                                : "day " + (pivotFixedTime + 1));
                    CreateButton(facetGridContent, "−",
                        cardCenter + new Vector2(-125, -118),
                        new Vector2(44, 32), Card,
                        () => NudgePivotFixedBucket(
                            pivotComparisonMode == 2 ? 1 : 0, -1));
                    CreateText(facetGridContent, fixedLabel, 14,
                        FontStyle.Bold, cardCenter + new Vector2(0, -118),
                        new Vector2(200, 30), TextAnchor.MiddleCenter,
                        pivotComparisonMode == 2 ? DepthColor : TimeColor);
                    CreateButton(facetGridContent, "+",
                        cardCenter + new Vector2(125, -118),
                        new Vector2(44, 32), Card,
                        () => NudgePivotFixedBucket(
                            pivotComparisonMode == 2 ? 1 : 0, 1));
                }
            }
            if (draftOperation == DraftOperation.Pivot)
                CreateButton(facetGridContent, "↻",
                    cardCenter + new Vector2(135, -170),
                    new Vector2(68, 52), Purple, RotatePivotMatrix);
            CreateText(facetGridContent,
                previewColumns + " COLUMNS  ×  " + previewRows + " ROWS",
                13, FontStyle.Bold, cardCenter + new Vector2(-35, -158),
                new Vector2(220, 24), TextAnchor.MiddleCenter, Ink);
            CreateOverlayArrow(facetGridContent, new Vector2(226, -55),
                new Vector2(320, -55), accent);
        }

        private static void CreateDraftSelectionWash(RectTransform parent,
            Vector2 center, Vector2 size, Color color)
        {
            GameObject washObject = new GameObject("Draft selection wash",
                typeof(RectTransform), typeof(Image));
            washObject.layer = 5;
            washObject.transform.SetParent(parent, false);
            RectTransform rect = washObject.GetComponent<RectTransform>();
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            Image image = washObject.GetComponent<Image>();
            image.color = new Color(color.r, color.g, color.b, 0.09f);
            image.raycastTarget = false;
        }

        private static void CreateDashedRect(RectTransform parent, Vector2 center,
            Vector2 size, Color color, float thickness)
        {
            const float dash = 18.0f;
            const float gap = 10.0f;
            int horizontalCount = Mathf.Max(1,
                Mathf.FloorToInt((size.x + gap) / (dash + gap)));
            int verticalCount = Mathf.Max(1,
                Mathf.FloorToInt((size.y + gap) / (dash + gap)));
            float horizontalStep = size.x / horizontalCount;
            float verticalStep = size.y / verticalCount;
            for (int index = 0; index < horizontalCount; index++)
            {
                float x = -size.x * 0.5f + horizontalStep * (index + 0.5f);
                float width = Mathf.Max(3, horizontalStep - gap);
                CreateOverlayRule(parent, center + new Vector2(x, size.y * 0.5f),
                    new Vector2(width, thickness), color, 0);
                CreateOverlayRule(parent, center + new Vector2(x, -size.y * 0.5f),
                    new Vector2(width, thickness), color, 0);
            }
            for (int index = 0; index < verticalCount; index++)
            {
                float y = -size.y * 0.5f + verticalStep * (index + 0.5f);
                float height = Mathf.Max(3, verticalStep - gap);
                CreateOverlayRule(parent, center + new Vector2(size.x * 0.5f, y),
                    new Vector2(thickness, height), color, 0);
                CreateOverlayRule(parent, center + new Vector2(-size.x * 0.5f, y),
                    new Vector2(thickness, height), color, 0);
            }
        }

        private static void CreateOverlayArrow(RectTransform parent, Vector2 from,
            Vector2 to, Color color)
        {
            Vector2 delta = to - from;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            CreateOverlayRule(parent, (from + to) * 0.5f,
                new Vector2(delta.magnitude, 4), color, angle);
            Vector2 direction = delta.normalized;
            Vector2 normal = new Vector2(-direction.y, direction.x);
            Vector2 head = to;
            CreateOverlayRule(parent, head - direction * 11 + normal * 7,
                new Vector2(21, 4), color, angle + 145);
            CreateOverlayRule(parent, head - direction * 11 - normal * 7,
                new Vector2(21, 4), color, angle - 145);
        }

        private static void CreateOverlayRule(RectTransform parent,
            Vector2 position, Vector2 size, Color color, float angle)
        {
            GameObject ruleObject = new GameObject("Draft guide",
                typeof(RectTransform), typeof(Image));
            ruleObject.layer = 5;
            ruleObject.transform.SetParent(parent, false);
            RectTransform rule = ruleObject.GetComponent<RectTransform>();
            rule.sizeDelta = size;
            rule.anchoredPosition = position;
            rule.localRotation = Quaternion.Euler(0, 0, angle);
            Image image = ruleObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private int DisplayTimeBucketIndex(int column, int row)
        {
            return activeGridTransposed ? row : column;
        }

        private int DisplayDepthBucketIndex(int column, int row)
        {
            return activeGridTransposed ? column : row;
        }

        private int SourceCellIndex(int column, int row)
        {
            int sourceColumns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 3);
            int timeIndex = Mathf.Clamp(DisplayTimeBucketIndex(column, row), 0, sourceColumns - 1);
            int sourceRows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 3);
            int depthIndex = Mathf.Clamp(DisplayDepthBucketIndex(column, row), 0, sourceRows - 1);
            return Mathf.Clamp(depthIndex * sourceColumns + timeIndex, 0,
                matrixMinimums.Length - 1);
        }

        private static string ActiveBucketLabel(
            S4DIndexBucketRequest[] buckets, int index, string[] fallback)
        {
            if (buckets != null && index >= 0 && index < buckets.Length &&
                !string.IsNullOrWhiteSpace(buckets[index].label))
                return buckets[index].label;
            return index >= 0 && index < fallback.Length ? fallback[index] : "bucket";
        }

        private void CreateFixedValueGroup(RectTransform parent, float centerX,
            float centerY, string title, string value, Color color,
            Action previous, Action next)
        {
            CreatePanelCard(parent, new Vector2(centerX, centerY),
                new Vector2(490, 118), color);
            CreateText(parent, title + "  /  ONE VALUE", 13, FontStyle.Bold,
                new Vector2(centerX, centerY + 40), new Vector2(430, 22),
                TextAnchor.MiddleCenter, color);
            CreateButton(parent, "-", new Vector2(centerX - 155, centerY - 16),
                new Vector2(92, 54), Card, previous);
            CreatePanelCard(parent, new Vector2(centerX, centerY - 16),
                new Vector2(190, 54), color);
            CreateText(parent, value, 17, FontStyle.Bold,
                new Vector2(centerX, centerY - 16), new Vector2(170, 42),
                TextAnchor.MiddleCenter, Ink);
            CreateButton(parent, "+", new Vector2(centerX + 155, centerY - 16),
                new Vector2(92, 54), color, next);
        }

        private void NudgeFixedTime(int direction)
        {
            if (selectedDataset == null)
                return;
            SetTime(Mathf.Clamp(selectedTime + direction, 0,
                selectedDataset.TimeCount - 1));
            InvalidateSlabConfiguration("Fixed Time changed");
            BuildStage();
        }

        private void NudgeFixedDepth(int direction)
        {
            if (selectedDataset == null)
                return;
            selectedZ = Mathf.Clamp(selectedZ + direction, 0,
                selectedDataset.DimZ - 1);
            slabNormalized = selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            UpdateSlabVisual(true);
            RefreshSlabTexture();
            RefreshVariableFacetStacks();
            InvalidateSlabConfiguration("Fixed Depth changed to z=" + selectedZ);
            BuildStage();
        }

        private void InvalidateSlabConfiguration(string reason,
            bool preserveAuthoredBoundaries = false)
        {
            preserveAuthoredBoundaries = preserveAuthoredBoundaries ||
                mainWorkspaceEntered;
            slabPreviewBuilt = false;
            intentConfigured = false;
            spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
            if (!preserveAuthoredBoundaries)
            {
                authorBoundaryConfirmed = false;
                authoredTimeBuckets = null;
                authoredDepthBuckets = null;
            }
            else if (mainWorkspaceEntered)
            {
                EnsureSavedAuthorBoundaries();
            }
            ClearSourcePreviewLayers();
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            BuildWorkflowToolbar();
            SetStatus(reason + (preserveAuthoredBoundaries &&
                authorBoundaryConfirmed
                    ? ". Saved Time and Depth ranges are retained; complete the axis bindings, then open MatPlot Intent."
                    : ". Complete the axis bindings and Time/Depth setup, then open MatPlot Intent."));
        }

        private void ClearSourcePreviewLayers()
        {
            Texture2D previousAtlas = matrixPreviewAtlas;
            bool previousAtlasIsLayer = previousAtlas != null &&
                sourcePreviewLayerAtlases.Contains(previousAtlas);
            sourcePreviewRunning = false;
            sourcePreviewRequestCursor = 0;
            DestroySourcePreviewLayerCanvases();
            matrixPreviewAtlas = null;
            for (int index = 0; index < sourcePreviewLayerAtlases.Count; index++)
                if (sourcePreviewLayerAtlases[index] != null)
                    Destroy(sourcePreviewLayerAtlases[index]);
            sourcePreviewLayerAtlases.Clear();
            sourcePreviewVariableIndices.Clear();
            sourcePreviewRenderVariableIndex = -1;
            // The alias normally points at layer zero and was destroyed in the
            // loop. Standalone legacy preview atlases still need cleanup.
            if (previousAtlas != null && !previousAtlasIsLayer)
                Destroy(previousAtlas);
            materializationVariableCursor = -1;
            materializationVariableIndices.Clear();
            DestroyMaterializedLayerCanvases();
            for (int index = 0; index < materializedLayerAtlases.Count; index++)
            {
                Texture2D layer = materializedLayerAtlases[index];
                if (layer != null && layer != s4dGridImage &&
                    !IsAnalysisNodeTexture(layer))
                    Destroy(layer);
            }
            materializedLayerAtlases.Clear();
            materializedLayerResults.Clear();
        }

        private static string[] BucketLabels(
            S4DIndexBucketRequest[] buckets, string[] fallback)
        {
            int count = Mathf.Max(1, buckets != null ? buckets.Length : fallback.Length);
            string[] labels = new string[count];
            for (int index = 0; index < count; index++)
                labels[index] = ActiveBucketLabel(buckets, index, fallback);
            return labels;
        }

        private void CreateWireGrid(RectTransform parent, Vector2 position, Vector2 size,
            int columns, int rows, Color color)
        {
            float cellWidth = size.x / columns;
            float cellHeight = size.y / rows;
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    GameObject cellObject = new GameObject("Placement cell", typeof(RectTransform));
                    cellObject.transform.SetParent(parent, false);
                    RectTransform cell = cellObject.GetComponent<RectTransform>();
                    cell.sizeDelta = new Vector2(cellWidth - 10, cellHeight - 10);
                    cell.anchoredPosition = position + new Vector2(
                        -size.x * 0.5f + cellWidth * (column + 0.5f),
                        size.y * 0.5f - cellHeight * (row + 0.5f));
                    Image cellImage = cellObject.AddComponent<Image>();
                    cellImage.color = Color.Lerp(Panel,
                        new Color(color.r * 0.22f, color.g * 0.22f,
                            color.b * 0.22f, 1.0f), 0.34f);
                    cellImage.raycastTarget = false;
                    Shadow shadow = cellObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.38f);
                    shadow.effectDistance = new Vector2(3, -4);
                    Outline outline = cellObject.AddComponent<Outline>();
                    outline.effectColor = new Color(color.r, color.g, color.b, 0.50f);
                    outline.effectDistance = new Vector2(1.5f, -1.5f);

                    GameObject corner = new GameObject("Cell corner accent",
                        typeof(RectTransform));
                    corner.transform.SetParent(cell, false);
                    RectTransform cornerRect = corner.GetComponent<RectTransform>();
                    cornerRect.anchorMin = new Vector2(0, 1);
                    cornerRect.anchorMax = new Vector2(0, 1);
                    cornerRect.pivot = new Vector2(0, 1);
                    cornerRect.anchoredPosition = new Vector2(0, 0);
                    cornerRect.sizeDelta = new Vector2(38, 4);
                    Image cornerImage = corner.AddComponent<Image>();
                    cornerImage.color = new Color(color.r, color.g, color.b, 0.90f);
                    cornerImage.raycastTarget = false;
                }
            }
        }

        private string DraftInstruction()
        {
            switch (draftOperation)
            {
                case DraftOperation.Pivot:
                    return "Copied from the current analysis. Change roles or fixed buckets, then preview and submit.";
                case DraftOperation.Drill:
                    return "Copied from the current analysis. Select one or more parent buckets to expand into an explicit child comparison.";
                case DraftOperation.RollUp:
                    return "Copied from the current analysis. Group adjacent faceted tick-blocks into coarser buckets.";
                default:
                    return string.Empty;
            }
        }

        private void BuildPivotAxisControls()
        {
            CreatePanelCard(panelContent, new Vector2(0, -91), new Vector2(1010, 126), Purple);
            CreateText(panelContent, "CHOOSE COMPARISON ORIENTATION", 13, FontStyle.Bold,
                new Vector2(-315, -52), new Vector2(390, 22),
                TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                "Pivot copies the current Slab. It changes only the Grid axes; the source snapshot remains in SlabTrail.",
                12, FontStyle.Normal, new Vector2(0, -77), new Vector2(940, 22),
                TextAnchor.MiddleLeft, Muted);
            CreateButton(panelContent, "TIME COLUMNS  /  DEPTH ROWS",
                new Vector2(-245, -117), new Vector2(450, 40),
                pivotTransposed ? Card : TimeColor, () => SetPivotOrientation(false));
            CreateButton(panelContent, "DEPTH COLUMNS  /  TIME ROWS",
                new Vector2(245, -117), new Vector2(450, 40),
                pivotTransposed ? Purple : Card, () => SetPivotOrientation(true));
        }

        private void SetPivotOrientation(bool transposed)
        {
            if (draftOperation != DraftOperation.Pivot || pivotTransposed == transposed)
                return;
            pivotComparisonMode = transposed ? 1 : 0;
            pivotTransposed = transposed;
            SetStatus(transposed
                ? "Pivot draft set to Depth columns x Time rows."
                : "Pivot draft set to Time columns x Depth rows.");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void SetPivotComparisonMode(int mode)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            pivotComparisonMode = Mathf.Clamp(mode, 0, 3);
            pivotTransposed = pivotComparisonMode == 1 ||
                pivotComparisonMode == 3;
            string[] labels =
            {
                "Time columns x Depth rows",
                "Depth columns x Time rows",
                "Time columns x Variable layers; Depth fixed",
                "Depth columns x Variable layers; Time fixed"
            };
            SetStatus("Pivot copied from " +
                (string.IsNullOrWhiteSpace(draftSourceNodeId)
                    ? "the current Slab" : draftSourceNodeId) +
                ". New comparison: " + labels[pivotComparisonMode] + ".");
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void NudgePivotFixedBucket(int dimension, int delta)
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            if (dimension == 0)
            {
                int count = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.TimeCount) : 1;
                pivotFixedTime = Mathf.Clamp(pivotFixedTime + delta, 0,
                    count - 1);
            }
            else
            {
                int count = selectedDataset != null
                    ? Mathf.Max(1, selectedDataset.DimZ == 92
                        ? 91 : selectedDataset.DimZ) : 1;
                pivotFixedDepth = Mathf.Clamp(pivotFixedDepth + delta, 0,
                    count - 1);
            }
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
        }

        private void ApplyPivotDraftConfiguration()
        {
            if (draftOperation != DraftOperation.Pivot)
                return;
            for (int index = 0; index < roles.Length; index++)
                roles[index] = pivotSourceRoles[index];
            roles[2] = DimensionRole.Mapped;
            if (pivotComparisonMode == 2)
            {
                roles[0] = DimensionRole.Faceted;
                roles[1] = DimensionRole.Fixed;
                roles[3] = DimensionRole.Faceted;
                selectedZ = pivotFixedDepth;
            }
            else if (pivotComparisonMode == 3)
            {
                roles[0] = DimensionRole.Fixed;
                roles[1] = DimensionRole.Faceted;
                roles[3] = DimensionRole.Faceted;
                selectedTime = pivotFixedTime;
            }
        }

        private void BeginDraft(DraftOperation operation)
        {
            if (selectedDataset == null)
            {
                SetStatus("Choose a dataset before creating an analysis draft.");
                return;
            }
            if (draftOperation == operation && facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf)
            {
                BuildFacetGridPanel();
                return;
            }
            draftSourceNodeId = currentAnalysisNode != null
                ? currentAnalysisNode.nodeId
                : string.Empty;
            draftOperation = operation;
            pivotTransposed = operation == DraftOperation.Pivot
                ? currentAnalysisNode == null || !currentAnalysisNode.gridTransposed
                : currentAnalysisNode != null && currentAnalysisNode.gridTransposed;
            for (int roleIndex = 0; roleIndex < roles.Length; roleIndex++)
                pivotSourceRoles[roleIndex] = roles[roleIndex];
            pivotComparisonMode = pivotTransposed ? 1 : 0;
            pivotFixedTime = selectedTime;
            pivotFixedDepth = selectedZ;
            ClearDraftTickSelections();
            draftTargetDimension = roles[0] == DimensionRole.Faceted
                ? 0 : 1;
            if (operation == DraftOperation.Pivot)
            {
                ResetSelectedTicksToActiveBuckets();
            }
            else if (operation == DraftOperation.Drill)
            {
                int count = DraftBucketCount(draftTargetDimension);
                (draftTargetDimension == 0
                    ? selectedTimeTicks
                    : selectedDepthTicks)[Mathf.Clamp(count / 2, 0,
                        MaxFacetAxisBuckets - 1)] = true;
            }
            else if (operation == DraftOperation.RollUp)
            {
                int[] groups = draftTargetDimension == 0
                    ? timeRollupGroups : depthRollupGroups;
                bool[] ticks = draftTargetDimension == 0
                    ? selectedTimeTicks : selectedDepthTicks;
                int count = DraftBucketCount(draftTargetDimension);
                for (int index = 0; index < Mathf.Min(2, count); index++)
                {
                    groups[index] = 1;
                    ticks[index] = true;
                }
            }
            facetGridLayered = false;
            facetGridPeeledLayers = 0;
            // Keep the completed spatial workspace visible. Draft operations
            // use their own compact editor and never reopen the legacy slab
            // controller.
            legacyPanelVisible = false;
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            // Pivot, Drill, and Roll-up are edited directly on the result grid.
            // A second abstract panel duplicated state and obscured the cells.
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            SetStatus(operation +
                " is active on the current result grid.");
        }

        private static void SetAllTicks(bool[] ticks, bool value)
        {
            if (ticks == null)
                return;
            for (int index = 0; index < ticks.Length; index++)
                ticks[index] = value;
        }

        private void ClearDraftTickSelections()
        {
            Array.Clear(selectedTimeTicks, 0, selectedTimeTicks.Length);
            Array.Clear(selectedDepthTicks, 0, selectedDepthTicks.Length);
            Array.Clear(timeRollupGroups, 0, timeRollupGroups.Length);
            Array.Clear(depthRollupGroups, 0, depthRollupGroups.Length);
            activeRollupGroup = 1;
        }

        private void ResetSelectedTicksToActiveBuckets()
        {
            ClearDraftTickSelections();
            int timeCount = Mathf.Clamp(DraftBucketCount(0), 1,
                selectedTimeTicks.Length);
            int depthCount = Mathf.Clamp(DraftBucketCount(1), 1,
                selectedDepthTicks.Length);
            for (int index = 0; index < timeCount; index++)
                selectedTimeTicks[index] = true;
            for (int index = 0; index < depthCount; index++)
                selectedDepthTicks[index] = true;
        }

        private int DraftBucketCount(int dimension)
        {
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            return buckets != null && buckets.Length > 0
                ? Mathf.Min(buckets.Length, MaxFacetAxisBuckets)
                : 3;
        }

        private S4DIndexBucketRequest[] DraftSourceBuckets(int dimension)
        {
            AnalysisNodeState source = FindAnalysisNode(draftSourceNodeId) ??
                currentAnalysisNode;
            S4DIndexBucketRequest[] buckets = source != null
                ? dimension == 0 ? source.timeBuckets : source.depthBuckets
                : dimension == 0 ? activeTimeBuckets : activeDepthBuckets;
            return buckets;
        }

        private void CancelDraft()
        {
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            pivotTransposed = currentAnalysisNode != null &&
                currentAnalysisNode.gridTransposed;
            slabPreviewBuilt = false;
            ResetSelectedTicksToActiveBuckets();
            if (draftCanvas != null)
                draftCanvas.gameObject.SetActive(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && s4dGridImage != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            SetStatus("Draft cancelled. No analysis node was created.");
        }

        private void OpenIntentEditor()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("MatPlot Intent is locked: " + missing);
                return;
            }
            EnsureSavedAuthorBoundaries();
            if (!authorBoundaryConfirmed)
            {
                SetStatus("MatPlot Intent is locked until Time and Depth are confirmed.");
                return;
            }
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            if (intentCanvas != null)
            {
                HidePrimaryToolsExcept(intentCanvas);
                intentCanvas.transform.localPosition = IntentToolDockPosition;
                ShowComposerTool(intentCanvas);
                BuildIntentPanel();
            }
            SetStatus("Intent composer opened. The saved Time and Depth choices determine the matrix size.");
        }

        private void BeginGridPlacement()
        {
            if (!AreSpatialAxisBindingsComplete(out string missing))
            {
                SetStatus("Full Matrix is locked: " + missing);
                return;
            }
            if (!authorBoundaryConfirmed)
            {
                SetStatus("Full Matrix is locked until Time and Depth are confirmed.");
                return;
            }
            if (!intentConfigured)
            {
                SetStatus("Full Matrix is locked until MatPlot Intent is resolved.");
                return;
            }
            BuildS4DGridRequest();
            spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
            // FULL MATRIX is the user's commit action.  The matrix already has a
            // stable dock position, so an extra placement-preview confirmation
            // only duplicates the choice and interrupts the analysis flow.
            // Enter materialization immediately and let the normal Matrix panel
            // report progress/results once the job starts.
            ConfirmGridPlacement();
        }

        private void ContinueGridPlacement()
        {
            placementConfirmed = false;
            stage = Stage.Matrix;
            SetStatus("Placement preview ready. Grip to adjust, trigger to confirm.");
            if (facetGridCanvas != null)
            {
                HidePrimaryToolsExcept(facetGridCanvas);
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            BuildStage();
        }

        private void ConfirmGridPlacement()
        {
            if (spatialWorkflowStep != SpatialWorkflowStep.SourcePreviewReady ||
                jobRunning)
            {
                SetStatus(jobRunning
                    ? "MatPlotAgent is already materializing this Grid."
                    : "Confirm the source preview before materializing Full Matrix.");
                return;
            }
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            SetStatus("Grid anchored. Starting immutable snapshot materialization.");
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            StartS4DGridJob();
        }

        private void CreateTransformSummary()
        {
            CreatePanelCard(panelContent, new Vector2(0, 146), new Vector2(1010, 70), Cyan);
            CreateText(panelContent,
                "Variable  " + selectedDataset.Name + "     " +
                    FacetAxisSummary() + "     Mapped  Horizontal",
                14, FontStyle.Bold, new Vector2(0, 159), new Vector2(950, 24),
                TextAnchor.MiddleLeft, Ink);
            CreateText(panelContent,
                "Time mean  equal-frame     Depth mean  voxel     Missing  excluded     Scale  shared across Grid",
                13, FontStyle.Normal, new Vector2(0, 130), new Vector2(950, 22),
                TextAnchor.MiddleLeft, Muted);
        }

        private string MaterializationStageLabel()
        {
            if (progress < 0.08f)
                return "resolving footprints";
            if (progress < 0.3f)
                return "computing cells";
            if (progress < 0.9f)
                return "rendering shared-scale panels";
            return "validating snapshot";
        }

        private void BuildDigestCard()
        {
            CreatePanelCard(panelContent, new Vector2(400, -55), new Vector2(250, 332), Purple);
            CreateText(panelContent,
                "FINDINGS",
                18, FontStyle.Bold,
                new Vector2(400, 84), new Vector2(205, 28), TextAnchor.MiddleLeft, Purple);
            CreateText(panelContent,
                gridCellSelected
                    ? CellLabel(selectedGridColumn, selectedGridRow)
                    : Mathf.Max(1, activeGridColumns * activeGridRows) +
                        " CELLS  /  CURRENT GRID",
                11, FontStyle.Bold,
                new Vector2(400, 58), new Vector2(205, 20), TextAnchor.MiddleLeft, Muted);
            if (gridCellSelected)
            {
                int index = SourceCellIndex(selectedGridColumn, selectedGridRow);
                int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
                int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
                CreateDigestRow("minimum", matrixMinimums[index].ToString("0.##"), 20, Cyan);
                CreateDigestRow("mean", matrixMeans[index].ToString("0.##"), -27, Green);
                CreateDigestRow("maximum", matrixMaximums[index].ToString("0.##"), -74, Amber);
                CreateText(panelContent,
                    selectedDataset.Name + "\n" +
                    selectedDataset.GetTimeLabel(matrixTimes[Mathf.Clamp(
                        timeIndex, 0, matrixTimes.Length - 1)]) +
                    "  /  z " + matrixDepths[Mathf.Clamp(
                        depthIndex, 0, matrixDepths.Length - 1)],
                    13, FontStyle.Normal, new Vector2(400, -139), new Vector2(205, 76),
                    TextAnchor.UpperLeft, Ink);
            }
            else
            {
                FindDigestExtremes(out int minimumIndex, out int maximumIndex,
                    out int widestIndex);
                CreateText(panelContent, ActiveDigestHeadline(), 11,
                    FontStyle.Bold, new Vector2(400, 27),
                    new Vector2(205, 38), TextAnchor.UpperLeft,
                    currentDigest != null ? Green :
                    digestRunning ? Cyan : Amber);
                CreateText(panelContent,
                    ActiveDigestSummary(minimumIndex, maximumIndex,
                        widestIndex),
                    11, FontStyle.Normal, new Vector2(400, -70),
                    new Vector2(205, 146),
                    TextAnchor.UpperLeft, Ink);
            }
            CreateText(panelContent,
                gridStale ? "PREVIOUSLY INSPECTED" :
                inspected ? "SUPPORTED BY SOURCE" :
                evidenceLocalized ? "LOCAL PATTERN" :
                boundarySuspect ? "RECHECK BOUNDARY" :
                gridCellSelected ? (selectedCellPinned ? "PINNED" : "READY TO GROUND") :
                "SELECT A CELL TO GROUND",
                11, FontStyle.Bold,
                new Vector2(400, gridCellSelected ? -145 : -174),
                new Vector2(205, 22),
                TextAnchor.MiddleLeft,
                gridStale ? Muted : inspected ? Green : boundarySuspect ? Amber :
                selectedCellPinned ? Amber : Cyan);
            if (gridCellSelected)
                CreateButton(panelContent, "GROUND THIS CELL",
                    new Vector2(400, -184), new Vector2(205, 38),
                    Cyan, () => SelectS4DGridCell(
                        selectedGridColumn, selectedGridRow));
        }

        private void CreateDigestRow(string title, string detail, float y, Color color)
        {
            CreateText(panelContent, title, 12, FontStyle.Bold,
                new Vector2(400, y), new Vector2(205, 20), TextAnchor.MiddleLeft, color);
            CreateText(panelContent, detail, 11, FontStyle.Normal,
                new Vector2(400, y - 18), new Vector2(205, 18), TextAnchor.MiddleLeft, Muted);
        }

        private void SetGroundMode(GroundMode mode)
        {
            groundMode = mode;
            if (mode == GroundMode.Playback)
            {
                StopGroundPlayback();
                SetGroundAggregateVisible(false);
                if (currentView != null)
                {
                    currentView.SetVisible(cubeVisible);
                    currentView.ApplyOpacity(FieldOpacity);
                }
                groundPlaybackCoroutine = StartCoroutine(PlayGroundTimeBucket());
            }
            else
            {
                StopGroundPlayback();
                RestoreGroundRepresentative();
                ApplyGroundAggregateVisual();
            }
            SetStatus(mode == GroundMode.Aggregate
                ? "Aggregate selected: the MatPlot cell is grounded on its complete T x Z footprint."
                : "Playback running: only source frames in the selected time bucket are shown.");
            RecordTrailEvent("GROUND",
                mode == GroundMode.Aggregate ? "aggregate" : "playback");
            BuildStage();
        }

        private System.Collections.IEnumerator PlayGroundTimeBucket()
        {
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || timeIndex < 0 ||
                timeIndex >= activeTimeBuckets.Length ||
                activeTimeBuckets[timeIndex] == null ||
                activeTimeBuckets[timeIndex].indices == null ||
                activeTimeBuckets[timeIndex].indices.Length == 0)
            {
                groundPlaybackCoroutine = null;
                yield break;
            }

            int[] frames = activeTimeBuckets[timeIndex].indices;
            int cursor = 0;
            while (groundDocked && stage == Stage.Analyze &&
                   groundMode == GroundMode.Playback)
            {
                int frame = Mathf.Clamp(frames[cursor], 0, selectedDataset.TimeCount - 1);
                selectedTime = frame;
                ApplyTimeFilter();
                RefreshSlabTexture();
                UpdateSlabVisual(false);
                RebuildTimeMarkers();
                SetStatus("Playback  " + CellLabel(selectedGridColumn, selectedGridRow) +
                    "  |  " + selectedDataset.GetTimeLabel(frame) +
                    "  (" + (cursor + 1) + "/" + frames.Length + ")");
                cursor = (cursor + 1) % frames.Length;
                yield return new WaitForSeconds(1.35f);
            }
            groundPlaybackCoroutine = null;
        }

        private void StopGroundPlayback()
        {
            if (groundPlaybackCoroutine == null)
                return;
            StopCoroutine(groundPlaybackCoroutine);
            groundPlaybackCoroutine = null;
        }

        private void RestoreGroundRepresentative()
        {
            if (selectedDataset == null)
                return;
            int timeIndex = Mathf.Clamp(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow),
                0, matrixTimes.Length - 1);
            int depthIndex = Mathf.Clamp(
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow),
                0, matrixDepths.Length - 1);
            selectedTime = Mathf.Clamp(matrixTimes[timeIndex], 0, selectedDataset.TimeCount - 1);
            selectedZ = Mathf.Clamp(matrixDepths[depthIndex], 0, selectedDataset.DimZ - 1);
            slabNormalized = selectedDataset.DimZ > 1
                ? selectedZ / (float)(selectedDataset.DimZ - 1)
                : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            RebuildTimeMarkers();
        }

        private void ApplyGroundAggregateVisual()
        {
            if (groundAggregateVolume == null && !groundAggregateLoading)
                LoadGroundAggregateVolume();
            SetGroundAggregateVisible(groundAggregateVolume != null);
            if (currentView != null)
            {
                currentView.SetVisible(cubeVisible);
                currentView.ApplyOpacity(GroundContextOpacity);
            }
            SetGroundEvidenceVisuals(true);
        }

        private void LoadGroundAggregateVolume()
        {
            if (groundAggregateLoading || !gridCellSelected)
                return;
            groundAggregateSnapshotId = SelectedCellSnapshotId();
            if (string.IsNullOrWhiteSpace(groundAggregateSnapshotId))
            {
                SetStatus(
                    "Ground blocked: this Grid has no completed immutable snapshot. Re-materialize it.");
                return;
            }
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || activeDepthBuckets == null ||
                timeIndex < 0 || timeIndex >= activeTimeBuckets.Length ||
                depthIndex < 0 || depthIndex >= activeDepthBuckets.Length)
            {
                SetStatus("Ground blocked: selected cell footprint is unavailable.");
                return;
            }
            DestroyGroundAggregateVolume();
            groundAggregateLoading = true;
            string cellId = activeTimeBuckets[timeIndex].id + "__" +
                activeDepthBuckets[depthIndex].id;
            VolumeSTCubeS4DAnalysisClient client =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 300, 1.0f);
            SetStatus("Loading the snapshot aggregate volume for " +
                CellLabel(selectedGridColumn, selectedGridRow) + "...");
            StartCoroutine(client.GroundAggregateVolume(
                groundAggregateSnapshotId, cellId,
                OnGroundAggregateVolumeComplete));
        }

        private string SelectedCellSnapshotId()
        {
            int index = SourceCellIndex(
                selectedGridColumn, selectedGridRow);
            index = Mathf.Clamp(index, 0, facetCellSnapshotIds.Length - 1);
            return !string.IsNullOrWhiteSpace(facetCellSnapshotIds[index])
                ? facetCellSnapshotIds[index]
                : s4dSnapshotId;
        }

        private void OnGroundAggregateVolumeComplete(S4DGroundVolumeResult result)
        {
            groundAggregateLoading = false;
            if (result == null || !result.Succeeded)
            {
                SetStatus(result != null
                    ? result.Error
                    : "Ground aggregate service returned no volume.");
                BuildStage();
                return;
            }
            groundSnapshotCellMean = result.SnapshotCellMean;
            groundReconstructedCellMean = result.ReconstructedCellMean;
            groundValidFraction = result.ValidFraction;
            float replacement = result.Minimum -
                Mathf.Max(0.0001f, result.Maximum - result.Minimum);
            float[] values = result.Values;
            for (int index = 0; index < values.Length; index++)
                if (float.IsNaN(values[index]) || float.IsInfinity(values[index]))
                    values[index] = replacement;
            groundAggregateDataset = ScriptableObject.CreateInstance<VolumeDataset>();
            groundAggregateDataset.datasetName =
                "Ground " + CellLabel(selectedGridColumn, selectedGridRow);
            groundAggregateDataset.filePath =
                "snapshot://" + groundAggregateSnapshotId;
            groundAggregateDataset.dimX = result.DimX;
            groundAggregateDataset.dimY = result.DimY;
            groundAggregateDataset.dimZ = result.DimZ;
            groundAggregateDataset.data = values;
            groundAggregateDataset.normalizationMinimum = result.Minimum;
            groundAggregateDataset.normalizationMaximum = result.Maximum;
            groundAggregateDataset.scale = Vector3.one;
            groundAggregateVolume = VolumeSTCubeRawVolumeFactory.CreateObject(
                groundAggregateDataset, groundAggregateDataset.filePath);
            if (groundAggregateVolume == null)
            {
                Destroy(groundAggregateDataset);
                groundAggregateDataset = null;
                SetStatus("Ground aggregate was computed but Unity could not create its volume.");
                return;
            }
            // The raw factory starts with its generic white transfer function.  In
            // Ground this used to flash as an opaque white slab and then compete
            // with the contextual field.  Keep the object hidden until it has the
            // exact same colour language as the source variable.
            groundAggregateVolume.gameObject.SetActive(false);
            VolumeControllerObject sourceController = currentView != null
                ? currentView.GetManagedController()
                : null;
            if (sourceController != null && sourceController.transferFunction != null)
            {
                groundAggregateVolume.SetTransferFunctionMode(TFRenderMode.TF1D);
                groundAggregateVolume.SetTransferFunction(sourceController.transferFunction);
            }
            groundAggregateVolume.SetLightingEnabled(false);
            groundAggregateVolume.name = "Snapshot Ground Aggregate Volume";
            groundAggregateVolume.transform.SetParent(spatialRoot.transform, false);
            int depthFirst;
            int depthLast;
            int ignoredTimeFirst;
            int ignoredTimeLast;
            TryGetGroundBucketRanges(
                out ignoredTimeFirst, out ignoredTimeLast, out depthFirst, out depthLast);
            float denominator = Mathf.Max(1, selectedDataset.DimZ - 1);
            float y0 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthFirst / (float)denominator);
            float y1 = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY,
                depthLast / (float)denominator);
            float lower = Mathf.Min(y0, y1);
            float upper = Mathf.Max(y0, y1);
            groundAggregateVolume.transform.localPosition =
                new Vector3(0.0f, (lower + upper) * 0.5f, 0.0f);
            groundAggregateVolume.transform.localRotation =
                Quaternion.Euler(90.0f, 0.0f, 0.0f);
            groundAggregateVolume.transform.localScale = new Vector3(
                FieldHalfWidth * 1.60f,
                FieldHalfDepth * 1.60f,
                Mathf.Max(0.08f, upper - lower));
            SetGroundAggregateVisible(
                groundDocked && groundMode == GroundMode.Aggregate);
            if (currentView != null)
            {
                currentView.SetVisible(cubeVisible);
                currentView.ApplyOpacity(GroundContextOpacity);
            }
            SetStatus("Ground verified: snapshot mean " +
                groundSnapshotCellMean.ToString("0.###") +
                ", reconstructed mean " +
                groundReconstructedCellMean.ToString("0.###") +
                ", difference " +
                Mathf.Abs(groundSnapshotCellMean -
                    groundReconstructedCellMean).ToString("0.###") +
                ", valid coverage " +
                (groundValidFraction * 100.0f).ToString("0.0") + "%.");
            BuildStage();
        }

        private void SetGroundAggregateVisible(bool visible)
        {
            if (groundAggregateVolume != null)
                groundAggregateVolume.gameObject.SetActive(visible && cubeVisible);
        }

        private void DestroyGroundAggregateVolume()
        {
            if (groundAggregateVolume != null)
                Destroy(groundAggregateVolume.gameObject);
            if (groundAggregateDataset != null)
                Destroy(groundAggregateDataset);
            groundAggregateVolume = null;
            groundAggregateDataset = null;
            groundSnapshotCellMean = float.NaN;
            groundReconstructedCellMean = float.NaN;
            groundValidFraction = 0.0f;
        }

        private string GroundEvidenceComparison()
        {
            if (float.IsNaN(groundSnapshotCellMean) ||
                float.IsNaN(groundReconstructedCellMean))
            {
                return "Load the source volume to compare the immutable cell " +
                    "snapshot with a reconstruction from raw data.";
            }

            float difference = Mathf.Abs(
                groundSnapshotCellMean - groundReconstructedCellMean);
            return "SNAPSHOT MEAN  " + groundSnapshotCellMean.ToString("0.###") +
                "\nRAW REBUILD     " + groundReconstructedCellMean.ToString("0.###") +
                "\nDIFFERENCE      " + difference.ToString("0.###") +
                "   |   VALID " + (groundValidFraction * 100.0f).ToString("0.0") + "%";
        }

        private Rect SelectedGridCellUv()
        {
            int columns = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : activeGridColumns);
            int rows = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : activeGridRows);
            int timeIndex = Mathf.Clamp(
                DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow), 0, columns - 1);
            int depthIndex = Mathf.Clamp(
                DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow), 0, rows - 1);
            return new Rect(
                timeIndex / (float)columns,
                (rows - 1 - depthIndex) / (float)rows,
                1.0f / columns,
                1.0f / rows);
        }

        private string GroundSelectionHeadline()
        {
            return CellLabel(selectedGridColumn, selectedGridRow) + "  |  " +
                SelectedTimeRangeLabel() + "  |  " + SelectedDepthRangeLabel();
        }

        private string GroundFootprintSummary()
        {
            int timeIndex = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            int depthIndex = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            return selectedDataset.Name + "\nRepresentative: " +
                selectedDataset.GetTimeLabel(matrixTimes[Mathf.Clamp(
                    timeIndex, 0, matrixTimes.Length - 1)]) +
                ", z " + matrixDepths[Mathf.Clamp(
                    depthIndex, 0, matrixDepths.Length - 1)];
        }

        private string SelectedTimeRangeLabel()
        {
            int index = DisplayTimeBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeTimeBuckets == null || index < 0 || index >= activeTimeBuckets.Length)
                return "time bucket";
            return BucketRangeLabel(activeTimeBuckets[index], "frames");
        }

        private string SelectedDepthRangeLabel()
        {
            int index = DisplayDepthBucketIndex(selectedGridColumn, selectedGridRow);
            if (activeDepthBuckets == null || index < 0 || index >= activeDepthBuckets.Length)
                return "depth bucket";
            return BucketRangeLabel(activeDepthBuckets[index], "z");
        }

        private static string BucketRangeLabel(S4DIndexBucketRequest bucket, string prefix)
        {
            if (bucket == null || bucket.indices == null || bucket.indices.Length == 0)
                return prefix + " -";
            return prefix + " " + bucket.indices[0] + "-" +
                bucket.indices[bucket.indices.Length - 1];
        }

        private void AcceptBoundary()
        {
            inspected = true;
            boundarySuspect = false;
            evidenceLocalized = false;
            int cellIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            cellIndex = Mathf.Clamp(cellIndex, 0, facetCellInspected.Length - 1);
            facetCellInspected[cellIndex] = true;
            facetCellBoundarySuspect[cellIndex] = false;
            facetCellLocalized[cellIndex] = false;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = true;
                currentAnalysisNode.boundarySuspect = false;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = true;
                currentAnalysisNode.suspectCells[cellIndex] = false;
                currentAnalysisNode.localizedCells[cellIndex] = false;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "finding supported by source");
            SetStatus("Conclusion saved: finding supported by the source volume.");
            BuildStage();
        }

        private void MarkBoundarySuspect()
        {
            inspected = false;
            boundarySuspect = true;
            evidenceLocalized = false;
            int cellIndex = SourceCellIndex(selectedGridColumn, selectedGridRow);
            cellIndex = Mathf.Clamp(cellIndex, 0, facetCellInspected.Length - 1);
            facetCellInspected[cellIndex] = false;
            facetCellBoundarySuspect[cellIndex] = true;
            facetCellLocalized[cellIndex] = false;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = false;
                currentAnalysisNode.boundarySuspect = true;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = false;
                currentAnalysisNode.suspectCells[cellIndex] = true;
                currentAnalysisNode.localizedCells[cellIndex] = false;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "boundary requires review");
            SetStatus("Conclusion saved: review the boundary before relying on this finding.");
            BuildStage();
        }

        private void MarkEvidenceLocalized()
        {
            inspected = false;
            boundarySuspect = false;
            evidenceLocalized = true;
            int cellIndex = Mathf.Clamp(
                SourceCellIndex(selectedGridColumn, selectedGridRow),
                0, facetCellLocalized.Length - 1);
            facetCellInspected[cellIndex] = false;
            facetCellBoundarySuspect[cellIndex] = false;
            facetCellLocalized[cellIndex] = true;
            if (currentAnalysisNode != null)
            {
                currentAnalysisNode.inspected = false;
                currentAnalysisNode.boundarySuspect = false;
                EnsureNodeGroundStatus(currentAnalysisNode);
                currentAnalysisNode.verifiedCells[cellIndex] = false;
                currentAnalysisNode.suspectCells[cellIndex] = false;
                currentAnalysisNode.localizedCells[cellIndex] = true;
            }
            FocusDigestPageOnSelection();
            stage = Stage.Result;
            RecordTrailEvent("EVIDENCE", "local pattern only");
            SetStatus("Conclusion saved: this pattern is local to the selected footprint.");
            BuildStage();
        }

        private static void EnsureNodeGroundStatus(AnalysisNodeState node)
        {
            if (node.verifiedCells == null ||
                node.verifiedCells.Length != MaxFacetCells)
                node.verifiedCells = new bool[MaxFacetCells];
            if (node.suspectCells == null ||
                node.suspectCells.Length != MaxFacetCells)
                node.suspectCells = new bool[MaxFacetCells];
            if (node.localizedCells == null ||
                node.localizedCells.Length != MaxFacetCells)
                node.localizedCells = new bool[MaxFacetCells];
            if (node.pinnedCells == null ||
                node.pinnedCells.Length != MaxFacetCells)
                node.pinnedCells = new bool[MaxFacetCells];
        }

        private void ToggleDataVisibility()
        {
            cubeVisible = !cubeVisible;
            if (spatialRoot != null)
                spatialRoot.SetActive(cubeVisible);
            if (currentView != null)
                currentView.SetVisible(cubeVisible &&
                    roles[3] != DimensionRole.Faceted &&
                    !(groundDocked && groundMode == GroundMode.Aggregate &&
                      groundAggregateVolume != null));
            SetGroundAggregateVisible(cubeVisible && groundDocked &&
                groundMode == GroundMode.Aggregate);
            SetStatus(cubeVisible ? "Continuous Data Container shown." : "Continuous Data Container hidden.");
            BuildMainMenu();
        }

        private void ToggleDataPresentation()
        {
            smallMultiples = !smallMultiples;
            SetStatus(smallMultiples
                ? "Small-multiples presentation selected. Time remains linked to the same continuous field."
                : "Animated-volume presentation selected. Use the time rail to scrub.");
            BuildMainMenu();
        }

        private void SelectBoundaryDimension(int index)
        {
            int maximum = initialBoundarySetupActive
                ? (IsForVrSurfaceDataset ? 0 : 1) : 3;
            if (initialBoundarySetupActive && index == 1 &&
                !initialTimeBoundaryComplete)
            {
                SetStatus("Save the two Time cuts before continuing to Depth.");
                return;
            }
            if (boundaryDimension == BoundaryDimension.Depth &&
                index != (int)BoundaryDimension.Depth)
                EndDepthSliceInspection(true);
            if (boundaryDimension == BoundaryDimension.Time &&
                index != (int)BoundaryDimension.Time)
            {
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
            }
            boundaryDimension = (BoundaryDimension)Mathf.Clamp(index, 0, maximum);
            if (boundaryDimension == BoundaryDimension.Time ||
                boundaryDimension == BoundaryDimension.Depth)
                EnterBoundaryAuthoringView();
            else
                ExitBoundaryAuthoringView();
            BuildBoundaryPanel();
        }

        private string BoundaryDefaultLabel()
        {
            switch (boundaryDimension)
            {
                case BoundaryDimension.Time: return "during";
                case BoundaryDimension.Depth: return "middle";
                case BoundaryDimension.Horizontal: return "Region 1";
                default: return "ocean variables";
            }
        }

        private void NudgeActiveBoundary(int direction)
        {
            if (selectedDataset == null)
                return;
            if (boundaryDimension == BoundaryDimension.Time)
            {
                if (direction < 0)
                    timeBoundaryStart = Mathf.Clamp(timeBoundaryStart - 1,
                        0, timeBoundaryEnd - 1);
                else
                    timeBoundaryEnd = Mathf.Clamp(timeBoundaryEnd + 1,
                        timeBoundaryStart + 1, selectedDataset.TimeCount - 1);
                UpdateTimeBoundaryHandles();
                PreviewBoundaryTime(direction < 0 ? timeBoundaryStart : timeBoundaryEnd);
            }
            else if (boundaryDimension == BoundaryDimension.Depth)
            {
                if (direction < 0)
                    depthBoundaryLow = Mathf.Clamp(depthBoundaryLow - 0.04f,
                        0.0f, depthBoundaryHigh - 0.05f);
                else
                    depthBoundaryHigh = Mathf.Clamp(depthBoundaryHigh + 0.04f,
                        depthBoundaryLow + 0.05f, 1.0f);
                UpdateDepthBoundaryPlanes();
                BeginDepthSliceInspection(
                    direction < 0 ? depthBoundaryLow : depthBoundaryHigh);
            }
            SetStatus("Boundary preview moved. Apply to update the shared bucket ladder.");
            BuildBoundaryPanel();
        }

        private void NudgeBoundaryFixedValue(int direction)
        {
            if (selectedDataset == null)
                return;
            if (boundaryDimension == BoundaryDimension.Time)
            {
                selectedTime = Mathf.Clamp(selectedTime + direction, 0,
                    Mathf.Max(0, selectedDataset.TimeCount - 1));
                PreviewBoundaryTime(selectedTime);
                SetStatus("Fixed Time preview: " +
                    selectedDataset.GetTimeLabel(selectedTime) + ".");
            }
            else if (boundaryDimension == BoundaryDimension.Depth)
            {
                selectedZ = Mathf.Clamp(selectedZ + direction, 0,
                    Mathf.Max(0, selectedDataset.DimZ - 1));
                slabNormalized = selectedDataset.DimZ > 1
                    ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
                UpdateSlabVisual(false);
                UpdateDepthBoundaryPlanes();
                RefreshSlabTexture();
                BeginDepthSliceInspection(slabNormalized);
                SetStatus("Fixed Depth preview: z=" + selectedZ + ".");
            }
            BuildBoundaryPanel();
        }

        private void ApplyBoundaryChange()
        {
            if (boundaryDimension == BoundaryDimension.Time)
            {
                CommitBoundaryTimePreview();
                HideBoundaryDayPreviewSmoothly();
            }
            if (initialBoundarySetupActive &&
                boundaryDimension == BoundaryDimension.Time &&
                !IsForVrSurfaceDataset)
            {
                initialTimeBoundaryComplete = true;
                boundaryDimension = BoundaryDimension.Depth;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                BuildBoundaryPanel();
                SetStatus(
                    roles[1] == DimensionRole.Fixed
                        ? "Slab step 2 of 2: choose the single fixed Depth value."
                        : "Slab step 2 of 2: place two Depth cuts for Surface, Middle, and Deep.");
                return;
            }

            bool completedInitialSetup = initialBoundarySetupActive;
            EndDepthSliceInspection(true);
            StoreEffectiveBoundaryValues();
            if (initialBoundarySetupActive &&
                boundaryVariableQueueIndex + 1 < boundaryVariableQueue.Count)
            {
                boundaryVariableQueueIndex++;
                ActivateBoundaryVariableQueueEntry();
                RefreshVariableFacetStacks();
                ApplyPrimaryVolumeVisibility();
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                boundaryDimension = BoundaryDimension.Time;
                UpdateTimeBoundaryHandles();
                UpdateDepthBoundaryPlanes();
                BuildBoundaryPanel();
                SetStatus("Custom Field " +
                    (boundaryVariableQueueIndex + 1) + "/" +
                    boundaryVariableQueue.Count + ": configure Time for " +
                    selectedDataset.Name + ".");
                return;
            }
            CommitAuthorBoundaryBuckets();
            RefreshVariableFacetStacks();
            ApplyPrimaryVolumeVisibility();
            authorBoundaryConfirmed = true;
            initialTimeBoundaryComplete = true;
            initialDepthBoundaryComplete = true;
            initialBoundarySetupActive = false;
            boundaryVariableQueue.Clear();
            boundaryVariableQueueIndex = 0;
            ResetBoundaryInteractionFieldCenter();
            boundaryEditActive = false;
            // CommitAuthorBoundaryBuckets runs while the editor is still active.
            // Refresh once more after leaving edit mode so the finalized ranges
            // replace the CUT handles immediately on the world axes.
            UpdateAnalysisAxisLabels();
            for (int index = 0; index < analysisNodes.Count; index++)
                UpdateNodeStaleDependencies(analysisNodes[index]);
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            if (currentAnalysisNode != null &&
                currentAnalysisNode.staleCells != null)
                Array.Copy(currentAnalysisNode.staleCells, facetCellStale,
                    Mathf.Min(facetCellStale.Length,
                        currentAnalysisNode.staleCells.Length));
            gridStale = currentAnalysisNode != null
                ? currentAnalysisNode.stale
                : s4dGridImage != null;
            RecordTrailEvent("BOUNDARY EDIT",
                boundaryDimension == BoundaryDimension.Time
                    ? "time buckets changed"
                    : boundaryDimension == BoundaryDimension.Depth
                        ? "depth buckets changed"
                        : "author buckets changed");
            slabPreviewBuilt = true;
            intentConfigured = false;
            spatialWorkflowStep = SpatialWorkflowStep.Intent;
            ClearSourcePreviewLayers();
            SetStatus(gridStale
                ? "Ranges saved. The previous Grid is stale; enter a new MatPlot intent."
                : "Time and Depth configuration saved. Open MatPlot Intent.");
            boundaryCanvas.gameObject.SetActive(false);
            SetTimeBoundaryHandleVisibility(false);
            SetDepthBoundaryVisibility(false);
            if (slabPreviewCanvas != null)
                slabPreviewCanvas.gameObject.SetActive(false);
            if (intentCanvas != null)
                intentCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
            ExitBoundaryAuthoringView();
            stage = completedInitialSetup ? Stage.Slab : boundaryReturnStage;
            if (completedInitialSetup && preconfigurationActive)
            {
                preconfigurationActive = false;
                mainWorkspaceEntered = true;
                legacyPanelVisible = false;
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(true);
                RefreshSpatialAxisControllers();
                UpdateVariablePaletteFollow(true);
            }
            ReturnToSpatialWorkflow();
            BuildStage();
            BuildWorkflowToolbar();
            BuildMainMenu();
        }

        private void UpdateNodeStaleDependencies(AnalysisNodeState node)
        {
            if (node == null)
                return;
            if (node.staleCells == null ||
                node.staleCells.Length != MaxFacetCells)
                node.staleCells = new bool[MaxFacetCells];
            Array.Clear(node.staleCells, 0, node.staleCells.Length);
            bool timeDependsOnBuckets = node.roleValues == null ||
                node.roleValues.Length < 1 ||
                (DimensionRole)node.roleValues[0] != DimensionRole.Mapped;
            bool depthDependsOnBuckets = node.roleValues == null ||
                node.roleValues.Length < 2 ||
                (DimensionRole)node.roleValues[1] != DimensionRole.Mapped;
            int timeBucketCount = node.timeBuckets != null
                ? Mathf.Min(node.timeBuckets.Length, MaxFacetAxisBuckets) : 0;
            int depthBucketCount = node.depthBuckets != null
                ? Mathf.Min(node.depthBuckets.Length, MaxFacetAxisBuckets) : 0;
            for (int depth = 0; depth < depthBucketCount; depth++)
            {
                for (int time = 0; time < timeBucketCount; time++)
                {
                    bool timeChanged = timeDependsOnBuckets &&
                        BucketChanged(node.timeBuckets, authoredTimeBuckets, time);
                    bool depthChanged = depthDependsOnBuckets &&
                        BucketChanged(node.depthBuckets, authoredDepthBuckets, depth);
                    node.staleCells[depth * timeBucketCount + time] =
                        timeChanged || depthChanged;
                }
            }
            node.stale = Array.Exists(node.staleCells, value => value);
        }

        private static bool BucketChanged(S4DIndexBucketRequest[] previous,
            S4DIndexBucketRequest[] current, int index)
        {
            if (previous == null || current == null ||
                index >= previous.Length || index >= current.Length)
                return true;
            int[] left = previous[index] != null ? previous[index].indices : null;
            int[] right = current[index] != null ? current[index].indices : null;
            if (left == null || right == null || left.Length != right.Length)
                return true;
            for (int value = 0; value < left.Length; value++)
                if (left[value] != right[value])
                    return true;
            return false;
        }

        private void CommitAuthorBoundaryBuckets()
        {
            if (selectedDataset == null)
                return;

            int timeCount = Mathf.Max(3, selectedDataset.TimeCount);
            int timeCutA = Mathf.Clamp(
                timeBoundaryStart + (IsForVrSurfaceDataset ? 0 : 1),
                1, timeCount - 2);
            int timeCutB = Mathf.Clamp(
                timeBoundaryEnd + 1, timeCutA + 1, timeCount - 1);
            authoredTimeBuckets = roles[0] == DimensionRole.Fixed
                ? new[]
                {
                    Bucket("time_fixed_" + selectedTime,
                        selectedDataset.GetTimeLabel(selectedTime),
                        new[] { Mathf.Clamp(selectedTime, 0, timeCount - 1) })
                }
                : new[]
                {
                    Bucket("before", "Before", CreateIndexRange(0, timeCutA)),
                    Bucket("during", "During", CreateIndexRange(timeCutA, timeCutB)),
                    Bucket("after", "After", CreateIndexRange(timeCutB, timeCount))
                };

            if (IsForVrSurfaceDataset)
            {
                authoredDepthBuckets = new[]
                {
                    Bucket("surface", "Hong Kong surface", new[] { 0 })
                };
                activeTimeBuckets = authoredTimeBuckets;
                activeDepthBuckets = authoredDepthBuckets;
                UpdateActiveRepresentativeIndices();
                UpdateAnalysisAxisLabels();
                return;
            }

            int depthCount = Mathf.Max(3, selectedDataset.DimZ);
            int analyzableDepthCount = depthCount == 92 ? 91 : depthCount;
            int depthCutA = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * analyzableDepthCount),
                1, analyzableDepthCount - 2);
            int depthCutB = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * analyzableDepthCount),
                depthCutA + 1, analyzableDepthCount - 1);
            authoredDepthBuckets = roles[1] == DimensionRole.Fixed
                ? new[]
                {
                    Bucket("depth_fixed_" + selectedZ, "z=" + selectedZ,
                        new[] { Mathf.Clamp(selectedZ, 0,
                            analyzableDepthCount - 1) })
                }
                : new[]
                {
                    Bucket("surface", "Surface", CreateIndexRange(0, depthCutA)),
                    Bucket("middle", "Middle", CreateIndexRange(depthCutA, depthCutB)),
                    Bucket("deep", "Deep",
                        CreateIndexRange(depthCutB, analyzableDepthCount))
                };

            activeTimeBuckets = authoredTimeBuckets;
            activeDepthBuckets = authoredDepthBuckets;
            UpdateActiveRepresentativeIndices();
            UpdateAnalysisAxisLabels();
        }

        private void NavigateFromTrail(Stage next)
        {
            trailCanvas.gameObject.SetActive(false);
            if (next == Stage.Matrix && placementConfirmed && facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
                return;
            }
            ReturnToSpatialWorkflow();
            Navigate(next);
        }

        private void NavigateToAnalysisNode(AnalysisNodeState node)
        {
            if (node == null || node.gridImage == null)
                return;
            node.dismissed = false;
            currentAnalysisNode = node;
            s4dGridImage = node.gridImage;
            s4dChartResultJson = node.chartResultJson;
            s4dSnapshotId = node.snapshotId;
            s4dJobId = node.jobId;
            s4dSharedMinimum = node.sharedMinimum;
            s4dSharedMaximum = node.sharedMaximum;
            s4dSharedUnit = node.sharedUnit;
            prompt = node.rawIntent ?? string.Empty;
            analysisQuestion = string.IsNullOrWhiteSpace(node.analysisQuestion)
                ? analysisQuestion : node.analysisQuestion;
            intentTask = string.IsNullOrWhiteSpace(node.analyticTask)
                ? "characterize_distribution"
                : node.analyticTask;
            intentMode = string.IsNullOrWhiteSpace(node.intentDisplayLabel)
                ? intentTask.Replace("_", " ").ToUpperInvariant()
                : node.intentDisplayLabel;
            intentConfigured = node.hasResolvedIntent;
            currentDigest = node.digest;
            digestError = node.digestError ?? string.Empty;
            digestRunning = node.digestPending;
            Array.Clear(facetCellSnapshotIds, 0, facetCellSnapshotIds.Length);
            if (node.cellSnapshotIds != null)
                Array.Copy(node.cellSnapshotIds, facetCellSnapshotIds,
                    Mathf.Min(facetCellSnapshotIds.Length,
                        node.cellSnapshotIds.Length));
            timeBoundaryStart = node.timeBoundaryStart;
            timeBoundaryEnd = node.timeBoundaryEnd;
            depthBoundaryLow = node.depthBoundaryLow;
            depthBoundaryHigh = node.depthBoundaryHigh;
            if (node.roleValues != null)
                for (int index = 0; index < roles.Length && index < node.roleValues.Length; index++)
                    roles[index] = (DimensionRole)node.roleValues[index];
            activeTimeBuckets = node.timeBuckets;
            activeDepthBuckets = node.depthBuckets;
            facetGridLayered = false;
            facetGridPeeledLayers = 0;
            activeGridTransposed = node.gridTransposed;
            pivotTransposed = node.gridTransposed;
            int timeBucketCount = activeTimeBuckets != null
                ? Mathf.Max(1, activeTimeBuckets.Length) : 3;
            int depthBucketCount = activeDepthBuckets != null
                ? Mathf.Max(1, activeDepthBuckets.Length) : 3;
            activeGridColumns = activeGridTransposed
                ? depthBucketCount : timeBucketCount;
            activeGridRows = activeGridTransposed
                ? timeBucketCount : depthBucketCount;
            UpdateActiveRepresentativeIndices();
            inspected = node.inspected;
            boundarySuspect = node.boundarySuspect;
            placementConfirmed = true;
            gridStale = node.stale;
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            if (node.staleCells != null)
                Array.Copy(node.staleCells, facetCellStale,
                    Mathf.Min(facetCellStale.Length, node.staleCells.Length));
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellLocalized, 0, facetCellLocalized.Length);
            evidenceLocalized = false;
            if (node.verifiedCells != null)
                Array.Copy(node.verifiedCells, facetCellInspected,
                    Mathf.Min(facetCellInspected.Length,
                        node.verifiedCells.Length));
            if (node.suspectCells != null)
                Array.Copy(node.suspectCells, facetCellBoundarySuspect,
                    Mathf.Min(facetCellBoundarySuspect.Length,
                        node.suspectCells.Length));
            if (node.localizedCells != null)
                Array.Copy(node.localizedCells, facetCellLocalized,
                    Mathf.Min(facetCellLocalized.Length,
                        node.localizedCells.Length));
            evidenceLocalized = Array.Exists(facetCellLocalized, value => value);
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            if (node.pinnedCells != null)
                Array.Copy(node.pinnedCells, facetCellPinned,
                    Mathf.Min(facetCellPinned.Length,
                        node.pinnedCells.Length));
            selectedCellPinned = false;
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            ResetSelectedTicksToActiveBuckets();
            trailCanvas.gameObject.SetActive(false);
            if (panelCanvas != null)
                panelCanvas.gameObject.SetActive(false);
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            stage = Stage.Matrix;
            SetStatus("Returned to " + node.nodeId + "  /  " +
                OperationLabel(node.bornFrom) + ".");
        }

        private void DeleteAnalysisLeaf(AnalysisNodeState node)
        {
            if (node == null || !IsLeafNode(node.nodeId))
            {
                SetStatus("Only a leaf analysis node can be deleted.");
                return;
            }
            if (pendingDeleteNodeId != node.nodeId)
            {
                pendingDeleteNodeId = node.nodeId;
                SetStatus("Delete " + node.nodeId +
                    "? Select CONFIRM to remove its Grid and resources.");
                BuildTrailPanel();
                return;
            }
            pendingDeleteNodeId = string.Empty;
            AnalysisNodeState parent = FindAnalysisNode(node.parentNodeId);
            bool deletingCurrent = node == currentAnalysisNode;
            if (deletingCurrent)
            {
                currentAnalysisNode = null;
                s4dGridImage = null;
                s4dChartResultJson = string.Empty;
                s4dSnapshotId = string.Empty;
            }
            analysisNodes.Remove(node);
            if (node.gridImage != null)
                Destroy(node.gridImage);
            if (deletingCurrent && parent != null)
                NavigateToAnalysisNode(parent);
            else if (deletingCurrent)
            {
                placementConfirmed = false;
                stage = Stage.Field;
                if (facetGridCanvas != null)
                    facetGridCanvas.gameObject.SetActive(false);
                ReturnToSpatialWorkflow();
                BuildStage();
            }
            SetStatus("Deleted leaf " + node.nodeId + " and its Facet Grid.");
            if (trailCanvas != null)
            {
                trailCanvas.gameObject.SetActive(true);
                BuildTrailPanel();
            }
        }

        private AnalysisNodeState FindAnalysisNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return null;
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].nodeId == nodeId)
                    return analysisNodes[index];
            return null;
        }

        private void ArrangeWorkspace()
        {
            if (panelCanvas != null)
                panelCanvas.transform.localPosition = PrimaryToolDockPosition;
            if (mainMenuCanvas != null)
                mainMenuCanvas.transform.localPosition = PrimaryToolDockPosition;
            if (boundaryCanvas != null)
                boundaryCanvas.transform.localPosition = BoundaryToolDockPosition;
            if (trailCanvas != null)
                trailCanvas.transform.localPosition = PrimaryToolDockPosition;
            SetStatus("Workspace arranged. The cube stays central and the active tool is docked on its right.");
            BuildTrailPanel();
        }

        private void LoadDataset(int index)
        {
            if (index < 0 || index >= datasets.Count || jobRunning)
                return;
            if (selectedDataset != null && datasets[index] != selectedDataset)
            {
                // Dataset switching is a display operation. Preserve the day and
                // playback state so choosing another variable does not reset the
                // temporal exploration the user is already watching.
                pendingDatasetDisplayTime = selectedTime;
                resumePlaybackAfterDatasetLoad = forVrSurfacePlayer != null &&
                    forVrSurfacePlayer.IsPlaying;
            }
            if (variableLoadRunning)
            {
                pendingDatasetLoadIndex = index;
                SetStatus("Queued " + datasets[index].Name +
                    "; finishing the current STC texture upload first...");
                return;
            }
            variableLoadRunning = true;
            pendingDatasetLoadIndex = -1;
            VolumeSTCubeSliceDataset next = datasets[index];
            SetStatus("Materializing " + next.Name + " as a continuous cube...");
            BuildStage();
            StartCoroutine(LoadDatasetAfterFeedback(index));
        }

        private IEnumerator LoadDatasetAfterFeedback(int index)
        {
            // Give the loading card one complete frame before the unavoidable GPU
            // texture upload. Without this yield Quest appears frozen after click.
            yield return null;
            yield return new WaitForEndOfFrame();
            LoadDatasetNow(index);
            variableLoadRunning = false;
            if (pendingDatasetLoadIndex >= 0 &&
                pendingDatasetLoadIndex < datasets.Count &&
                datasets[pendingDatasetLoadIndex] != selectedDataset)
            {
                int queuedIndex = pendingDatasetLoadIndex;
                pendingDatasetLoadIndex = -1;
                LoadDataset(queuedIndex);
                yield break;
            }
            pendingDatasetLoadIndex = -1;
            BuildStage();
        }

        private void LoadDatasetNow(int index)
        {
            if (index < 0 || index >= datasets.Count)
                return;
            VolumeSTCubeSliceDataset next = datasets[index];
            int requestedDisplayTime = pendingDatasetDisplayTime;
            bool shouldResumePlayback = resumePlaybackAfterDatasetLoad;
            pendingDatasetDisplayTime = -1;
            resumePlaybackAfterDatasetLoad = false;
            // Entering the main tri-axis workspace permanently locks the
            // Time/Depth setup. Switching the active variable must not turn
            // that into a second Author Boundary session.
            bool preservePreconfiguredBoundaries = mainWorkspaceEntered;
            if (forVrSurfacePlayer != null)
            {
                Destroy(forVrSurfacePlayer);
                forVrSurfacePlayer = null;
            }
            if (currentView != null)
                VolumeSTCubeAPI.DestroyView(currentView.viewId);

            VolumeSTCubeConfig config = VolumeSTCubeConfig.Default("quest_spatial_slab_lab");
            config.datasetName = next.Name;
            config.dataLayout = VolumeSTCubeDataLayout.XYZTimeSeries;
            config.showTimeline = false;
            config.timelineAutoPlay = false;
            config.opacity = FieldOpacity;
            int nextVariableIndex = datasets.IndexOf(next);
            int defaultTimeStart = Mathf.Clamp(
                Mathf.FloorToInt(next.TimeCount / 3.0f) - 1,
                0, Mathf.Max(0, next.TimeCount - 2));
            int defaultTimeEnd = Mathf.Clamp(
                Mathf.FloorToInt(next.TimeCount * 2.0f / 3.0f) - 1,
                defaultTimeStart + 1,
                Mathf.Max(defaultTimeStart + 1, next.TimeCount - 1));
            if (!sharedBoundariesInitialized)
            {
                sharedTimeBoundaryStart = defaultTimeStart;
                sharedTimeBoundaryEnd = defaultTimeEnd;
                sharedSelectedTime = Mathf.Clamp(next.TimeCount / 2, 0,
                    Mathf.Max(0, next.TimeCount - 1));
                sharedDepthBoundaryLow = 1.0f / 3.0f;
                sharedDepthBoundaryHigh = 2.0f / 3.0f;
                sharedSelectedZ = Mathf.Clamp(next.DimZ / 2, 0,
                    Mathf.Max(0, next.DimZ - 1));
                sharedBoundariesInitialized = true;
            }
            SpatialAxisRigState nextBoundaryState = nextVariableIndex >= 0
                ? spatialAxisRigStates.Find(state =>
                    state.boundVariable == nextVariableIndex) : null;
            selectedTime = Mathf.Clamp(nextBoundaryState != null &&
                    !nextBoundaryState.usesSharedBoundaries
                        ? nextBoundaryState.customSelectedTime
                        : sharedSelectedTime,
                0, Mathf.Max(0, next.TimeCount - 1));
            if (requestedDisplayTime >= 0)
                selectedTime = Mathf.Clamp(requestedDisplayTime, 0,
                    Mathf.Max(0, next.TimeCount - 1));
            config.initialTimeIndex = selectedTime;
            config.timeMin = selectedTime / (float)Mathf.Max(1, next.TimeCount);
            config.timeMax = (selectedTime + 1) /
                (float)Mathf.Max(1, next.TimeCount);
            if (!VolumeSTCubeAPI.TryCreateViewFromRawDataset(
                next, config, out currentView, out string error))
            {
                SetStatus(error);
                return;
            }

            selectedDataset = next;
            RefreshFieldDatasetSelector();
            LoadEffectiveBoundaryValues(nextBoundaryState);
            ResolveSelectedDatasetManifest();
            if (!preservePreconfiguredBoundaries)
            {
                authorBoundaryConfirmed = false;
                initialBoundarySetupActive = false;
                initialTimeBoundaryComplete = false;
                initialDepthBoundaryComplete = false;
                authoredTimeBuckets = null;
                authoredDepthBuckets = null;
            }
            activeTimeBuckets = null;
            activeDepthBuckets = null;
            ClearAnalysisHistory();
            slabNormalized = next.DimZ > 1 ? selectedZ / (float)(next.DimZ - 1) : 0.5f;
            ClearChart();
            HideLegacyAxis();
            ApplyTimeFilter();
            if (IsForVrSurfaceDataset)
            {
                forVrSurfacePlayer = spatialRoot.AddComponent<VolumeSTCubeForVrSurfacePlayer>();
                forVrSurfacePlayer.Initialize(next, selectedTime,
                    currentView.rootObject.transform, OnForVrSurfaceTimeChanged);
                if (shouldResumePlayback)
                    forVrSurfacePlayer.EnsurePlaybackContinues();
            }
            else
            {
                FrameVolume();
                StartCoroutine(RevealVolumeWhenTexturesReady(currentView));
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            RefreshSlabTexture();
            RefreshVariableFacetStacks();
            RefreshSpatialAxisControllers();
            int datasetIndex = datasets.IndexOf(next);
            int rigIndex = spatialAxisRigStates.FindIndex(
                state => state.boundVariable == datasetIndex);
            if (rigIndex >= 0)
                ApplySelectedAxisRigState(rigIndex, false);
            RebuildTimeMarkers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateSlabVisual(false);
            RecordTrailEvent("VARIABLE", "loaded " + next.Name);
            SetStatus(IsForVrSurfaceDataset
                ? next.Name + " ready: " + next.TimeCount +
                    " hourly Hong Kong surface frames."
                : next.Name + " ready: " + next.TimeCount + " times x " +
                    next.DimZ + " depth layers.");
            if (workflowToolbarCanvas != null)
                workflowToolbarCanvas.gameObject.SetActive(mainWorkspaceEntered);
            if (preconfigurationActive)
            {
                legacyPanelVisible = true;
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(true);
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(false);
            }
            else
            {
                legacyPanelVisible = false;
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
            }
        }

        private System.Collections.IEnumerator RevealVolumeWhenTexturesReady(
            VolumeSTCubeView targetView)
        {
            if (targetView == null || targetView.rootObject == null)
                yield break;

            SetStatus("Preparing the 3D field texture...");
            float deadline = Time.realtimeSinceStartup + 45.0f;
            bool ready = false;
            Renderer[] renderers = new Renderer[0];
            VolumeSTCubeRawTimeSeries series =
                targetView.rootObject.GetComponent<VolumeSTCubeRawTimeSeries>();
            int expectedIndex = targetView.config != null
                ? targetView.config.initialTimeIndex
                : selectedTime;
            while (targetView == currentView && Time.realtimeSinceStartup < deadline)
            {
                renderers = targetView.rootObject.GetComponentsInChildren<Renderer>(true);
                bool seriesReady = renderers.Length > 0 && series != null &&
                    series.CurrentIndex == expectedIndex &&
                    !series.IsTransitionPending;
                bool hasVolumeTexture = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    Material material = renderer != null ? renderer.sharedMaterial : null;
                    if (material == null)
                        continue;
                    Texture data = material.HasProperty("_DataTex")
                        ? material.GetTexture("_DataTex")
                        : null;
                    // The Quest DVR path intentionally runs unlit to avoid keeping a
                    // second RGBA 3D gradient texture resident. The scalar texture is
                    // sufficient for the visible continuous-field rendering.
                    if (data is Texture3D)
                    {
                        hasVolumeTexture = true;
                        break;
                    }
                }
                // A view also contains helper/axis renderers without _DataTex.
                // Requiring every renderer to own a Texture3D kept the real STC
                // hidden until the 45-second timeout and left an apparently empty
                // Continuous Field after a variable was bound.
                ready = seriesReady && hasVolumeTexture;
                if (ready)
                    break;
                yield return null;
            }

            if (targetView != currentView)
                yield break;
            for (int index = 0; index < renderers.Length; index++)
                if (renderers[index] != null)
                    renderers[index].enabled = cubeVisible;
            RefreshVariableFacetStacks();
            FrameVolume();
            // Renderer bounds can settle one frame after the Texture3D upload.
            // A second fit keeps the newly bound field centred in its paired box.
            StartCoroutine(RefitVolumeAfterFrameChange());
            SetStatus(ready
                ? selectedDataset.Name + " ready: " + selectedDataset.TimeCount +
                    " times x " + selectedDataset.DimZ + " depth layers."
                : "3D texture preparation timed out; showing the available field.");
            BuildStage();
        }

        private void ResolveSelectedDatasetManifest()
        {
            if (selectedDataset == null)
                return;
            datasetManifestResolving = true;
            datasetManifestError = string.Empty;
            selectedDataset.DatasetId = string.Empty;
            selectedDataset.VariableId = string.Empty;
            VolumeSTCubeS4DAnalysisClient resolver =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 30, 1.0f);
            StartCoroutine(resolver.ResolveDataset(
                selectedDataset.Name,
                selectedDataset.DimX,
                selectedDataset.DimY,
                selectedDataset.DimZ,
                selectedDataset.TimeCount,
                OnDatasetManifestResolved));
        }

        private void OnDatasetManifestResolved(
            S4DDatasetResolution resolution,
            string error)
        {
            datasetManifestResolving = false;
            if (selectedDataset == null)
                return;
            if (resolution == null)
            {
                datasetManifestError = string.IsNullOrWhiteSpace(error)
                    ? "No validated S4D manifest matches this variable."
                    : error;
                SetStatus(datasetManifestError);
                BuildStage();
                return;
            }
            selectedDataset.DatasetId = resolution.datasetId;
            selectedDataset.DatasetVersion = resolution.datasetVersion;
            selectedDataset.VariableId = resolution.variableId;
            selectedDataset.Unit = resolution.unit;
            selectedDataset.ValueSemantics = resolution.valueSemantics;
            datasetManifestError = string.Empty;
            SetStatus(
                selectedDataset.Name + " linked to manifest " +
                resolution.datasetId + " / " + resolution.datasetVersion + ".");
            BuildStage();
        }

        /// <summary>
        /// Keeps the two time cuts synchronized while the user edits the
        /// combined six-event STC. The cuts form Before, During and After.
        /// </summary>
        public void PreviewForVrCombinedTimeRange(int firstCutIndex,
            int secondCutIndex, int activeCut)
        {
            if (selectedDataset == null || !IsForVrSurfaceDataset)
                return;
            int last = Mathf.Max(1, selectedDataset.TimeCount - 1);
            timeBoundaryStart = Mathf.Clamp(firstCutIndex, 0, last - 1);
            timeBoundaryEnd = Mathf.Clamp(secondCutIndex,
                timeBoundaryStart + 1, last);
            selectedTime = activeCut <= 0 ? timeBoundaryStart : timeBoundaryEnd;
            sharedSelectedTime = selectedTime;
            if (fieldTimeSummaryText != null)
                fieldTimeSummaryText.text = "TIME  " + TimeRangeSummary() +
                    "\nGEOMETRY  HONG KONG WATER SURFACE (NO DEPTH AXIS)";
            if (boundaryCurrentRangeText != null && boundaryCanvas != null &&
                boundaryCanvas.gameObject.activeSelf)
                boundaryCurrentRangeText.text =
                    TimeRangeSummary().ToUpperInvariant();
            SetStatus("STC CUT " + (activeCut <= 0 ? "A" : "B") +
                " preview: " + selectedDataset.GetTimeLabel(selectedTime) +
                ". Current ranges: " + TimeRangeSummary() + ".");
        }

        /// <summary>
        /// Commits the two STC cuts as the existing Set Time Range step.
        /// MatPlot remains later in the established axis/intent/matrix flow.
        /// </summary>
        public void ConfirmForVrCombinedTimeRange(int firstCutIndex,
            int secondCutIndex)
        {
            if (selectedDataset == null || !IsForVrSurfaceDataset)
            {
                SetStatus("Select one of the four For_VR variables before confirming the STC time range.");
                return;
            }

            roles[0] = DimensionRole.Faceted;
            roles[1] = DimensionRole.Fixed;
            roles[2] = DimensionRole.Mapped;
            int last = Mathf.Max(1, selectedDataset.TimeCount - 1);
            timeBoundaryStart = Mathf.Clamp(firstCutIndex, 0, last - 1);
            timeBoundaryEnd = Mathf.Clamp(secondCutIndex,
                timeBoundaryStart + 1, last);
            selectedTime = timeBoundaryStart;
            selectedZ = 0;
            slabNormalized = 0.0f;
            sharedSelectedTime = selectedTime;
            sharedSelectedZ = 0;
            sharedBoundariesInitialized = true;
            StoreEffectiveBoundaryValues();
            SetTime(selectedTime);

            // Rebuild the authoritative time bucket, but do not start MatPlot.
            // The normal downstream workflow owns intent, matrix and findings.
            CommitAuthorBoundaryBuckets();
            authorBoundaryConfirmed = true;
            initialTimeBoundaryComplete = true;
            initialDepthBoundaryComplete = true;
            initialBoundarySetupActive = false;
            slabPreviewBuilt = true;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            ClearSourcePreviewLayers();
            UpdateTimeBoundaryHandles();
            UpdateDepthBoundaryPlanes();
            UpdateAnalysisAxisLabels();
            RecordTrailEvent("XYT TIME RANGE",
                TimeRangeSummary() + " selected from the combined six-event STC");

            // A combined-STC selection is the For_VR Set Time Range step.
            // Return to the normal workspace at its next stage.
            if (!mainWorkspaceEntered)
            {
                preconfigurationActive = false;
                mainWorkspaceEntered = true;
                stage = Stage.Slab;
                legacyPanelVisible = false;
                if (spatialAxisComposerRoot != null)
                    spatialAxisComposerRoot.SetActive(true);
                if (workflowToolbarCanvas != null)
                    workflowToolbarCanvas.gameObject.SetActive(true);
                if (panelCanvas != null)
                    panelCanvas.gameObject.SetActive(false);
                RefreshSpatialAxisControllers();
                UpdateVariablePaletteFollow(true);
            }

            if (AreSpatialAxisBindingsComplete(out string missing))
            {
                spatialWorkflowStep = SpatialWorkflowStep.Intent;
                SetStatus("Three time ranges fixed from the combined STC: " +
                    TimeRangeSummary() +
                    ". Continue the existing workflow; MatPlot remains in the later analysis stage.");
            }
            else
            {
                spatialWorkflowStep = SpatialWorkflowStep.AxisBinding;
                if (workflowToolbarCanvas != null)
                    workflowToolbarCanvas.gameObject.SetActive(true);
                SetStatus("Three time ranges fixed from the combined STC: " +
                    TimeRangeSummary() +
                    ". Continue with axis binding (" + missing + ").");
            }
            BuildWorkflowToolbar();
            BuildStage();
            BuildMainMenu();
        }

        private void SetTime(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            selectedTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            if (forVrSurfacePlayer != null)
                forVrSurfacePlayer.ShowFrame(selectedTime, false);
            else
            {
                ApplyTimeFilter();
                FrameVolume();
                StartCoroutine(RefitVolumeAfterFrameChange());
            }
            // Multi-variable views each own a current-day 3D volume. Rebuild
            // only entries whose day changed; never replace them with slices.
            RefreshVariableFacetStacks();
            RefreshSlabTexture();
            RebuildTimeMarkers();
            SetStatus(IsForVrSurfaceDataset
                ? "Surface time: " + selectedDataset.GetTimeLabel(selectedTime) + "."
                : "Time pivot: " + selectedDataset.GetTimeLabel(selectedTime) +
                    ", z=" + selectedZ + ".");
            BuildStage();
        }

        private bool IsForVrSurfaceDataset =>
            VolumeSTCubeForVrSurfacePlayer.Supports(selectedDataset);

        private void OnForVrSurfaceTimeChanged(int timeIndex)
        {
            if (selectedDataset == null)
                return;
            selectedTime = Mathf.Clamp(timeIndex, 0, selectedDataset.TimeCount - 1);
            // Playback reads the 2D surface frame directly. Avoid scheduling a
            // hidden 3D texture upload on every tick; the selected index is
            // still used by the downstream S4D request when playback pauses.
        }

        private void ApplyTimeFilter()
        {
            if (currentView == null || selectedDataset == null)
                return;
            float minimum = selectedTime / (float)selectedDataset.TimeCount;
            float maximum = (selectedTime + 1) / (float)selectedDataset.TimeCount;
            currentView.ApplyTimeFilter(minimum, maximum);
            VolumeControllerObject controller = currentView.GetManagedController();
            if (controller == null && currentView.rootObject != null)
                controller = currentView.rootObject.GetComponent<VolumeControllerObject>();
            VolumeSTCubeOriginalSceneAdapter.ApplyVariableOpacityPreset(
                controller, selectedDataset.Name, 0.82f);
        }

        private void SetDepth(float normalized)
        {
            slabNormalized = Mathf.Clamp01(normalized);
            UpdateSlabVisual(true);
            RefreshSlabTexture();
            BuildStage();
        }

        private void BeginSlabInteraction()
        {
            if (selectedDataset == null || rayInteractor == null)
                return;
            if (drawingRegion)
            {
                if (TryGetSlabPoint(rayInteractor.PointerRay, out Vector2 point))
                {
                    regionDragging = true;
                    regionStart = point;
                    region = new Rect(point.x - 0.001f, point.y - 0.001f, 0.002f, 0.002f);
                    UpdateRegionVisual();
                }
                return;
            }

            draggingSlab = true;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                desktopDragStartMouseY = FlatPointerPosition.y;
                desktopDragStartSlab = slabNormalized;
                return;
            }
#endif
            float handNormalized = GetHandDepthNormalized();
            slabDragOffset = slabNormalized - handNormalized;
        }

        private void UpdateSlabInteraction()
        {
            if (rayInteractor == null || selectedDataset == null)
                return;
            if (draggingSlab)
            {
                if (rayInteractor.TriggerHeld)
                {
#if UNITY_EDITOR || SLABLAB_FLAT
                    if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                        slabNormalized = Mathf.Clamp01(desktopDragStartSlab +
                            (FlatPointerPosition.y - desktopDragStartMouseY) / Mathf.Max(360.0f, Screen.height * 0.72f));
                    else
#endif
                    slabNormalized = Mathf.Clamp01(GetHandDepthNormalized() + slabDragOffset);
                    UpdateSlabVisual(false);
                }
                if (rayInteractor.TriggerReleased)
                {
                    draggingSlab = false;
                    RefreshSlabTexture();
                    SetStatus("Slab grounded at z=" + selectedZ + ". Materialize or draw a region.");
                    BuildStage();
                }
            }
            if (regionDragging)
            {
                if (rayInteractor.TriggerHeld && TryGetSlabPoint(rayInteractor.PointerRay, out Vector2 point))
                {
                    float radius = Mathf.Clamp(Vector2.Distance(regionStart, point), 0.002f, 0.48f);
                    region = new Rect(
                        regionStart.x - radius,
                        regionStart.y - radius,
                        radius * 2.0f,
                        radius * 2.0f);
                    UpdateRegionVisual();
                }
                if (rayInteractor.TriggerReleased)
                {
                    regionDragging = false;
                    drawingRegion = false;
                    if (region.width < 0.04f)
                        region = new Rect(0.28f, 0.28f, 0.44f, 0.44f);
                    UpdateRegionVisual();
                    SetStatus("Region grounded. It will be compared with the rest of the slab.");
                    BuildStage();
                }
            }
        }

        private float GetHandDepthNormalized()
        {
            if (spatialRoot == null || rayInteractor == null)
                return 0.5f;

            // Drag on a vertical interaction plane through the Field. The plane
            // follows the Field rotation, so the cut remains directly under the
            // visible controller ray even after the user rotates the workspace.
            Plane dragPlane = new Plane(spatialRoot.transform.forward,
                spatialRoot.transform.position);
            if (dragPlane.Raycast(rayInteractor.PointerRay, out float distance) &&
                distance >= 0.0f && distance <= rayInteractor.maxDistance)
            {
                Vector3 local = spatialRoot.transform.InverseTransformPoint(
                    rayInteractor.PointerRay.GetPoint(distance));
                return Mathf.Clamp01(Mathf.InverseLerp(
                    volumeLocalMinY, volumeLocalMaxY, local.y));
            }

            Vector3 fallbackLocal = spatialRoot.transform.InverseTransformPoint(
                rayInteractor.transform.position);
            return Mathf.Clamp01(Mathf.InverseLerp(
                volumeLocalMinY, volumeLocalMaxY, fallbackLocal.y));
        }

        private bool TryGetSlabPoint(Ray ray, out Vector2 normalized)
        {
            normalized = Vector2.zero;
            Plane plane = new Plane(spatialRoot.transform.up, slabObject.transform.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0.0f || distance > rayInteractor.maxDistance)
                return false;
            Vector3 local = spatialRoot.transform.InverseTransformPoint(ray.GetPoint(distance));
            normalized = new Vector2(
                Mathf.Clamp01(local.x / (FieldHalfWidth * 1.88f) + 0.5f),
                Mathf.Clamp01(local.z / (FieldHalfDepth * 1.88f) + 0.5f));
            return true;
        }

        private void UpdateSlabVisual(bool updateLabel)
        {
            if (slabObject == null)
                return;
            float y = Mathf.Lerp(volumeLocalMinY, volumeLocalMaxY, slabNormalized);
            slabObject.transform.localPosition = new Vector3(0.0f, y, 0.0f);
            if (!depthInspectionActive)
            {
                slabPreviewObject.transform.localPosition =
                    new Vector3(0.0f, y + 0.013f, 0.0f);
                regionRoot.transform.localPosition =
                    new Vector3(0.0f, y + 0.024f, 0.0f);
            }
            if (selectedDataset != null)
                selectedZ = Mathf.Clamp(Mathf.RoundToInt(slabNormalized * (selectedDataset.DimZ - 1)), 0, selectedDataset.DimZ - 1);
            if (updateLabel && slabLabel != null && selectedDataset != null)
                slabLabel.text = selectedDataset.Name + "  |  " + selectedDataset.GetTimeLabel(selectedTime) + "  |  z=" + selectedZ;
            UpdateAnalysisAxisLabels();
        }

        private void RefreshSlabTexture()
        {
            if (selectedDataset == null)
                return;
            try
            {
                if (slabTexture != null)
                    Destroy(slabTexture);
                VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                    selectedDataset.RawPaths[selectedTime], selectedDataset.IniPaths[selectedTime], selectedZ);
                slabTexture = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 512, 512);
                slabPreviewMaterial.mainTexture = slabTexture;
                slabPreviewMaterial.mainTextureScale = Vector2.one;
                slabPreviewMaterial.mainTextureOffset = Vector2.zero;
            }
            catch (Exception exception)
            {
                SetStatus("Slab read failed: " + exception.Message);
            }
        }

        private bool UsesFacetedVariableFields()
        {
            if (roles[3] != DimensionRole.Faceted)
                return false;

            int uniqueVariables = 0;
            HashSet<int> seen = new HashSet<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable < 0 || variable >= datasets.Count ||
                    !seen.Add(variable))
                    continue;
                uniqueVariables++;
                if (uniqueVariables > 1)
                    return true;
            }
            return false;
        }

        // currentView is the hidden source/import view used by time filtering.
        // In Variable=Faceted mode each visible Field owns a separate paired
        // volume, so showing currentView as well produces a second unrelated
        // volume inside the first Field during boundary authoring.
        private void ApplyPrimaryVolumeVisibility()
        {
            if (currentView == null)
                return;
            bool aggregateGroundOwnsView = groundDocked &&
                groundMode == GroundMode.Aggregate &&
                groundAggregateVolume != null;
            currentView.SetVisible(cubeVisible &&
                !UsesFacetedVariableFields() &&
                !aggregateGroundOwnsView);
        }

        private void RefreshVariableFacetStacks()
        {
            // Preserve the actual volume objects while their wire frames are
            // rebuilt. Destroying the old frame without detaching these first
            // would also destroy their Texture3D-backed renderers.
            foreach (KeyValuePair<int, VolumeRenderedObject> entry in
                pairedVariableVolumes)
            {
                if (entry.Value != null && spatialRoot != null)
                    entry.Value.transform.SetParent(spatialRoot.transform, true);
            }
            if (variableFacetStacksRoot != null)
                Destroy(variableFacetStacksRoot);
            variableFacetStacksRoot = null;
            for (int index = 0; index < variableFacetStackTextures.Count; index++)
                if (variableFacetStackTextures[index] != null)
                    Destroy(variableFacetStackTextures[index]);
            variableFacetStackTextures.Clear();

            List<int> boundVariables = new List<int>();
            for (int index = 0; index < spatialAxisRigStates.Count; index++)
            {
                int variable = spatialAxisRigStates[index].boundVariable;
                if (variable >= 0 && variable < datasets.Count &&
                    !boundVariables.Contains(variable))
                    boundVariables.Add(variable);
            }
            bool showMultiples = roles[3] == DimensionRole.Faceted &&
                boundVariables.Count > 1 && spatialRoot != null;
            if (!showMultiples)
            {
                ClearPairedVariableVolumes();
                if (currentView != null)
                    currentView.SetVisible(cubeVisible &&
                        !(groundDocked && groundMode == GroundMode.Aggregate &&
                          groundAggregateVolume != null));
                if (slabObject != null)
                    slabObject.SetActive(true);
                if (slabPreviewObject != null)
                    // The textured XY preview is an authored/inspected slice,
                    // not a permanent mid-plane. Keeping it active here showed
                    // an opaque white sheet before a Depth selection existed.
                    slabPreviewObject.SetActive(depthInspectionActive ||
                        (boundaryEditActive &&
                         boundaryDimension == BoundaryDimension.Horizontal));
                return;
            }

            if (currentView != null)
                currentView.SetVisible(false);
            // A faceted variable layout is a set of independent continuous
            // fields. The authoring slab belongs to the later boundary step and
            // must not masquerade as the variable's data here.
            if (slabObject != null)
                slabObject.SetActive(false);
            if (slabPreviewObject != null)
                slabPreviewObject.SetActive(false);
            RemoveUnusedPairedVariableVolumes(boundVariables);
            variableFacetStacksRoot = new GameObject(
                "Variable Faceted Continuous STC Fields");
            variableFacetStacksRoot.transform.SetParent(spatialRoot.transform, false);

            int count = boundVariables.Count;
            for (int variableIndex = 0; variableIndex < count; variableIndex++)
            {
                int boundVariable = boundVariables[variableIndex];
                VolumeSTCubeSliceDataset dataset = datasets[boundVariable];
                int rigIndex = spatialAxisRigStates.FindIndex(state =>
                    state.boundVariable == boundVariable);
                SpatialAxisRigState rigState = rigIndex >= 0
                    ? spatialAxisRigStates[rigIndex] : null;

                // Each axis body owns one complete STC field. Field 1 reuses the
                // existing Continuous Field wire cube; later variables receive
                // full-size sibling copies outside it, never mini cubes inside it.
                GameObject fieldFrame = new GameObject(dataset.Name +
                    " paired STC field frame");
                fieldFrame.transform.SetParent(
                    variableFacetStacksRoot.transform, false);
                // Layout follows the visible variable order rather than the
                // backing rig-state slot. This keeps the remaining Fields in
                // the intended left / above-axis / right-axis positions after
                // a variable is removed and another one is added.
                fieldFrame.transform.localPosition = PairedFieldCenter(
                    variableIndex, count);
                // Every Field is a stable, upright data space. The shared
                // tri-axis controls semantic layout but never rotates Fields.
                fieldFrame.transform.localRotation = Quaternion.identity;
                // Keep the primary Field dominant. Extra variables remain
                // available as compact STC cards instead of three competing
                // full-size coordinate systems.
                fieldFrame.transform.localScale = variableIndex == 0
                    ? Vector3.one : Vector3.one * 0.62f;

                float halfWidth = FieldHalfWidth;
                float halfHeight = FieldHalfHeight;
                float halfDepth = FieldHalfDepth;
                CreatePairedFieldWireFrame(fieldFrame.transform, halfWidth,
                    halfHeight, halfDepth, rigIndex, dataset.Name,
                    variableIndex != 0);

                int requestedTime = rigState != null &&
                    !rigState.usesSharedBoundaries
                    ? rigState.customSelectedTime
                    : sharedSelectedTime;
                int time = Mathf.Clamp(requestedTime, 0,
                    Mathf.Max(0, dataset.TimeCount - 1));
                VolumeRenderedObject volume = GetOrCreatePairedVariableVolume(
                    boundVariable, time);
                if (volume != null)
                {
                    volume.gameObject.name = dataset.Name +
                        " continuous STC volume";
                    volume.transform.SetParent(fieldFrame.transform, false);
                    volume.transform.localPosition = Vector3.zero;
                    volume.transform.localRotation = Quaternion.identity;
                    volume.transform.localScale = Vector3.one;
                    FitPairedVolumeToField(volume.transform,
                        fieldFrame.transform, halfWidth, halfHeight, halfDepth);
                }
            }

            // Quiet magnetic rails make the T-shaped composition read as one
            // system without visually cutting through any volume rendering.
            Color railColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
            float railZ = -FieldHalfDepth - 0.055f;
            if (count > 1)
            {
                Vector3 upperCenter = PairedFieldCenter(1, count);
                CreateWorldLine("Upper Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX, 0.56f, railZ),
                    new Vector3(upperCenter.x,
                        upperCenter.y - FieldHalfHeight - 0.055f, railZ),
                    railColor, 0.0045f);
            }
            if (count > 2)
            {
                Vector3 rightCenter = PairedFieldCenter(2, count);
                CreateWorldLine("Right Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX + 0.58f, 0.0f, railZ),
                    new Vector3(rightCenter.x - FieldHalfWidth - 0.055f,
                        0.0f, railZ), railColor, 0.0045f);
            }
            if (count > 3)
            {
                Vector3 upperRightCenter = PairedFieldCenter(3, count);
                CreateWorldLine("Upper-right Field magnetic rail",
                    variableFacetStacksRoot.transform,
                    new Vector3(SpatialAxisDockX,
                        SpatialFieldOrbitY, railZ),
                    new Vector3(upperRightCenter.x -
                        FieldHalfWidth - 0.055f,
                        upperRightCenter.y, railZ), railColor, 0.0045f);
            }
        }

        private VolumeRenderedObject GetOrCreatePairedVariableVolume(
            int variableIndex, int timeIndex)
        {
            if (variableIndex < 0 || variableIndex >= datasets.Count)
                return null;
            if (pairedVariableVolumes.TryGetValue(variableIndex,
                    out VolumeRenderedObject existing) && existing != null &&
                pairedVariableVolumeTimes.TryGetValue(variableIndex,
                    out int existingTime) && existingTime == timeIndex)
                return existing;

            DestroyPairedVariableVolume(variableIndex);
            VolumeSTCubeSliceDataset dataset = datasets[variableIndex];
            if (dataset.RawPaths == null || dataset.IniPaths == null ||
                timeIndex < 0 || timeIndex >= dataset.RawPaths.Length ||
                timeIndex >= dataset.IniPaths.Length)
                return null;
            try
            {
                VolumeRenderedObject volume = VolumeSTCubeRawVolumeFactory.Import(
                    dataset.RawPaths[timeIndex], dataset.IniPaths[timeIndex],
                    dataset.Name);
                if (volume == null)
                    return null;
                // Keep Quest on the scalar unlit DVR path. Enabling lighting
                // allocates an additional gradient Texture3D for every variable.
                volume.SetLightingEnabled(false);
                ApplyPairedVolumeAppearance(volume);
                pairedVariableVolumes[variableIndex] = volume;
                pairedVariableVolumeTimes[variableIndex] = timeIndex;
                Renderer[] renderers = volume.GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (renderer == null)
                        continue;
                    // Match the established STC convention used by the primary
                    // controller: texture Z is shown as the vertical depth axis.
                    renderer.transform.localRotation =
                        Quaternion.Euler(90.0f, 0.0f, 0.0f);
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.allowOcclusionWhenDynamic = false;
                    renderer.sortingOrder = -100;
                }
                if (volume.dataset != null)
                    volume.dataset.rotation =
                        Quaternion.Euler(90.0f, 0.0f, 0.0f);
                Collider[] colliders = volume.GetComponentsInChildren<Collider>(true);
                for (int index = 0; index < colliders.Length; index++)
                    if (colliders[index] != null)
                        Destroy(colliders[index]);
                return volume;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Continuous paired STC volume failed: " +
                    exception.Message);
                return null;
            }
        }

        private void ApplyPairedVolumeAppearance(VolumeRenderedObject volume)
        {
            if (volume == null)
                return;

            VolumeControllerObject sourceController = currentView != null
                ? currentView.GetManagedController() : null;
            if (sourceController == null && currentView != null &&
                currentView.rootObject != null)
                sourceController = currentView.rootObject.GetComponentInChildren<
                    VolumeControllerObject>(true);

            // Always establish the colour-capable DVR path. The raw object
            // factory otherwise starts with its generic grayscale transfer
            // function, which made faceted variables appear white or black.
            volume.SetRenderMode(RenderMode.DirectVolumeRendering);
            volume.SetTransferFunctionMode(TFRenderMode.TF1D);
            volume.SetLightingEnabled(false);
            if (sourceController == null)
                return;

            if (sourceController.transferFunction != null)
            {
                volume.transferFunction = sourceController.transferFunction;
                volume.SetTransferFunction(sourceController.transferFunction);
            }
            volume.transferFunction2D = sourceController.transferFunction2D;
            volume.SetVisibilityWindow(sourceController.GetVisibilityWindow().x,
                sourceController.GetVisibilityWindow().y);
            volume.SetRayTerminationEnabled(
                sourceController.GetRayTerminationEnabled());
            volume.SetCubicInterpolationEnabled(
                sourceController.GetCubicInterpolationEnabled());
            volume.SetHighlightPosition(sourceController.highlightPosition);
            volume.SetHighlightRadius(sourceController.highlightRadius);

            // Clone the already validated STC material so shader keywords and
            // colour sampling exactly match the primary Field. Restore the new
            // object's own scalar texture and dimensions after cloning.
            MeshRenderer sourceRenderer = null;
            if (sourceController.meshRenderers != null)
            {
                for (int index = 0; index < sourceController.meshRenderers.Length;
                    index++)
                {
                    if (sourceController.meshRenderers[index] != null &&
                        sourceController.meshRenderers[index].sharedMaterial != null)
                    {
                        sourceRenderer = sourceController.meshRenderers[index];
                        break;
                    }
                }
            }
            if (sourceRenderer == null || volume.meshRenderer == null ||
                volume.meshRenderer.sharedMaterial == null)
                return;

            Material previousMaterial = volume.meshRenderer.sharedMaterial;
            Texture dataTexture = previousMaterial.HasProperty("_DataTex")
                ? previousMaterial.GetTexture("_DataTex") : null;
            Material matchedMaterial = new Material(sourceRenderer.sharedMaterial);
            if (matchedMaterial.HasProperty("_DataTex"))
                matchedMaterial.SetTexture("_DataTex", dataTexture);
            if (matchedMaterial.HasProperty("_GradientTex"))
                matchedMaterial.SetTexture("_GradientTex", null);
            if (matchedMaterial.HasProperty("_TextureSize") &&
                volume.dataset != null)
                matchedMaterial.SetVector("_TextureSize", new Vector3(
                    volume.dataset.dimX, volume.dataset.dimY,
                    volume.dataset.dimZ));
            matchedMaterial.DisableKeyword("LIGHTING_ON");
            matchedMaterial.EnableKeyword("MODE_DVR");
            matchedMaterial.DisableKeyword("MODE_MIP");
            matchedMaterial.DisableKeyword("MODE_SURF");
            volume.meshRenderer.sharedMaterial = matchedMaterial;
            Destroy(previousMaterial);
        }

        private void FitPairedVolumeToField(Transform volumeRoot,
            Transform fieldFrame, float halfWidth, float halfHeight,
            float halfDepth)
        {
            if (volumeRoot == null || fieldFrame == null)
                return;
            volumeRoot.localPosition = Vector3.zero;
            volumeRoot.localScale = Vector3.one;
            Renderer[] renderers =
                volumeRoot.GetComponentsInChildren<Renderer>(true);
            if (!TryRendererBoundsInSpace(renderers, fieldFrame,
                    out Bounds localBounds))
                return;
            Vector3 size = localBounds.size;
            float fit = Mathf.Min(
                halfWidth * 1.56f / Mathf.Max(0.0001f, size.x),
                Mathf.Min(
                    halfHeight * 1.48f / Mathf.Max(0.0001f, size.y),
                    halfDepth * 1.56f / Mathf.Max(0.0001f, size.z)));
            volumeRoot.localScale *= Mathf.Clamp(fit, 0.001f, 20.0f);
            if (!TryRendererBoundsInSpace(renderers, fieldFrame,
                    out localBounds))
                return;
            float stretch = Mathf.Clamp(
                halfHeight * 1.42f / Mathf.Max(0.0001f, localBounds.size.y),
                1.0f, FieldVerticalExaggeration);
            Vector3 scale = volumeRoot.localScale;
            scale.y *= stretch;
            volumeRoot.localScale = scale;
            if (TryRendererBoundsInSpace(renderers, fieldFrame,
                    out localBounds))
                volumeRoot.localPosition -= localBounds.center;
        }

        private static bool TryRendererBoundsInSpace(Renderer[] renderers,
            Transform space, out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds world = renderer.bounds;
                Vector3 minimum = world.min;
                Vector3 maximum = world.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldPoint = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 localPoint = space.InverseTransformPoint(worldPoint);
                    if (!hasBounds)
                    {
                        bounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localPoint);
                    }
                }
            }
            return hasBounds;
        }

        private void RemoveUnusedPairedVariableVolumes(List<int> active)
        {
            List<int> stale = new List<int>();
            foreach (KeyValuePair<int, VolumeRenderedObject> entry in
                pairedVariableVolumes)
                if (!active.Contains(entry.Key))
                    stale.Add(entry.Key);
            for (int index = 0; index < stale.Count; index++)
                DestroyPairedVariableVolume(stale[index]);
        }

        private void DestroyPairedVariableVolume(int variableIndex)
        {
            if (pairedVariableVolumes.TryGetValue(variableIndex,
                    out VolumeRenderedObject volume) && volume != null)
            {
                // Destroy is deferred until the end of the Unity frame. Hide
                // the outgoing renderers immediately so a newly imported day
                // can never overlap the previous day for one visible frame.
                Renderer[] outgoingRenderers =
                    volume.GetComponentsInChildren<Renderer>(true);
                for (int index = 0; index < outgoingRenderers.Length; index++)
                    if (outgoingRenderers[index] != null)
                        outgoingRenderers[index].enabled = false;
                VolumeDataset dataset = volume.dataset;
                if (dataset != null)
                {
                    dataset.ReleaseRuntimeTextures();
                    Destroy(dataset);
                }
                Destroy(volume.gameObject);
            }
            pairedVariableVolumes.Remove(variableIndex);
            pairedVariableVolumeTimes.Remove(variableIndex);
        }

        private void ClearPairedVariableVolumes()
        {
            List<int> variables = new List<int>(pairedVariableVolumes.Keys);
            for (int index = 0; index < variables.Count; index++)
                DestroyPairedVariableVolume(variables[index]);
        }

        private void CreateHolographicFieldFrame(Transform parent,
            Vector3[] corners, int[,] edges, string prefix)
        {
            // Dyson-inspired construction language: a very quiet structural
            // silhouette plus bright, rounded corner brackets. The Field stays
            // legible without twelve luminous bars crossing nearby workspaces.
            Color ghost = new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f);
            Color bracket = new Color(Cyan.r, Cyan.g, Cyan.b, 0.86f);
            for (int edge = 0; edge < edges.GetLength(0); edge++)
            {
                Vector3 a = corners[edges[edge, 0]];
                Vector3 b = corners[edges[edge, 1]];
                CreateWorldLine(prefix + " soft edge", parent, a, b,
                    ghost, 0.0024f);

                float length = Vector3.Distance(a, b);
                float fraction = Mathf.Clamp(0.19f /
                    Mathf.Max(0.001f, length), 0.10f, 0.24f);
                Vector3 nearA = Vector3.Lerp(a, b, fraction);
                Vector3 nearB = Vector3.Lerp(b, a, fraction);
                CreateWorldLine(prefix + " corner A", parent, a, nearA,
                    bracket, 0.0062f);
                CreateWorldLine(prefix + " corner B", parent, b, nearB,
                    bracket, 0.0062f);
            }
        }

        private void CreatePairedFieldWireFrame(Transform parent,
            float halfWidth, float halfHeight, float halfDepth, int rigIndex,
            string variableName, bool drawOuterFrame)
        {
            Vector3[] corners =
            {
                new Vector3(-halfWidth,-halfHeight,-halfDepth),
                new Vector3(halfWidth,-halfHeight,-halfDepth),
                new Vector3(halfWidth,-halfHeight,halfDepth),
                new Vector3(-halfWidth,-halfHeight,halfDepth),
                new Vector3(-halfWidth,halfHeight,-halfDepth),
                new Vector3(halfWidth,halfHeight,-halfDepth),
                new Vector3(halfWidth,halfHeight,halfDepth),
                new Vector3(-halfWidth,halfHeight,halfDepth)
            };
            int[,] edges =
            {
                {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},
                {0,4},{1,5},{2,6},{3,7}
            };
            if (drawOuterFrame)
            {
                CreateHolographicFieldFrame(parent, corners, edges,
                    "Paired Field");
#if false // Compact secondary Fields only need frame and variable selector.
                float bottom = -halfHeight + 0.115f;
                float innerDepth = halfDepth - 0.070f;
                float left = -halfWidth + 0.075f;
                CreateWorldLine("Paired field X Time", parent,
                    new Vector3(left, bottom, innerDepth),
                    new Vector3(halfWidth - 0.075f, bottom, innerDepth),
                    TimeColor, 0.009f);
                CreateWorldLine("Paired field Y Variable", parent,
                    new Vector3(left, bottom, -halfDepth + 0.07f),
                    new Vector3(left, bottom, innerDepth),
                    VariableColor, 0.009f);
                CreateWorldLine("Paired field Z Depth", parent,
                    new Vector3(left, -halfHeight + 0.075f, innerDepth),
                    new Vector3(left, halfHeight - 0.075f, innerDepth),
                    DepthAxisColor, 0.010f);
                CreateWorldLabel("X  TIME",
                    new Vector3(0.24f, bottom + 0.040f, innerDepth),
                    0.0072f, TextAnchor.MiddleCenter, TimeColor, parent);
                CreateWorldLabel("Y  VARIABLE",
                    new Vector3(left + 0.025f, bottom + 0.050f,
                        -halfDepth + 0.12f),
                    0.0068f, TextAnchor.LowerLeft, VariableColor, parent);
                CreateWorldLabel("Z  DEPTH",
                    new Vector3(left + 0.035f, halfHeight - 0.055f,
                        innerDepth - 0.020f),
                    0.0068f, TextAnchor.UpperLeft, DepthAxisColor, parent);
#endif
            }

#if false // The clickable selector below already carries this variable name.
            string number = rigIndex >= 0 ? (rigIndex + 1).ToString() : "?";
            CreateWorldLabel(number + "  |  " + variableName.ToUpperInvariant(),
                new Vector3(0.0f, halfHeight - 0.075f, halfDepth), 0.0062f,
                TextAnchor.MiddleCenter, VariableColor, parent);
#endif

            if (rigIndex >= 0 && rigIndex < spatialAxisRigStates.Count)
            {
                int boundVariable = spatialAxisRigStates[rigIndex].boundVariable;
                if (boundVariable >= 0 && boundVariable < datasets.Count)
                {
                    GameObject selector = GameObject.CreatePrimitive(
                        PrimitiveType.Cube);
                    selector.name = variableName + " active Field selector";
                    selector.layer = 5;
                    selector.transform.SetParent(parent, false);
                    selector.transform.localPosition = new Vector3(
                        0.0f, halfHeight - 0.075f, halfDepth + 0.025f);
                    selector.transform.localScale =
                        new Vector3(0.62f, 0.080f, 0.035f);
                    selector.GetComponent<Renderer>().material =
                        CreateStableOpaqueMaterial(new Color(
                            VariableColor.r * 0.28f,
                            VariableColor.g * 0.28f,
                            VariableColor.b * 0.28f, 1.0f));
                    int capturedVariable = boundVariable;
                    selector.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked =
                        () => LoadDataset(capturedVariable);
                    CreatePalettePhysicalText(selector.transform,
                        variableName.ToUpperInvariant(),
                        new Vector3(-0.012f, 0.0f, 0.021f),
                        0.0038f, Ink);
                }
            }

            if (rigIndex < 0 || rigIndex >= spatialAxisRigStates.Count)
                return;
#if false // Mapping is already visible on the shared tri-axis composer.
            SpatialAxisRigState state = spatialAxisRigStates[rigIndex];
            int valueAxis = state.timeAxis >= 0 && state.depthAxis >= 0
                ? RemainingAxis(state.timeAxis, state.depthAxis) : -1;
            string[] names = { "X", "Y", "Z" };
            string timeAxis = state.timeAxis >= 0 ? names[state.timeAxis] : "—";
            string depthAxis = state.depthAxis >= 0 ? names[state.depthAxis] : "—";
            string value = valueAxis >= 0 ? names[valueAxis] : "—";
            CreateWorldLabel("TIME=" + timeAxis + "   DEPTH=" + depthAxis +
                    "   VALUE=" + value,
                new Vector3(0.0f, -halfHeight - 0.065f, halfDepth), 0.0041f,
                TextAnchor.MiddleCenter, Ink, parent);
#endif
        }

        private int RepresentativeFacetDepth(int layer, int depthCount)
        {
            if (authoredDepthBuckets != null && layer >= 0 &&
                layer < authoredDepthBuckets.Length)
                return Mathf.Clamp(RepresentativeIndex(
                    authoredDepthBuckets[layer].indices), 0,
                    Mathf.Max(0, depthCount - 1));
            return Mathf.Clamp(Mathf.RoundToInt((layer + 0.5f) *
                depthCount / 3.0f), 0, Mathf.Max(0, depthCount - 1));
        }

        private void BuildMatrixTextures()
        {
            DestroyTextures(matrixTextures);
            int timeBucketCount = Mathf.Max(1,
                activeTimeBuckets != null ? activeTimeBuckets.Length : 1);
            int depthBucketCount = Mathf.Max(1,
                activeDepthBuckets != null ? activeDepthBuckets.Length : 1);
            int cellCount = Mathf.Clamp(timeBucketCount * depthBucketCount,
                1, MaxFacetCells);
            matrixTextures = new Texture2D[cellCount];
            if (selectedDataset == null || activeTimeBuckets == null ||
                activeDepthBuckets == null)
                return;
            UpdateActiveRepresentativeIndices();
            try
            {
                for (int row = 0; row < depthBucketCount; row++)
                {
                    VolumeSTCubeSliceDataset cellDataset =
                        DatasetForBucket(activeDepthBuckets[row]);
                    int z = Mathf.Clamp(RepresentativeIndex(
                        activeDepthBuckets[row].indices), 0,
                        Mathf.Max(0, cellDataset.DimZ - 1));
                    for (int column = 0; column < timeBucketCount; column++)
                    {
                        int time = Mathf.Clamp(RepresentativeIndex(
                            activeTimeBuckets[column].indices), 0,
                            Mathf.Max(0, cellDataset.TimeCount - 1));
                        VolumeSTCubeRawSlice slice = VolumeSTCubeRawSliceReader.ReadSlice(
                            cellDataset.RawPaths[time],
                            cellDataset.IniPaths[time], z);
                        int index = row * timeBucketCount + column;
                        matrixTextures[index] = VolumeSTCubeRawSliceReader.CreatePreviewTexture(slice, 260, 92);
                        matrixMinimums[index] = slice.Minimum;
                        matrixMaximums[index] = slice.Maximum;
                        double sum = 0.0;
                        if (slice.Values != null)
                            for (int valueIndex = 0; valueIndex < slice.Values.Length; valueIndex++)
                                sum += slice.Values[valueIndex];
                        matrixMeans[index] = slice.Values != null && slice.Values.Length > 0
                            ? (float)(sum / slice.Values.Length)
                            : 0.0f;
                    }
                }
                SetStatus("Slab preview materialized: " + activeGridColumns +
                    " x " + activeGridRows + " real XY panels for " +
                    FacetAxisSummary() + ".");
            }
            catch (Exception exception)
            {
                SetStatus("Fill-Matrix failed: " + exception.Message);
            }
        }

        private void StartS4DGridJob()
        {
            if (selectedDataset == null || jobRunning)
                return;
            // The current workflow goes directly from a resolved intent to
            // Full Matrix.  Source-preview atlases are optional UI evidence,
            // not a prerequisite for the MatPlotAgent request.  Keeping the
            // old atlas check here caused valid 1x1/2x2/3x3 requests to be
            // rejected locally before S4D or MatPlotAgent ever saw them.
            if (spatialWorkflowStep != SpatialWorkflowStep.Materializing ||
                !intentConfigured)
            {
                SetStatus("MatPlotAgent blocked: complete axis binding, boundaries, and intent first.");
                return;
            }
            if (!HasResolvedDatasetManifest())
            {
                SetStatus(datasetManifestResolving
                    ? "Waiting for validated dataset metadata..."
                    : "Full Matrix blocked: " + datasetManifestError);
                return;
            }
            if (materializationVariableCursor < 0)
            {
                materializationVariableIndices.Clear();
                materializationVariableIndices.AddRange(
                    ActiveBoundVariableIndices());
                if (materializationVariableIndices.Count == 0)
                {
                    SetStatus("MatPlotAgent blocked: no bound variable layer.");
                    spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                    return;
                }
                for (int index = 0; index < materializedLayerAtlases.Count; index++)
                {
                    Texture2D oldLayer = materializedLayerAtlases[index];
                    if (oldLayer != null && oldLayer != s4dGridImage &&
                        !IsAnalysisNodeTexture(oldLayer))
                        Destroy(oldLayer);
                }
                materializedLayerAtlases.Clear();
                materializedLayerResults.Clear();
                materializationVariableCursor = 0;
            }
            BuildMatrixBucketSelections();
            jobRunning = true;
            progress = materializationVariableIndices.Count > 0
                ? materializationVariableCursor /
                    (float)materializationVariableIndices.Count
                : 0.0f;
            displayedGridProgress = progress;
            targetGridProgress = progress;
            lastStageProgressBucket = -1;
            DestroyTextures(streamingCellTextures);
            if (variableFacetStacksRoot != null)
                Destroy(variableFacetStacksRoot);
            for (int index = 0; index < variableFacetStackTextures.Count; index++)
                if (variableFacetStackTextures[index] != null)
                    Destroy(variableFacetStackTextures[index]);
            variableFacetStackTextures.Clear();
            s4dGridFailure = string.Empty;
            stage = Stage.Matrix;
            int materializeVariableIndex = materializationVariableIndices[
                Mathf.Clamp(materializationVariableCursor, 0,
                    materializationVariableIndices.Count - 1)];
            VolumeSTCubeSliceDataset materializeDataset =
                datasets[materializeVariableIndex];
            SetStatus("Preparing MatPlotAgent variable layer " +
                (materializationVariableCursor + 1) + "/" +
                materializationVariableIndices.Count + ": " +
                materializeDataset.Name + "...");
            BuildStage();
            if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                BuildFacetGridPanel();
            s4dClient = new VolumeSTCubeS4DAnalysisClient(s4dUrl, 300, 1.0f);
            S4DFacetGridRequest request =
                BuildS4DGridRequestForVariable(materializeVariableIndex);
            request.datasetId = materializeDataset.DatasetId;
            request.variableId = materializeDataset.VariableId;
            StartCoroutine(s4dClient.Materialize(
                request,
                OnS4DGridProgress,
                OnS4DGridComplete,
                OnS4DCellReady));
        }

        private void OnS4DCellReady(string cellId, Texture2D texture)
        {
            if (texture == null || string.IsNullOrWhiteSpace(cellId))
                return;
            int target = -1;
            for (int depth = 0; activeDepthBuckets != null &&
                depth < activeDepthBuckets.Length; depth++)
            {
                for (int time = 0; activeTimeBuckets != null &&
                    time < activeTimeBuckets.Length; time++)
                {
                    string expected = activeTimeBuckets[time].id + "__" +
                        activeDepthBuckets[depth].id;
                    if (expected == cellId)
                    {
                        target = depth * activeTimeBuckets.Length + time;
                        break;
                    }
                }
                if (target >= 0)
                    break;
            }
            if (target < 0 || target >= streamingCellTextures.Length)
            {
                Destroy(texture);
                return;
            }
            if (streamingCellTextures[target] != null)
                Destroy(streamingCellTextures[target]);
            streamingCellTextures[target] = texture;
            SetStatus("MatPlotAgent cell ready: " + cellId + ".");
            bool gridVisible = facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            if (gridVisible)
            {
                RawImage image = facetGridCellImages[target];
                if (image != null)
                {
                    image.texture = texture;
                    image.uvRect = new Rect(0, 0, 1, 1);
                    image.color = Color.white;
                }
                if (facetGridCellPlaceholders[target] != null)
                    facetGridCellPlaceholders[target].SetActive(false);
                if (facetGridCellStateLabels[target] != null)
                {
                    facetGridCellStateLabels[target].text = "VALIDATED";
                    facetGridCellStateLabels[target].color = Green;
                }
                int ready = 0;
                for (int i = 0; i < streamingCellTextures.Length; i++)
                    if (streamingCellTextures[i] != null)
                        ready++;
                if (facetGridValidatedText != null)
                    facetGridValidatedText.text = ready + " / " +
                        Mathf.Max(1, activeGridColumns * activeGridRows) +
                        " PANELS READY";
            }
            else if (stage == Stage.Matrix)
                BuildStage();
        }

        private void MergeCellIntoGridAtlas(int sourceIndex, Texture2D cell)
        {
            if (s4dGridImage == null || cell == null)
                return;
            int columns = Mathf.Max(1, activeTimeBuckets != null
                ? activeTimeBuckets.Length : 3);
            int rows = Mathf.Max(1, activeDepthBuckets != null
                ? activeDepthBuckets.Length : 3);
            int time = Mathf.Clamp(sourceIndex % columns, 0, columns - 1);
            int depth = Mathf.Clamp(sourceIndex / columns, 0, rows - 1);
            int targetWidth = Mathf.Max(1, s4dGridImage.width / columns);
            int targetHeight = Mathf.Max(1, s4dGridImage.height / rows);
            Color[] pixels = new Color[targetWidth * targetHeight];
            for (int y = 0; y < targetHeight; y++)
            {
                float v = (y + 0.5f) / targetHeight;
                for (int x = 0; x < targetWidth; x++)
                {
                    float u = (x + 0.5f) / targetWidth;
                    pixels[y * targetWidth + x] =
                        cell.GetPixelBilinear(u, v);
                }
            }
            int xOffset = time * targetWidth;
            int yOffset = (rows - 1 - depth) * targetHeight;
            s4dGridImage.SetPixels(
                xOffset, yOffset, targetWidth, targetHeight, pixels);
            s4dGridImage.Apply(false, false);
        }

        private S4DFacetGridRequest BuildS4DGridRequestForVariable(
            int variableIndex)
        {
            int savedTimeStart = timeBoundaryStart;
            int savedTimeEnd = timeBoundaryEnd;
            int savedTime = selectedTime;
            float savedDepthLow = depthBoundaryLow;
            float savedDepthHigh = depthBoundaryHigh;
            int savedDepth = selectedZ;
            bool savedConfirmed = authorBoundaryConfirmed;
            SpatialAxisRigState state = spatialAxisRigStates.Find(item =>
                item.boundVariable == variableIndex);
            if (state != null && !state.usesSharedBoundaries)
            {
                timeBoundaryStart = state.customTimeBoundaryStart;
                timeBoundaryEnd = state.customTimeBoundaryEnd;
                selectedTime = state.customSelectedTime;
                depthBoundaryLow = state.customDepthBoundaryLow;
                depthBoundaryHigh = state.customDepthBoundaryHigh;
                selectedZ = state.customSelectedZ;
                // Authored buckets represent the shared ladder. A custom
                // variable intentionally derives its own buckets below.
                authorBoundaryConfirmed = false;
            }
            S4DFacetGridRequest request = BuildS4DGridRequest();
            timeBoundaryStart = savedTimeStart;
            timeBoundaryEnd = savedTimeEnd;
            selectedTime = savedTime;
            depthBoundaryLow = savedDepthLow;
            depthBoundaryHigh = savedDepthHigh;
            selectedZ = savedDepth;
            authorBoundaryConfirmed = savedConfirmed;
            return request;
        }

        private S4DFacetGridRequest BuildS4DGridRequest()
        {
            int timeCount = selectedDataset != null ? selectedDataset.TimeCount : 30;
            int depthCount = selectedDataset != null ? selectedDataset.DimZ : 92;
            int analyzableDepthCount = depthCount == 92 ? 91 : depthCount;

            int timeA = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, timeCount - 2));
            int timeB = Mathf.Clamp(timeBoundaryEnd + 1, timeA + 1, timeCount - 1);
            int surfaceEnd = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryLow * analyzableDepthCount),
                1, Mathf.Max(1, analyzableDepthCount - 2));
            int middleEnd = Mathf.Clamp(
                Mathf.RoundToInt(depthBoundaryHigh * analyzableDepthCount),
                surfaceEnd + 1, analyzableDepthCount - 1);

            S4DIndexBucketRequest[] timeBuckets =
            {
                Bucket("before", "Before", CreateIndexRange(0, timeA)),
                Bucket("during", "During", CreateIndexRange(timeA, timeB)),
                Bucket("after", "After", CreateIndexRange(timeB, timeCount))
            };
            S4DIndexBucketRequest[] depthBuckets =
            {
                Bucket("surface", "Surface", CreateIndexRange(0, surfaceEnd)),
                Bucket("middle", "Middle", CreateIndexRange(surfaceEnd, middleEnd)),
                Bucket("deep", "Deep", CreateIndexRange(middleEnd, analyzableDepthCount))
            };
            if (IsForVrSurfaceDataset)
                depthBuckets = new[]
                {
                    Bucket("surface", "Hong Kong surface", new[] { 0 })
                };
            // Slab Frame consumes the exact Author Boundary ladder that the
            // user confirmed before entering configuration.  It must not
            // silently derive a second, independent set of buckets.
            if (authorBoundaryConfirmed &&
                authoredTimeBuckets != null && authoredTimeBuckets.Length > 0)
                timeBuckets = authoredTimeBuckets;
            if (authorBoundaryConfirmed &&
                authoredDepthBuckets != null && authoredDepthBuckets.Length > 0)
                depthBuckets = authoredDepthBuckets;
            AnalysisNodeState draftSource = FindAnalysisNode(draftSourceNodeId);
            if (draftOperation != DraftOperation.None && draftSource != null)
            {
                if (draftSource.timeBuckets != null && draftSource.timeBuckets.Length > 0)
                    timeBuckets = draftSource.timeBuckets;
                if (draftSource.depthBuckets != null && draftSource.depthBuckets.Length > 0)
                    depthBuckets = draftSource.depthBuckets;
            }
            ApplyDraftBucketOperation(ref timeBuckets, ref depthBuckets);

            if (roles[0] == DimensionRole.Fixed)
            {
                int fixedTime = Mathf.Clamp(selectedTime, 0,
                    Mathf.Max(0, timeCount - 1));
                timeBuckets = new[]
                {
                    Bucket("time_fixed_" + fixedTime,
                        selectedDataset != null
                            ? selectedDataset.GetTimeLabel(fixedTime)
                            : "Fixed time",
                        new[] { fixedTime })
                };
            }
            if (roles[1] == DimensionRole.Fixed)
            {
                int fixedDepth = Mathf.Clamp(selectedZ, 0,
                    Mathf.Max(0, analyzableDepthCount - 1));
                depthBuckets = new[]
                {
                    Bucket("depth_fixed_" + fixedDepth,
                        "z=" + fixedDepth, new[] { fixedDepth })
                };
            }
            // The three buttons emitted by the Time and Depth tokens are the
            // authoritative Matrix selection.  Filter only the normal authored
            // three-part ladder; drill/pivot drafts may intentionally carry a
            // different number of buckets and retain their own topology.
            if (roles[0] == DimensionRole.Faceted && timeBuckets.Length == 3)
                timeBuckets = FilterBucketsByMask(timeBuckets,
                    selectedTimeBucketMask);
            if (roles[1] == DimensionRole.Faceted && depthBuckets.Length == 3)
                depthBuckets = FilterBucketsByMask(depthBuckets,
                    selectedDepthBucketMask);
            // Variable faceting is represented as independent spatial layers.
            // Never replace Depth rows with variable buckets: doing so silently
            // changed a requested 3x3 Time x Depth grid into nine unrelated rows.
            activeTimeBuckets = timeBuckets;
            activeDepthBuckets = depthBuckets;
            bool axisRequestsTranspose = spatialAxisRigStates.Count > 0 &&
                spatialAxisRigStates[0].depthAxis == 0 &&
                spatialAxisRigStates[0].timeAxis != 0;
            activeGridTransposed = draftOperation != DraftOperation.None
                ? pivotTransposed
                : currentAnalysisNode != null
                    ? currentAnalysisNode.gridTransposed
                    : axisRequestsTranspose;
            if (draftOperation == DraftOperation.Drill)
            {
                // Keep the expanded axis horizontal so its child panels remain
                // legible in VR instead of becoming nine compressed rows.
                if (depthBuckets.Length > 3 && timeBuckets.Length <= 3)
                    activeGridTransposed = true;
                else if (timeBuckets.Length > 3 && depthBuckets.Length <= 3)
                    activeGridTransposed = false;
            }
            activeGridColumns = activeGridTransposed
                ? depthBuckets.Length
                : timeBuckets.Length;
            activeGridRows = activeGridTransposed
                ? timeBuckets.Length
                : depthBuckets.Length;
            UpdateActiveRepresentativeIndices();

            return new S4DFacetGridRequest
            {
                datasetId = selectedDataset.DatasetId,
                variableId = selectedDataset.VariableId,
                timeBuckets = timeBuckets,
                depthBuckets = depthBuckets,
                dimensionRoles = BuildDimensionRoleRequest(),
                rawIntent = prompt,
                analysisQuestion = analysisQuestion,
                analyticTask = intentTask,
                // A re-materialization is committed atomically as a complete
                // Grid snapshot.  Streaming cells are previews only; they must
                // never be blended into the stale Grid that remains in
                // SlabTrail while the job is running.
                requestedCellIds = new string[0],
                hasSharedScaleOverride = rematerializingStaleCells,
                sharedScaleMinimum = s4dSharedMinimum,
                sharedScaleMaximum = s4dSharedMaximum
            };
        }

        private static S4DIndexBucketRequest[] FilterBucketsByMask(
            S4DIndexBucketRequest[] buckets, bool[] mask)
        {
            if (buckets == null || buckets.Length == 0 || mask == null)
                return buckets;
            List<S4DIndexBucketRequest> selected =
                new List<S4DIndexBucketRequest>();
            int count = Mathf.Min(buckets.Length, mask.Length);
            for (int index = 0; index < count; index++)
                if (mask[index] && buckets[index] != null)
                    selected.Add(buckets[index]);
            // ToggleAxisBucketSelection prevents this state, but retaining one
            // bucket here makes restored/legacy scenes safe as well.
            if (selected.Count == 0 && buckets[0] != null)
                selected.Add(buckets[0]);
            return selected.ToArray();
        }

        private string[] RequestedRematerializationCellIds(
            S4DIndexBucketRequest[] timeBuckets,
            S4DIndexBucketRequest[] depthBuckets)
        {
            if (!rematerializingStaleCells)
                return new string[0];
            List<string> result = new List<string>();
            for (int depth = 0; depth < depthBuckets.Length; depth++)
                for (int time = 0; time < timeBuckets.Length; time++)
                {
                    int index = depth * timeBuckets.Length + time;
                    if (index < rematerializedCellMask.Length &&
                        rematerializedCellMask[index])
                        result.Add(timeBuckets[time].id + "__" +
                            depthBuckets[depth].id);
                }
            return result.ToArray();
        }

        private S4DDimensionRoleRequest[] BuildDimensionRoleRequest()
        {
            string[] dimensions = { "time", "depth", "horizontal", "variable" };
            S4DDimensionRoleRequest[] assignments =
                new S4DDimensionRoleRequest[dimensions.Length];
            for (int index = 0; index < dimensions.Length; index++)
            {
                assignments[index] = new S4DDimensionRoleRequest
                {
                    dimension = dimensions[index],
                    role = RoleLabel(roles[index]).ToLowerInvariant()
                };
            }
            return assignments;
        }

        private bool HasResolvedDatasetManifest()
        {
            return selectedDataset != null &&
                !string.IsNullOrWhiteSpace(selectedDataset.DatasetId) &&
                !string.IsNullOrWhiteSpace(selectedDataset.VariableId);
        }

        private void ApplyDraftBucketOperation(
            ref S4DIndexBucketRequest[] timeBuckets,
            ref S4DIndexBucketRequest[] depthBuckets)
        {
            if (draftOperation == DraftOperation.Drill)
            {
                if (roles[0] == DimensionRole.Faceted &&
                    CountSelectedTicks(selectedTimeTicks) > 0)
                    timeBuckets = ExpandSelectedBuckets(
                        timeBuckets, selectedTimeTicks, "time");
                else if (roles[1] == DimensionRole.Faceted &&
                    CountSelectedTicks(selectedDepthTicks) > 0)
                    depthBuckets = ExpandSelectedBuckets(
                        depthBuckets, selectedDepthTicks, "depth");
            }
            else if (draftOperation == DraftOperation.RollUp)
            {
                if (draftTargetDimension == 0 &&
                    roles[0] == DimensionRole.Faceted)
                    timeBuckets = MergeBucketGroups(timeBuckets,
                        timeRollupGroups, "time_group");
                else if (draftTargetDimension == 1 &&
                    roles[1] == DimensionRole.Faceted)
                    depthBuckets = MergeBucketGroups(depthBuckets,
                        depthRollupGroups, "depth_group");
            }
            else if (draftOperation == DraftOperation.Pivot)
            {
                // Pivot's row/column chips are direct visibility controls.
                // What is switched off in the preview must also be absent from
                // the materialization request.
                timeBuckets = FilterBucketsByMask(timeBuckets,
                    selectedTimeTicks);
                depthBuckets = FilterBucketsByMask(depthBuckets,
                    selectedDepthTicks);
            }
        }

        private static S4DIndexBucketRequest[] ExpandSelectedBuckets(
            S4DIndexBucketRequest[] source, bool[] selected, string prefix)
        {
            List<S4DIndexBucketRequest> result =
                new List<S4DIndexBucketRequest>();
            for (int index = 0; index < source.Length; index++)
            {
                if (index >= selected.Length || !selected[index])
                {
                    result.Add(source[index]);
                    continue;
                }
                S4DIndexBucketRequest[] children = SplitBucketIntoChildren(
                    source[index], prefix + "_" + source[index].id);
                result.AddRange(children);
            }
            return result.ToArray();
        }

        private static S4DIndexBucketRequest[] SplitBucketIntoChildren(
            S4DIndexBucketRequest source, string prefix)
        {
            int count = source.indices != null ? source.indices.Length : 0;
            if (count < 3)
                return new[] { source };
            S4DIndexBucketRequest[] children = new S4DIndexBucketRequest[3];
            for (int child = 0; child < 3; child++)
            {
                int start = Mathf.FloorToInt(child * count / 3.0f);
                int end = child == 2
                    ? count
                    : Mathf.FloorToInt((child + 1) * count / 3.0f);
                int[] indices = new int[Mathf.Max(1, end - start)];
                for (int index = 0; index < indices.Length; index++)
                    indices[index] = source.indices[Mathf.Min(start + index, count - 1)];
                children[child] = Bucket(
                    prefix + "_child_" + (child + 1),
                    source.label + " " + (child + 1),
                    indices);
            }
            return children;
        }

        private static S4DIndexBucketRequest[] MergeBucketGroups(
            S4DIndexBucketRequest[] source, int[] groups, string mergedId)
        {
            List<S4DIndexBucketRequest> result = new List<S4DIndexBucketRequest>();
            for (int index = 0; index < source.Length; index++)
            {
                int group = index < groups.Length ? groups[index] : 0;
                if (group <= 0)
                {
                    result.Add(source[index]);
                    continue;
                }
                bool firstInGroup = index == 0 || groups[index - 1] != group;
                if (!firstInGroup)
                    continue;
                List<int> mergedIndices = new List<int>();
                List<string> mergedLabels = new List<string>();
                for (int member = index; member < source.Length &&
                    member < groups.Length && groups[member] == group; member++)
                {
                    if (source[member].indices != null)
                        mergedIndices.AddRange(source[member].indices);
                    mergedLabels.Add(source[member].label);
                }
                result.Add(Bucket(
                    mergedId + "_" + group,
                    "Group " + group + " (" +
                        string.Join("+", mergedLabels.ToArray()) + ")",
                    mergedIndices.ToArray()));
            }
            return result.ToArray();
        }

        private void UpdateActiveRepresentativeIndices()
        {
            for (int column = 0; column < matrixTimes.Length; column++)
                if (activeTimeBuckets != null && column < activeTimeBuckets.Length)
                    matrixTimes[column] = RepresentativeIndex(activeTimeBuckets[column].indices);
            for (int row = 0; row < matrixDepths.Length; row++)
                if (activeDepthBuckets != null && row < activeDepthBuckets.Length)
                    matrixDepths[row] = RepresentativeIndex(activeDepthBuckets[row].indices);
        }

        private static int RepresentativeIndex(int[] indices)
        {
            return indices == null || indices.Length == 0
                ? 0
                : indices[indices.Length / 2];
        }

        private static S4DIndexBucketRequest Bucket(string id, string label,
            int[] indices, string variableId = null)
        {
            return new S4DIndexBucketRequest
            {
                id = id,
                label = label,
                indices = indices,
                variableId = variableId
            };
        }

        private static string SafeBucketId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "variable";
            StringBuilder result = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = char.ToLowerInvariant(value[index]);
                result.Append(char.IsLetterOrDigit(character) ? character : '_');
            }
            return result.ToString().Trim('_');
        }

        private static int[] CreateIndexRange(int startInclusive, int endExclusive)
        {
            int count = Mathf.Max(0, endExclusive - startInclusive);
            int[] values = new int[count];
            for (int index = 0; index < count; index++)
                values[index] = startInclusive + index;
            return values;
        }

        private void BuildMatrixBucketSelections()
        {
            if (selectedDataset == null)
                return;
            if (s4dGridImage != null && draftOperation == DraftOperation.None &&
                activeTimeBuckets != null && activeDepthBuckets != null)
            {
                UpdateActiveRepresentativeIndices();
                return;
            }
            int timeCount = selectedDataset.TimeCount;
            int depthCount = selectedDataset.DimZ;
            int timeA = Mathf.Clamp(timeBoundaryStart, 1, Mathf.Max(1, timeCount - 2));
            int timeB = Mathf.Clamp(timeBoundaryEnd + 1, timeA + 1, timeCount - 1);
            int depthA = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryLow * depthCount),
                1, Mathf.Max(1, depthCount - 2));
            int depthB = Mathf.Clamp(Mathf.RoundToInt(depthBoundaryHigh * depthCount),
                depthA + 1, depthCount - 1);
            matrixTimes[0] = Mathf.Clamp((timeA - 1) / 2, 0, timeCount - 1);
            matrixTimes[1] = Mathf.Clamp((timeA + timeB - 1) / 2, 0, timeCount - 1);
            matrixTimes[2] = Mathf.Clamp((timeB + timeCount - 1) / 2, 0, timeCount - 1);
            matrixDepths[0] = Mathf.Clamp((depthA - 1) / 2, 0, depthCount - 1);
            matrixDepths[1] = Mathf.Clamp((depthA + depthB - 1) / 2, 0, depthCount - 1);
            matrixDepths[2] = Mathf.Clamp((depthB + depthCount - 1) / 2, 0, depthCount - 1);
        }

        private void OnS4DGridProgress(string message, float value)
        {
            float layerProgress = Mathf.Clamp01(value);
            int layerCount = Mathf.Max(1, materializationVariableIndices.Count);
            int layerIndex = Mathf.Clamp(materializationVariableCursor, 0,
                layerCount - 1);
            progress = Mathf.Clamp01((layerIndex + layerProgress) / layerCount);
            targetGridProgress = Mathf.Max(targetGridProgress, progress);
            SetStatus("Layer " + (layerIndex + 1) + "/" + layerCount +
                "  " + message + " (" + Mathf.RoundToInt(progress * 100) + "%)");
            bool gridVisible = facetGridCanvas != null &&
                facetGridCanvas.gameObject.activeSelf;
            if (gridVisible)
            {
                if (gridProgressAnimation == null)
                    gridProgressAnimation = StartCoroutine(AnimateGridProgress());
                if (facetGridProgressStageText != null)
                    facetGridProgressStageText.text =
                        MaterializationStageLabel().ToUpperInvariant();
            }
            else if (stage == Stage.Matrix)
            {
                // The Matrix stage is still rebuilt when its coarse milestone changes,
                // not for every network poll. This prevents a visible one-frame pause
                // from destroying and recreating hundreds of UI objects each second.
                int bucket = Mathf.Clamp(Mathf.FloorToInt(progress * 10.0f), 0, 10);
                if (bucket != lastStageProgressBucket)
                {
                    lastStageProgressBucket = bucket;
                    BuildStage();
                }
            }
        }

        private VolumeSTCubeSliceDataset DatasetForBucket(
            S4DIndexBucketRequest bucket)
        {
            if (bucket != null && !string.IsNullOrWhiteSpace(bucket.variableId))
            {
                for (int index = 0; index < datasets.Count; index++)
                {
                    VolumeSTCubeSliceDataset candidate = datasets[index];
                    if (string.Equals(candidate.VariableId, bucket.variableId,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, bucket.variableId,
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(candidate.Name, bucket.label,
                            StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            return selectedDataset;
        }

        private void OnS4DGridComplete(S4DFacetGridResult result)
        {
            jobRunning = false;
            targetGridProgress = 1.0f;
            displayedGridProgress = 1.0f;
            UpdateFacetGenerationProgress();
            s4dClient = null;
            bool completedRematerialization = rematerializingStaleCells;
            if (result == null || !result.Succeeded)
            {
                spatialWorkflowStep = SpatialWorkflowStep.SourcePreviewReady;
                rematerializingStaleCells = false;
                Array.Clear(rematerializedCellMask, 0,
                    rematerializedCellMask.Length);
                progress = 0.0f;
                s4dGridFailure = result != null
                    ? result.Error
                    : "S4D analysis service returned no Grid result.";
                SetStatus(s4dGridFailure);
                BuildStage();
                if (facetGridCanvas != null && facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                return;
            }
            materializedLayerAtlases.Add(result.Panel);
            materializedLayerResults.Add(result);
            if (materializationVariableCursor + 1 <
                materializationVariableIndices.Count)
            {
                // Keep the completed atlas resident, release only the cell-sized
                // streaming textures, and proceed serially to avoid a 27-texture
                // peak on Quest.
                result.Panel = null;
                materializationVariableCursor++;
                spatialWorkflowStep = SpatialWorkflowStep.Materializing;
                SetStatus("Variable layer " + materializationVariableCursor +
                    " complete. Starting layer " +
                    (materializationVariableCursor + 1) + "/" +
                    materializationVariableIndices.Count + "...");
                StartS4DGridJob();
                return;
            }
            Texture2D previousImage = s4dGridImage;
            if (previousImage != null && !IsAnalysisNodeTexture(previousImage))
                Destroy(previousImage);
            s4dGridImage = result.Panel;
            s4dChartResultJson = result.ChartResultJson;
            s4dSnapshotId = result.SnapshotId;
            s4dJobId = result.JobId;
            ApplyAuthoritativeCellStatistics(result);
            if (result.SharedScale != null)
            {
                s4dSharedMinimum = result.SharedScale.minimum;
                s4dSharedMaximum = result.SharedScale.maximum;
                s4dSharedUnit = result.SharedScale.unit;
            }
            s4dGridFailure = string.Empty;
            gridStale = false;
            Array.Clear(facetCellStale, 0, facetCellStale.Length);
            Array.Clear(facetCellInspected, 0, facetCellInspected.Length);
            Array.Clear(facetCellBoundarySuspect, 0,
                facetCellBoundarySuspect.Length);
            Array.Clear(facetCellPinned, 0, facetCellPinned.Length);
            selectedCellPinned = false;
            for (int index = 0; index < facetCellSnapshotIds.Length; index++)
                facetCellSnapshotIds[index] = result.SnapshotId;
            rematerializingStaleCells = false;
            Array.Clear(rematerializedCellMask, 0,
                rematerializedCellMask.Length);
            placementConfirmed = true;
            spatialWorkflowStep = SpatialWorkflowStep.Result;
            materializationVariableCursor = -1;
            progress = 1.0f;
            AnalysisNodeState committed = CommitAnalysisNode(result);
            if (completedRematerialization)
                RecordTrailEvent("RE-MATERIALIZE",
                    "complete snapshot " + result.SnapshotId, committed);
            StartDigestForNode(committed);
            draftOperation = DraftOperation.None;
            draftSourceNodeId = string.Empty;
            ResetSelectedTicksToActiveBuckets();
            SetStatus(completedRematerialization
                ? "Complete Grid re-materialized atomically as " +
                    committed.nodeId + ". The stale parent snapshot remains in SlabTrail."
                : "Validated MatPlotAgent Grid committed as " + committed.nodeId +
                    ". Job " + result.JobId + ".");
            BuildStage();
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            BuildMaterializedVariableLayerPanels();
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
        }

        private AnalysisNodeState CommitAnalysisNode(S4DFacetGridResult result)
        {
            DraftOperation operation = draftOperation;
            string parentId = !string.IsNullOrWhiteSpace(draftSourceNodeId)
                ? draftSourceNodeId
                : currentAnalysisNode != null ? currentAnalysisNode.nodeId : string.Empty;
            AnalysisNodeState node = new AnalysisNodeState
            {
                nodeId = "SLAB-" + (nextAnalysisNodeNumber++).ToString("00"),
                parentNodeId = parentId,
                bornFrom = operation,
                jobId = result != null ? result.JobId : string.Empty,
                snapshotId = result != null ? result.SnapshotId : string.Empty,
                datasetId = selectedDataset != null ? selectedDataset.DatasetId : string.Empty,
                variableId = selectedDataset != null ? selectedDataset.VariableId : string.Empty,
                rawIntent = prompt,
                analyticTask = intentTask,
                intentDisplayLabel = intentMode,
                hasResolvedIntent = intentConfigured,
                title = OperationNodeTitle(operation),
                subtitle = DraftSelectionSummary(operation),
                gridImage = s4dGridImage,
                chartResultJson = s4dChartResultJson,
                timeBoundaryStart = timeBoundaryStart,
                timeBoundaryEnd = timeBoundaryEnd,
                depthBoundaryLow = depthBoundaryLow,
                depthBoundaryHigh = depthBoundaryHigh,
                roleValues = new[]
                {
                    (int)roles[0], (int)roles[1], (int)roles[2], (int)roles[3]
                },
                timeBuckets = activeTimeBuckets,
                depthBuckets = activeDepthBuckets,
                gridTransposed = activeGridTransposed,
                inspected = inspected,
                boundarySuspect = boundarySuspect,
                pinned = false,
                dismissed = false,
                staleCells = new bool[MaxFacetCells],
                verifiedCells = new bool[MaxFacetCells],
                suspectCells = new bool[MaxFacetCells],
                localizedCells = new bool[MaxFacetCells],
                pinnedCells = new bool[MaxFacetCells],
                sharedMinimum = s4dSharedMinimum,
                sharedMaximum = s4dSharedMaximum,
                sharedUnit = s4dSharedUnit,
                cellSnapshotIds = (string[])facetCellSnapshotIds.Clone(),
                digest = null,
                digestError = string.Empty,
                digestPending = false
            };
            analysisNodes.Add(node);
            currentAnalysisNode = node;
            return node;
        }

        private void StartDigestForNode(AnalysisNodeState node)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.jobId))
                return;
            node.digest = null;
            node.digestError = string.Empty;
            node.digestPending = true;
            if (node == currentAnalysisNode)
            {
                currentDigest = null;
                digestError = string.Empty;
                digestRunning = true;
            }
            VolumeSTCubeS4DAnalysisClient digestClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(digestClient.GenerateDigest(node.jobId,
                (digest, error) => OnDigestComplete(node, digest, error)));
        }

        private void OnDigestComplete(AnalysisNodeState node,
            S4DDigestResult digest, string error)
        {
            if (node == null || FindAnalysisNode(node.nodeId) != node)
                return;
            node.digestPending = false;
            node.digest = digest;
            node.digestError = error ?? string.Empty;
            if (digest != null && !string.IsNullOrWhiteSpace(node.jobId))
                layerDigestCache[node.jobId] = digest;
            if (node == currentAnalysisNode)
            {
                currentDigest = digest;
                digestError = node.digestError;
                digestRunning = false;
                if (stage == Stage.Result)
                    BuildStage();
                // The world-space Facet Grid is normally still open when the
                // asynchronous LLM digest finishes. Refresh its docked summary
                // card as well; otherwise it remains stuck on the initial
                // "select a cell" placeholder until another interaction occurs.
                if (facetGridCanvas != null &&
                    facetGridCanvas.gameObject.activeSelf)
                    BuildFacetGridPanel();
                if (aiFindingsCanvas != null &&
                    aiFindingsCanvas.gameObject.activeSelf)
                    BuildAiFindingsPanel();
                else if (digest != null && stage == Stage.Result)
                    OpenAiFindingsPanel();
            }
            if (trailCanvas != null && trailCanvas.gameObject.activeSelf)
                BuildTrailPanel();
            SetStatus(digest != null
                ? ((digest.generatedBy != null &&
                    digest.generatedBy.StartsWith("llm:",
                        StringComparison.OrdinalIgnoreCase))
                    ? "AI " + Mathf.Max(1, activeGridColumns * activeGridRows) +
                        "-panel summary ready for "
                    : "Evidence-based Findings ready for ") + node.nodeId + "."
                : "Findings fallback active for " + node.nodeId + ": " +
                    node.digestError);
        }

        private static string OperationNodeTitle(DraftOperation operation)
        {
            switch (operation)
            {
                case DraftOperation.Pivot: return "PIVOT VIEW";
                case DraftOperation.Drill: return "DRILLED VIEW";
                case DraftOperation.RollUp: return "ROLLED-UP VIEW";
                default: return "FULL MATRIX";
            }
        }

        private string DraftSelectionSummary(DraftOperation operation)
        {
            if (operation == DraftOperation.Pivot)
                return RoleLabel(roles[0]) + " Time  /  " +
                    RoleLabel(roles[1]) + " Depth";
            if (operation == DraftOperation.Drill)
                return "EXPAND  " + TickSelectionSummary();
            if (operation == DraftOperation.RollUp)
                return "MERGE  " + TickSelectionSummary();
            return FacetAxisSummary().ToUpperInvariant() + "  /  " +
                (selectedDataset != null ? selectedDataset.Name : "dataset");
        }

        private string TickSelectionSummary()
        {
            string time = SelectedDraftBucketLabels(0);
            string depth = SelectedDraftBucketLabels(1);
            if (!string.IsNullOrEmpty(time) && !string.IsNullOrEmpty(depth))
                return time + " + " + depth;
            return !string.IsNullOrEmpty(time) ? time :
                !string.IsNullOrEmpty(depth) ? depth : "no buckets";
        }

        private string SelectedDraftBucketLabels(int dimension)
        {
            bool[] ticks = dimension == 0
                ? selectedTimeTicks : selectedDepthTicks;
            int[] groups = dimension == 0
                ? timeRollupGroups : depthRollupGroups;
            S4DIndexBucketRequest[] buckets = DraftSourceBuckets(dimension);
            List<string> selected = new List<string>();
            int count = DraftBucketCount(dimension);
            for (int index = 0; index < count && index < ticks.Length; index++)
            {
                if (ticks[index])
                {
                    string label = buckets != null && index < buckets.Length
                        ? buckets[index].label : "bucket " + (index + 1);
                    selected.Add(draftOperation == DraftOperation.RollUp &&
                        groups[index] > 0
                            ? "G" + groups[index] + ":" + label
                            : label);
                }
            }
            return string.Join("+", selected.ToArray());
        }

        private bool IsAnalysisNodeTexture(Texture2D texture)
        {
            for (int index = 0; index < analysisNodes.Count; index++)
                if (analysisNodes[index].gridImage == texture)
                    return true;
            return false;
        }

        private void CancelS4DGridJob()
        {
            if (s4dClient != null)
                s4dClient.Cancel();
            SetStatus("Cancelling the S4D Grid job...");
        }

        private void RetryS4DGridJob()
        {
            if (jobRunning)
                return;
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            StartS4DGridJob();
        }

        private void RematerializeS4DGrid()
        {
            Array.Clear(rematerializedCellMask, 0,
                rematerializedCellMask.Length);
            for (int index = 0; index < facetCellStale.Length; index++)
                rematerializedCellMask[index] = facetCellStale[index];
            rematerializingStaleCells =
                s4dGridImage != null &&
                Array.Exists(rematerializedCellMask, value => value);
            gridStale = s4dGridImage != null;
            if (facetGridCanvas != null)
            {
                facetGridCanvas.gameObject.SetActive(true);
                BuildFacetGridPanel();
            }
            SetStatus(rematerializingStaleCells
                ? "Re-materializing a complete replacement Grid; the stale snapshot remains visible until commit..."
                : "Re-materializing the complete Facet Grid...");
            spatialWorkflowStep = SpatialWorkflowStep.Materializing;
            StartS4DGridJob();
        }

        private void SelectMatrixCell(int column, int row)
        {
            selectedTime = matrixTimes[Mathf.Clamp(column, 0,
                matrixTimes.Length - 1)];
            selectedZ = matrixDepths[Mathf.Clamp(row, 0,
                matrixDepths.Length - 1)];
            slabNormalized = selectedDataset.DimZ > 1 ? selectedZ / (float)(selectedDataset.DimZ - 1) : 0.5f;
            ApplyTimeFilter();
            UpdateSlabVisual(false);
            RefreshSlabTexture();
            RebuildTimeMarkers();
            SetStatus("Grounded matrix cell: " + selectedDataset.GetTimeLabel(selectedTime) + ", z=" + selectedZ + ".");
            BuildStage();
        }

        private void ToggleRegionDrawing()
        {
            drawingRegion = !drawingRegion;
            SetStatus(drawingRegion ? "Draw mode: drag directly across the cyan slab." : "Draw mode cancelled.");
            BuildStage();
        }

        private void CenterRegion()
        {
            drawingRegion = false;
            region = new Rect(0.28f, 0.28f, 0.44f, 0.44f);
            UpdateRegionVisual();
            SetStatus("Centered region restored.");
            BuildStage();
        }

        private void UpdateRegionVisual()
        {
            if (regionRoot == null)
                return;
            Vector2 center = region.center;
            float radius = Mathf.Min(region.width, region.height) * 0.5f;
            for (int index = 0; index < regionLines.Length; index++)
            {
                float angleA = Mathf.PI * 2.0f * index / regionLines.Length;
                float angleB = Mathf.PI * 2.0f * (index + 1) / regionLines.Length;
                Vector3 a = new Vector3(
                    (center.x + Mathf.Cos(angleA) * radius - 0.5f) * 1.08f,
                    0.0f,
                    (center.y + Mathf.Sin(angleA) * radius - 0.5f) * 1.08f);
                Vector3 b = new Vector3(
                    (center.x + Mathf.Cos(angleB) * radius - 0.5f) * 1.08f,
                    0.0f,
                    (center.y + Mathf.Sin(angleB) * radius - 0.5f) * 1.08f);
                SetLine(regionLines[index], a, b);
            }
        }

        private void SetPrompt(string value)
        {
            prompt = value;
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            BuildStage();
        }

        private void StartVoiceInput()
        {
            // Editing the next analysis task is independent of an existing
            // materialization/digest job.  The previous guard made both VOICE
            // and TYPE appear clickable while silently ignoring the click.
            Debug.Log("[QuestVoice] VOICE pressed. recording=" +
                questVoiceRecording + " uploading=" + questVoiceUploading +
                " jobRunning=" + jobRunning);
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (questVoiceRecording)
            {
                StopQuestVoiceRecordingAndTranscribe();
                return;
            }
            if (questVoiceUploading)
                return;
#endif
            voiceReviewPending = false;
            voiceInputActive = true;
            textInputActive = false;
            keyboardInputWasVoice = true;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                desktopEditingPrompt = true;
                SetStatus("Voice-input desktop fallback: type in the Game view. " +
                    "Enter or Esc moves the transcript to review.");
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                return;
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
            {
                voiceInputActive = false;
                SetStatus("Microphone permission is required for Quest voice input.");
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);
                if (questVoicePermissionCoroutine != null)
                    StopCoroutine(questVoicePermissionCoroutine);
                questVoicePermissionCoroutine = StartCoroutine(
                    WaitForQuestMicrophonePermission());
                return;
            }
#endif
            OpenQuestSystemVoiceKeyboard();
        }

        private void OpenTextKeyboard()
        {
            Debug.Log("[QuestVoice] TYPE pressed. jobRunning=" + jobRunning);
            voiceReviewPending = false;
            voiceInputActive = false;
            textInputActive = true;
            keyboardInputWasVoice = false;
            vrKeyboardOriginalPrompt = prompt;
            vrKeyboardVisible = true;
            SetStatus("Point and click the VR keyboard. DONE saves the task.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

        private void OpenQuestSystemVoiceKeyboard()
        {
            voiceReviewPending = false;
            voiceInputActive = true;
            textInputActive = false;
            keyboardInputWasVoice = true;
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            StartQuestVoiceRecording();
#else
            desktopEditingPrompt = true;
            SetStatus("Desktop voice fallback: type the transcript, then press Enter.");
#endif
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private void StartQuestVoiceRecording()
        {
            try
            {
                string[] devices = Microphone.devices;
                questVoiceDevice = devices != null && devices.Length > 0
                    ? devices[0] : string.Empty;
                questVoiceClip = Microphone.Start(questVoiceDevice, false, 10, 16000);
                if (questVoiceClip == null)
                    throw new InvalidOperationException("Quest microphone did not start.");
                Debug.Log("[QuestVoice] Microphone.Start succeeded. device='" +
                    questVoiceDevice + "' devices=" +
                    (devices == null ? 0 : devices.Length));
                questVoiceRecording = true;
                questVoiceUploading = false;
                if (questVoiceAutoStopCoroutine != null)
                    StopCoroutine(questVoiceAutoStopCoroutine);
                questVoiceAutoStopCoroutine = StartCoroutine(
                    AutoStopQuestVoiceRecording());
                SetStatus("RECORDING: speak now. Press STOP when finished.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestVoice] Microphone start failed: " + exception);
                questVoiceRecording = false;
                voiceInputActive = false;
                SetStatus("Microphone could not start: " + exception.Message +
                    ". Use TYPE instead.");
            }
        }

        private IEnumerator AutoStopQuestVoiceRecording()
        {
            yield return new WaitForSecondsRealtime(8.0f);
            questVoiceAutoStopCoroutine = null;
            if (questVoiceRecording)
                StopQuestVoiceRecordingAndTranscribe();
        }

        private void StopQuestVoiceRecordingAndTranscribe()
        {
            if (!questVoiceRecording || questVoiceClip == null)
            {
                Debug.LogWarning("[QuestVoice] STOP ignored because no recording is active.");
                return;
            }
            int sampleCount = Mathf.Max(1,
                Microphone.GetPosition(questVoiceDevice));
            Debug.Log("[QuestVoice] STOP pressed. capturedFrames=" + sampleCount);
            Microphone.End(questVoiceDevice);
            questVoiceRecording = false;
            if (questVoiceAutoStopCoroutine != null)
            {
                StopCoroutine(questVoiceAutoStopCoroutine);
                questVoiceAutoStopCoroutine = null;
            }
            byte[] wav = EncodeWav(questVoiceClip, sampleCount);
            Debug.Log("[QuestVoice] WAV encoded. bytes=" + wav.Length);
            Destroy(questVoiceClip);
            questVoiceClip = null;
            questVoiceUploading = true;
            voiceInputActive = true;
            SetStatus("TRANSCRIBING: sending the recording to S4D...");
            BuildIntentPanel();
            VolumeSTCubeS4DAnalysisClient voiceClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(voiceClient.TranscribeAudio(wav,
                OnQuestVoiceTranscribed));
        }

        private void OnQuestVoiceTranscribed(string transcript, string error)
        {
            Debug.Log("[QuestVoice] Transcription completed. transcriptChars=" +
                (transcript == null ? 0 : transcript.Length) + " error='" +
                (error ?? string.Empty) + "'");
            questVoiceUploading = false;
            voiceInputActive = false;
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                prompt = transcript.Trim();
                voiceReviewPending = true;
                intentConfigured = false;
                intentResolutionError = string.Empty;
                PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
                PlayerPrefs.Save();
                SetStatus("Voice transcript ready. CONFIRM it or TYPE to edit.");
            }
            else
            {
                voiceReviewPending = false;
                SetStatus(string.IsNullOrWhiteSpace(error)
                    ? "No speech was recognized. Try again or use TYPE."
                    : error);
            }
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private static byte[] EncodeWav(AudioClip clip, int sampleFrames)
        {
            int channels = Mathf.Max(1, clip.channels);
            int frames = Mathf.Clamp(sampleFrames, 1, clip.samples);
            float[] samples = new float[frames * channels];
            clip.GetData(samples, 0);
            using (MemoryStream stream = new MemoryStream(44 + samples.Length * 2))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int dataLength = samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);
                for (int index = 0; index < samples.Length; index++)
                    writer.Write((short)Mathf.RoundToInt(
                        Mathf.Clamp(samples[index], -1.0f, 1.0f) * 32767.0f));
                writer.Flush();
                return stream.ToArray();
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private IEnumerator WaitForQuestMicrophonePermission()
        {
            float deadline = Time.realtimeSinceStartup + 12.0f;
            while (Time.realtimeSinceStartup < deadline &&
                !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
                yield return null;
            questVoicePermissionCoroutine = null;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
            {
                SetStatus("Microphone permission was not granted. Use the Quest keyboard or type the intent.");
                voiceInputActive = false;
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                yield break;
            }
            OpenQuestSystemVoiceKeyboard();
        }
#endif

        private void UpdateKeyboard()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            UpdateQuestNativeSpeechRecognizer();
#endif
#if UNITY_EDITOR || SLABLAB_FLAT
            if (desktopEditingPrompt)
            {
                bool commit = false;
                string typed = Input.inputString;
                for (int index = 0; index < typed.Length; index++)
                {
                    char character = typed[index];
                    if (character == '\b')
                    {
                        if (prompt.Length > 0)
                            prompt = prompt.Substring(0, prompt.Length - 1);
                    }
                    else if (character == '\n' || character == '\r')
                    {
                        commit = true;
                    }
                    else if (!char.IsControl(character))
                    {
                        prompt += character;
                    }
                }
                if (promptText != null)
                    promptText.text = prompt;
                if (intentPromptText != null)
                    intentPromptText.text = prompt;
                if (Input.GetKeyDown(KeyCode.Escape))
                    commit = true;
                if (!commit)
                    return;

                desktopEditingPrompt = false;
                voiceInputActive = false;
                textInputActive = false;
                voiceReviewPending = true;
                intentConfigured = false;
                intentResolutionError = string.Empty;
                SetStatus(keyboardInputWasVoice
                    ? "Voice transcript ready. Confirm it, record again, or edit by typing."
                    : "Typed task ready. Confirm it, or continue editing.");
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                RefreshIntentSurfaces();
                BuildStage();
                return;
            }
#endif
            if (keyboard == null)
                return;
            prompt = keyboard.text;
            if (promptText != null)
                promptText.text = prompt;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
            if (keyboard.status == TouchScreenKeyboard.Status.Visible)
                return;
            PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
            PlayerPrefs.Save();
            keyboard = null;
            voiceInputActive = false;
            textInputActive = false;
            voiceReviewPending = true;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            SetStatus(keyboardInputWasVoice
                ? "Quest voice transcript ready. Confirm it, record again, or edit by typing."
                : "Typed task ready. Confirm it, or continue editing.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
            BuildStage();
        }

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private sealed class QuestSpeechRecognitionListener : AndroidJavaProxy
        {
            private readonly object gate = new object();
            private string transcript = string.Empty;
            private bool final;
            private int error = -1;
            private bool changed;

            public QuestSpeechRecognitionListener()
                : base("android.speech.RecognitionListener") { }

            public void onReadyForSpeech(AndroidJavaObject parameters) { }
            public void onBeginningOfSpeech() { }
            public void onRmsChanged(float rmsdB) { }
            public void onBufferReceived(byte[] buffer) { }
            public void onEndOfSpeech() { }
            public void onEvent(int eventType, AndroidJavaObject parameters) { }

            public void onError(int nextError)
            {
                lock (gate)
                {
                    error = nextError;
                    final = true;
                    changed = true;
                }
            }

            public void onPartialResults(AndroidJavaObject results)
            {
                StoreResults(results, false);
            }

            public void onResults(AndroidJavaObject results)
            {
                StoreResults(results, true);
            }

            private void StoreResults(AndroidJavaObject results, bool isFinal)
            {
                string value = string.Empty;
                try
                {
                    using (AndroidJavaObject matches =
                        results.Call<AndroidJavaObject>("getStringArrayList",
                            "results_recognition"))
                    {
                        if (matches != null && matches.Call<int>("size") > 0)
                            value = matches.Call<string>("get", 0) ?? string.Empty;
                    }
                }
                catch (Exception) { }
                lock (gate)
                {
                    transcript = value;
                    final = isFinal;
                    error = -1;
                    changed = true;
                }
            }

            public bool TryTake(out string nextTranscript,
                out bool isFinal, out int nextError)
            {
                lock (gate)
                {
                    nextTranscript = transcript;
                    isFinal = final;
                    nextError = error;
                    if (!changed)
                        return false;
                    changed = false;
                    return true;
                }
            }
        }

        private void OpenQuestNativeSpeechRecognizer()
        {
            try
            {
                using (AndroidJavaClass unityPlayer =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity"))
                using (AndroidJavaClass speechClass =
                    new AndroidJavaClass("android.speech.SpeechRecognizer"))
                {
                    if (!speechClass.CallStatic<bool>("isRecognitionAvailable", activity))
                    {
                        voiceInputActive = false;
                        SetStatus("Quest speech service is unavailable. Use TYPE instead.");
                        return;
                    }
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        using (AndroidJavaClass callbackUnityPlayer =
                            new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (AndroidJavaObject callbackActivity =
                            callbackUnityPlayer.GetStatic<AndroidJavaObject>(
                                "currentActivity"))
                        using (AndroidJavaClass callbackSpeechClass =
                            new AndroidJavaClass("android.speech.SpeechRecognizer"))
                        {
                            DestroyQuestNativeSpeechRecognizer();
                            questSpeechListener = new QuestSpeechRecognitionListener();
                            questSpeechRecognizer =
                                callbackSpeechClass.CallStatic<AndroidJavaObject>(
                                    "createSpeechRecognizer", callbackActivity);
                            questSpeechRecognizer.Call("setRecognitionListener",
                                questSpeechListener);
                            using (AndroidJavaObject recognizerIntent =
                                new AndroidJavaObject("android.content.Intent",
                                    "android.speech.action.RECOGNIZE_SPEECH"))
                            {
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.LANGUAGE_MODEL", "free_form");
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.PARTIAL_RESULTS", true);
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.MAX_RESULTS", 3);
                                questSpeechRecognizer.Call("startListening",
                                    recognizerIntent);
                            }
                        }
                    }));
                }
                SetStatus("Listening... Speak the analysis task now.");
            }
            catch (Exception exception)
            {
                voiceInputActive = false;
                SetStatus("Quest speech could not start: " + exception.Message +
                    ". Use TYPE instead.");
            }
        }

        private void UpdateQuestNativeSpeechRecognizer()
        {
            if (questSpeechListener == null ||
                !questSpeechListener.TryTake(out string transcript,
                    out bool isFinal, out int error))
                return;
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                prompt = transcript.Trim();
                if (intentPromptText != null)
                    intentPromptText.text = prompt;
            }
            if (!isFinal)
                return;

            voiceInputActive = false;
            voiceReviewPending = error < 0 && !string.IsNullOrWhiteSpace(prompt);
            intentConfigured = false;
            intentResolutionError = string.Empty;
            if (voiceReviewPending)
            {
                PlayerPrefs.SetString("VolumeSTCube.Quest.SpatialPrompt", prompt);
                PlayerPrefs.Save();
                SetStatus("Voice transcript ready. Confirm, record again, or TYPE to edit.");
            }
            else
                SetStatus(QuestSpeechErrorMessage(error));
            DestroyQuestNativeSpeechRecognizer();
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private static string QuestSpeechErrorMessage(int error)
        {
            if (error == 7)
                return "No speech was recognized. Move closer and try VOICE again.";
            if (error == 9)
                return "Microphone permission was denied. Enable it or use TYPE.";
            if (error == 1 || error == 2)
                return "Speech service network error. Check Wi-Fi or use TYPE.";
            return "Speech recognition stopped (error " + error + "). Try again or use TYPE.";
        }

        private void DestroyQuestNativeSpeechRecognizer()
        {
            if (questSpeechRecognizer == null)
                return;
            try
            {
                questSpeechRecognizer.Call("cancel");
                questSpeechRecognizer.Call("destroy");
                questSpeechRecognizer.Dispose();
            }
            catch (Exception) { }
            questSpeechRecognizer = null;
            questSpeechListener = null;
        }
#endif

        private void StartMatPlotJob()
        {
            if (selectedDataset == null || jobRunning || string.IsNullOrWhiteSpace(prompt))
                return;
            string csv;
            try
            {
                string output = Path.Combine(Application.temporaryCachePath, "VolumeSTCubeSpatial");
                csv = VolumeSTCubeRawSliceReader.ExportRegionCsv(selectedDataset, selectedTime, selectedZ, output, region);
            }
            catch (Exception exception)
            {
                SetStatus("Region export failed: " + exception.Message);
                return;
            }

            jobRunning = true;
            progress = 0.0f;
            BuildStage();
            string contextualPrompt = prompt.Trim() +
                "\n\nThis CSV is a grounded XY slab from a continuous XYZ+T field." +
                " Columns are x, y, value, region. Region is either selected or rest." +
                " Variable: " + selectedDataset.Name + ". Time: " + selectedDataset.GetTimeLabel(selectedTime) +
                ". Z layer: " + selectedZ + " of " + selectedDataset.DimZ + "." +
                " Clearly distinguish selected from rest and do not invent physical units.";
            VolumeSTCubeMatPlotClient client = new VolumeSTCubeMatPlotClient(matPlotUrl, 180);
            StartCoroutine(client.Run(contextualPrompt, csv, OnJobProgress, OnJobComplete));
        }

        private void OnJobProgress(string message, float value)
        {
            progress = value;
            SetStatus(message + " (" + Mathf.RoundToInt(value * 100) + "%)");
        }

        private void OnJobComplete(VolumeSTCubeMatPlotResult result)
        {
            jobRunning = false;
            if (result == null || !result.Succeeded)
            {
                SetStatus(result != null ? result.Error : "MatPlotAgent returned no result.");
                BuildStage();
                return;
            }
            ClearChart();
            chartImage = result.Image;
            stage = Stage.Result;
            SetStatus("Verified result ready. Job " + result.JobId + ".");
            BuildStage();
        }

        private void FrameVolume()
        {
            Transform volumeRoot = currentView != null && currentView.rootObject != null
                ? currentView.rootObject.transform
                : null;
            if (volumeRoot == null)
            {
                VolumeControllerObject controller = FindObjectOfType<VolumeControllerObject>();
                volumeRoot = controller != null ? controller.transform : null;
            }
            if (volumeRoot == null || spatialRoot == null)
                return;

            // The field, its semantic axes and every authoring cut must share one
            // transform.  Keeping the imported renderer as a loose world object
            // made controller rotation move the axes while the data stayed behind.
            if (volumeRoot.parent != spatialRoot.transform)
                volumeRoot.SetParent(spatialRoot.transform, true);
            volumeRoot.position = spatialRoot.transform.position;
            volumeRoot.rotation = spatialRoot.transform.rotation;
            volumeRoot.localRotation = boundaryAuthoringCanonicalView
                ? Quaternion.identity : fieldAxisRemapRotation;
            volumeRoot.localScale = Vector3.one * 0.105f;

            Renderer[] renderers = volumeRoot.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds combined = new Bounds(volumeRoot.position, Vector3.zero);
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                // The volume is an emissive analytical surface. Shadows and
                // dynamic occlusion can produce large black cards on Quest when
                // its thin transparent layers are viewed edge-on.
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.sortingOrder = -100;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
            if (!hasBounds)
                return;

            Vector3 size = combined.size;
            float fit = Mathf.Min(
                (FieldHalfWidth * 1.64f) / Mathf.Max(0.0001f, size.x),
                Mathf.Min(
                    (FieldHalfHeight * 1.54f) / Mathf.Max(0.0001f, size.y),
                    (FieldHalfDepth * 1.64f) / Mathf.Max(0.0001f, size.z)));
            volumeRoot.localScale *= Mathf.Clamp(fit, 0.05f, 6.0f);

            hasBounds = false;
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null || !renderer.enabled)
                    continue;
                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }
            if (hasBounds)
            {
                // The source ocean layers are physically very thin. Preserve X/Z fit,
                // but exaggerate the display height so depth cuts remain legible in VR.
                float targetHeight = FieldHalfHeight * 1.48f;
                float stretch = Mathf.Clamp(
                    targetHeight / Mathf.Max(0.0001f, combined.size.y),
                    1.0f, FieldVerticalExaggeration);
                Vector3 stretchedScale = volumeRoot.localScale;
                stretchedScale.y *= stretch;
                volumeRoot.localScale = stretchedScale;

                hasBounds = false;
                for (int index = 0; index < renderers.Length; index++)
                {
                    Renderer renderer = renderers[index];
                    if (renderer == null || !renderer.enabled)
                        continue;
                    if (!hasBounds)
                    {
                        combined = renderer.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(renderer.bounds);
                    }
                }
            }
            if (hasBounds)
            {
                // Axis remapping can rotate the locally exaggerated dimension into
                // world X or Z. Refit once more after exaggeration so no variable
                // can escape the Continuous Field wire cube.
                Vector3 stretchedBounds = combined.size;
                float containment = Mathf.Min(
                    (FieldHalfWidth * 1.60f) /
                        Mathf.Max(0.0001f, stretchedBounds.x),
                    Mathf.Min(
                        (FieldHalfHeight * 1.50f) /
                            Mathf.Max(0.0001f, stretchedBounds.y),
                        (FieldHalfDepth * 1.60f) /
                            Mathf.Max(0.0001f, stretchedBounds.z)));
                if (containment < 1.0f)
                {
                    volumeRoot.localScale *= Mathf.Clamp(containment, 0.05f, 1.0f);
                    hasBounds = false;
                    for (int index = 0; index < renderers.Length; index++)
                    {
                        Renderer renderer = renderers[index];
                        if (renderer == null || !renderer.enabled)
                            continue;
                        if (!hasBounds)
                        {
                            combined = renderer.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            combined.Encapsulate(renderer.bounds);
                        }
                    }
                }
            }
            // Renderer.bounds is world-axis aligned, while the Field frame can be
            // rotated. Fit and centre once in the frame's own coordinates so the
            // visible data cannot cross any of the six Field faces.
            if (TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                out Bounds localBounds))
            {
                float localContainment = Mathf.Min(
                    (FieldHalfWidth * 1.20f) / Mathf.Max(0.0001f, localBounds.size.x),
                    Mathf.Min(
                        (FieldHalfHeight * 1.20f) / Mathf.Max(0.0001f, localBounds.size.y),
                        (FieldHalfDepth * 1.20f) / Mathf.Max(0.0001f, localBounds.size.z)));
                if (localContainment < 1.0f)
                {
                    volumeRoot.localScale *= Mathf.Clamp(localContainment, 0.05f, 1.0f);
                    TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                        out localBounds);
                }

                volumeRoot.localPosition -= localBounds.center;
                if (TryGetLocalRendererBounds(spatialRoot.transform, renderers,
                    out localBounds))
                {
                    volumeLocalMinY = Mathf.Clamp(localBounds.min.y,
                        -FieldHalfHeight * 0.90f, FieldHalfHeight * 0.78f);
                    volumeLocalMaxY = Mathf.Clamp(localBounds.max.y,
                        volumeLocalMinY + 0.12f, FieldHalfHeight * 0.90f);
                    UpdateDepthBoundaryPlanes();
                }
            }
        }

        private static bool TryGetLocalRendererBounds(
            Transform frame, Renderer[] renderers, out Bounds localBounds)
        {
            localBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool found = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer == null || !renderer.enabled)
                    continue;
                Bounds worldBounds = renderer.bounds;
                Vector3 minimum = worldBounds.min;
                Vector3 maximum = worldBounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 worldCorner = new Vector3(
                        (corner & 1) == 0 ? minimum.x : maximum.x,
                        (corner & 2) == 0 ? minimum.y : maximum.y,
                        (corner & 4) == 0 ? minimum.z : maximum.z);
                    Vector3 localCorner = frame.InverseTransformPoint(worldCorner);
                    if (!found)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }
            return found;
        }

        private System.Collections.IEnumerator RefitVolumeAfterFrameChange()
        {
            yield return null;
            yield return null;
            FrameVolume();
        }

        private static void HideLegacyAxis()
        {
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour != null && behaviour.GetType().Name == "AxisContainer")
                    behaviour.gameObject.SetActive(false);
            }
        }

        private static void HideInitialSceneVolumes()
        {
            VolumeControllerObject[] controllers = FindObjectsOfType<VolumeControllerObject>();
            for (int i = 0; i < controllers.Length; i++)
            {
                VolumeControllerObject controller = controllers[i];
                if (controller == null)
                    continue;
                HashSet<VolumeDataset> released = new HashSet<VolumeDataset>();
                VolumeRenderedObject[] volumes =
                    controller.GetComponentsInChildren<VolumeRenderedObject>(true);
                for (int volumeIndex = 0; volumeIndex < volumes.Length; volumeIndex++)
                {
                    VolumeDataset dataset = volumes[volumeIndex] != null
                        ? volumes[volumeIndex].dataset : null;
                    if (dataset == null || !released.Add(dataset))
                        continue;
                    dataset.ReleaseRuntimeTextures();
                    Destroy(dataset);
                }
                VolumeSTCubeOriginalSceneAdapter.ClearExistingVolumes(controller);
                // The controller is reused when a variable is selected. Keeping it
                // active but empty avoids retaining the authored demo volume while
                // still allowing the runtime loader to attach the chosen dataset.
                controller.gameObject.SetActive(true);
                controller.SetLightingEnabled(false);
            }
        }

        private Button CreateButton(RectTransform parent, string label, Vector2 position, Vector2 size, Color color, Action action)
        {
            GameObject obj = new GameObject(label, typeof(RectTransform));
            obj.layer = 5;
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = obj.AddComponent<Image>();
            image.sprite = RoundedUiSprite();
            image.type = Image.Type.Sliced;
            bool neutral = IsNeutralControlColor(color);
            Color buttonFill = ThemedButtonFill(color);
            image.color = buttonFill;
            Outline outline = obj.AddComponent<Outline>();
            outline.effectColor = new Color(
                Mathf.Min(1.0f, color.r * 1.18f + 0.08f),
                Mathf.Min(1.0f, color.g * 1.18f + 0.08f),
                Mathf.Min(1.0f, color.b * 1.18f + 0.08f),
                neutral ? 0.34f : 0.70f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            Shadow shadow = obj.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.52f);
            shadow.effectDistance = new Vector2(3.0f, -4.0f);
            Button button = obj.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.13f, 1.13f, 1.13f, 1.0f);
            colors.pressedColor = new Color(0.68f, 0.78f, 0.84f, 1.0f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.38f, 0.44f, 0.48f, 0.72f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            GameObject accentObject = new GameObject("Button accent", typeof(RectTransform));
            accentObject.transform.SetParent(rect, false);
            RectTransform accentRect = accentObject.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0, 0);
            accentRect.anchorMax = new Vector2(1, 0);
            accentRect.pivot = new Vector2(0.5f, 0);
            accentRect.anchoredPosition = new Vector2(0, 1);
            accentRect.sizeDelta = new Vector2(-6, neutral ? 2 : 4);
            Image accentImage = accentObject.AddComponent<Image>();
            accentImage.color = neutral
                ? new Color(Muted.r, Muted.g, Muted.b, 0.34f)
                : new Color(color.r, color.g, color.b, 0.94f);
            accentImage.raycastTarget = false;

            Color labelColor = IdealButtonLabel(buttonFill);
            Text buttonLabel = CreateText(rect, label,
                Mathf.RoundToInt(Mathf.Clamp(size.y * 0.31f, 14, 20)),
                FontStyle.Bold, new Vector2(0, 1),
                size - new Vector2(28, 14), TextAnchor.MiddleCenter,
                labelColor);
            buttonLabel.raycastTarget = false;
            buttonLabel.resizeTextForBestFit = true;
            buttonLabel.resizeTextMinSize = Mathf.RoundToInt(
                12 * ActiveUiFontScale);
            buttonLabel.resizeTextMaxSize = buttonLabel.fontSize;
            buttonLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            buttonLabel.verticalOverflow = VerticalWrapMode.Truncate;
            buttonLabel.lineSpacing = 0.94f;
            BoxCollider collider = obj.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 12);
            VolumeSTCubeQuestClickTarget target = obj.AddComponent<VolumeSTCubeQuestClickTarget>();
            target.Clicked = () => InvokeButtonWithoutInterruptingPlayback(action);
            return button;
        }

        private void InvokeButtonWithoutInterruptingPlayback(Action action)
        {
            VolumeSTCubeForVrSurfacePlayer playerBefore = forVrSurfacePlayer;
            bool wasPlaying = playerBefore != null && playerBefore.IsPlaying;
            action?.Invoke();
            // Normal workflow buttons may rebuild panels, boundaries and
            // previews, but they do not own the independent surface player.
            // If the same player survived the action, preserve its running
            // state and current frame. START/PAUSE lives in the player itself
            // and therefore remains the sole playback toggle.
            if (wasPlaying && playerBefore != null &&
                playerBefore == forVrSurfacePlayer)
                playerBefore.EnsurePlaybackContinues();
        }

        private static bool IsNeutralControlColor(Color color)
        {
            return Mathf.Abs(color.r - Card.r) < 0.02f &&
                Mathf.Abs(color.g - Card.g) < 0.02f &&
                Mathf.Abs(color.b - Card.b) < 0.02f;
        }

        private static Color ThemedButtonFill(Color color)
        {
            Color fill = IsNeutralControlColor(color)
                ? Color.Lerp(Panel, Card, 0.86f)
                : Color.Lerp(Card, color, 0.76f);
            fill.a = 1.0f;
            return fill;
        }

        private static Color IdealButtonLabel(Color fill)
        {
            float luminance = fill.r * 0.2126f + fill.g * 0.7152f +
                fill.b * 0.0722f;
            return luminance > 0.52f
                ? new Color(0.012f, 0.027f, 0.042f, 1.0f)
                : Ink;
        }

        private void CreateTextureCard(RectTransform parent, Texture texture, string label, Vector2 position, Vector2 size,
            Color color, Action action)
        {
            GameObject cardObject = new GameObject(label, typeof(RectTransform));
            cardObject.layer = 5;
            cardObject.transform.SetParent(parent, false);
            RectTransform card = cardObject.GetComponent<RectTransform>();
            card.sizeDelta = size;
            card.anchoredPosition = position;
            Image cardBackground = cardObject.AddComponent<Image>();
            cardBackground.sprite = RoundedUiSprite();
            cardBackground.type = Image.Type.Sliced;
            cardBackground.color = Color.Lerp(Card, color, 0.45f);
            CreateRawImage(card, texture, new Vector2(0, 8), new Vector2(size.x - 12, size.y - 30));
            CreateText(card, label, 14, FontStyle.Bold, new Vector2(0, -size.y * 0.4f),
                new Vector2(size.x - 8, 18), TextAnchor.MiddleCenter, Ink);
            BoxCollider collider = cardObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(size.x, size.y, 12);
            cardObject.AddComponent<VolumeSTCubeQuestClickTarget>().Clicked = action;
        }

        private RawImage CreateRawImage(RectTransform parent, Texture texture, Vector2 position, Vector2 size)
        {
            GameObject frameObject = new GameObject("Data image frame", typeof(RectTransform));
            frameObject.transform.SetParent(parent, false);
            RectTransform frame = frameObject.GetComponent<RectTransform>();
            frame.sizeDelta = size;
            frame.anchoredPosition = position;
            Image frameImage = frameObject.AddComponent<Image>();
            frameImage.sprite = RoundedUiSprite();
            frameImage.type = Image.Type.Sliced;
            frameImage.color = new Color(0.004f, 0.012f, 0.020f, 0.96f);
            frameImage.raycastTarget = false;
            Outline frameOutline = frameObject.AddComponent<Outline>();
            frameOutline.effectColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.28f);
            frameOutline.effectDistance = new Vector2(1.5f, -1.5f);

            GameObject obj = new GameObject("Data image", typeof(RectTransform));
            obj.transform.SetParent(frame, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size - new Vector2(10, 10);
            rect.anchoredPosition = Vector2.zero;
            RawImage image = obj.AddComponent<RawImage>();
            image.texture = texture;
            image.color = Color.white;
            image.raycastTarget = false;
            if (texture != null)
            {
                AspectRatioFitter fitter = obj.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = texture.width / (float)Mathf.Max(1, texture.height);
            }
            return image;
        }

        private Text CreateTextBox(RectTransform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject obj = new GameObject("Question", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            obj.AddComponent<Image>().color = new Color(0.008f, 0.017f, 0.028f, 1.0f);
            return CreateText(rect, value, fontSize, FontStyle.Normal, Vector2.zero,
                size - new Vector2(24, 18), TextAnchor.MiddleLeft, Ink);
        }

        private Text CreateText(RectTransform parent, string value, int fontSize, FontStyle style,
            Vector2 position, Vector2 size, TextAnchor anchor, Color color)
        {
            GameObject obj = new GameObject("Text", typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            RectTransform rect = obj.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = obj.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = Mathf.RoundToInt(fontSize * ActiveUiFontScale);
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color;
            text.lineSpacing = 1.0f;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                // Start from the largest requested desktop size, then let each
                // label shrink only as far as necessary to remain inside its
                // own RectTransform. This prevents both overlap and clipping
                // while avoiding uniformly tiny late-workflow panels.
                text.resizeTextForBestFit = true;
                int desktopMinimum = Mathf.RoundToInt(
                    Mathf.Max(12.0f, fontSize * 0.62f) *
                    ActiveUiFontScale);
                text.resizeTextMinSize = Mathf.Min(text.fontSize,
                    desktopMinimum);
                text.resizeTextMaxSize = text.fontSize;
            }
            else
#endif
            if (fontSize <= 18 && (value.Length > 28 || value.IndexOf('\n') >= 0))
            {
                text.resizeTextForBestFit = true;
                text.resizeTextMinSize = Mathf.RoundToInt(
                    11 * ActiveUiFontScale);
                text.resizeTextMaxSize = text.fontSize;
            }
            text.raycastTarget = false;
            if (fontSize >= 24)
            {
                Shadow titleShadow = obj.AddComponent<Shadow>();
                titleShadow.effectColor = new Color(0.0f, 0.0f, 0.0f, 0.72f);
                titleShadow.effectDistance = new Vector2(2.0f, -2.0f);
            }
            return text;
        }

        private void UpgradeButtonLabelToCrispText(Button button, string value)
        {
            if (button == null)
                return;
            Text legacy = button.GetComponentInChildren<Text>(true);
            if (legacy == null)
                return;
            bool multiline = value.IndexOf('\n') >= 0;
            AddCrispTextOverlay(legacy, value,
                multiline ? 38.0f : 56.0f,
                multiline ? 22.0f : 26.0f,
                true);
        }

        private void UpgradeCanvasLabelsToCrispText(RectTransform root,
            string primaryValue)
        {
            if (root == null)
                return;
            Text[] labels = root.GetComponentsInChildren<Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                Text legacy = labels[index];
                if (legacy == null || !legacy.enabled)
                    continue;
                bool primary = !string.IsNullOrEmpty(primaryValue) &&
                    string.Equals(legacy.text, primaryValue,
                        StringComparison.Ordinal);
                Button ownerButton = legacy.GetComponentInParent<Button>();
                float height = Mathf.Max(18.0f,
                    legacy.rectTransform.rect.height);
                float maximum = primary
                    ? 64.0f
                    : ownerButton != null
                        ? Mathf.Clamp(height * 0.84f, 30.0f, 58.0f)
                        : Mathf.Clamp(height * 0.88f, 22.0f, 58.0f);
                float minimum = primary ? 38.0f :
                    ownerButton != null ? 18.0f : 14.0f;
                AddCrispTextOverlay(legacy, legacy.text, maximum, minimum,
                    ownerButton != null || primary);
            }
        }

        private TMPro.TextMeshProUGUI AddCrispTextOverlay(Text legacy, string value,
            float maximumSize, float minimumSize, bool bold)
        {
            if (legacy == null || legacy.transform.parent == null)
                return null;
            legacy.enabled = false;

            GameObject labelObject = new GameObject("Crisp SDF label",
                typeof(RectTransform));
            labelObject.layer = legacy.gameObject.layer;
            labelObject.transform.SetParent(legacy.rectTransform, false);
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(3.0f, 2.0f);
            rect.offsetMax = new Vector2(-3.0f, -2.0f);

            TMPro.TextMeshProUGUI text =
                labelObject.AddComponent<TMPro.TextMeshProUGUI>();
            if (crispFontAsset != null)
                text.font = crispFontAsset;
            else if (TMPro.TMP_Settings.defaultFontAsset != null)
                text.font = TMPro.TMP_Settings.defaultFontAsset;
            text.text = value;
            text.color = legacy.color;
            text.fontStyle = bold || legacy.fontStyle == FontStyle.Bold
                ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
            text.alignment = ToTmpAlignment(legacy.alignment);
            text.enableWordWrapping = value.IndexOf('\n') >= 0;
            text.overflowMode = TMPro.TextOverflowModes.Truncate;
            text.enableAutoSizing = true;
            text.fontSizeMin = Mathf.Min(minimumSize, maximumSize);
            text.fontSizeMax = maximumSize;
            text.lineSpacing = value.IndexOf('\n') >= 0 ? -18.0f : 0.0f;
            text.raycastTarget = false;
            return text;
        }

        private static TMPro.TextAlignmentOptions ToTmpAlignment(
            TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft:
                    return TMPro.TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter:
                    return TMPro.TextAlignmentOptions.Top;
                case TextAnchor.UpperRight:
                    return TMPro.TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft:
                    return TMPro.TextAlignmentOptions.Left;
                case TextAnchor.MiddleRight:
                    return TMPro.TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft:
                    return TMPro.TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter:
                    return TMPro.TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight:
                    return TMPro.TextAlignmentOptions.BottomRight;
                default:
                    return TMPro.TextAlignmentOptions.Center;
            }
        }

        private void ApplyAlwaysVisiblePanelMaterials()
        {
            // Canvas sorting is sufficient for normal panels. Applying one
            // generic always-on-top material to both panel backgrounds and
            // dynamic-font Text components broke the font atlas; applying it
            // only to backgrounds made those backgrounds cover the text. Keep
            // ordinary panels on Unity's normal UI material path.
            Canvas[] canvases =
            {
                panelCanvas, mainMenuCanvas, boundaryCanvas, trailCanvas,
                facetGridCanvas, aiFindingsCanvas, slabPreviewCanvas,
                intentCanvas, draftCanvas
            };
            for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
            {
                Canvas canvas = canvases[canvasIndex];
                if (canvas == null)
                    continue;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 30000 + canvasIndex;
            }
        }

        private void EnsureDragCardMaterials()
        {
            if (uiAlwaysVisibleMaterial == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader != null)
                {
                    uiAlwaysVisibleMaterial = new Material(shader);
                    uiAlwaysVisibleMaterial.name =
                        "Variable drag card always visible";
                    ConfigureAlwaysVisibleMaterial(uiAlwaysVisibleMaterial,
                        5000);
                }
            }

            Font targetFont = font != null ? font :
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (targetFont == null || targetFont.material == null)
                return;
            targetFont.RequestCharactersInTexture(
                "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-", 32,
                FontStyle.Bold);
            if (uiAlwaysVisibleFontMaterial == null)
            {
                uiAlwaysVisibleFontMaterial = new Material(targetFont.material);
                uiAlwaysVisibleFontMaterial.name =
                    "Variable drag font always visible";
                ConfigureAlwaysVisibleMaterial(uiAlwaysVisibleFontMaterial,
                    5001);
            }
            uiAlwaysVisibleFontMaterial.mainTexture =
                targetFont.material.mainTexture;
        }

        private static void ConfigureAlwaysVisibleMaterial(Material material,
            int renderQueue)
        {
            if (material == null)
                return;
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest",
                (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("unity_GUIZTestMode",
                (int)UnityEngine.Rendering.CompareFunction.Always);
            material.renderQueue = renderQueue;
        }

        private LineRenderer CreateWorldLine(string name, Transform parent, Vector3 a, Vector3 b, Color color, float width)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            LineRenderer line = obj.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
            line.startWidth = line.endWidth = width;
            line.numCapVertices = 10;
            line.numCornerVertices = 10;
            line.startColor = line.endColor = color;
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = color;
            line.material = material;
            return line;
        }

        private TextMesh CreateWorldLabel(string value, Vector3 localPosition, float characterSize,
            TextAnchor anchor, Color color, Transform customParent = null)
        {
            GameObject obj = new GameObject(value);
            obj.transform.SetParent(customParent != null ? customParent : spatialRoot.transform, false);
            obj.transform.localPosition = localPosition;
            obj.transform.localRotation = Quaternion.identity;
            TextMesh text = obj.AddComponent<TextMesh>();
            // TextMesh requires a matching legacy font material. Keep spatial
            // axis labels on Unity's proven built-in face; Poppins remains the
            // shared SDF/UGUI face for panels, buttons, and MatPlot output.
            text.font = worldFont != null
                ? worldFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = value;
            // Rasterize well above the final physical size so small axis and
            // variable captions remain sharp instead of becoming a few blurred
            // pixels in the desktop Game view.
            float readableScale = 1.0f;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                readableScale = characterSize <= 0.010f ? 1.70f : 1.18f;
#endif
            text.fontSize = 256;
            text.characterSize = characterSize * readableScale * 0.25f;
            text.anchor = anchor;
            text.alignment = TextAlignment.Center;
            text.color = color;
            return text;
        }

        private static void SetLine(LineRenderer line, Vector3 a, Vector3 b)
        {
            if (line == null)
                return;
            line.SetPosition(0, a);
            line.SetPosition(1, b);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
            if (statusCrispText != null)
                statusCrispText.text = message;
            Debug.Log("VolumeSTCube Slab Lab: " + message);
        }

        private void ClearChart()
        {
            if (chartImage != null)
                Destroy(chartImage);
            chartImage = null;
            progress = 0;
        }

        private void ClearS4DGrid()
        {
            DestroyGroundAggregateVolume();
            DestroyTextures(streamingCellTextures);
            if (s4dGridImage != null && !IsAnalysisNodeTexture(s4dGridImage))
                Destroy(s4dGridImage);
            s4dGridImage = null;
            s4dChartResultJson = string.Empty;
            s4dSnapshotId = string.Empty;
            s4dJobId = string.Empty;
            currentDigest = null;
            digestError = string.Empty;
            digestRunning = false;
            Array.Clear(facetCellSnapshotIds, 0,
                facetCellSnapshotIds.Length);
            activeTimeBuckets = null;
            activeDepthBuckets = null;
            activeGridColumns = 3;
            activeGridRows = 3;
            activeGridTransposed = false;
            pivotTransposed = false;
        }

        private void ClearAnalysisHistory()
        {
            DestroyGroundAggregateVolume();
            ClearRetainedResultViews();
            for (int index = 0; index < analysisNodes.Count; index++)
            {
                Texture2D texture = analysisNodes[index].gridImage;
                if (texture != null)
                    Destroy(texture);
            }
            analysisNodes.Clear();
            trailEvents.Clear();
            nextTrailEventSequence = 1;
            currentAnalysisNode = null;
            nextAnalysisNodeNumber = 1;
            s4dGridImage = null;
            s4dChartResultJson = string.Empty;
            s4dSnapshotId = string.Empty;
            s4dJobId = string.Empty;
            currentDigest = null;
            digestError = string.Empty;
            digestRunning = false;
            Array.Clear(facetCellSnapshotIds, 0,
                facetCellSnapshotIds.Length);
            activeTimeBuckets = null;
            activeDepthBuckets = null;
            activeGridColumns = 3;
            activeGridRows = 3;
            activeGridTransposed = false;
            pivotTransposed = false;
            draftSourceNodeId = string.Empty;
            draftOperation = DraftOperation.None;
        }

        private static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.name == "Persistent panel chrome")
                    continue;
                // Destroy is deferred until the end of the frame. Disable the
                // old control immediately so a rebuilt panel cannot be covered
                // or clicked through its previous buttons for one frame.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private static void DestroyTextures(Texture2D[] textures)
        {
            if (textures == null)
                return;
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] != null)
                    Destroy(textures[i]);
                textures[i] = null;
            }
        }

        private void BuildSharedColorScale(RectTransform parent)
        {
            if (sharedColorbarTexture == null)
            {
                sharedColorbarTexture = new Texture2D(
                    12, 128, TextureFormat.RGBA32, false);
                sharedColorbarTexture.name = "S4D Shared Viridis Scale";
                for (int y = 0; y < sharedColorbarTexture.height; y++)
                {
                    float t = y / (float)(sharedColorbarTexture.height - 1);
                    Color color = t < 0.5f
                        ? Color.Lerp(
                            new Color(0.267f, 0.005f, 0.329f),
                            new Color(0.128f, 0.567f, 0.551f), t * 2.0f)
                        : Color.Lerp(
                            new Color(0.128f, 0.567f, 0.551f),
                            new Color(0.993f, 0.906f, 0.144f),
                            (t - 0.5f) * 2.0f);
                    for (int x = 0; x < sharedColorbarTexture.width; x++)
                        sharedColorbarTexture.SetPixel(x, y, color);
                }
                sharedColorbarTexture.Apply(false, false);
            }
            CreateText(parent, "SHARED", 10, FontStyle.Bold,
                new Vector2(270, 230), new Vector2(100, 18),
                TextAnchor.MiddleCenter, Muted);
            CreateRawImage(parent, sharedColorbarTexture,
                new Vector2(250, 10), new Vector2(22, 390));
            CreateText(parent, s4dSharedMaximum.ToString("0.###"),
                10, FontStyle.Bold, new Vector2(310, 196),
                new Vector2(88, 20), TextAnchor.MiddleLeft, Ink);
            CreateText(parent, s4dSharedMinimum.ToString("0.###"),
                10, FontStyle.Bold, new Vector2(310, -176),
                new Vector2(88, 20), TextAnchor.MiddleLeft, Ink);
            CreateText(parent, string.IsNullOrWhiteSpace(s4dSharedUnit)
                    ? "value" : s4dSharedUnit,
                10, FontStyle.Normal, new Vector2(310, 10),
                new Vector2(88, 54), TextAnchor.MiddleLeft, Muted);
        }

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (questVoiceRecording)
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
    }
}
