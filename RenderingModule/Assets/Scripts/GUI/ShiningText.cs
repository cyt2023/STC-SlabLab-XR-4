using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class ShiningText : MonoBehaviour
{
    private TextMeshProUGUI textToShine;
    public Color originalColor;
    public Color shiningColor;
    public bool isActive = false;

    void Start()
    {
        textToShine = GetComponent<TextMeshProUGUI>();
        textToShine.color = originalColor;
    }

    public void toggleActiveByColor()
    {
        isActive = !isActive;
    }

    public void Update()
    {
        //Debug.Log(isActive);
        if (isActive)
        {
            textToShine.color = originalColor;
        }
        else
        {
            textToShine.color = shiningColor;
        }
    }
}