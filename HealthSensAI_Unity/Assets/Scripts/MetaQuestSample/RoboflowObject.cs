using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Represents a single object detected via Roboflow object detection.
/// Handles enabling/disabling visuals, setting debug text, and auto-hiding after a delay.
/// </summary>
public class RoboflowObject : MonoBehaviour
{
    [Header("Roboflow Object Settings")]
    [SerializeField] private float autoDisableDuration = 1000f; // Time in seconds before this object hides itself again if not tracked.
    [SerializeField] private GameObject debugTextObject; // Reference to the text GameObject (used to rotate it toward camera).
    [SerializeField] private TMPro.TextMeshProUGUI debugText; // Reference to the TextMeshPro component for displaying debug info.
    [SerializeField] private GameObject objectToSpawnPrefab; // Assign in Inspector
    [SerializeField] private UnityEngine.UI.RawImage croppedImageDisplay; // Assign in Inspector

    [Header("Tracked For 3 Seconds")]
    [SerializeField] private GameObject trackedPrefab; // Prefab to instantiate after 3 seconds of tracking
    [SerializeField] private Vector3 trackedSpawnPosition = new Vector3(0, 2, 0); // Fixed position for spawning trackedPrefab

    private string @class = "DefaultObjectName"; // The class name of the detected object (e.g. "bear", "panda").
    public int classID = 0; // The class index (optional), e.g. 0 for bear, 1 for panda.
    private Coroutine autoDisableCoroutine; // Reference to the coroutine used to delay auto-disable.
    private Coroutine trackedCoroutine; // Reference to the coroutine for 3 seconds tracking
    public float Confidence { get; set; }
    private NutritionAnalyzer analyzer; // Assign in Inspector
    public Texture2D CroppedTexture { get; set; }

    private bool isTrackedRoutineStarted = false;



    public UnityEvent OnHandTrigger;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("QuestHand"))
        {
            Vector3 spawnPosition = transform.position + Vector3.up * 0.5f; // 0.5 units above
            Instantiate(objectToSpawnPrefab, spawnPosition, Quaternion.identity);

            OnHandTrigger?.Invoke();
        }
    }

    /// <summary>
    /// Resets this object to its initial state: disabled, zeroed position and rotation.
    /// </summary>
    public void Init(string @class, int classId)
    {
        this.gameObject.SetActive(false);
        this.gameObject.transform.position = Vector3.zero;
        this.gameObject.transform.rotation = Quaternion.identity;
        this.@class = @class;
        this.classID = classId;
        if (analyzer == null) analyzer = GetComponent<NutritionAnalyzer>();
        isTrackedRoutineStarted = false;
    }

    /// <summary>
    /// Gets the class name of this object.
    /// </summary>
    public int ClassID
    {
        get => classID;
    }

    /// <summary>
    /// Sets the debug label text
    /// </summary>
    public void SetDebugText(string text)
    {
        if (debugText != null)
        {
            debugText.text = text;
        }
        ApplyCroppedTextureToUI();
    }

    public void ApplyCroppedTextureToUI()
    {
        if (croppedImageDisplay != null && CroppedTexture != null)
        {
            croppedImageDisplay.texture = CroppedTexture;
        }
    }


    /// <summary>
    /// Enables the object
    /// </summary>
    public void Enable()
    {
        this.gameObject.SetActive(true);
    }

    /// <summary>
    /// Disables the object
    /// </summary>
    public void Disable()
    {
        this.gameObject.SetActive(false);
        isTrackedRoutineStarted = false;
    }

    /// <summary>
    /// Called whenever this object was successfully detected and updated.
    /// </summary>
    /// <param name="position">World position where object was detected.</param>
    /// <param name="CameraPosition">Camera position to face the label toward.</param>
    public void SuccesfullyTracked(Vector3 position, Vector3 CameraPosition)
    {
        this.gameObject.transform.position = position;
        this.debugTextObject.transform.rotation = Quaternion.LookRotation(debugTextObject.transform.position - CameraPosition);
        this.Enable();

        if (autoDisableCoroutine != null)
        {
            StopCoroutine(autoDisableCoroutine);
        }
        autoDisableCoroutine = StartCoroutine(AutoDisableAfterDelay());

        if (!isTrackedRoutineStarted)
        {
            trackedCoroutine = StartCoroutine(TrackedFor3SecondsRoutine());
            isTrackedRoutineStarted = true;
        }
    }


    /// <summary>
    /// Coroutine that waits a few seconds and then disables the object.
    /// </summary>
    private IEnumerator AutoDisableAfterDelay()
    {
        yield return new WaitForSeconds(autoDisableDuration);
        Disable();
    }

    /// <summary>
    /// Coroutine that instantiates trackedPrefab at a fixed position if tracked for 3 seconds.
    /// </summary>
    private IEnumerator TrackedFor3SecondsRoutine()
    {
        yield return new WaitForSeconds(3f);
        if (this.gameObject.activeSelf && trackedPrefab != null)
        {
            Debug.Log("Tracked for 3 seconds");
            Instantiate(trackedPrefab, transform.position, transform.rotation);
            if (CroppedTexture != null && analyzer != null)
            {
                byte[] imgBytes = CroppedTexture.EncodeToPNG();
                string base64Image = System.Convert.ToBase64String(imgBytes);

                StartCoroutine(analyzer.AnalyzeImage(
                    base64Image,
                    (nutrition) =>
                    {
                        debugText.text = "✅ Nutrition Info:\n" + nutrition.ToString();
                    },
                    (error) =>
                    {
                        debugText.text = "❌ " + error;
                    }
                ));
            }
        }
    }
}
