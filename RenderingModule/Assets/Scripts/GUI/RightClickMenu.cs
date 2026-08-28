using System;
using UnityEngine;

public class RightClickMenu : MonoBehaviour
{
    public MenuButton buttonPrefab; // 环绕按钮的预制体
    public int numberOfButtons = 4; // 按钮数量
    public float radius = 4f; // 按钮环绕的半径
    public float longPressDuration = 1f; // 长按右键的持续时间
    public float resetDistanceThreshold = 100f; // 长按右键的移动距离阈值
    public float intervalX = 120f; // 按钮之间的间隔
    public float littleOffset = 60f; // 按钮的微调
    public float animationDuration = 0.5f;  //  出现动画持续时间

    private MenuButton[] buttons;
    private bool isRightClicking = false;
    private bool menuActive = false;
    private float rightClickStartTime = 0f;
    private Vector3 targetMousePosition;
    private Vector3 initialMousePosition;
    private Vector3[] initialButtonPositions;
    private Vector3[] targetButtonPositions;
    private float animationStartTime;

    public float tiltAmount = 30f; // 倾斜的最大角度
    public float tiltSpeed = 5f; // 倾斜速度

    void Start()
    {
        buttons = new MenuButton[numberOfButtons];
        initialButtonPositions = new Vector3[numberOfButtons];
        targetButtonPositions = new Vector3[numberOfButtons];

        for (int i = 0; i < numberOfButtons; i++)
        {
            buttons[i] = Instantiate(buttonPrefab, transform);
            buttons[i].SetActive(false);
        }

        buttons[0].SetText("λ Setter");
        buttons[1].SetText("Opacity");
        buttons[2].SetText("TemporalSlice");
        buttons[3].SetText("SpatialSlice");
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(1))
        {
            initialMousePosition = Input.mousePosition;
            isRightClicking = true;
            rightClickStartTime = Time.time;
        }

        if (isRightClicking && !menuActive && Vector3.Distance(Input.mousePosition, initialMousePosition) > resetDistanceThreshold)
        {
            isRightClicking = false;
            foreach (MenuButton button in buttons)
            {
                button.SetActive(false);
            }
            menuActive = false;
        }

        if (Input.GetMouseButton(0))
        {
            isRightClicking = false;
            rightClickStartTime = Time.time;
            foreach (MenuButton button in buttons)
            {
                button.SetActive(false);
            }
            menuActive = false;
        }

        //if (Input.GetMouseButtonUp(1))
        //{
        //    isRightClicking = false;
        //    rightClickStartTime = Time.time;
        //    foreach (MenuButton button in buttons)
        //    {
        //        button.SetActive(false);
        //    }
        //    menuActive = false;
        //}

        if (isRightClicking && !menuActive && Time.time - rightClickStartTime >= longPressDuration)
        {
            menuActive = true;
            targetMousePosition = Input.mousePosition;
            targetMousePosition.z = Camera.main.nearClipPlane;

            // 计算按钮的目标位置，并初始化按钮位置
            for (int i = 0; i < numberOfButtons; i++)
            {
                Vector3 offset = new Vector3(intervalX, 20 * (i - 1.5f) * radius, 0);
                if (i == 0 || i == numberOfButtons - 1)
                {
                    offset.x = offset.x - littleOffset;
                }
                targetButtonPositions[i] = targetMousePosition + offset;
                buttons[i].SetPosition(Input.mousePosition);
                initialButtonPositions[i] = Input.mousePosition;
            }

            // 启动动画
            animationStartTime = Time.time;
        }

        if (isRightClicking && menuActive)
        {
            // 更新按钮的位置，通过插值实现渐变动画
            float progress = (Time.time - animationStartTime) / animationDuration;
            for (int i = 0; i < numberOfButtons; i++)
            {
                buttons[i].SetActive(true);
                buttons[i].SetPosition(Vector3.Lerp(initialButtonPositions[i], targetButtonPositions[i], progress));

                // 获取鼠标在屏幕上的位置
                Vector2 mousePosition = Input.mousePosition;
               
                // 计算UI元素的倾斜角度
                float tiltX = Mathf.Clamp((mousePosition.y - buttons[i].GetPosition().y) / buttons[i].GetPosition().y, -1f, 1f) * tiltAmount;
                float tiltY = Mathf.Clamp((mousePosition.x - buttons[i].GetPosition().x) / buttons[i].GetPosition().x, -1f, 1f) * tiltAmount;

                // 创建倾斜角度的旋转
                Quaternion tiltRotation = Quaternion.Euler(tiltX, tiltY, 0f);

                // 应用倾斜旋转到UI元素
                buttons[i].SetRotation(Quaternion.Slerp(buttons[i].GetRotation(), tiltRotation, Time.deltaTime * tiltSpeed));
            }


        }
    }
}
