using UnityEngine;
using UnityEngine.InputSystem;

public class SuperCamera : MonoBehaviour
{
    public GameObject pivot;

    public Key resetKey = Key.Space;

    [Range(0f, 100f)]
    public float rotationSensibility = 10f;
    public bool invertRotationX = false;
    public bool invertRotationY = false;

    [Range(0f, 100f)]
    public float translationSensibility = 10f;
    public bool invertTranslationX = false;
    public bool invertTranslationY = false;

    public float zoomMax = 2f;
    public float zoomMin = 20f;

    [Range(0f, 1000f)]
    public float wheelSensibility = 500f;

    private float delayDoubleClic = 0.2f;

    private Vector3 oldCamPos;
    private Quaternion oldCamRot;

    private Vector3 oldMousePos;
    private float timeDoubleClic;
    private bool firstClic = false;

    private Vector3 pivotPos;

    private Camera cam;

    void Start()
    {
        cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("SuperCamera: Aucun Camera.main trouvé dans la scène.");
            enabled = false;
            return;
        }

        if (pivot == null)
        {
            Debug.LogError("SuperCamera: 'pivot' n'est pas assigné.");
            enabled = false;
            return;
        }

        pivotPos = pivot.transform.position;
        oldCamPos = cam.transform.position;
        oldCamRot = cam.transform.rotation;
    }

    void Update()
    {
        Debug.DrawRay(pivotPos, Vector3.up, Color.red);
        Debug.DrawRay(pivotPos, cam.transform.right, Color.green);

        // --- Reset camera ---
        if (Keyboard.current != null && Keyboard.current[resetKey].wasPressedThisFrame)
        {
            cam.transform.position = oldCamPos;
            cam.transform.rotation = oldCamRot;
        }

        var mouse = Mouse.current;
        if (mouse == null)
        {
            // Pas de souris (mobile/console) => on ne fait rien
            return;
        }

        // --- Wheel zoom ---
        float wheel = mouse.scroll.ReadValue().y; // souvent +/-120 par cran selon device
        if (Mathf.Abs(wheel) > 0.0001f)
        {
            // on normalise un peu pour retrouver un feeling proche de l'ancien "Mouse ScrollWheel"
            float wheelNormalized = wheel / 120f;

            Vector3 movVec = (pivotPos - cam.transform.position).normalized;
            movVec *= wheelNormalized / 20f * wheelSensibility;

            Vector3 newPos = cam.transform.position + movVec;
            float dist = (newPos - pivotPos).magnitude;

            if (dist >= zoomMax && dist <= zoomMin)
            {
                cam.transform.position = newPos;
            }
        }

        // --- Double click (left) ---
        bool doubleClic = false;
        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (firstClic)
            {
                doubleClic = true;
                firstClic = false;
            }
            else
            {
                firstClic = true;
                timeDoubleClic = Time.time;
            }
        }

        if (firstClic && Time.time - timeDoubleClic > delayDoubleClic)
        {
            firstClic = false;
        }

        if (doubleClic)
        {
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                pivotPos = hit.point;
                //Debug.Log(hit.point);
            }
            else
            {
                pivotPos = pivot.transform.position;
                //Debug.Log("reset Pivot");
            }
        }

        bool leftHeld = mouse.leftButton.isPressed;
        bool middleHeld = mouse.middleButton.isPressed;

        if (!leftHeld && !middleHeld)
        {
            Cursor.visible = true;
            return;
        }
        else
        {
            Cursor.visible = false;
        }

        Vector3 mousePos = mouse.position.ReadValue();

        // "premier frame" de drag : on initialise oldMousePos
        if (mouse.leftButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame)
        {
            oldMousePos = mousePos;
            return;
        }

        if (middleHeld)
        {
            int factorX = invertTranslationX ? 1 : -1;
            int factorY = invertTranslationY ? 1 : -1;

            transform.Translate(new Vector3(
                factorX * translationSensibility * (mousePos.x - oldMousePos.x) / 100f,
                0f,
                0f
            ), Space.Self);

            transform.Translate(new Vector3(
                0f,
                factorY * translationSensibility * (mousePos.y - oldMousePos.y) / 100f,
                0f
            ), Space.Self);
        }
        else
        {
            int factorX = invertRotationX ? -1 : 1;
            int factorY = invertRotationY ? -1 : 1;

            transform.RotateAround(pivotPos, Vector3.up,
                factorX * rotationSensibility * (mousePos.x - oldMousePos.x) / 100f);

            transform.RotateAround(pivotPos, cam.transform.right,
                factorY * -rotationSensibility * (mousePos.y - oldMousePos.y) / 100f);
        }

        oldMousePos = mousePos;
    }
}
