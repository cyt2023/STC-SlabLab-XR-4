using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using OculusTouchController = UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile.OculusTouchController;

namespace UnityVolumeRendering
{
    /// <summary>Copies an OpenXR node pose onto a transform without XR Interaction Toolkit.</summary>
    public sealed class VolumeSTCubeXRTrackedNode : MonoBehaviour
    {
        public XRNode node = XRNode.Head;
        private InputDevice device;

        private void OnEnable()
        {
            Application.onBeforeRender += UpdatePose;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= UpdatePose;
        }

        private void Update()
        {
            UpdatePose();
        }

        private void UpdatePose()
        {
            if (!device.isValid)
                device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return;

            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position))
                transform.localPosition = position;
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
                transform.localRotation = rotation;
        }
    }

    /// <summary>Physics-backed target used by the Quest controller laser.</summary>
    public sealed class VolumeSTCubeQuestClickTarget : MonoBehaviour
    {
        public Action Clicked;
        private Graphic graphic;
        private Color normalColor;
        private Outline outline;
        private Color normalOutlineColor;
        private Vector2 normalOutlineDistance;

        private void Awake()
        {
            graphic = GetComponent<Graphic>();
            if (graphic != null)
                normalColor = graphic.color;
            outline = GetComponent<Outline>();
            if (outline != null)
            {
                normalOutlineColor = outline.effectColor;
                normalOutlineDistance = outline.effectDistance;
            }
        }

        public void SetHovered(bool hovered)
        {
            if (graphic == null)
                return;
            // Physics-backed world-space buttons are not driven by Unity's
            // EventSystem.  Mutating their fill here while Button also owns the
            // target Graphic made the palette alternate cyan/purple on Quest.
            // Keep the fill stable and use one outline-only hover indication.
            if (GetComponent<Button>() != null)
            {
                graphic.color = normalColor;
                if (outline != null)
                {
                    outline.effectColor = hovered
                        ? new Color(0.22f, 0.92f, 1.0f, 1.0f)
                        : normalOutlineColor;
                    outline.effectDistance = hovered
                        ? new Vector2(3.0f, -3.0f)
                        : normalOutlineDistance;
                }
                return;
            }
            graphic.color = hovered
                ? Color.Lerp(normalColor,
                    new Color(0.18f, 0.82f, 1.0f, normalColor.a), 0.28f)
                : normalColor;
        }

        public void Invoke()
        {
            Clicked?.Invoke();
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        private void OnMouseDown()
        {
            // World-space controls primarily use Quest physics rays. Unity's
            // GraphicRaycaster is unreliable on a steep perspective canvas,
            // so desktop preview clicks use the same collider-backed path.
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
                return;
            Invoke();
        }
#endif
    }

    /// <summary>Marks a complete world-space panel as grip-movable.</summary>
    public sealed class VolumeSTCubeQuestPanelHandle : MonoBehaviour
    {
        public Color accent = Color.cyan;
    }

    /// <summary>Right Touch controller laser and trigger click implementation.</summary>
    public sealed class VolumeSTCubeQuestRayInteractor : MonoBehaviour
    {
        public Action TogglePanelRequested;
        public float maxDistance = 12.0f;
        public Vector3 fallbackRayOrigin = new Vector3(0.0f, 0.012f, 0.065f);
        public Vector3 fallbackRayEuler = new Vector3(-35.0f, 0.0f, 0.0f);
        public Ray PointerRay { get; private set; }
        public bool TriggerHeld { get; private set; }
        public bool TriggerPressed { get; private set; }
        public bool TriggerReleased { get; private set; }
        public bool GripHeld { get; private set; }
        public bool GripPressed { get; private set; }
        public bool GripReleased { get; private set; }

        private InputDevice device;
        private LineRenderer laser;
        private GameObject laserTip;
        private Renderer laserTipRenderer;
        private VolumeSTCubeQuestClickTarget hovered;
        private bool previousTrigger;
        private bool previousGrip;
        private bool previousSecondary;
        private OculusTouchController openXrAimDevice;
        private bool aimSourceLogged;

        private void Awake()
        {
            laser = gameObject.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.useWorldSpace = true;
            laser.startWidth = 0.006f;
            laser.endWidth = 0.002f;
            laser.startColor = new Color(0.1f, 0.86f, 1.0f, 0.95f);
            laser.endColor = new Color(0.1f, 0.58f, 1.0f, 0.12f);
            Material material = new Material(Shader.Find("Sprites/Default"));
            material.color = Color.white;
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("unity_GUIZTestMode",
                (int)UnityEngine.Rendering.CompareFunction.Always);
            material.SetInt("_ZWrite", 0);
            material.renderQueue = 5000;
            laser.material = material;
            laser.sortingOrder = short.MaxValue;

            laserTip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            laserTip.name = "Quest ray hit cursor";
            Destroy(laserTip.GetComponent<Collider>());
            laserTip.transform.localScale = Vector3.one * 0.018f;
            laserTipRenderer = laserTip.GetComponent<Renderer>();
            laserTipRenderer.material = new Material(material);
            laserTipRenderer.material.color = laser.startColor;
            laserTipRenderer.sortingOrder = short.MaxValue;
            laserTip.SetActive(false);
        }

        private void Update()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                UpdateFlatScreenPointer();
                return;
            }
            if (!device.isValid)
                device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            Ray ray;
            if (!TryGetOpenXrAimRay(out ray))
            {
                Quaternion fallbackRotation = transform.rotation *
                    Quaternion.Euler(fallbackRayEuler);
                ray = new Ray(transform.TransformPoint(fallbackRayOrigin),
                    fallbackRotation * Vector3.forward);
                if (!aimSourceLogged && device.isValid)
                {
                    Debug.Log("VolumeSTCube Quest ray: calibrated Touch grip fallback.");
                    aimSourceLogged = true;
                }
            }
            UpdatePointer(ray, ReadButton(CommonUsages.triggerButton, CommonUsages.trigger));
            UpdateGrip(ReadButton(CommonUsages.gripButton, CommonUsages.grip));

            bool secondary = false;
            device.TryGetFeatureValue(CommonUsages.secondaryButton, out secondary);
            if (secondary && !previousSecondary)
                TogglePanelRequested?.Invoke();
            previousSecondary = secondary;
        }

        private bool TryGetOpenXrAimRay(out Ray ray)
        {
            ray = default(Ray);
            if (openXrAimDevice == null || !openXrAimDevice.added)
            {
                openXrAimDevice = UnityEngine.InputSystem.InputSystem
                    .GetDevice<OculusTouchController>(
                        UnityEngine.InputSystem.CommonUsages.RightHand);
            }
            if (openXrAimDevice == null || openXrAimDevice.pointerPosition == null ||
                openXrAimDevice.pointerRotation == null)
                return false;

            Vector3 trackingPosition = openXrAimDevice.pointerPosition.ReadValue();
            Quaternion trackingRotation = openXrAimDevice.pointerRotation.ReadValue();
            if (trackingRotation.x * trackingRotation.x +
                trackingRotation.y * trackingRotation.y +
                trackingRotation.z * trackingRotation.z +
                trackingRotation.w * trackingRotation.w < 0.5f)
                return false;

            Transform trackingOrigin = transform.parent;
            Vector3 worldPosition = trackingOrigin != null
                ? trackingOrigin.TransformPoint(trackingPosition)
                : trackingPosition;
            Quaternion worldRotation = trackingOrigin != null
                ? trackingOrigin.rotation * trackingRotation
                : trackingRotation;
            Vector3 direction = worldRotation * Vector3.forward;
            // Start at the physical front of the Touch controller, not inside
            // the user's hand or at the grip pivot.
            worldPosition += direction * 0.025f;
            ray = new Ray(worldPosition, direction);
            if (!aimSourceLogged)
            {
                Debug.Log("VolumeSTCube Quest ray: OpenXR Touch pointer pose.");
                aimSourceLogged = true;
            }
            return true;
        }

        private void UpdateFlatScreenPointer()
        {
            Camera camera = GetComponent<Camera>();
            if (camera == null)
                camera = Camera.main;
            Vector2 pointerPosition = Input.mousePosition;
            bool pointerHeld = Input.GetMouseButton(0);
            if (Input.touchCount > 0)
            {
                Touch primaryTouch = Input.GetTouch(0);
                pointerPosition = primaryTouch.position;
                pointerHeld = primaryTouch.phase != TouchPhase.Ended &&
                    primaryTouch.phase != TouchPhase.Canceled;
            }
            if (VolumeSTCubeFlatScreenHUD.IsPointerOverHud(pointerPosition))
                pointerHeld = false;
            Ray ray = camera != null
                ? camera.ScreenPointToRay(pointerPosition)
                : new Ray(transform.position, transform.forward);
            UpdatePointer(ray, pointerHeld);
            UpdateGrip(Input.GetKey(KeyCode.G) || Input.GetMouseButton(2) ||
                Input.touchCount >= 3);

            bool secondary = Input.GetKey(KeyCode.B);
            if (secondary && !previousSecondary)
                TogglePanelRequested?.Invoke();
            previousSecondary = secondary;
        }

        private void UpdateGrip(bool grip)
        {
            GripPressed = grip && !previousGrip;
            GripReleased = !grip && previousGrip;
            GripHeld = grip;
            previousGrip = grip;
        }

        private void UpdatePointer(Ray ray, bool trigger)
        {
            PointerRay = ray;
            float distance = maxDistance;
            VolumeSTCubeQuestClickTarget target = null;
            bool hasSurface = false;
            float nearestSurface = maxDistance;
            float nearestTarget = maxDistance;
            UnityEngine.RaycastHit[] hits = UnityEngine.Physics.RaycastAll(
                ray, maxDistance, 1 << 5, QueryTriggerInteraction.Collide);
            for (int index = 0; index < hits.Length; index++)
            {
                UnityEngine.RaycastHit hit = hits[index];
                if (hit.distance < nearestSurface)
                {
                    nearestSurface = hit.distance;
                    hasSurface = true;
                }
                VolumeSTCubeQuestClickTarget candidate =
                    hit.collider.GetComponentInParent<VolumeSTCubeQuestClickTarget>();
                if (candidate != null && hit.distance < nearestTarget)
                {
                    nearestTarget = hit.distance;
                    target = candidate;
                }
            }
            distance = target != null ? nearestTarget : nearestSurface;

            if (target != hovered)
            {
                if (hovered != null)
                    hovered.SetHovered(false);
                hovered = target;
                if (hovered != null)
                    hovered.SetHovered(true);
            }

            laser.SetPosition(0, ray.origin);
            laser.SetPosition(1, ray.origin + ray.direction * distance);
            laser.startColor = hovered != null
                ? new Color(1.0f, 0.7f, 0.16f, 1.0f)
                : new Color(0.1f, 0.86f, 1.0f, 0.95f);
            if (laserTip != null)
            {
                laserTip.SetActive(hasSurface || target != null);
                laserTip.transform.position = ray.origin + ray.direction * distance;
                laserTip.transform.localScale = Vector3.one *
                    (target != null ? 0.028f : 0.018f);
                if (laserTipRenderer != null)
                    laserTipRenderer.material.color = target != null
                        ? new Color(1.0f, 0.70f, 0.16f, 1.0f)
                        : new Color(0.1f, 0.86f, 1.0f, 1.0f);
            }

            TriggerPressed = trigger && !previousTrigger;
            TriggerReleased = !trigger && previousTrigger;
            TriggerHeld = trigger;
            if (trigger && !previousTrigger && hovered != null)
            {
                hovered.Invoke();
                if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
                    device.SendHapticImpulse(0, 0.32f, 0.045f);
            }
            previousTrigger = trigger;
        }

        private bool ReadButton(InputFeatureUsage<bool> buttonUsage, InputFeatureUsage<float> axisUsage)
        {
            if (!device.isValid)
                return false;
            if (device.TryGetFeatureValue(buttonUsage, out bool pressed) && pressed)
                return true;
            return device.TryGetFeatureValue(axisUsage, out float value) && value > 0.72f;
        }
    }

    /// <summary>Comfort locomotion and volume rotation for the two Touch controllers.</summary>
    public sealed class VolumeSTCubeQuestLocomotion : MonoBehaviour
    {
        public Action ToggleSlabFrameRequested;
        public Transform head;
        public float moveSpeed = 2.2f;
        public float turnSpeed = 48.0f;

        private InputDevice left;
        private InputDevice right;
        private bool previousReset;
        private bool previousSlabToggle;
        private Vector3 initialPosition;
        private Quaternion initialRotation;
        private Vector3 initialHeadEuler;

        private void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
            initialHeadEuler = head != null ? head.localEulerAngles : Vector3.zero;
        }

        private void Update()
        {
            if (VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
            {
                UpdateFlatScreenLocomotion();
                return;
            }
            if (!left.isValid)
                left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if (!right.isValid)
                right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 movement) && head != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                Vector3 rightDirection = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
                transform.position += (forward * movement.y + rightDirection * movement.x) * moveSpeed * Time.deltaTime;
            }

            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 turn))
            {
                VolumeControllerObject controller = FindObjectOfType<VolumeControllerObject>();
                VolumeSTCubeQuestSpatialWorkbench spatialWorkbench =
                    FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>();
                // Quest sticks never scale the data. Horizontal turn rotates the
                // complete Field assembly (volume + XYZ axes + authored planes).
                if (spatialWorkbench != null)
                    spatialWorkbench.RotateField(
                        -turn.x * turnSpeed * Time.deltaTime);
                else if (controller != null)
                    controller.transform.Rotate(Vector3.up,
                        -turn.x * turnSpeed * Time.deltaTime, Space.World);
            }

            bool reset = false;
            left.TryGetFeatureValue(CommonUsages.primaryButton, out reset);
            if (reset && !previousReset)
            {
                transform.SetPositionAndRotation(initialPosition, initialRotation);
                VolumeSTCubeQuestSpatialWorkbench spatialWorkbench = FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>();
                if (spatialWorkbench != null)
                {
                    spatialWorkbench.ResetVolumeLayout();
                    // Left-controller X is also the emergency comfort recenter.
                    // This fixes a workspace anchored while the headset was being
                    // put on or while the user happened to be looking down.
                    spatialWorkbench.RecenterQuestWorkspace();
                }
                else
                {
                    VolumeControllerObject controller = FindObjectOfType<VolumeControllerObject>();
                    if (controller != null)
                    {
                        controller.transform.position = new Vector3(0.0f, 3.55f, 0.0f);
                        controller.transform.rotation = Quaternion.identity;
                        controller.transform.localScale = Vector3.one * 0.45f;
                    }
                }
            }
            previousReset = reset;

            bool slabToggle = false;
            left.TryGetFeatureValue(CommonUsages.secondaryButton, out slabToggle);
            if (slabToggle && !previousSlabToggle)
                ToggleSlabFrameRequested?.Invoke();
            previousSlabToggle = slabToggle;
        }

        private float previousTouchDistance;

        private void UpdateFlatScreenLocomotion()
        {
            if (head == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            Vector3 rightDirection = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
            Vector2 movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed * 2.2f : moveSpeed;
            transform.position += (forward * movement.y + rightDirection * movement.x) * speed * Time.deltaTime;

            if (Input.GetMouseButton(1))
            {
                RotateView(Input.GetAxis("Mouse X") * 2.6f,
                    -Input.GetAxis("Mouse Y") * 2.2f);
            }

            if (Input.touchCount == 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                Vector2 averageDelta = (first.deltaPosition + second.deltaPosition) * 0.5f;
                RotateView(averageDelta.x * 0.12f, -averageDelta.y * 0.12f);

                float currentDistance = Vector2.Distance(first.position, second.position);
                if (previousTouchDistance > 0.0f)
                {
                    float pinch = currentDistance - previousTouchDistance;
                    Vector3 planarForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                    transform.position += planarForward * pinch * 0.008f;
                }
                previousTouchDistance = currentDistance;
            }
            else
            {
                previousTouchDistance = 0.0f;
            }

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                Vector3 planarForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
                transform.position += planarForward * wheel * 0.45f;
            }

            // Desktop mouse-wheel scaling made the data volume drift away from the
            // Field and its authored boundary planes. Field fitting is controlled by
            // VolumeSTCubeQuestSpatialWorkbench.FrameVolume instead.

            if (Input.GetKeyDown(KeyCode.X))
            {
                transform.SetPositionAndRotation(initialPosition, initialRotation);
                head.localEulerAngles = initialHeadEuler;
                VolumeSTCubeQuestSpatialWorkbench spatialWorkbench = FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>();
                if (spatialWorkbench != null)
                    spatialWorkbench.ResetVolumeLayout();
            }

            if (Input.GetKeyDown(KeyCode.Y))
                ToggleSlabFrameRequested?.Invoke();
        }

        private void RotateView(float yawDelta, float pitchDelta)
        {
            Vector3 euler = head.localEulerAngles;
            float pitch = euler.x > 180.0f ? euler.x - 360.0f : euler.x;
            pitch = Mathf.Clamp(pitch + pitchDelta, -80.0f, 80.0f);
            head.localRotation = Quaternion.Euler(pitch, euler.y + yawDelta, 0.0f);
        }
    }

    public static class VolumeSTCubeQuestBootstrap
    {
        private const string DesktopPreviewEditorPref = "VolumeSTCube.SlabLabDesktopPreview";

        public static bool IsFlatScreenEnabled
        {
            get
            {
#if SLABLAB_FLAT
                return true;
#elif UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(DesktopPreviewEditorPref, true);
#elif UNITY_STANDALONE || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsDesktopPreviewEnabled
        {
            get
            {
                return IsFlatScreenEnabled;
            }
        }

        private static bool ShouldInstall
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#elif UNITY_EDITOR
                return IsDesktopPreviewEnabled;
#elif UNITY_STANDALONE || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCompatibilitySweep()
        {
            if (!ShouldInstall)
                return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ShouldInstall)
                VolumeSTCubeQuestCompatibilityGuard.SuppressDesktopOnlyBehaviours();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (!ShouldInstall)
                return;
            if (IsFlatScreenEnabled)
                ConfigureFlatScreenRendering();
            if (UnityEngine.Object.FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>() != null)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!IsFlatScreenEnabled)
            {
                // Keep the eye swapchain at native resolution. 1.20 supersampling
                // combined with 4x MSAA allocates well over a GB of tiled colour and
                // depth buffers on Quest 3 before any volume is loaded.
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 1.0f;
                QualitySettings.antiAliasing = 2;
                AnisotropicFiltering previousFiltering = QualitySettings.anisotropicFiltering;
                if (previousFiltering == AnisotropicFiltering.Disable)
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }
#endif

            foreach (Canvas existingCanvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                existingCanvas.gameObject.SetActive(false);

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Quest Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            CameraController legacyController = camera.GetComponent<CameraController>();
            if (legacyController != null)
                legacyController.enabled = false;
            camera.orthographic = false;
            camera.nearClipPlane = 0.035f;
            camera.farClipPlane = 250.0f;
            // The scene camera may have been authored as a desktop sub-viewport.
            // OpenXR must always receive a full, unscaled eye render target.
            camera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            camera.targetTexture = null;
            camera.targetDisplay = 0;
            camera.cullingMask = ~0;
            camera.usePhysicalProperties = false;
#if UNITY_EDITOR
            camera.allowDynamicResolution = false;
#endif
            camera.stereoTargetEye = IsFlatScreenEnabled
                ? StereoTargetEyeMask.None
                : StereoTargetEyeMask.Both;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f, 1.0f);

            GameObject rig = new GameObject("VolumeSTCube Quest XR Rig");
            if (IsFlatScreenEnabled)
            {
                rig.name = "VolumeSTCube Slab Lab Flat Screen";
                rig.transform.position = Vector3.zero;
            }
            else
            {
                rig.transform.position = new Vector3(0.0f, -0.35f, -8.5f);
            }
#if UNITY_EDITOR
            rig.transform.position = Vector3.zero;
#endif
            camera.transform.SetParent(rig.transform, false);
            camera.transform.localPosition = IsFlatScreenEnabled
                ? new Vector3(0.0f, 1.6f, 0.0f)
                : Vector3.zero;
            camera.transform.localRotation = Quaternion.identity;

            VolumeSTCubeQuestRayInteractor ray;
            GameObject leftHand = null;
            if (IsFlatScreenEnabled)
            {
                ray = camera.gameObject.GetComponent<VolumeSTCubeQuestRayInteractor>();
                if (ray == null)
                    ray = camera.gameObject.AddComponent<VolumeSTCubeQuestRayInteractor>();
            }
            else
            {
                VolumeSTCubeXRTrackedNode headTracker = camera.gameObject.GetComponent<VolumeSTCubeXRTrackedNode>();
                if (headTracker == null)
                    headTracker = camera.gameObject.AddComponent<VolumeSTCubeXRTrackedNode>();
                headTracker.node = XRNode.Head;

                leftHand = new GameObject("Quest Left Controller");
                leftHand.transform.SetParent(rig.transform, false);
                leftHand.AddComponent<VolumeSTCubeXRTrackedNode>().node = XRNode.LeftHand;

                GameObject rightHand = new GameObject("Quest Right Controller");
                rightHand.transform.SetParent(rig.transform, false);
                rightHand.AddComponent<VolumeSTCubeXRTrackedNode>().node = XRNode.RightHand;
                ray = rightHand.AddComponent<VolumeSTCubeQuestRayInteractor>();
            }

            Transform leftControllerTransform = null;
            if (leftHand != null)
                leftControllerTransform = leftHand.transform;

            VolumeSTCubeQuestLocomotion locomotion = rig.AddComponent<VolumeSTCubeQuestLocomotion>();
            locomotion.head = camera.transform;
            rig.AddComponent<VolumeSTCubeQuestCompatibilityGuard>();

            VolumeSTCubeQuestSpatialWorkbench workbench = rig.AddComponent<VolumeSTCubeQuestSpatialWorkbench>();
            workbench.Initialize(camera, ray, leftControllerTransform);
            ray.TogglePanelRequested = workbench.TogglePanel;
            // Do not bind the left secondary controller button to a workflow
            // panel. It is easy to press while gripping and previously opened
            // the Slab/MatPlot preview with no causal connection to the task.
            // Slab Frame remains available from the main menu and stage controls.
            locomotion.ToggleSlabFrameRequested = null;

            if (IsFlatScreenEnabled)
                VolumeSTCubeFlatScreenHUD.Install(rig, workbench);

            if (!IsFlatScreenEnabled)
                rig.AddComponent<VolumeSTCubeQuestTrackingOrigin>();
        }

        private static void ConfigureFlatScreenRendering()
        {
            // Keep the Editor preview predictable, but let desktop windows and
            // tablets retain their user-selected/native resolution at runtime.
#if UNITY_EDITOR
            const int previewWidth = 1920;
            const int previewHeight = 1080;
            Screen.SetResolution(previewWidth, previewHeight, false);
#endif
            ScalableBufferManager.ResizeBuffers(1.0f, 1.0f);
            QualitySettings.resolutionScalingFixedDPIFactor = 1.0f;
            QualitySettings.globalTextureMipmapLimit = 0;
            int minimumAntiAliasing = Application.isMobilePlatform ? 2 : 4;
            QualitySettings.antiAliasing = Mathf.Max(
                QualitySettings.antiAliasing, minimumAntiAliasing);
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 1.0f;
            UnityEngine.XR.XRSettings.renderViewportScale = 1.0f;
            Debug.Log("VolumeSTCube flat screen: native render at 100% buffer scale.");
        }
    }

    [DefaultExecutionOrder(-9000)]
    public sealed class VolumeSTCubeQuestCompatibilityGuard : MonoBehaviour
    {
        private int nextSweepFrame;

        private void Awake()
        {
            SuppressDesktopOnlyBehaviours();
        }

        private void Start()
        {
            // RuntimeInitializeOnLoad callbacks can create the legacy integration
            // after this guard's Awake. Start still runs before any first Update.
            SuppressDesktopOnlyBehaviours();
        }

        private void LateUpdate()
        {
            if (Time.frameCount < nextSweepFrame)
                return;
            nextSweepFrame = Time.frameCount + 30;
            SuppressDesktopOnlyBehaviours();
        }

        internal static void SuppressDesktopOnlyBehaviours()
        {
            VolumeSTCubeMatPlotWorkbench[] desktopWorkbenches = FindObjectsOfType<VolumeSTCubeMatPlotWorkbench>();
            for (int index = 0; index < desktopWorkbenches.Length; index++)
                desktopWorkbenches[index].gameObject.SetActive(false);

            VolumeSTCubeSceneIntegration[] integrations = FindObjectsOfType<VolumeSTCubeSceneIntegration>();
            for (int index = 0; index < integrations.Length; index++)
                integrations[index].enabled = false;

            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>();
            for (int index = 0; index < behaviours.Length; index++)
            {
                MonoBehaviour behaviour = behaviours[index];
                if (behaviour == null)
                    continue;

                string typeName = behaviour.GetType().Name;
                if (typeName == "TClipper" || typeName == "MapMouseTrigger")
                    behaviour.enabled = false;
                else if (typeName == "AxisContainer" || typeName == "Map" || typeName == "UpperPlane")
                    behaviour.gameObject.SetActive(false);
            }

            Canvas[] canvases = FindObjectsOfType<Canvas>();
            for (int index = 0; index < canvases.Length; index++)
            {
                Canvas current = canvases[index];
                if (current.GetComponentInParent<VolumeSTCubeQuestWorkbench>() == null &&
                    current.GetComponentInParent<VolumeSTCubeQuestSpatialWorkbench>() == null)
                    current.gameObject.SetActive(false);
            }

            TextMesh[] labels = FindObjectsOfType<TextMesh>();
            for (int index = 0; index < labels.Length; index++)
            {
                TextMesh label = labels[index];
                if (label.GetComponentInParent<VolumeSTCubeQuestSpatialWorkbench>() == null)
                    label.gameObject.SetActive(false);
            }

            TMPro.TMP_Text[] legacyTmpLabels = FindObjectsOfType<TMPro.TMP_Text>();
            for (int index = 0; index < legacyTmpLabels.Length; index++)
            {
                TMPro.TMP_Text label = legacyTmpLabels[index];
                if (label.GetComponentInParent<VolumeSTCubeQuestSpatialWorkbench>() == null)
                    label.gameObject.SetActive(false);
            }

            string[] legacyObjectNames = { "Map", "Map Text", "Upper Cliper", "AxisContainer", "TimeRangeText" };
            for (int index = 0; index < legacyObjectNames.Length; index++)
            {
                GameObject legacy = GameObject.Find(legacyObjectNames[index]);
                if (legacy != null)
                    legacy.SetActive(false);
            }
        }
    }

    public sealed class VolumeSTCubeQuestTrackingOrigin : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null;
            List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
            SubsystemManager.GetInstances(inputSubsystems);
            for (int i = 0; i < inputSubsystems.Count; i++)
            {
                XRInputSubsystem subsystem = inputSubsystems[i];
                if (subsystem != null && subsystem.running)
                    subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
            }
        }
    }
}
