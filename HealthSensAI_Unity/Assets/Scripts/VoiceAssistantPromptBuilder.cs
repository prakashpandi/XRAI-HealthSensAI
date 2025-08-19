using System.Text;
using UnityEngine;

/// <summary>
/// Builds a dynamic system prompt for the voice assistant using the latest
/// persisted profile & intake data (saved by ApiPlayerDataFetcher).
/// </summary>
public static class VoiceAssistantPromptBuilder
{
    public static string BuildPrompt(User user, Intake daily, Intake weekly)
    {
        if (user == null)
            return "User profile not available yet. Provide general guidance and ask the user to wait until sync finishes.";

        string SafeJoin(string label, string[] arr)
            => arr == null || arr.Length == 0 ? label + " None" : label + " " + string.Join(", ", arr);

        var sb = new StringBuilder();
        sb.AppendLine("You are Aiden, a personalized nutrition & fitness coach running on mixed reality glasses.");
        sb.AppendLine($"User: {user.username} (ID {user.id})");
        sb.AppendLine("Profile:");
        sb.AppendLine($"- Age: {user.age}");
        sb.AppendLine($"- Gender: {user.gender}");
        sb.AppendLine($"- Height: {user.height} cm");
        sb.AppendLine($"- Weight: {user.weight} kg");
        sb.AppendLine($"- Body Fat %: {user.body_fat_percentage}");
        sb.AppendLine($"- Goals: {user.goals}");
        sb.AppendLine("- Allergies: " + (user.allergies == null || user.allergies.Length == 0 ? "None" : string.Join(", ", user.allergies)));
        sb.AppendLine("- Conditions: " + (user.conditions == null || user.conditions.Length == 0 ? "None" : string.Join(", ", user.conditions)));
        sb.AppendLine("- Notes: " + (user.notes == null || user.notes.Length == 0 ? "None" : string.Join("; ", user.notes)));
        sb.AppendLine();
        sb.AppendLine("Latest Intake:");
        sb.AppendLine("- Today: " + (daily == null
            ? "?"
            : $"{daily.calories} kcal, Protein {daily.protein} g, Carbs {daily.carbs} g, Fats {daily.fats} g, Fiber {daily.fiber} g, Water {daily.water} L"));
        sb.AppendLine("- Weekly (aggregate): " + (weekly == null
            ? "?"
            : $"{weekly.calories} kcal, Protein {weekly.protein} g, Carbs {weekly.carbs} g, Fats {weekly.fats} g, Fiber {weekly.fiber} g, Water {weekly.water} L"));
        sb.AppendLine();
        sb.AppendLine("Use this data for personalized, context‑aware coaching. If data is missing, acknowledge it and ask clarifying questions rather than guessing.");
        sb.AppendLine("Safety: Do not diagnose, prescribe medication or supplements, or guess allergens. Encourage professional consultation for medical issues.");
        sb.AppendLine("Style: Be concise, actionable, one step at a time. Offer healthy swaps and end responses with a gentle prompt (e.g., 'Log this meal?' or 'Need a swap?').");
        return sb.ToString().Trim();
    }
}
