using MapController;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MainGUIController : MonoBehaviour
{
    enum DisplayMode
    {
        ShowAll,
        HideMap,
        HideUI,
        HideAll
    }

    private Map map;
    public GameObject MainDisplayGUI;

    private DisplayMode displayMode;
    // Start is called before the first frame update
    void Start()
    {
        map = GameObject.FindObjectOfType<Map>();
    }

    // Update is called once per frame
    void Update()
    {
        //  ¼àÌý¼üÅÌ°´¼üAlt, ÇÐ»»ÏÔÊ¾Ä£Ê½
        if (Input.GetKeyDown(KeyCode.Tab))
        {
            switch (displayMode)
            {
                case DisplayMode.ShowAll:
                    displayMode = DisplayMode.HideMap;
                    map.gameObject.SetActive(false);
                    MainDisplayGUI.SetActive(true);
                    break;
                case DisplayMode.HideMap:
                    displayMode = DisplayMode.HideUI;
                    map.gameObject.SetActive(true);
                    MainDisplayGUI.SetActive(false);
                    break;
                case DisplayMode.HideUI:
                    displayMode = DisplayMode.HideAll;
                    map.gameObject.SetActive(false);
                    MainDisplayGUI.SetActive(false);
                    break;
                case DisplayMode.HideAll:
                    displayMode = DisplayMode.ShowAll;
                    map.gameObject.SetActive(true);
                    MainDisplayGUI.SetActive(true);
                    break;
            }
        }
    }
}
