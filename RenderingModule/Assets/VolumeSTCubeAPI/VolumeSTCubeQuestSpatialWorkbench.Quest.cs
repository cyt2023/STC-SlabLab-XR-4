using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    /// <summary>Split out of VolumeSTCubeQuestSpatialWorkbench: Quest side of the workbench.</summary>
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        public void RecenterQuestWorkspace()
        {
            if (!VolumeSTCubeQuestBootstrap.IsFlatScreenEnabled)
                StartCoroutine(PlaceInitialQuestWorkspace());
        }

        private IEnumerator PlaceInitialQuestWorkspace()
        {
            // OpenXR tracking poses are not guaranteed to be available during
            // RuntimeInitializeOnLoad. Wait briefly, then anchor the workspace to
            // the user's actual launch gaze instead of the Guardian world axes.
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
                if (xrCamera != null &&
                    (xrCamera.transform.localPosition.sqrMagnitude > 0.01f ||
                     Quaternion.Angle(xrCamera.transform.localRotation,
                         Quaternion.identity) > 1.0f))
                    break;
            }
            if (xrCamera == null)
                yield break;

            // Panels must follow the actual gaze pitch. Projecting this direction
            // onto the floor made a panel anchored while the user looked slightly
            // up appear at the very bottom of both Quest eye buffers.
            Vector3 gaze = xrCamera.transform.forward.normalized;
            if (gaze.sqrMagnitude < 0.01f)
                gaze = transform.forward;
            Vector3 forward = Vector3.ProjectOnPlane(gaze, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f)
                forward = transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 head = xrCamera.transform.position;

            if (stage == Stage.DatasetImport)
            {
                questImportHeadLocked = true;
                UpdateQuestImportHeadLock(true);
                Debug.Log("VolumeSTCube Quest import panel head-locked. head=" +
                    head.ToString("F2") + ", gaze=" + gaze.ToString("F2") +
                    ", panel=" + panelCanvas.transform.position.ToString("F2"));
                yield break;
            }

            questImportHeadLocked = false;
            PlaceQuestAnalysisWorkspace(head, forward, right);
        }
    }
}
