using AxisController;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityVolumeRendering;

public class ControlPanel : MonoBehaviour, IPointerExitHandler, IPointerEnterHandler
{
    public GameObject displayBtn;
    public Slider isosurfaceSlider;
    public Slider lightIntensitySlider;
    public Slider thresholdSlider;
    public Slider opacitySlider;
    public Slider timeRangeSlider;
    public Toggle renderModeToogle;
    public TClipper tClipper;

    private VolumeControllerObject volControllerObj;
    private AxisContainer axisContainer;
    private TMP_InputField opacityText;
    private TMP_InputField thresholdText;
    private TMP_InputField isosurfaceText;
    private TMP_InputField lightIntensityText;

    private float threshold;
    private float opacity;
    private float isosurfaceVal;
    private float lightIntensity;
    private bool isMouseInside = true;
    // Start is called before the first frame update
    void Start()
    {
        volControllerObj = FindObjectOfType<VolumeControllerObject>();
        opacityText = opacitySlider.GetComponentInChildren<TMP_InputField>();
        thresholdText = thresholdSlider.GetComponentInChildren<TMP_InputField>();
        isosurfaceText = isosurfaceSlider.GetComponentInChildren<TMP_InputField>();
        lightIntensityText = lightIntensitySlider.GetComponentInChildren<TMP_InputField>();
        tClipper = FindObjectOfType<TClipper>();
        axisContainer = FindObjectOfType<AxisContainer>();

        resetAll();

        gameObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if(!isMouseInside && Input.GetMouseButtonDown(0))
        {
            gameObject.SetActive(false);
            displayBtn.SetActive(true);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isMouseInside = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isMouseInside = false;
        //RuntimeTransferFunctionEditor.ShowWindow(volControllerObj);
    }

    public void SetOpacity(float value)
    {
        volControllerObj.SetOpacity(value);
        opacity = value;

        opacityText.text = Mathf.Floor(value * 100).ToString();
        opacitySlider.SetValueWithoutNotify(value);
    }

    public void SetThreshold(float value)
    {
        Vector2 visibilityWindow = volControllerObj.GetVisibilityWindow();
        visibilityWindow.x = value;
        volControllerObj.SetVisibilityWindow(visibilityWindow);
        threshold = value;

        thresholdText.text = Mathf.Floor(value * 500).ToString();
        thresholdSlider.SetValueWithoutNotify(value);
    }

    public void SetIsosurfaceVal(float value)
    {
        isosurfaceVal = value;
        volControllerObj.SetIsosurfaceValue(value);

        isosurfaceSlider.SetValueWithoutNotify(value);
        isosurfaceText.text = Mathf.Floor(value * 500).ToString();
        
    }

    public void SetLightIntensity(float value)
    {
        lightIntensity = value;
        volControllerObj.SetLightIntensity(value);

        lightIntensitySlider.SetValueWithoutNotify(value);
        lightIntensityText.text = value.ToString();
        
    }

    public void SetTimeRange(float value)
    {
        tClipper.setClipWindowByFloat(value);
    }

    //  需要一个双向绑定，判断timeRange当前的值
    public void updateTimeRangeSlide(float value)
    {
        timeRangeSlider.value = value;
    }

    //  更新TimeRangeSlider的最值
    public void updateTimeRangeMinMaxVal(float min, float max)
    {
        timeRangeSlider.minValue = min;
        timeRangeSlider.maxValue = max;
    }

    public void OnInputOpacityChanged(string value)
    {
        float opacity = float.Parse(value) / 100;
        opacity = Mathf.Clamp(opacity, 0, 1);
        SetOpacity(opacity);
    }

    public void OnInputThresholdChanged(string value)
    {
        float threshold = float.Parse(value) / 500;
        threshold = Mathf.Clamp(threshold, 0, 1);
        SetThreshold(threshold);
    }

    public void OnInputLightIntensityChanged(string value)
    {
        float lightIntensity = float.Parse(value);
        lightIntensity = Mathf.Clamp(lightIntensity, 0, 1);
        SetLightIntensity(lightIntensity);
    }

    public void OnInputIsosurfaceValChanged(string value)
    {
        float isosurfaceVal = float.Parse(value) / 500;
        isosurfaceVal = Mathf.Clamp(isosurfaceVal, 0, 1);
        SetIsosurfaceVal(isosurfaceVal);
    }

    public void OnSetRenderMode()
    {
        UnityVolumeRendering.RenderMode oldRenderMode = volControllerObj.GetRenderMode();
        Debug.Log(oldRenderMode);
        UnityVolumeRendering.RenderMode newRenderMode = oldRenderMode == UnityVolumeRendering.RenderMode.DirectVolumeRendering? 
                                                                        UnityVolumeRendering.RenderMode.IsosurfaceRendering 
                                                                        : UnityVolumeRendering.RenderMode.DirectVolumeRendering;
        volControllerObj.SetRenderMode(newRenderMode);
        SetOpacity(opacity);
        SetThreshold(threshold);
    }

    public void resetAll()
    {
        tClipper.resetAll();
        volControllerObj.SetHighlightPosition(new Vector2(0.5f, 0.5f));
        volControllerObj.SetHighlightRadius(1.0f);
        renderModeToogle.SetIsOnWithoutNotify(volControllerObj.GetRenderMode() == UnityVolumeRendering.RenderMode.IsosurfaceRendering);
        SetOpacity(0.5f);
        SetThreshold(volControllerObj.GetRenderMode() == UnityVolumeRendering.RenderMode.DirectVolumeRendering ? 0.08f : 0.35f);
        SetLightIntensity(volControllerObj.GetRenderMode() == UnityVolumeRendering.RenderMode.DirectVolumeRendering ? 0.15f : 0.5f);
        SetIsosurfaceVal(0.5f);

        axisContainer.resetAxis();
    }
}
