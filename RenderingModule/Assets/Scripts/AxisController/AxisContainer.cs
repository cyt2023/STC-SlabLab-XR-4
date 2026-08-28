using DateUtils;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using UnityVolumeRendering;
namespace AxisController
{
    public class AxisContainer : MonoBehaviour
    {
        public Axis prefab;
        public float xScale = 1;
        
        public float zScale = 1;

        public float volumeHeight = 31.8f;

        public List<Axis> axisList = new List<Axis>();

        private int axisListLength = 20;

        public bool isActive = true;

        public TextMeshProUGUI axisBtnText;
        
        void Start()
        {
            updateAxis();
            isActive = true;
        }

        public void setPosition(Vector3 position)
        {
            transform.position = position;
        }

        public void setPosition(Vector2 positionXZ)
        {
            transform.position = new Vector3(positionXZ.x, transform.position.y, positionXZ.y);
        }

        public void setScale(Vector3 scaleVec)
        {
            axisList.ForEach(axis => axis.setAxis(scaleVec));
        }

        public void setAxisLength(int length)
        {
            if(axisListLength != length)
            {
                Debug.Log("setAxisLength");
                axisListLength = length;
                updateAxis();
            }
        }

        public void toggleActive()
        {
            isActive = !isActive;
            if (!isActive)
            {
                axisBtnText.text = "Show Axis";
            }
            else
            {
                axisBtnText.text = "Hide Axis";
            }
            foreach (Axis axis in axisList)
            {
                axis.gameObject.SetActive(isActive);
            }
        }

        public void updateAxis()
        {
            //  Çå¿ÕaxisList
            if (axisList.Count > 0)
            {
                for (int i = 0; i < axisList.Count; i++)
                {
                    Destroy(axisList[i].gameObject);
                    axisList.RemoveAt(i);
                }
                Debug.Log("axisList.Count: " + axisList.Count);
            }
            float yScale = volumeHeight / axisListLength;
            for (int i = 0; i < axisListLength; i++)
            {
                Axis axis = Instantiate(prefab, transform);
                axis.setAxis(new Vector3(xScale, yScale, zScale), (float)i, DateUtil.getDateStrByIndex(i, DateUtil.DefaultStartDate, DateUtil.DefaultEndDate, axisListLength));
                axisList.Add(axis);
            }
            setPosition(new Vector3(0, -7.5f + yScale / 2, 0));
        }

        public void resetAxis()
        {
            float yScale = volumeHeight / axisListLength;
            setPosition(new Vector3(0, -7.5f + yScale / 2, 0));
        }
    }
}

