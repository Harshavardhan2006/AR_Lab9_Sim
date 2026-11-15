using UnityEngine;

// Added to every object the user places. Handles colour, selection and sitting on the surface.
public class PlacedObject : MonoBehaviour
{
    private Renderer rend;
    private Color baseColor;
    private bool isSelected;
    private Vector3 surfacePoint;

    void Awake()
    {
        rend = GetComponentInChildren<Renderer>();
        baseColor = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
        ApplyColor();
    }

    // Put the object on the detected surface at this point
    public void PlaceAt(Vector3 point)
    {
        surfacePoint = point;
        Refresh();
    }

    // Keep the bottom of the object on the surface (needed after scaling)
    public void Refresh()
    {
        transform.position = surfacePoint + Vector3.up * rend.bounds.extents.y;
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        ApplyColor();
    }

    public void CycleColor()
    {
        baseColor = Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.8f, 1f);
        ApplyColor();
    }

    private void ApplyColor()
    {
        rend.material.color = isSelected
            ? Color.Lerp(baseColor, Color.yellow, 0.6f)   // selected = yellow tint
            : baseColor;
    }
}
