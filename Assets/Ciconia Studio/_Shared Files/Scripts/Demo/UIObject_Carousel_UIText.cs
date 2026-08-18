using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIObject_Carousel_UIText : MonoBehaviour
{
    [Header("Objects to toggle (drag & drop)")]
    [SerializeField] private List<GameObject> objects = new List<GameObject>();

    [Header("Label (Unity UI Text)")]
    [SerializeField] private Text label;

    [SerializeField] private List<string> labels = new List<string>();

    [Header("Start index")]
    [SerializeField] private int index = 0;

    [Header("Behavior")]
    [SerializeField] private bool wrapAround = true; // true = boucle, false = bloque aux extrémités

    void Start()
    {
        if (objects == null || objects.Count == 0)
        {
            Debug.LogWarning("[UIObject_Carousel] No objects assigned.");
            return;
        }

        index = Mathf.Clamp(index, 0, objects.Count - 1);
        SetActive(index);
    }

    public void Next()
    {
        if (objects == null || objects.Count == 0) return;

        index++;

        if (wrapAround)
            index = (index + objects.Count) % objects.Count;
        else
            index = Mathf.Clamp(index, 0, objects.Count - 1);

        SetActive(index);
    }

    public void Previous()
    {
        if (objects == null || objects.Count == 0) return;

        index--;

        if (wrapAround)
            index = (index + objects.Count) % objects.Count;
        else
            index = Mathf.Clamp(index, 0, objects.Count - 1);

        SetActive(index);
    }

    public void SetActive(int index)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] == null)
                continue;

            objects[i].SetActive(i == index);
        }

        UpdateLabel(index);
    }

    public void UpdateLabel(int index)
    {
        if (label == null)
            return;

        if (labels != null && index < labels.Count && !string.IsNullOrEmpty(labels[index]))
            label.text = labels[index];
        else if (objects != null && index < objects.Count && objects[index] != null)
            label.text = objects[index].name;
        else
            label.text = "";
    }
}