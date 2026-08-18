using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIObject_Carousel : MonoBehaviour
{
    [Header("Objects to toggle (drag & drop)")]
    [SerializeField] private List<GameObject> objects = new List<GameObject>();
    [Header("Objects to toggle (drag & drop)")]
    [SerializeField] private TMP_Text label;
    [SerializeField] List<string> labels = new List<string>();


    [Header("Start index")]
    [SerializeField] private int index = 0;

    [Header("Behavior")]
    [SerializeField] private bool wrapAround = true; // true = boucle, false = bloque aux extrémités




    void Start()
    {
        if (objects == null || objects.Count == 0)
        {
            Debug.LogWarning("[DemoObjectCarousel] No objects assigned.");
            return;
        }

        index = Mathf.Clamp(index, 0, objects.Count-1);
        SetActive(index);


        for (int i = 0; i < objects.Count; i++)
        {
            index = (i + objects.Count) % objects.Count;
        }

        index = 0;


    }

    // Update is called once per frame
    void Update()
    {
        //incremention(n);
        //Debug.Log(n);
        
    }

    public void Next()
    {
        if (objects == null || objects.Count == 0) return;

        index++;

        if (wrapAround == true)
        {
            index = (index + objects.Count) % objects.Count;
        }
        else
        {
            index = Mathf.Clamp(index, 0, objects.Count - 1);
        }

        SetActive(index);
    }

    public void previous()
    {
        if (objects == null || objects.Count == 0) return;

        index--;

        if (wrapAround == true)
        {
            index = (index + objects.Count) % objects.Count;  
        }
        else
        {
            index = Mathf.Clamp(index, 0, objects.Count - 1);
        }


        SetActive(index);

    }



    public void SetActive(int index)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] == null)
                continue;

            if (i == index)
            {
                objects[i].SetActive(true);
            }
            else
            {
                objects[i].SetActive(false);
            }
        }

        UpdateLabel(index);


    }

    public void UpdateLabel(int index)
    {

        if (label == null)
            return;

        if (labels != null && index < labels.Count && !string.IsNullOrEmpty(labels[index]))
        {
            label.text = labels[index];
        }
        else if (objects != null && index < objects.Count && objects[index] != null)
        {
            label.text = objects[index].name;
        }
        else
        {
            label.text = "";
        }

    }


}

