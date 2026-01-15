using UnityEngine;
using Leap;

public class TabletopBoardControls : MonoBehaviour
{
    [Header("Refs")]
    public LeapServiceProvider provider; // Drag "Service Provider (Desktop)" here
    public Transform cam;               // Drag Main Camera here

    [Header("Pinch")]
    [Range(0f, 1f)] public float pinchStrength = 0.8f;

    [Header("Rotate")]
    public float degreesPerMeter = 300f;   // sensitivity
    public float rotationSmooth = 16f;     // higher = snappier
    public float pitchMin = -70f;
    public float pitchMax = 70f;

    [Header("Zoom (scales board)")]
    public bool zoomByScalingBoard = true;
    public float minScale = 0.05f;
    public float maxScale = 2.0f;
    public float zoomSmooth = 16f;

    [Header("Coordinate toggle (try if it feels wrong)")]
    public bool pinchPositionIsProviderLocal = true;

    // -------------------------------
    // EXHIBITION / AUTO ORBIT CAMERA
    // -------------------------------
    [Header("Exhibition (auto camera orbit)")]
    public bool autoOrbit = false;
    public Transform orbitPivot;                 // If null, defaults to this transform
    public float orbitDegreesPerSecond = 10f;    // How fast the camera orbits
    public float orbitPitch = 20f;               // Tilt downward (degrees)
    public float orbitDistance = 0f;             // 0 = use current distance on enable
    public float orbitLookAtHeight = 0.0f;       // Look-at offset above pivot
    public float orbitSmooth = 10f;              // Higher = snappier camera motion
    public float resumeAfterSeconds = 1.0f;      // Wait after interaction before resuming orbit

    private float orbitYaw;                      // current orbit angle around Y
    private float lastInteractTime = -999f;

    // Interaction state
    private bool onePinch;
    private int oneHandId;
    private Vector3 lastPinchCam;

    private bool twoPinch;
    private float startHandDist;
    private Vector3 startScale;

    // Smoothed targets
    private float targetYaw;
    private float targetPitch;
    private Vector3 targetScale;

    // Home pose (for reset)
    private Vector3 homePos;
    private Quaternion homeRot;
    private Vector3 homeScale;

    void Reset()
    {
        cam = Camera.main ? Camera.main.transform : null;
        targetScale = transform.localScale;
    }

    void Awake()
    {
        // Save "home" pose once
        homePos = transform.position;
        homeRot = transform.rotation;
        homeScale = transform.localScale;

        targetScale = homeScale;

        Vector3 e = homeRot.eulerAngles;
        targetYaw = e.y;
        targetPitch = NormalizePitch(e.x);

        if (orbitPivot == null) orbitPivot = transform;

        // If autoOrbit starts enabled, initialize from current camera pose
        if (autoOrbit) SetupOrbitFromCurrentCamera();
    }

    void Update()
    {
        if (provider == null || cam == null) return;

        Frame f = provider.CurrentFrame;
        Hand L = f.GetHand(Chirality.Left);
        Hand R = f.GetHand(Chirality.Right);

        bool lPinch = IsPinching(L);
        bool rPinch = IsPinching(R);

        // Any pinch = interaction; pause orbit and reset resume timer
        if (lPinch || rPinch) lastInteractTime = Time.time;

        // ----- TWO HAND: ZOOM -----
        if (lPinch && rPinch)
        {
            Vector3 pL = GetPinchWorld(L);
            Vector3 pR = GetPinchWorld(R);
            float dist = Vector3.Distance(pL, pR);

            if (!twoPinch)
            {
                twoPinch = true;
                onePinch = false;
                startHandDist = Mathf.Max(0.0001f, dist);
                startScale = transform.localScale;
            }
            else
            {
                float ratio = dist / startHandDist;

                if (zoomByScalingBoard)
                {
                    Vector3 desired = startScale * ratio;
                    float s = Mathf.Clamp(desired.x, minScale, maxScale);
                    targetScale = new Vector3(s, s, s);
                }
            }

            return;
        }

        // Not two-hand pinching anymore
        twoPinch = false;

        // ----- ONE HAND: ROTATE -----
        Hand active = lPinch ? L : (rPinch ? R : null);
        if (active != null)
        {
            Vector3 pinchW = GetPinchWorld(active);
            Vector3 pinchCam = cam.InverseTransformPoint(pinchW);

            if (!onePinch || oneHandId != active.Id)
            {
                onePinch = true;
                oneHandId = active.Id;
                lastPinchCam = pinchCam;
                return;
            }

            Vector3 delta = pinchCam - lastPinchCam;
            lastPinchCam = pinchCam;

            targetYaw += delta.x * degreesPerMeter;
            targetPitch += -delta.y * degreesPerMeter;
            targetPitch = Mathf.Clamp(targetPitch, pitchMin, pitchMax);

            return;
        }

        // No pinching
        onePinch = false;
    }

