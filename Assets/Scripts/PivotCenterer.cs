using UnityEngine;

public class PivotCenterer : MonoBehaviour
{
    public Transform boardRoot; // assign ### OBJECT

    [ContextMenu("Center Pivot To Board")]
    public void CenterPivotToBoard()
    {
        if (boardRoot == null) return;

        var renderers = boardRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        // Move pivot to visual center (world space)
        transform.position = b.center;

        // Parent board under pivot while keeping world positions
        boardRoot.SetParent(transform, true);
    }
}
