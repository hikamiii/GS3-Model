using UnityEngine;

public class PoseHoldInvoker : MonoBehaviour
{
    public float holdSeconds = 0.8f;
    public TabletopBoardControls target;

    float t;
    bool fired;

    // Hook this to "While Pose Detected"
    public void WhilePoseDetected()
    {
        if (fired || target == null) return;
        t += Time.deltaTime;
        if (t >= holdSeconds)
        {
            fired = true;
            target.ResetToHome();
        }
    }

    // Hook this to "On Pose Lost"
    public void OnPoseLost()
    {
        t = 0f;
        fired = false;
    }
}
