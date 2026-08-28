using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class MenuButton : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    public void SetText(string text)
    {
        //Debug.Log(transform.Find("Text").GetComponent<TextMeshProUGUI>());
        transform.Find("Text").GetComponent<TextMeshProUGUI>().SetText(text);
        
    }

    public void SetPosition(Vector3 position)
    {
        RectTransform rt = GetComponent<RectTransform>();
        rt.position = position;
    }

    public Vector3 GetLocalPosition()
    {
        RectTransform rt = GetComponent<RectTransform>();
        return rt.localPosition;
    }

    public Vector3 GetPosition()
    {
        RectTransform rt = GetComponent<RectTransform>();
        return rt.position;
    }

    public void SetRotation(Quaternion rotation)
    {
        RectTransform rt = GetComponent<RectTransform>();
        rt.rotation = rotation;
    }

    public Quaternion GetRotation()
    {
        RectTransform rt = GetComponent<RectTransform>();
        return rt.rotation;
    }
}
