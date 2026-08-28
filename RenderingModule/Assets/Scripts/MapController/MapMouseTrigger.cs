using AxisController;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityVolumeRendering;

public class MapMouseTrigger : MonoBehaviour
{
    UnityEngine.RaycastHit hit;
    public LayerMask mapHighlightClickableLayer;
    private VolumeControllerObject volumeControllerObj;
    public bool isSetHighlightPosition = false;
    public bool dragable = true;
    public Toggle toggle;
    public ShiningText shiningText;

    private AxisContainer axisContainer;

    // 新增空心圆
    public GameObject hollowCirclePrefab; // 空心圆预制体
    private GameObject hollowCircleInstance; // 空心圆实例
    private LineRenderer hollowCircleLineRenderer;
    private int circleSegments = 500; // 圆的分段数

    public void changeHighlightState()
    {
        setIsSetHighlightPosition(!isSetHighlightPosition);
        //toggle.isOn = isSetHighlightPosition;
    }

    public void setIsSetHighlightPosition(bool state)
    {
        isSetHighlightPosition = state;
        //FindObjectOfType<UIBehaviour>().setSpatialClippingStateText(isSetHighlightPosition);
        toggle.SetIsOnWithoutNotify(state);
        shiningText.isActive = state;
    }

    // Start is called before the first frame update
    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Mouse and screen-space pointer input are not used by the Quest runtime.
        enabled = false;
        return;
#endif
        axisContainer = FindObjectOfType<AxisContainer>();
        volumeControllerObj = FindObjectOfType<VolumeControllerObject>();

        // 实例化空心圆
        if (hollowCirclePrefab != null)
        {
            hollowCircleInstance = Instantiate(hollowCirclePrefab, Vector3.zero, Quaternion.identity, transform);  //transform 作为父对象，使空心圆与地图图层一起移动和旋转
            hollowCircleLineRenderer = hollowCircleInstance.GetComponent<LineRenderer>();
            if (hollowCircleLineRenderer != null)
            {
                // 设置颜色为黑色              
                hollowCircleLineRenderer.startColor = Color.black;
                hollowCircleLineRenderer.endColor = Color.black;


                // 设置线条宽度减小
                hollowCircleLineRenderer.startWidth = 0.02f;
                hollowCircleLineRenderer.endWidth = 0.02f;
            }

            hollowCircleInstance.SetActive(false); // 初始时隐藏
        }
    }

    // Update is called once per frame
    void Update()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        if (isSetHighlightPosition)
        {
            //  按下Space键退出设置高亮位置
            if(Input.GetKeyDown(KeyCode.Space))
            {
                setIsSetHighlightPosition(!isSetHighlightPosition);   
            }

            if(Physics.Raycast(ray, out hit, 1000, mapHighlightClickableLayer.value))
            {
                if (!EventSystem.current.IsPointerOverGameObject())
                {
                    Vector3 hitPoint = hit.point;
                    float positionX = (hitPoint.x + 5) / 10;
                    float positionY = (hitPoint.z + 5) / 10;

                    volumeControllerObj.SetHighlightPosition(new Vector2(positionX, positionY));
                    axisContainer.setPosition(new Vector2(hitPoint.x, hitPoint.z));  //鼠标坐标

                    // 更新空心圆的位置和缩放
                    if (hollowCircleInstance != null)
                    {
                        hollowCircleInstance.SetActive(true);

                        Vector3 hollowCirclePosition = new Vector3(hitPoint.x, transform.position.y, hitPoint.z);
                        hollowCircleInstance.transform.position = hollowCirclePosition;
                        UpdateHollowCircle(hitPoint, volumeControllerObj.GetHighlightRadius());
                    }
                }
            }
            else
            {
                // 如果射线未击中可点击图层，隐藏空心圆
                if (hollowCircleInstance != null)
                {
                    hollowCircleInstance.SetActive(false);
                }
            }


            //  滚轮调整高亮半径
            float radius = Mathf.Clamp(volumeControllerObj.GetHighlightRadius() + Input.mouseScrollDelta.y * 0.015f, 0.0f, 0.707f);
            volumeControllerObj.SetHighlightRadius(radius);
            float axisScaleX = Mathf.Clamp((radius + 0.05f) * 2 * 10, 2.0f, 10.0f);
            float axisScaleZ = Mathf.Clamp((radius + 0.05f) * 2 * 10, 2.0f, 10.0f);
            axisContainer.setScale(new Vector3(axisScaleX, -1, axisScaleZ));

            // 更新空心圆的缩放
            if (hollowCircleInstance != null && hollowCircleLineRenderer != null)
            {
                if(radius >= 0.69)
                {
                    hollowCircleInstance.SetActive(false);
                }
                else
                {
                    UpdateHollowCircle(hit.point, (radius) * 10);
                }
            }
        }
        else
        {
            // 当不在设置高亮位置时，隐藏空心圆
            if (hollowCircleInstance != null)
            {
                print(transform.position.y);
                Vector3 hollowCirclePosition = new Vector3(volumeControllerObj.GetHighlightPosition().x * 10 - 5, transform.position.y, volumeControllerObj.GetHighlightPosition().y * 10 - 5); // Slight offset to prevent z-fighting
                hollowCircleInstance.transform.position = hollowCirclePosition;
                UpdateHollowCircle(hollowCirclePosition, volumeControllerObj.GetHighlightRadius() * 10);
            }
        }

    }

    //画圆
    void UpdateHollowCircle(Vector3 center, float radius)
    {
        if (hollowCircleLineRenderer == null)
            return;

        // 计算圆的顶点
        Vector3[] points = new Vector3[circleSegments + 1];
        float angle = 0f;
        float step = 360f / circleSegments;

        for (int i = 0; i <= circleSegments; i++)
        {
            float rad = Mathf.Deg2Rad * angle;
            float x = Mathf.Cos(rad) * radius;
            float z = Mathf.Sin(rad) * radius;
            points[i] = new Vector3(x, 0.02f, z) + center;
            angle += step;
        }

        // 设置 LineRenderer 的位置
        hollowCircleLineRenderer.positionCount = points.Length;
        hollowCircleLineRenderer.SetPositions(points);
    }
}
