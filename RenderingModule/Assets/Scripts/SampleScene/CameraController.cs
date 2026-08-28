using AxisController;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UIElements;

namespace UnityVolumeRendering
{
    /// <summary>
    /// Camera controller for sample scene.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        // Normal movement speed
        public float movementSpeed = 1.2f;
        // Rotation speed
        public float rotationSpeed = 2.0f;
        // Mouse wheel scroll speed
        public float scrollSpeed = 0.01f;
        // Multiplied applied to movement speed shen shift key is held down
        public float shiftSpeedMultiplier = 3.0f;
        // Speed at which to interpolate between movement positions and directions
        public float smoothingSpeed = 15.0f;
        // Maximum angle at which to rotate the camera
        public float maxRotationAngle = 90.0f;
        // Minimum angle at which to rotate the camera
        public float minRotationAngle = 0.0f;

        public bool debug_unlock_rotation = false;

        private const float MIN_SIZE = 2.0f;
        private const float MAX_SIZE = 21.0f;

        private Vector3 positionDelta = Vector3.zero;
        private float sizeDelta = 0.0f;
        private Vector3 forwardDelta = Vector3.zero;

        private Vector3 currentVelocity = Vector3.zero;
        private Vector3 currentRotationVelocity = Vector3.zero;

        private float rotationX = 0.0f;
        private float rotationY = 0.0f;
        private float currentSideRotation = 0.0f;
        private float currentUpRotation = 0.0f;

        public bool isFocusing = false;
        private Vector3 focusTargetPosition = new Vector3();
        private Vector3 focusTargetForward = new Vector3();
        //private Vector3 movementDir = Vector3.zero;
        private float focusTargetSize = 2.0f;

        private Vector3 defaultPosition;
        private Vector3 defaultForward;
        private float defaultSize;

        private Camera mainCamera;
        private MapMouseTrigger mapMouseTrigger;
        private AxisContainer axisContainer;
        private TClipper tClipper;

        public void focusObject(GameObject gameobject)
        {
            focusTargetPosition = gameobject.transform.position;
            focusTargetForward = gameobject.transform.forward;
            focusTargetSize = 4.0f;
            defaultPosition = mainCamera.transform.position;
            defaultForward = mainCamera.transform.forward;
            defaultSize = mainCamera.orthographicSize;

            Debug.Log(defaultForward);

            isFocusing = true;
        }

        public void resetCamera()
        {
            focusTargetPosition = defaultPosition;
            focusTargetForward = defaultForward;
            focusTargetSize = defaultSize;
            isFocusing = true;
        }

        private Vector3 getTransformWithConstraint(Vector3 positionOffset)
        {
            Vector3 res_transform = new Vector3(transform.position.x + positionOffset.x, transform.position.y + positionOffset.y, transform.position.z + positionOffset.z);
            res_transform.x = Mathf.Clamp(res_transform.x, -5, 5);
            res_transform.y = Mathf.Clamp(res_transform.y, -5, 30);
            res_transform.z = Mathf.Clamp(res_transform.z, -5, 5);
            return res_transform;
        }

        private void Start()
        {
            mainCamera = GetComponent<Camera>();
            mapMouseTrigger = FindObjectOfType<MapMouseTrigger>();
            tClipper = FindObjectOfType<TClipper>();
            axisContainer = FindObjectOfType<AxisContainer>();

            defaultPosition = mainCamera.transform.position;
            defaultForward = mainCamera.transform.forward;
            defaultSize = mainCamera.orthographicSize;
        }

