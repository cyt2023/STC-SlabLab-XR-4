using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.VisualScripting;
using UnityEngine;

namespace UnityVolumeRendering
{
    [ExecuteInEditMode]
    public class VolumeControllerObject : MonoBehaviour
    {
        [SerializeField, HideInInspector]
        public TransferFunction transferFunction;

        [SerializeField, HideInInspector]
        public TransferFunction2D transferFunction2D;

        public MeshRenderer[] meshRenderers = new MeshRenderer[8];

        [SerializeField, HideInInspector]
        public VolumeRenderedObject[] volumeContainerObjects = new VolumeRenderedObject[8];

        [SerializeField, HideInInspector]
        private RenderMode renderMode = RenderMode.IsosurfaceRendering;

        [SerializeField, HideInInspector]
        private TFRenderMode tfRenderMode;

        [SerializeField, HideInInspector]
        private HighLightMode highLightMode;

        [SerializeField, HideInInspector]
        private bool lightingEnabled = true;

        [SerializeField, HideInInspector]
        private LightSource lightSource;

        [SerializeField, HideInInspector]
        private float lightIntensity = 0.5f;

        [SerializeField, HideInInspector]
        private float isosurfaceVal = 0.5f;

        [SerializeField, HideInInspector]
        public Vector2 highlightPosition = new Vector2(0.5f, 0.5f);

        [SerializeField, HideInInspector]
        public float highlightRadius = 0.707f;

        [SerializeField, HideInInspector]
        public Vector2 clipedHeightWindow = new Vector2(0f, 1f);

        // Minimum and maximum gradient threshold for lighting contribution. Values below min will be unlit, and between min and max will be partly shaded.
        [SerializeField, HideInInspector]
        private Vector2 gradientLightingThreshold = new Vector2(0.02f, 0.15f);

        // Gradient magnitude threshold. Voxels with gradient magnitude less than this will not be rendered in isosurface rendering mode.
        [SerializeField, HideInInspector]
        private float minGradient = 0.0f;

        // Minimum/maximum data value threshold for rendering. Values outside of this range will not be rendered.
        [SerializeField, HideInInspector]
        private Vector2 visibilityWindow = new Vector2(0.0f, 1.0f);

        // Early ray termination
        [SerializeField, HideInInspector]
        private bool rayTerminationEnabled = true;

        // Tri-cubic interpolation of data texture (expensive, but looks better)
        [SerializeField, HideInInspector]
        private bool cubicInterpolationEnabled = false;

        [Header("VolumeSTCube Scene Layout")]
        public float originalSceneLayerSpacingScale = 1.0f;
        public float originalSceneLayerYOffset = 0.0f;
        public Vector2 originalSceneHorizontalOffset = Vector2.zero;
        public Vector2 originalSceneHorizontalScale = Vector2.one;
        // 计算渲染体高度
        [SerializeField, HideInInspector]
        private float volumeHeight = 0;

        private CrossSectionManager crossSectionManager;

        private SemaphoreSlim updateMatLock = new SemaphoreSlim(1, 1);

        public void Awake()
        {
            meshRenderers = new MeshRenderer[transform.childCount];
            //  在子类中找到所有的VolumeContainerObject
            for (int i = 0; i < transform.childCount; i++)
            {
                meshRenderers[i] = transform.GetChild(i).GetChild(0).GetComponent<MeshRenderer>();
                volumeContainerObjects[i] = transform.GetChild(i).GetComponent<VolumeRenderedObject>();

                volumeContainerObjects[i].transform.localScale = new Vector3(10, 4, 10);
                volumeContainerObjects[i].transform.localPosition = new Vector3(0, -5.25253f + 3.96f * i, 0);
            }
            if (meshRenderers.Length == 0)
            {
                // XYZ+Time streaming intentionally creates the controller before
                // its selected frame finishes loading in the background.
                volumeContainerObjects = new VolumeRenderedObject[0];
                return;
            }
            transferFunction = volumeContainerObjects[0].transferFunction;
            transferFunction2D = volumeContainerObjects[0].transferFunction2D;
            UpdateMaterialProperties();
        }

