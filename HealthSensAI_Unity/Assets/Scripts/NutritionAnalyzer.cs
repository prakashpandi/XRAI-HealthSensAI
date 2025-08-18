using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

public class NutritionAnalyzer : MonoBehaviour
{
    [SerializeField] private string apiKey = "";
    private const string endpoint = "https://api.openai.com/v1/responses";

    public IEnumerator AnalyzeImage(Texture2D texture, Action<JObject> onSuccess, Action<string> onError)
    {
        if (texture == null)
        {
            onError?.Invoke("Texture2D is null.");
            yield break;
        }

        // Encode texture to PNG and convert to base64
        byte[] pngData = texture.EncodeToPNG();
        if (pngData == null || pngData.Length == 0)
        {
            onError?.Invoke("Failed to encode Texture2D to PNG.");
            yield break;
        }

        string base64Image = Convert.ToBase64String(pngData);

        // Call the existing AnalyzeImage method
        yield return StartCoroutine(AnalyzeImage(base64Image, onSuccess, onError));
    }

    public IEnumerator AnalyzeImage(string base64Image, Action<JObject> onSuccess, Action<string> onError)
    {
        var payload = new JObject
        {
            ["model"] = "gpt-5",
            ["input"] = new JArray
            {
                new JObject
                {
                    ["role"] = "user",
                    ["content"] = new JArray
                    {
                        new JObject
                        {
                            ["type"] = "input_text",
                            ["text"] =
                                "Analyze this image. Return nutrition info in STRICT JSON ONLY, no text. " +
                                "Format: {\"food_found\": bool, \"food\": string, \"calories\": int, \"protein_g\": float, \"fat_g\": float, \"carbs_g\": float}. " +
                                "If no food is detected, set food_found=false and all other fields null or 0."
                        },
                        new JObject
                        {
                            ["type"] = "input_image",
                            ["image_url"] = "data:image/png;base64," + base64Image
                        }
                    }
                }
            }
        };

        string jsonPayload = payload.ToString(Newtonsoft.Json.Formatting.None);

        using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"API Error: {request.error}\n{request.downloadHandler.text}");
                yield break;
            }

            string result = request.downloadHandler.text;
            JObject parsed = JObject.Parse(result);

            string output = null;
            var outputs = parsed["output"] as JArray;
            if (outputs != null)
            {
                foreach (var item in outputs)
                {
                    var contentArray = item["content"] as JArray;
                    if (contentArray != null)
                    {
                        foreach (var content in contentArray)
                        {
                            if (content["type"]?.ToString() == "output_text")
                            {
                                output = content["text"]?.ToString();
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(output)) break;
                }
            }
            if (string.IsNullOrEmpty(output))
            {
                onError?.Invoke("No valid output from model.");
                yield break;
            }

            try
            {
                JObject nutrition = JObject.Parse(output);

                // Safely check the food_found flag
                bool foodFound = nutrition.Value<bool?>("food_found") ?? false;

                if (!foodFound)
                {
                    Debug.Log("🚫 No food detected in the image.");
                }
                else
                {
                    Debug.Log($"✅ Food: {nutrition["food"]}, Calories: {nutrition["calories"]}");
                }

                onSuccess?.Invoke(nutrition);
            }
            catch (Exception e)
            {
                onError?.Invoke("Model returned invalid JSON:\n" + output + "\nError: " + e.Message);
            }
        }
    }
}
