using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace AxisController
{
    public class AxisFrame : MonoBehaviour
    {
        private float xScale = 1;
        private float yScale = 1;
        private float zScale = 1;
        

        public void setScale(float x, float y, float z)
        {
            xScale = x;
            yScale = y;
            zScale = z;
            transform.localScale = new Vector3(xScale, yScale, zScale);
        }
        // Start is called before the first frame update
        void Start()
        {

        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}