        public void SetRenderMode(RenderMode mode)
        {
            Task task = SetRenderModeAsync(mode);
        }

        public async Task SetRenderModeAsync(RenderMode mode, IProgressHandler progressHandler = null)
        {
            if (renderMode != mode)
            {
                renderMode = mode;
                SetVisibilityWindow(0.0f, 1.0f); // reset visibility window
                SetLightingEnabled(false);
            }
            await UpdateMaterialPropertiesAsync(progressHandler);
        }

        public void SetTransferFunctionMode(TFRenderMode mode)
        {
            Task task = SetTransferFunctionModeAsync(mode);
        }

        public async Task SetTransferFunctionModeAsync(TFRenderMode mode, IProgressHandler progressHandler = null)
        {
            if (progressHandler == null)
                progressHandler = NullProgressHandler.instance;

            progressHandler.StartStage(0.3f, "Generating transfer function texture");
            tfRenderMode = mode;
            if (tfRenderMode == TFRenderMode.TF1D && transferFunction != null)
                transferFunction.GenerateTexture();
            else if (transferFunction2D != null)
                transferFunction2D.GenerateTexture();
            progressHandler.EndStage();
            progressHandler.StartStage(0.7f, "Updating material properties");
            await UpdateMaterialPropertiesAsync(progressHandler);
            progressHandler.EndStage();
        }

        public TFRenderMode GetTransferFunctionMode()
        {
            return tfRenderMode;
        }

        public RenderMode GetRenderMode()
        {
            return renderMode;
        }

        public bool GetLightingEnabled()
        {
            return lightingEnabled;
        }

        public LightSource GetLightSource()
        {
            return lightSource;
        }

        public CrossSectionManager GetCrossSectionManager()
        {
            if (crossSectionManager == null)
                crossSectionManager = GetComponent<CrossSectionManager>();
            if (crossSectionManager == null)
                crossSectionManager = gameObject.AddComponent<CrossSectionManager>();
            return crossSectionManager;
        }

        public void SetIsosurfaceValue(float val)
        {
            if (val != isosurfaceVal)
            {
                isosurfaceVal = val;
                UpdateMaterialProperties();
            }
        }

        public float GetIsosurfaceValue()
        {
            return isosurfaceVal;
        }

        public void SetLightIntensity(float intensity)
        {
            if (intensity != lightIntensity)
            {
                lightIntensity = intensity;
                UpdateMaterialProperties();
            }
        }

        public float GetLightIntensity()
        {
            return lightIntensity;
        }

        public Vector2 GetClipedHeightWindow()
        {
            return clipedHeightWindow;
        }

        public void SetClipedHeight(Vector2 window)
        {
            if(window != clipedHeightWindow)
            {
                clipedHeightWindow = window;
                UpdateMaterialProperties();
            }
        }

        public void SetHighlightPosition(Vector2 position)
        {
            if (position != highlightPosition)
            {
                highlightPosition = position;
                UpdateMaterialProperties();
            }
        }

        public Vector2 GetHighlightPosition()
        {
            return highlightPosition;
        }

        public void SetHighlightRadius(float radius)
        {
            if (radius != highlightRadius)
            {
                highlightRadius = Mathf.Clamp(radius, 0, 0.707f);
                UpdateMaterialProperties();
            }
        }

        public float GetHighlightRadius()
        {
            return highlightRadius;
        }

        public float GetHeight()
        {
            if(volumeHeight == 0)
            {
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    volumeHeight += meshRenderers[i].bounds.size.y;
                }
            }
            return volumeHeight;
        }

        public float GetBottomY()
        {
            return meshRenderers[0].transform.position.y - GetHeight() / 2 / meshRenderers.Length;
        }

