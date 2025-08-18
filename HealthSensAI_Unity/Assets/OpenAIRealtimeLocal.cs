using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class OpenAIRealtimeLocal : MonoBehaviour
{
    private AudioClip recordedClip;
    private bool isRecording = false;

    // Start recording from the default microphone
    public void StartRecording(int durationSeconds = 5)
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("No microphone detected!");
            return;
        }

        recordedClip = Microphone.Start(null, false, durationSeconds, 16000); // 16kHz sample rate
        isRecording = true;
        Debug.Log("Recording started...");
    }

    // Stop recording and send audio
    public void StopRecordingAndSend()
    {
        if (!isRecording) return;

        Microphone.End(null);
        isRecording = false;
        Debug.Log("Recording stopped.");

        byte[] wavData = AudioClipToWav(recordedClip);
        StartCoroutine(GetSessionAndPostAudio(wavData));
    }

    // Converts AudioClip to WAV byte array
    private byte[] AudioClipToWav(AudioClip clip)
    {
        // WAV conversion utility (simple version for PCM 16-bit mono)
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);

        byte[] wav = WavUtility.FromAudioClip(clip); // Use a WAV utility, see below
        return wav;
    }
    
    IEnumerator GetSessionAndPostAudio(byte[] audioBytes)
    {
        string getUrl = "http://localhost:3000/api/session";
        UnityWebRequest getRequest = UnityWebRequest.Get(getUrl);
        yield return getRequest.SendWebRequest();

        if (getRequest.result == UnityWebRequest.Result.Success)
        {
            string sessionData = getRequest.downloadHandler.text;
            // Parse sessionData if needed

            // Now POST audio (replace with your actual POST endpoint)
            string postUrl = "http://localhost:3000/api/responses";
            UnityWebRequest postRequest = new UnityWebRequest(postUrl, "POST");
            postRequest.uploadHandler = new UploadHandlerRaw(audioBytes);
            postRequest.downloadHandler = new DownloadHandlerBuffer();
            postRequest.SetRequestHeader("Content-Type", "audio/wav");

            yield return postRequest.SendWebRequest();

            if (postRequest.result == UnityWebRequest.Result.Success)
                Debug.Log("Audio posted!");
            else
                Debug.LogError("POST error: " + postRequest.error);
        }
        else
        {
            Debug.LogError("GET error: " + getRequest.error);
        }
    }
}