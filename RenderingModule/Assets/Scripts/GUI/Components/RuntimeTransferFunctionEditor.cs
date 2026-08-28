using UnityEngine;

namespace UnityVolumeRendering
{
    public class RuntimeTransferFunctionEditor : MonoBehaviour
    {
        private static RuntimeTransferFunctionEditor instance = null;
        private TransferFunction tf = null;
        private VolumeRenderedObject volRendObject = null;
        private VolumeControllerObject volControllerObj = null;

        private int windowID;
        private Rect windowRect = new Rect(150 * 3, 15 * 3, WINDOW_WIDTH, WINDOW_HEIGHT);

        private TransferFunctionEditor tfEditor = new TransferFunctionEditor();

        private const int WINDOW_WIDTH = 620 * 3;
        private const int WINDOW_HEIGHT = 400 * 3;

        public static void ShowWindow(VolumeRenderedObject volRendObj)
        {
            if(instance != null)
                GameObject.Destroy(instance);

            GameObject obj = new GameObject("RuntimeTransferFunctionEditor");
            instance = obj.AddComponent<RuntimeTransferFunctionEditor>();
            instance.volRendObject = volRendObj;
        }

        public static void ShowWindow(VolumeControllerObject volControllerObj)
        {
            if(instance != null)
                GameObject.Destroy(instance);

            GameObject obj = new GameObject("RuntimeTransferFunctionEditor");
            instance = obj.AddComponent<RuntimeTransferFunctionEditor>();
            instance.volControllerObj = volControllerObj;
        }

        private void Awake()
        {
            // Fetch a unique ID for our window (see GUI.Window)
            windowID = WindowGUID.GetUniqueWindowID();
        }

        private void OnEnable()
        {
            tfEditor.Initialise();
        }

        private void Update()
        {
            // Close window if object has been destroyed
            if (!volRendObject && !volControllerObj)
                CloseWindow();
        }

        private void OnGUI()
        {
            GUI.skin.window.fontSize = 28;
            windowRect = GUI.Window(windowID, windowRect, UpdateWindow, "Transfer function");
        }

        private void UpdateWindow(int windowID)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUI.skin.button.alignment = TextAnchor.MiddleCenter;
            GUI.skin.button.fontSize = 28;

            Color oldColour = GUI.color; // Used for setting GUI.color when drawing UI elements

            //if (volRendObject == null)
            //    return;
            if (volControllerObj == null)
                return;

            //tf = volRendObject.transferFunction;
            tf = volControllerObj.transferFunction;

            float contentWidth = Mathf.Min(WINDOW_WIDTH, (WINDOW_HEIGHT - 100.0f) * 2.0f);
            float contentHeight = contentWidth * 0.5f;
            
            Rect outerRect = new Rect(0.0f, 0.0f, contentWidth, contentHeight);
            Rect tfEditorRect = new Rect(outerRect.x + 20.0f, outerRect.y + 20.0f, outerRect.width - 40.0f, outerRect.height - 50.0f);

            //tfEditor.SetVolumeObject(volRendObject);
            tfEditor.SetVolumeObject(volControllerObj);
            tfEditor.DrawOnGUI(tfEditorRect);

             // Clear TF
            if(GUI.Button(new Rect(tfEditorRect.x, tfEditorRect.y + tfEditorRect.height + 20.0f * 3, 70.0f * 3, 30.0f * 3), "Clear"))
            {
                tf = ScriptableObject.CreateInstance<TransferFunction>();
                tf.alphaControlPoints.Add(new TFAlphaControlPoint(0.07f, 0.0f));
                tf.alphaControlPoints.Add(new TFAlphaControlPoint(0.95f, 0.1f));
                tf.alphaControlPoints.Add(new TFAlphaControlPoint(1.0f, 0.4f));

                tf.colourControlPoints.Add(new TFColourControlPoint(0.0f, new Color(0.368f, 0.309f, 0.635f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.125f, new Color(0.248f, 0.591f, 0.717f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.25f, new Color(0.538f, 0.815f, 0.645f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.375f, new Color(0.848f, 0.939f, 0.607f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.5f, new Color(1.0f, 0.998f, 0.745f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.625f, new Color(0.995f, 0.825f, 0.5f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.75f, new Color(0.973f, 0.547f, 0.318f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(0.875f, new Color(0.862f, 0.283f, 0.3f, 1.0f)));
                tf.colourControlPoints.Add(new TFColourControlPoint(1.0f, new Color(0.62f, 0.004f, 0.259f, 1.0f)));
                volControllerObj.SetTransferFunction(tf);
                //volRendObject.SetTransferFunction(tf);
                tfEditor.ClearSelection();
            }

            //// Colour picker
            //Color? selectedColour = tfEditor.GetSelectedColour();
            //if (selectedColour != null)
            //{
            //    Color newColour = GUIUtils.ColourField(new Rect(tfEditorRect.x + 250, tfEditorRect.y + tfEditorRect.height + 20, 80.0f, 30.0f), selectedColour.Value);
            //    tfEditor.SetSelectedColour(newColour);
            //}

            //GUI.skin.label.wordWrap = false;
            //GUI.Label(new Rect(tfEditorRect.x, tfEditorRect.y + tfEditorRect.height + 55.0f, 720.0f, 50.0f), "Left click to select and move a control point.\nRight click to add a control point, and ctrl + right click to delete.");

            //GUI.color = oldColour;

            //if (GUI.Button(new Rect(WINDOW_WIDTH - 100 * 3, WINDOW_HEIGHT - 40 * 3, 90 * 3, 30 * 3), "Close"))
            //{
            //    CloseWindow();
            //}
        }

        /// <summary>
        /// Pick the colour control point, nearest to the specified position.
        /// </summary>
        /// <param name="maxDistance">Threshold for maximum distance. Points further away than this won't get picked.</param>
        private int PickColourControlPoint(float position, float maxDistance = 0.03f)
        {
            int nearestPointIndex = -1;
            float nearestDist = 1000.0f;
            for (int i = 0; i < tf.colourControlPoints.Count; i++)
            {
                TFColourControlPoint ctrlPoint = tf.colourControlPoints[i];
                float dist = Mathf.Abs(ctrlPoint.dataValue - position);
                if (dist < maxDistance && dist < nearestDist)
                {
                    nearestPointIndex = i;
                    nearestDist = dist;
                }
            }
            return nearestPointIndex;
        }

        /// <summary>
        /// Pick the alpha control point, nearest to the specified position.
        /// </summary>
        /// <param name="maxDistance">Threshold for maximum distance. Points further away than this won't get picked.</param>
        private int PickAlphaControlPoint(Vector2 position, float maxDistance = 0.05f)
        {
            int nearestPointIndex = -1;
            float nearestDist = 1000.0f;
            for (int i = 0; i < tf.alphaControlPoints.Count; i++)
            {
                TFAlphaControlPoint ctrlPoint = tf.alphaControlPoints[i];
                Vector2 ctrlPos = new Vector2(ctrlPoint.dataValue, ctrlPoint.alphaValue);
                float dist = (ctrlPos - position).magnitude;
                if (dist < maxDistance && dist < nearestDist)
                {
                    nearestPointIndex = i;
                    nearestDist = dist;
                }
            }
            return nearestPointIndex;
        }

        private void CloseWindow()
        {
            GameObject.Destroy(this.gameObject);
        }
    }
}
