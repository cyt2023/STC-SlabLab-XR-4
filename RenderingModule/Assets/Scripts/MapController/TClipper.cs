using DateUtils;
using EventAnchor;
using MapController;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityVolumeRendering;

public class TClipper : MonoBehaviour
{
    private Map map;
    private UpperPlane upperPlane;
    private ControlPanel controlPanel;
    private float mapPositionY;
    private float upperPlanePositionY;
    private bool isActiveUpper = true;

    private VolumeControllerObject volumeControllerObj;
    private AnchorList anchorList;

    public bool isUpperDragging = false;
    public bool isMapDragging = false;
    public TextMeshPro mapText;
    public TextMeshPro upperClipedText;
    public TextMeshPro timeRangeText;
    public TextMeshProUGUI upperPlaneText;

    private void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        enabled = false;
#elif UNITY_EDITOR
        // The Quest/desktop-preview workbench owns time and depth clipping.
        // Disable this legacy scene component before Unity can schedule Start.
        if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            enabled = false;
#endif
    }

    // Start is called before the first frame update
    void Start()
    {
#if UNITY_EDITOR || UNITY_ANDROID
        // This project now uses the Slab Lab boundary authoring flow in both
        // Editor preview and Quest builds. The legacy clipper is intentionally
        // dormant on those targets.
        enabled = false;
        return;
#else
        if (!enabled)
            return;

        if (GameObject.FindObjectOfType<VolumeSTCubeQuestSpatialWorkbench>() != null)
        {
            enabled = false;
            return;
        }
        if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
        {
            enabled = false;
            return;
        }
        volumeControllerObj = GameObject.FindObjectOfType<VolumeControllerObject>();
        map = GameObject.FindObjectOfType<Map>();
        upperPlane = GameObject.FindObjectOfType<UpperPlane>();
        controlPanel = GameObject.FindObjectOfType<ControlPanel>();
        anchorList = GameObject.FindObjectOfType<AnchorList>();

        // The S4D Quest workbench intentionally disables the legacy map/upper-plane
        // scene. In that mode these references are absent, so the desktop clipper
        // must stay dormant instead of failing before the workbench can start.
        if (map == null || upperPlane == null || controlPanel == null || volumeControllerObj == null ||
            mapText == null || upperClipedText == null || timeRangeText == null || Camera.main == null)
        {
            enabled = false;
            return;
        }

        mapPositionY = map.transform.position.y;
        upperPlanePositionY = upperPlane.transform.position.y;

        updateTClipper();
#endif
    }


    public void resetAll()
    {
        setClipWindowByTime(volumeControllerObj.GetBottomY(), volumeControllerObj.GetTopY());

        updateTClipper();
    }

    public void SetMapDragging(bool isMapDrag)
    {
        isMapDragging = isMapDrag;
        mapText.gameObject.SetActive(isDragging());
        upperClipedText.gameObject.SetActive(isDragging());
        timeRangeText.gameObject.SetActive(isDragging());
    }

    public void SetUpperClipDragging(bool isUpperDrag)
    {
        isUpperDragging = isUpperDrag;
        mapText.gameObject.SetActive(isDragging());
        upperClipedText.gameObject.SetActive(isDragging());
        timeRangeText.gameObject.SetActive(isDragging());
    }

    public void updateTClipper()
    {
        //  根据地图和UpperCliper的位置更新volumeController
        mapPositionY = map.transform.position.y;
        upperPlanePositionY = upperPlane.transform.position.y;

        float mapPositionYNormalized = (mapPositionY - volumeControllerObj.GetBottomY()) / volumeControllerObj.GetHeight();
        float upperPlanePositionYNormalized = (upperPlanePositionY - volumeControllerObj.GetBottomY()) / volumeControllerObj.GetHeight();

        ////  更新TimeRangeSlider
        controlPanel.updateTimeRangeSlide((mapPositionYNormalized + upperPlanePositionYNormalized) / 2);

        Vector2 clipWindow = new Vector2(mapPositionYNormalized, upperPlanePositionYNormalized); 
        volumeControllerObj.SetClipedHeight(clipWindow);

        //  根据地图和UpperCliper的位置更新Text位置和内容
        mapText.transform.position = new Vector3(mapText.transform.position.x, mapPositionY - 1, mapText.transform.position.z);
        upperClipedText.transform.position = new Vector3(upperClipedText.transform.position.x, upperPlanePositionY - 1, upperClipedText.transform.position.z);
        //mapText.text = DateUtil.getDateStrByFloat(mapPositionYNormalized, DateUtil.DefaultStartDate, DateUtil.DefaultEndDate, ((int)(DateUtil.DefaultEndDate - DateUtil.DefaultStartDate).TotalDays) + 1);
        //upperClipedText.text = DateUtil.getDateStrByFloat(upperPlanePositionYNormalized, DateUtil.DefaultStartDate, DateUtil.DefaultEndDate.AddDays(2), ((int)(DateUtil.DefaultEndDate - DateUtil.DefaultStartDate).TotalDays));
        DateTime mapTextDate = DateUtil.getDateTimeByFloat(mapPositionYNormalized, DateUtil.DefaultStartDate, DateUtil.DefaultEndDate, ((int)(DateUtil.DefaultEndDate - DateUtil.DefaultStartDate).TotalDays) + 1);
        DateTime upperClipedTextDate = DateUtil.getDateTimeByFloat(upperPlanePositionYNormalized, DateUtil.DefaultStartDate, DateUtil.DefaultEndDate.AddDays(2), ((int)(DateUtil.DefaultEndDate - DateUtil.DefaultStartDate).TotalDays));
        mapText.text = DateUtil.getDateStrByDateTime(mapTextDate);
        upperClipedText.text = DateUtil.getDateStrByDateTime(upperClipedTextDate);
        timeRangeText.text = DateUtil.getDayRangeByDateTime(mapTextDate, upperClipedTextDate).ToString() + " Days";
        timeRangeText.rectTransform.transform.position = new Vector3(timeRangeText.rectTransform.transform.position.x, (mapPositionY + upperPlanePositionY) / 2 - 1, timeRangeText.rectTransform.transform.position.z);

        //  保持文字始终朝向摄像机
        timeRangeText.transform.forward = Camera.main.transform.forward;
        mapText.transform.forward = Camera.main.transform.forward;
        upperClipedText.transform.forward = Camera.main.transform.forward;

    }

    public void toggleUpperPlaneActive()
    {
        isActiveUpper = !isActiveUpper;
        if (!isActiveUpper)
        {
            upperPlaneText.text = "Show UpperPlane";
        }
        else
        {
            upperPlaneText.text = "Hide UpperPlane";
        }

        upperPlane.gameObject.SetActive(isActiveUpper);
    }

    public float GetMapPositionY()
    {
        return mapPositionY;
    }

    public float GetUpperPlanePositionY()
    {
        return upperPlanePositionY;
    }

    public bool isDragging()
    {
        return isMapDragging || isUpperDragging;
    }

    public void setClipWindowByTime(float startTime, float endTime)
    {
        map.transform.position = new Vector3(map.transform.position.x, startTime, map.transform.position.z);
        upperPlane.transform.position = new Vector3(upperPlane.transform.position.x, endTime, upperPlane.transform.position.z);

        updateTClipper();
        updateAnchorList();
    }

    //  根据Float平移Window
    public void setClipWindowByFloat(float val)
    {
        float delta_height = volumeControllerObj.GetBottomY() + volumeControllerObj.GetHeight() * val;
        float map_delta_height = Mathf.Clamp(delta_height - (upperPlanePositionY - mapPositionY) / 2, volumeControllerObj.GetBottomY(), volumeControllerObj.GetTopY() - (upperPlanePositionY - mapPositionY));
        float upperPlane_delta_height = Mathf.Clamp(delta_height + (upperPlanePositionY - mapPositionY) / 2, volumeControllerObj.GetBottomY() + (upperPlanePositionY - mapPositionY), volumeControllerObj.GetTopY());
        map.transform.position = new Vector3(map.transform.position.x, map_delta_height, map.transform.position.z);
        upperPlane.transform.position = new Vector3(upperPlane.transform.position.x, upperPlane_delta_height, upperPlane.transform.position.z);

        updateTClipper();
        updateAnchorList();
    }

    public void updateAnchorList()
    {
        anchorList.SetActiveByTimeRange(mapPositionY, upperPlanePositionY);

    }
}