    void LateUpdate()
    {
        // Smooth rotation (board)
        Quaternion desiredRot = Quaternion.Euler(targetPitch, targetYaw, 0f);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRot,
            1f - Mathf.Exp(-rotationSmooth * Time.deltaTime)
        );

        // Smooth zoom (board)
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            1f - Mathf.Exp(-zoomSmooth * Time.deltaTime)
        );

        // Auto-orbit camera (exhibition)
        if (autoOrbit && cam != null)
        {
            bool canOrbit = (Time.time - lastInteractTime) >= resumeAfterSeconds;
            if (canOrbit) ApplyAutoOrbit();
        }
    }

    // --------- EXHIBITION HELPERS ---------

    public void SetAutoOrbit(bool enabled)
    {
        autoOrbit = enabled;
        if (autoOrbit)
        {
            if (orbitPivot == null) orbitPivot = transform;
            SetupOrbitFromCurrentCamera();
            lastInteractTime = Time.time; // optional: give a tiny delay before it starts moving
        }
    }

    private void SetupOrbitFromCurrentCamera()
    {
        if (cam == null) return;
        if (orbitPivot == null) orbitPivot = transform;

        Vector3 pivotPos = orbitPivot.position;
        Vector3 rel = cam.position - pivotPos;

        // If orbitDistance wasn't set, use current distance
        if (orbitDistance <= 0.0001f) orbitDistance = rel.magnitude;

        // Initialize yaw from current camera position around pivot (ignore Y)
        Vector3 flat = new Vector3(rel.x, 0f, rel.z);
        if (flat.sqrMagnitude > 0.000001f)
        {
            orbitYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }
    }

    private void ApplyAutoOrbit()
    {
        if (orbitPivot == null) orbitPivot = transform;

        orbitYaw += orbitDegreesPerSecond * Time.deltaTime;

        Vector3 pivotPos = orbitPivot.position;
        Vector3 lookAt = pivotPos + Vector3.up * orbitLookAtHeight;

        // Build an orbit position from yaw + pitch + distance
        Quaternion orbitRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        Vector3 offset = orbitRot * (Vector3.back * orbitDistance);
        Vector3 desiredPos = pivotPos + offset;

        float t = 1f - Mathf.Exp(-orbitSmooth * Time.deltaTime);

        cam.position = Vector3.Lerp(cam.position, desiredPos, t);

        Quaternion desiredRot = Quaternion.LookRotation((lookAt - cam.position).normalized, Vector3.up);
        cam.rotation = Quaternion.Slerp(cam.rotation, desiredRot, t);
    }

    // --------- EXISTING PUBLIC API ---------

    public void ResetToHome()
    {
        transform.position = homePos;
        transform.rotation = homeRot;
        transform.localScale = homeScale;

        // Sync targets so smoothing doesn't "fight" the snap
        targetScale = homeScale;
        Vector3 e = homeRot.eulerAngles;
        targetYaw = e.y;
        targetPitch = NormalizePitch(e.x);

        onePinch = false;
        twoPinch = false;

        // Re-sync orbit from the current camera pose if needed
        if (autoOrbit) SetupOrbitFromCurrentCamera();
    }

    private bool IsPinching(Hand h)
    {
        return h != null && h.PinchStrength >= pinchStrength;
    }

    private Vector3 GetPinchWorld(Hand h)
    {
        Vector3 p = h.GetPinchPosition();
        return pinchPositionIsProviderLocal ? provider.transform.TransformPoint(p) : p;
    }

    private float NormalizePitch(float xDeg)
    {
        xDeg %= 360f;
        if (xDeg > 180f) xDeg -= 360f;
        return xDeg;
    }
}
