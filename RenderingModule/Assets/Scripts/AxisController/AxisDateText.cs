using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
namespace AxisController
{
    public class AxisDateText : MonoBehaviour
    {
        private string text = "default";
        public AxisSide axisSide;

        public void setText(string val)
        {
            text = val;
            GetComponent<TextMeshPro> ().text = text;
        }

        public void setPosition(Vector3 position)
        {
            RectTransform rt = GetComponent<RectTransform>();
            //transform.localPosition = position;
            rt.localPosition = position;
            //transform.position = position;
        }

        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {
            //  保持文字始终朝向摄像机
            RectTransform rt = GetComponent<RectTransform>();
            rt.forward = Camera.main.transform.forward;
        }
    }
}

