using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EventAnchor
{
    public enum AnchorState
    {
        Checked,
        Unchecked,
        Inactive
    }
    public class Anchor : MonoBehaviour
    {
        public Vector3 position = new Vector3(0, 0, 0);
        public float height = 0;
        public float radius = 0;
        public float startTime = 0;
        public float endTime = 0;
        public AnchorState state = AnchorState.Unchecked;

        public bool _debugMode = false;

        //  x: 圆柱圆心坐标x
        //  y: 圆柱圆心坐标y
        //  height: 圆柱高度
        //  radius: 圆柱半径
        //  startTime: 圆柱开始时间
        //  endTime: 圆柱结束时间
        public void init(float x, float y, float height, float radius, float startTime, float endTime)
        {
            this.position.x = x;
            this.position.z = (startTime - endTime) / 2;
            this.position.y = y;
            this.startTime = startTime;
            this.endTime = endTime;
            this.height = height;
            this.radius = radius;
        }

        public void setState(AnchorState state)
        {
            this.state = state;
        }

        public void setScale(Vector3 scale)
        {
            transform.localScale = scale;
        }

        private void Update()
        {
            if (!_debugMode)
            {
                //  关闭MeshRenderer
                MeshRenderer meshRendererOfAnchor = transform.GetChild(0).gameObject.GetComponent<MeshRenderer>();
                meshRendererOfAnchor.enabled = false;
            }
            else
            {
                //  开启MeshRenderer
                MeshRenderer meshRendererOfAnchor = transform.GetChild(0).gameObject.GetComponent<MeshRenderer>();
                meshRendererOfAnchor.enabled = true;
            }
        }
    }
}

