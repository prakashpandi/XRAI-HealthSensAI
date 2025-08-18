using UnityEngine;
using System.Collections;
using Newtonsoft.Json.Linq;

public class NutritionDemo : MonoBehaviour
{
    public NutritionAnalyzer analyzer;
    public Texture2D testImage; // assign in Inspector

    public void CallOpenAI()
    {
        if (analyzer == null) analyzer = GetComponent<NutritionAnalyzer>();

        byte[] imgBytes = testImage.EncodeToPNG();
        string base64Image = System.Convert.ToBase64String(imgBytes);

        StartCoroutine(analyzer.AnalyzeImage(
            base64Image,
            (nutrition) =>
            {
                Debug.Log("✅ Nutrition Info:\n" + nutrition.ToString());
            },
            (error) =>
            {
                Debug.LogError("❌ " + error);
            }
        ));
    }
}