        private void Update()
        {
            if (isFocusing)
            {
                //Vector3 targetWorldPosition = transform.TransformDirection(focusTargetPosition);
                Vector3 targetWorldPosition = focusTargetPosition;
                Vector3 targetPositionDelta = (targetWorldPosition - transform.position) * movementSpeed * Time.deltaTime;
                Vector3 targetForward = focusTargetForward;
                Vector3 targetForwardDelta = (targetForward - transform.forward) * movementSpeed * Time.deltaTime;
                float targetSizeDelta = (mainCamera.orthographicSize - focusTargetSize) * movementSpeed * Time.deltaTime;

                if ((targetWorldPosition - transform.position).magnitude < 0.05f && mainCamera.orthographicSize - focusTargetSize < 0.01f)
                {
                    isFocusing = false;
                    return;
                }
                //positionDelta = Vector3.Lerp(this.positionDelta, targetPositionDelta, Time.deltaTime * smoothingSpeed * 1.5f);
                //transform.position += positionDelta;
                transform.position = Vector3.SmoothDamp(transform.position, targetWorldPosition, ref currentVelocity, Time.deltaTime * smoothingSpeed);
                //forwardDelta = Vector3.Lerp(this.forwardDelta, targetForwardDelta, Time.deltaTime * smoothingSpeed);
                //transform.forward += forwardDelta;
                //transform.forward = Vector3.SmoothDamp(transform.forward, targetForward, ref currentRotationVelocity, Time.deltaTime);
                // 使用 Quaternion.Lerp 调整朝向
                //Quaternion targetRotation = Quaternion.LookRotation(targetForward);
                //transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * smoothingSpeed * 1.5f);
                //transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * smoothingSpeed * 1.5f);
                //sizeDelta = Mathf.Lerp(this.sizeDelta, targetSizeDelta, Time.deltaTime * smoothingSpeed);
                mainCamera.orthographicSize = Mathf.SmoothDamp(mainCamera.orthographicSize, focusTargetSize, ref sizeDelta, Time.deltaTime * smoothingSpeed);
                //mainCamera.orthographicSize -= this.sizeDelta;
            }
            else
            {
                //  Focus过程中无法移动
                Vector3 movementDir = Vector3.zero;
                //Debug.Log(00000);
                if (Input.GetMouseButton(0) && !EventSystem.current.IsPointerOverGameObject() && !tClipper.isDragging())
                {
                    //Debug.Log(11111);
                    movementDir.y -= Input.GetAxis("Mouse Y") * movementSpeed * 5 * mainCamera.orthographicSize / MAX_SIZE;
                    movementDir.x -= Input.GetAxis("Mouse X") * movementSpeed * 5 * mainCamera.orthographicSize / MAX_SIZE;
                }

                Vector3 targetPositionDelta = transform.TransformDirection(movementDir) * movementSpeed * Time.deltaTime;
                positionDelta = Vector3.Lerp(this.positionDelta, targetPositionDelta, Time.deltaTime * smoothingSpeed);
                //transform.position += this.positionDelta;
                transform.position = getTransformWithConstraint(this.positionDelta);
                if (!mapMouseTrigger.isSetHighlightPosition)
                {
                    mainCamera.orthographicSize -= Input.mouseScrollDelta.y * scrollSpeed;
                    mainCamera.orthographicSize = Mathf.Clamp(mainCamera.orthographicSize, MIN_SIZE, MAX_SIZE);
                }
                    
                if (Input.GetMouseButton(1) && !EventSystem.current.IsPointerOverGameObject())
                {
                    rotationY += Input.GetAxis("Mouse X") * rotationSpeed * mainCamera.orthographicSize * 1.2f / MAX_SIZE;
                    rotationX += -Input.GetAxis("Mouse Y") * rotationSpeed * mainCamera.orthographicSize * 1.2f / MAX_SIZE;
                }
            }
            //  限制旋转角度
            if (!debug_unlock_rotation)
            {
                rotationX = Mathf.Clamp(rotationX, minRotationAngle, maxRotationAngle);
                currentSideRotation = Mathf.LerpAngle(currentSideRotation, rotationY, Time.deltaTime * 5);
                currentUpRotation = Mathf.Lerp(currentUpRotation, rotationX, Time.deltaTime * 5);
                transform.rotation = Quaternion.Euler(currentUpRotation, currentSideRotation, 0);
            }
                

        }

    }
}