        public float GetTopY()
        {
            return meshRenderers[meshRenderers.Length - 1].transform.position.y + GetHeight() / 2 / meshRenderers.Length;
        }

        // 整体提升体的透明度
        public void SetOpacity(float opacity)
        {
            opacity -= 0.5f;
            float minOpacity = 0.0f;
            float minPosition = 0.06f - 0.06f * 2 * opacity;
            float maxOpacity = Mathf.Clamp(0.1f + 0.1f * opacity, 0.0f, 1.0f);
            TransferFunction tf = ScriptableObject.CreateInstance<TransferFunction>();
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(minPosition, minOpacity));
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(0.50f, maxOpacity * 0.33f));
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(0.95f, maxOpacity));
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(1.0f, 0.8f));

            tf.colourControlPoints.Add(new TFColourControlPoint(0.0f, new Color(0.368f, 0.309f, 0.635f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.125f, new Color(0.248f, 0.591f, 0.717f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.25f, new Color(0.538f, 0.815f, 0.645f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.375f, new Color(0.848f, 0.939f, 0.607f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.5f, new Color(1.0f, 0.998f, 0.745f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.625f, new Color(0.995f, 0.825f, 0.5f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.75f, new Color(0.973f, 0.547f, 0.318f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(0.875f, new Color(0.862f, 0.283f, 0.3f, 1.0f)));
            tf.colourControlPoints.Add(new TFColourControlPoint(1.0f, new Color(0.62f, 0.004f, 0.259f, 1.0f)));
            SetTransferFunction(tf);
        }

        public void SetLightingEnabled(bool enable)
        {
            if (enable != lightingEnabled)
            {
                lightingEnabled = enable;
                UpdateMaterialProperties();
            }
        }

        public async Task SetLightingEnabledAsync(bool enable, IProgressHandler progressHandler = null)
        {
            if (enable != lightingEnabled)
            {
                lightingEnabled = enable;
                await UpdateMaterialPropertiesAsync(progressHandler);
            }
        }

        public void SetLightSource(LightSource source)
        {
            if (lightSource != source)
            {
                lightSource = source;
                UpdateMaterialProperties();
            }
        }

        public void SetGradientLightingThreshold(Vector2 threshold)
        {
            if (gradientLightingThreshold != threshold)
            {
                gradientLightingThreshold = threshold;
                UpdateMaterialProperties();
            }
        }

        public Vector2 GetGradientLightingThreshold()
        {
            return gradientLightingThreshold;
        }

        public void SetGradientVisibilityThreshold(float min)
        {
            if (minGradient != min)
            {
                minGradient = min;
                UpdateMaterialProperties();
            }
        }

        public float GetGradientVisibilityThreshold()
        {
            return minGradient;
        }

        public void SetVisibilityWindow(float min, float max)
        {
            SetVisibilityWindow(new Vector2(min, max));
        }

        public void SetVisibilityWindow(Vector2 window)
        {
            if (window != visibilityWindow)
            {
                visibilityWindow = window;
                UpdateMaterialProperties();
            }
        }

        public Vector2 GetVisibilityWindow()
        {
            return visibilityWindow;
        }

        public bool GetRayTerminationEnabled()
        {
            return rayTerminationEnabled;
        }

        public void SetRayTerminationEnabled(bool enable)
        {
            if (enable != rayTerminationEnabled)
            {
                rayTerminationEnabled = enable;
                UpdateMaterialProperties();
            }
        }

        [System.Obsolete("Back-to-front rendering no longer supported")]
        public bool GetDVRBackwardEnabled()
        {
            return false;
        }

        [System.Obsolete("Back-to-front rendering no longer supported")]
        public void SetDVRBackwardEnabled(bool enable)
        {
            Debug.LogWarning("Back-to-front rendering no longer supported");
        }

        public bool GetCubicInterpolationEnabled()
        {
            return cubicInterpolationEnabled;
        }

        public void SetCubicInterpolationEnabled(bool enable)
        {
            if (enable != cubicInterpolationEnabled)
            {
                cubicInterpolationEnabled = enable;
                UpdateMaterialProperties();
            }
        }

        public void SetTransferFunction(TransferFunction tf)
        {
            this.transferFunction = tf;
            UpdateMaterialProperties();
        }

        public async Task SetTransferFunctionAsync(TransferFunction tf, IProgressHandler progressHandler = null)
        {
            for (int i = 1; i < meshRenderers.Length; i++)
            {
                if (meshRenderers[i].sharedMaterial == null)
                {
                    meshRenderers[i].sharedMaterial = new Material(Shader.Find("VolumeRendering/DirectVolumeRenderingShader"));
                    meshRenderers[i].sharedMaterial.SetTexture("_DataTex", volumeContainerObjects[i].dataset.GetDataTexture());
                }
            }

            if (transferFunction == null)
            {
                transferFunction = TransferFunctionDatabase.CreateTransferFunction();
            }

            this.transferFunction = tf;
            await UpdateMaterialPropertiesAsync(progressHandler);
        }

        private void UpdateMaterialProperties(IProgressHandler progressHandler = null)
        {
            Task task = UpdateMaterialPropertiesAsync(progressHandler);
        }

        private async Task UpdateMaterialPropertiesAsync(IProgressHandler progressHandler = null)
        {
            await updateMatLock.WaitAsync();
            try
            {
                for (int i = 0; i < meshRenderers.Length; i++)
                {
                    MeshRenderer meshRenderer = meshRenderers[i];
                    bool useGradientTexture = Application.platform != RuntimePlatform.Android &&
                        (tfRenderMode == TFRenderMode.TF2D ||
                         renderMode == RenderMode.DirectVolumeRendering ||
                         renderMode == RenderMode.IsosurfaceRendering);
                    Texture3D texture = useGradientTexture
                        ? await volumeContainerObjects[i].dataset.GetGradientTextureAsync(progressHandler)
                        : null;
                    meshRenderer.sharedMaterial.SetTexture("_GradientTex", texture);

                    UpdateMatInternal(i);
                }
            }
            finally
            {
                updateMatLock.Release();
            }
        }

        private void UpdateMatInternal(int index)
        {
            if (tfRenderMode == TFRenderMode.TF2D)
            {
                meshRenderers[index].sharedMaterial.SetTexture("_TFTex", transferFunction2D.GetTexture());
                meshRenderers[index].sharedMaterial.EnableKeyword("TF2D_ON");
            }
            else
            {
                meshRenderers[index].sharedMaterial.SetTexture("_TFTex", transferFunction.GetTexture());
                meshRenderers[index].sharedMaterial.DisableKeyword("TF2D_ON");
            }
            switch (renderMode)
            {
                case RenderMode.DirectVolumeRendering:
                    {
                        meshRenderers[index].sharedMaterial.EnableKeyword("MODE_DVR");
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_MIP");
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_SURF");
                        break;
                    }
                case RenderMode.MaximumIntensityProjectipon:
                    {
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_DVR");
                        meshRenderers[index].sharedMaterial.EnableKeyword("MODE_MIP");
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_SURF");
                        break;
                    }
                case RenderMode.IsosurfaceRendering:
                    {
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_DVR");
                        meshRenderers[index].sharedMaterial.DisableKeyword("MODE_MIP");
                        meshRenderers[index].sharedMaterial.EnableKeyword("MODE_SURF");
                        break;
                    }
            }

            if (lightingEnabled)
                meshRenderers[index].sharedMaterial.EnableKeyword("LIGHTING_ON");
            else
                meshRenderers[index].sharedMaterial.DisableKeyword("LIGHTING_ON");

            if (lightSource == LightSource.SceneMainLight)
                meshRenderers[index].sharedMaterial.EnableKeyword("USE_MAIN_LIGHT");
            else
                meshRenderers[index].sharedMaterial.DisableKeyword("USE_MAIN_LIGHT");

            meshRenderers[index].sharedMaterial.SetFloat("_VolumeLightFactor", lightIntensity);
            meshRenderers[index].sharedMaterial.SetFloat("_IsosurfaceVal", isosurfaceVal);
            meshRenderers[index].sharedMaterial.SetFloat("_MinVal", visibilityWindow.x);
            meshRenderers[index].sharedMaterial.SetFloat("_MaxVal", visibilityWindow.y);
            meshRenderers[index].sharedMaterial.SetFloat("_MinGradient", minGradient);
            meshRenderers[index].sharedMaterial.SetFloat("_LightingGradientThresholdStart", gradientLightingThreshold.x);
            meshRenderers[index].sharedMaterial.SetFloat("_LightingGradientThresholdEnd", gradientLightingThreshold.y);
            meshRenderers[index].sharedMaterial.SetVector("_TextureSize", new Vector3(volumeContainerObjects[index].dataset.dimX, volumeContainerObjects[index].dataset.dimY, volumeContainerObjects[index].dataset.dimZ));

            meshRenderers[index].sharedMaterial.SetFloat("_CircleX", highlightPosition.x);
            meshRenderers[index].sharedMaterial.SetFloat("_CircleY", highlightPosition.y);
            meshRenderers[index].sharedMaterial.SetFloat("_CircleRadius", highlightRadius);

            //  Cliped Height需要分块计算
            //  如果index < floor(cliped_height * meshRenderers.Length), index_height = 1
            //  如果index > floor(cliped_height * meshRenderers.Length), index_height = 0
            //  如果index = floor(cliped_height * meshRenderers.Length), index_height = 1 - frac(cliped_height * meshRenderers.Length)
            float index_heightX;
            float index_heightY;
            float clipedHeightBottom = clipedHeightWindow.x;
            float clipedHeightTop = clipedHeightWindow.y;
            //  判断底部裁切
            if (index < Mathf.Floor(clipedHeightBottom * meshRenderers.Length))
            {
                index_heightX = 1;
            }
            else if (index > Mathf.Floor(clipedHeightBottom * meshRenderers.Length))
            {
                index_heightX = 0;
            }
            else
            {
                index_heightX = (clipedHeightBottom * meshRenderers.Length - Mathf.Floor(clipedHeightBottom * meshRenderers.Length));
            }

            //  判断顶部裁切
            if (index < Mathf.Floor(clipedHeightTop * meshRenderers.Length))
            {
                index_heightY = 1;
                
            }
            else if (index > Mathf.Floor(clipedHeightTop * meshRenderers.Length))
            {
                index_heightY = 0;
            }
            else
            {
                index_heightY = (clipedHeightTop * meshRenderers.Length - Mathf.Floor(clipedHeightTop * meshRenderers.Length));
            }

            //  如果index_heightX = 1 或者 index_heightY = 0, 则不渲染
            if(index_heightX == 1 || index_heightY == 0)
            {
                volumeContainerObjects[index].gameObject.SetActive(false);
            }
            else
            {
                volumeContainerObjects[index].gameObject.SetActive(true);
                meshRenderers[index].sharedMaterial.SetFloat("_StartPlane", index_heightX);
                meshRenderers[index].sharedMaterial.SetFloat("_EndPlane", index_heightY);
            }

            if (rayTerminationEnabled)
                meshRenderers[index].sharedMaterial.EnableKeyword("RAY_TERMINATE_ON");
            else
                meshRenderers[index].sharedMaterial.DisableKeyword("RAY_TERMINATE_ON");

            if (cubicInterpolationEnabled)
                meshRenderers[index].sharedMaterial.EnableKeyword("CUBIC_INTERPOLATION_ON");
            else
                meshRenderers[index].sharedMaterial.DisableKeyword("CUBIC_INTERPOLATION_ON");
        }

    }
}
