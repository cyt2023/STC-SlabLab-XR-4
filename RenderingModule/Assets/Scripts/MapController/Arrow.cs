using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Arrow : MonoBehaviour
{
    private float mapPos;
    private float upperPlanePos;
    private float height;
    public void setRange(float mapPos, float upperPlanePos)
    {
        this.mapPos = mapPos;
        this.upperPlanePos = upperPlanePos;
        this.height = upperPlanePos - mapPos;
        updateRange();
    }

    public void updateRange()
    {
        transform.position = new Vector3(transform.position.x, (mapPos + upperPlanePos) / 2, transform.position.z);
        transform.localScale = new Vector3(transform.localScale.x, height / 2, transform.localScale.z);
    }
}
