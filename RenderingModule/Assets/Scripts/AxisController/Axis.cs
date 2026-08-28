using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace AxisController
{
    public enum AxisSide
    {
        Front,
        Back,
        Left,
        Right
    }
    public class Axis : MonoBehaviour
    {
        public AxisFrame axisFrame;
        public AxisDateText axisDateText;

        private float xScale = 1;
        private float yScale = 1;
        private float zScale = 1;

        private float height = 0;

        private string dateText = "default";

        // Start is called before the first frame update
        void Start()
        {
            updateAxis();
        }

        public void setAxis(Vector3 scaleVec, float h = -1, string text = "")
        {
            float oldYScale = yScale;
            bool isChangedYScale = scaleVec.y != -1 && oldYScale != scaleVec.y;

            xScale = scaleVec.x != -1 ? scaleVec.x : xScale;
            yScale = scaleVec.y != -1 ? scaleVec.y : yScale;
            zScale = scaleVec.z != -1 ? scaleVec.z : zScale;
            height = h != -1 ? h : height;
            dateText = text != "" ? text : dateText;

            //  修改scale后，需要修正height
            height = isChangedYScale ? (height / oldYScale) * yScale : height;

            //  修改scale后，需要更新axisText的位置
            axisDateText.setPosition(new Vector3(0, 0.25f * yScale / 1.5f, -zScale / 2));

            updateAxis();
        }

        public void updateAxis()
        {
            axisFrame.setScale(xScale, yScale, zScale);
            axisDateText.setText(dateText);
            transform.localPosition = new Vector3(0, height, 0);
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}

