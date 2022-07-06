using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enabler : MonoBehaviour
{
    public Temporal temp;
    bool toggleOn = false;

    void Start()
    {
        temp = gameObject.GetComponent<Temporal>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            toggleOn = !toggleOn;
        }
        if (toggleOn)
        {
            temp.enabled = false;
        }
        if (!toggleOn)
        {
            temp.enabled = true;
        }
    }

}
