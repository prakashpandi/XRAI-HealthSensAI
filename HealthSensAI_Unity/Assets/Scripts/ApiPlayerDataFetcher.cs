using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#region Data Models (match backend JSON exactly)

[Serializable]
public class ApiRoot
{
    public User user;
    public Intake dailyIntakeToday;
    public Intake[] weeklyIntakeThisWeek;
}

[Serializable]
public class User
{
    public string id;
    public string username;
    public int age;
    public string weight;               // "72.50"
    public string height;               // "178.00"
    public string body_fat_percentage;  // "14.50"
    public string gender;               // "male"
    public string goals;                // "muscle_gain"
    public string[] allergies;          // ["nuts","dairy"]
    public string[] conditions;         // ["none"]
    public string[] notes;              // ["Enjoys running", ...]
    public string created_at;
    public string updated_at;
}

[Serializable]
public class Intake
{
    public string id;
    public string user_id;
    public string date;        // ISO string
    public string calories;    // "1744.00"
    public string protein;     // "75.00"
    public string carbs;       // "197.00"
    public string fats;        // "64.00"
    public string fiber;       // "17.00"
    public string water;       // "2.00"
    public string created_at;
    public string updated_at;
}

#endregion

public class ApiPlayerDataFetcher : MonoBehaviour
{
    [Header("API")]
    [Tooltip("Endpoint that returns { user, dailyIntakeToday, weeklyIntakeThisWeek }")]
    public string apiUrl = "";

    public bool fetchOnStart = true;
    public int timeoutSeconds = 15;

    [Header("Auto Update")]
    [Tooltip("Interval in seconds to auto-fetch data. Set to 0 to disable.")]
    public float autoUpdateInterval = 0f;
    private Coroutine autoUpdateCoroutine;
    private bool isFetching;

    [Header("Debug / Fallback")]
    public bool debugMode = false;
    [TextArea(6, 16)]
    public string debugJson = @"{
  ""user"": {
    ""id"": ""1001"",
    ""username"": ""maher"",
    ""age"": 28,
    ""weight"": ""72.50"",
    ""height"": ""178.00"",
    ""body_fat_percentage"": ""14.50"",
    ""gender"": ""male"",
    ""goals"": ""muscle_gain"",
    ""allergies"": [""nuts"", ""dairy""],
    ""conditions"": [""none""],
    ""notes"": [""Enjoys running"", ""Gym 4x per week"", ""he have allergy from bana""],
    ""created_at"": ""2025-08-18T23:45:19.740Z"",
    ""updated_at"": ""2025-08-19T02:46:34.830Z""
  },
  ""dailyIntakeToday"": {
    ""id"": ""913d14eb-523b-4f1a-9876-8a4f999f0a14"",
    ""user_id"": ""1001"",
    ""date"": ""2025-08-19T00:00:00.000Z"",
    ""calories"": ""1744.00"",
    ""protein"": ""75.00"",
    ""carbs"": ""197.00"",
    ""fats"": ""64.00"",
    ""fiber"": ""17.00"",
    ""water"": ""2.00"",
    ""created_at"": ""2025-08-19T04:17:47.906Z"",
    ""updated_at"": ""2025-08-19T04:34:29.789Z""
  },
  ""weeklyIntakeThisWeek"": [
    {
      ""id"": ""65dc49d2-0690-4df1-95a2-d34273a76eee"",
      ""user_id"": ""1001"",
      ""date"": ""2025-08-18T00:00:00.000Z"",
      ""calories"": ""10696.00"",
      ""protein"": ""533.00"",
      ""carbs"": ""1344.00"",
      ""fats"": ""451.00"",
      ""fiber"": ""137.00"",
      ""water"": ""15.00"",
      ""created_at"": ""2025-08-19T04:34:40.447Z"",
      ""updated_at"": ""2025-08-19T04:34:40.447Z""
    }
  ]
}";

    #region PlayerPrefs Keys

    // User
    private const string K_User_Id = "user_id";
    private const string K_User_Username = "user_username";
    private const string K_User_Age = "user_age";
    private const string K_User_Weight = "user_weight";
    private const string K_User_Height = "user_height";
    private const string K_User_BFP = "user_body_fat_percentage";
    private const string K_User_Gender = "user_gender";
    private const string K_User_Goals = "user_goals";
    private const string K_User_Allergies = "user_allergies_csv";
    private const string K_User_Conditions = "user_conditions_csv";
    private const string K_User_Notes = "user_notes_csv";
    private const string K_User_CreatedAt = "user_created_at";
    private const string K_User_UpdatedAt = "user_updated_at";

    // Daily
    private const string K_Daily_Date = "daily_date";
    private const string K_Daily_Cal = "daily_calories";
    private const string K_Daily_Prot = "daily_protein";
    private const string K_Daily_Carbs = "daily_carbs";
    private const string K_Daily_Fats = "daily_fats";
    private const string K_Daily_Fiber = "daily_fiber";
    private const string K_Daily_Water = "daily_water";
    private const string K_Daily_Id = "daily_id";
    private const string K_Daily_UserId = "daily_user_id";
    private const string K_Daily_CreatedAt = "daily_created_at";
    private const string K_Daily_UpdatedAt = "daily_updated_at";

    // Weekly (first entry)
    private const string K_Weekly_Date = "weekly_date";
    private const string K_Weekly_Cal = "weekly_calories";
    private const string K_Weekly_Prot = "weekly_protein";
    private const string K_Weekly_Carbs = "weekly_carbs";
    private const string K_Weekly_Fats = "weekly_fats";
    private const string K_Weekly_Fiber = "weekly_fiber";
    private const string K_Weekly_Water = "weekly_water";
    private const string K_Weekly_Id = "weekly_id";
    private const string K_Weekly_UserId = "weekly_user_id";
    private const string K_Weekly_CreatedAt = "weekly_created_at";
    private const string K_Weekly_UpdatedAt = "weekly_updated_at";

    // Weekly (full array persistence)
    private const string K_Weekly_Count = "weekly_count"; // number of stored entries
    private const string K_Weekly_Prefix = "weekly_";     // weekly_{i}_field

    // Raw JSON snapshot
    private const string K_Raw_Json = "profile_raw_json";

    #endregion

    [Header("Logging")]
    [Tooltip("If true, prints verbose debug logs for fetch, parse, save and auto-update steps.")]
    public bool verboseLogging = true;

    // Fired every time fresh profile/intake data is successfully parsed & persisted
    public static event Action ProfileUpdated;
    private static DateTime _lastProfileUpdateTime;
    public static DateTime LastProfileUpdateTime => _lastProfileUpdateTime;

    private void Log(string msg)
    {
        if (verboseLogging)
            Debug.Log($"[ApiPlayerDataFetcher] {msg}");
    }

    private void Start()
    {
        if (debugMode)
        {
            Log("Debug mode active - using embedded JSON sample");
            TryParseAndSave(debugJson);
            return;
        }

        if (fetchOnStart && !string.IsNullOrEmpty(apiUrl))
        {
            Log("Starting initial fetch");
            StartCoroutine(FetchAndSave());
        }

        if (autoUpdateInterval > 0f && !string.IsNullOrEmpty(apiUrl))
        {
            StartAutoUpdate();
        }
    }

    public IEnumerator FetchAndSave()
    {
        if (isFetching)
        {
            Log("Fetch skipped (already in progress)");
            yield break;
        }
        isFetching = true;
        Log($"GET {apiUrl}");
        using (var req = UnityWebRequest.Get(apiUrl))
        {
            req.timeout = Mathf.Max(1, timeoutSeconds);
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Log($"Request failed: {req.error} (code {(req.responseCode)} )");
                yield break;
            }
            Log($"Request success ({req.downloadHandler?.text?.Length ?? 0} chars)");
            TryParseAndSave(req.downloadHandler.text);
        }
        isFetching = false;
        Log("Fetch cycle complete");
    }

    private IEnumerator AutoUpdateRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(autoUpdateInterval);
            Log("Auto-update tick");
            if (!string.IsNullOrEmpty(apiUrl) && autoUpdateInterval > 0f)
                yield return FetchAndSave();
            else
                yield return null;
        }
    }

    public void StartAutoUpdate()
    {
        if (autoUpdateCoroutine != null) return;
        if (autoUpdateInterval <= 0f) return;
        Log($"Starting auto-update every {autoUpdateInterval} seconds");
        autoUpdateCoroutine = StartCoroutine(AutoUpdateRoutine());
    }

    public void StopAutoUpdate()
    {
        if (autoUpdateCoroutine != null)
        {
            StopCoroutine(autoUpdateCoroutine);
            autoUpdateCoroutine = null;
            Log("Stopped auto-update");
        }
    }

    private void OnDisable()
    {
        StopAutoUpdate();
    }

    private void TryParseAndSave(string json)
    {
        ApiRoot root = null;
        try
        {
            root = JsonUtility.FromJson<ApiRoot>(json);
        }
        catch { /* ignore */ }

        if (root == null || root.user == null)
        {
            Log("Parse failed or missing 'user' field");
            return;
        }
        Log($"Parsed user '{root.user.username}' age {root.user.age}; daily calories: {root.dailyIntakeToday?.calories ?? "?"}");
        if (root.weeklyIntakeThisWeek != null)
            Log($"Weekly entries: {root.weeklyIntakeThisWeek.Length}");
        // Detailed field dump (can be toggled off by verboseLogging flag)
        if (verboseLogging)
        {
            var u = root.user;
            Log($"User weight={u.weight} height={u.height} body_fat%={u.body_fat_percentage} gender={u.gender} goals={u.goals}");
            Log($"User allergies=[{string.Join(";", u.allergies ?? new string[0])}] conditions=[{string.Join(";", u.conditions ?? new string[0])}] notes=[{string.Join(";", u.notes ?? new string[0])}]");
            if (root.dailyIntakeToday != null)
            {
                var d = root.dailyIntakeToday;
                Log($"Daily id={d.id} date={d.date} cal={d.calories} P={d.protein} C={d.carbs} F={d.fats} Fib={d.fiber} WaterL={d.water}");
            }
            if (root.weeklyIntakeThisWeek != null)
            {
                for (int i = 0; i < root.weeklyIntakeThisWeek.Length; i++)
                {
                    var w = root.weeklyIntakeThisWeek[i];
                    Log($"Week[{i}] id={w.id} date={w.date} cal={w.calories} P={w.protein} C={w.carbs} F={w.fats} Fib={w.fiber} WaterL={w.water}");
                }
            }
        }
        SaveAllToPrefs(root);
    }

    #region Save / Load

    private static string JoinCSV(string[] arr)
    {
        if (arr == null || arr.Length == 0) return string.Empty;
        return string.Join(",", arr);
    }

    private static string[] SplitCSV(string csv)
    {
        if (string.IsNullOrEmpty(csv)) return Array.Empty<string>();
        return csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void SaveAllToPrefs(ApiRoot r)
    {
        // Raw JSON snapshot
        try
        {
            var raw = JsonUtility.ToJson(r);
            PlayerPrefs.SetString(K_Raw_Json, raw);
        }
        catch { /* ignore */ }

        // User
        PlayerPrefs.SetString(K_User_Id, r.user.id ?? "");
        PlayerPrefs.SetString(K_User_Username, r.user.username ?? "");
        PlayerPrefs.SetInt(K_User_Age, r.user.age);
        PlayerPrefs.SetString(K_User_Weight, r.user.weight ?? "");
        PlayerPrefs.SetString(K_User_Height, r.user.height ?? "");
        PlayerPrefs.SetString(K_User_BFP, r.user.body_fat_percentage ?? "");
        PlayerPrefs.SetString(K_User_Gender, r.user.gender ?? "");
        PlayerPrefs.SetString(K_User_Goals, r.user.goals ?? "");
        PlayerPrefs.SetString(K_User_Allergies, JoinCSV(r.user.allergies));
        PlayerPrefs.SetString(K_User_Conditions, JoinCSV(r.user.conditions));
        PlayerPrefs.SetString(K_User_Notes, JoinCSV(r.user.notes));
        PlayerPrefs.SetString(K_User_CreatedAt, r.user.created_at ?? "");
        PlayerPrefs.SetString(K_User_UpdatedAt, r.user.updated_at ?? "");

        // Daily
        if (r.dailyIntakeToday != null)
        {
            var d = r.dailyIntakeToday;
            PlayerPrefs.SetString(K_Daily_Id, d.id ?? "");
            PlayerPrefs.SetString(K_Daily_UserId, d.user_id ?? "");
            PlayerPrefs.SetString(K_Daily_Date, d.date ?? "");
            PlayerPrefs.SetString(K_Daily_Cal, d.calories ?? "");
            PlayerPrefs.SetString(K_Daily_Prot, d.protein ?? "");
            PlayerPrefs.SetString(K_Daily_Carbs, d.carbs ?? "");
            PlayerPrefs.SetString(K_Daily_Fats, d.fats ?? "");
            PlayerPrefs.SetString(K_Daily_Fiber, d.fiber ?? "");
            PlayerPrefs.SetString(K_Daily_Water, d.water ?? "");
            PlayerPrefs.SetString(K_Daily_CreatedAt, d.created_at ?? "");
            PlayerPrefs.SetString(K_Daily_UpdatedAt, d.updated_at ?? "");
        }

        // Weekly (take first element if exists)
        if (r.weeklyIntakeThisWeek != null && r.weeklyIntakeThisWeek.Length > 0)
        {
            var w = r.weeklyIntakeThisWeek[0];
            PlayerPrefs.SetString(K_Weekly_Id, w.id ?? "");
            PlayerPrefs.SetString(K_Weekly_UserId, w.user_id ?? "");
            PlayerPrefs.SetString(K_Weekly_Date, w.date ?? "");
            PlayerPrefs.SetString(K_Weekly_Cal, w.calories ?? "");
            PlayerPrefs.SetString(K_Weekly_Prot, w.protein ?? "");
            PlayerPrefs.SetString(K_Weekly_Carbs, w.carbs ?? "");
            PlayerPrefs.SetString(K_Weekly_Fats, w.fats ?? "");
            PlayerPrefs.SetString(K_Weekly_Fiber, w.fiber ?? "");
            PlayerPrefs.SetString(K_Weekly_Water, w.water ?? "");
            PlayerPrefs.SetString(K_Weekly_CreatedAt, w.created_at ?? "");
            PlayerPrefs.SetString(K_Weekly_UpdatedAt, w.updated_at ?? "");
        }

        // Store entire weekly array
        var weekly = r.weeklyIntakeThisWeek;
        int count = weekly != null ? weekly.Length : 0;
        PlayerPrefs.SetInt(K_Weekly_Count, count);
        // Clean previous (up to a small max, e.g., 14)
        for (int old = count; old < 14; old++)
        {
            string baseKey = K_Weekly_Prefix + old + "_";
            PlayerPrefs.DeleteKey(baseKey + "id");
            PlayerPrefs.DeleteKey(baseKey + "date");
            PlayerPrefs.DeleteKey(baseKey + "calories");
            PlayerPrefs.DeleteKey(baseKey + "protein");
            PlayerPrefs.DeleteKey(baseKey + "carbs");
            PlayerPrefs.DeleteKey(baseKey + "fats");
            PlayerPrefs.DeleteKey(baseKey + "fiber");
            PlayerPrefs.DeleteKey(baseKey + "water");
        }
        for (int i = 0; i < count; i++)
        {
            var wi = weekly[i];
            string baseKey = K_Weekly_Prefix + i + "_";
            PlayerPrefs.SetString(baseKey + "id", wi.id ?? "");
            PlayerPrefs.SetString(baseKey + "user_id", wi.user_id ?? "");
            PlayerPrefs.SetString(baseKey + "date", wi.date ?? "");
            PlayerPrefs.SetString(baseKey + "calories", wi.calories ?? "");
            PlayerPrefs.SetString(baseKey + "protein", wi.protein ?? "");
            PlayerPrefs.SetString(baseKey + "carbs", wi.carbs ?? "");
            PlayerPrefs.SetString(baseKey + "fats", wi.fats ?? "");
            PlayerPrefs.SetString(baseKey + "fiber", wi.fiber ?? "");
            PlayerPrefs.SetString(baseKey + "water", wi.water ?? "");
            PlayerPrefs.SetString(baseKey + "created_at", wi.created_at ?? "");
            PlayerPrefs.SetString(baseKey + "updated_at", wi.updated_at ?? "");
        }

        PlayerPrefs.Save();

        _lastProfileUpdateTime = DateTime.UtcNow;
        try { ProfileUpdated?.Invoke(); } catch (Exception ex) { Debug.LogWarning($"[ApiPlayerDataFetcher] ProfileUpdated listeners threw: {ex.Message}"); }
    }

    // Example typed loaders (optional to use elsewhere in your app)
    public static User LoadUser()
    {
        if (!PlayerPrefs.HasKey(K_User_Id)) return null;

        return new User
        {
            id = PlayerPrefs.GetString(K_User_Id, ""),
            username = PlayerPrefs.GetString(K_User_Username, ""),
            age = PlayerPrefs.GetInt(K_User_Age, 0),
            weight = PlayerPrefs.GetString(K_User_Weight, ""),
            height = PlayerPrefs.GetString(K_User_Height, ""),
            body_fat_percentage = PlayerPrefs.GetString(K_User_BFP, ""),
            gender = PlayerPrefs.GetString(K_User_Gender, ""),
            goals = PlayerPrefs.GetString(K_User_Goals, ""),
            allergies = SplitCSV(PlayerPrefs.GetString(K_User_Allergies, "")),
            conditions = SplitCSV(PlayerPrefs.GetString(K_User_Conditions, "")),
            notes = SplitCSV(PlayerPrefs.GetString(K_User_Notes, "")),
            created_at = PlayerPrefs.GetString(K_User_CreatedAt, ""),
            updated_at = PlayerPrefs.GetString(K_User_UpdatedAt, "")
        };
    }

    public static Intake LoadDaily()
    {
        if (!PlayerPrefs.HasKey(K_Daily_Date)) return null;

        return new Intake
        {
            id = PlayerPrefs.GetString(K_Daily_Id, ""),
            user_id = PlayerPrefs.GetString(K_Daily_UserId, ""),
            date = PlayerPrefs.GetString(K_Daily_Date, ""),
            calories = PlayerPrefs.GetString(K_Daily_Cal, ""),
            protein = PlayerPrefs.GetString(K_Daily_Prot, ""),
            carbs = PlayerPrefs.GetString(K_Daily_Carbs, ""),
            fats = PlayerPrefs.GetString(K_Daily_Fats, ""),
            fiber = PlayerPrefs.GetString(K_Daily_Fiber, ""),
            water = PlayerPrefs.GetString(K_Daily_Water, ""),
            created_at = PlayerPrefs.GetString(K_Daily_CreatedAt, ""),
            updated_at = PlayerPrefs.GetString(K_Daily_UpdatedAt, "")
        };
    }

    public static Intake LoadWeeklyAggregate()
    {
        if (!PlayerPrefs.HasKey(K_Weekly_Date)) return null;

        return new Intake
        {
            id = PlayerPrefs.GetString(K_Weekly_Id, ""),
            user_id = PlayerPrefs.GetString(K_Weekly_UserId, ""),
            date = PlayerPrefs.GetString(K_Weekly_Date, ""),
            calories = PlayerPrefs.GetString(K_Weekly_Cal, ""),
            protein = PlayerPrefs.GetString(K_Weekly_Prot, ""),
            carbs = PlayerPrefs.GetString(K_Weekly_Carbs, ""),
            fats = PlayerPrefs.GetString(K_Weekly_Fats, ""),
            fiber = PlayerPrefs.GetString(K_Weekly_Fiber, ""),
            water = PlayerPrefs.GetString(K_Weekly_Water, ""),
            created_at = PlayerPrefs.GetString(K_Weekly_CreatedAt, ""),
            updated_at = PlayerPrefs.GetString(K_Weekly_UpdatedAt, "")
        };
    }

    public static Intake[] LoadWeeklyAll()
    {
        int count = PlayerPrefs.GetInt(K_Weekly_Count, 0);
        if (count <= 0) return Array.Empty<Intake>();
        var list = new Intake[count];
        for (int i = 0; i < count; i++)
        {
            string baseKey = K_Weekly_Prefix + i + "_";
            list[i] = new Intake
            {
                id = PlayerPrefs.GetString(baseKey + "id", ""),
                user_id = PlayerPrefs.GetString(baseKey + "user_id", ""),
                date = PlayerPrefs.GetString(baseKey + "date", ""),
                calories = PlayerPrefs.GetString(baseKey + "calories", ""),
                protein = PlayerPrefs.GetString(baseKey + "protein", ""),
                carbs = PlayerPrefs.GetString(baseKey + "carbs", ""),
                fats = PlayerPrefs.GetString(baseKey + "fats", ""),
                fiber = PlayerPrefs.GetString(baseKey + "fiber", ""),
                water = PlayerPrefs.GetString(baseKey + "water", ""),
                created_at = PlayerPrefs.GetString(baseKey + "created_at", ""),
                updated_at = PlayerPrefs.GetString(baseKey + "updated_at", "")
            };
        }
        return list;
    }

    [ContextMenu("Debug/Log Stored Data Snapshot")]
    public void LogStoredData()
    {
        var u = LoadUser();
        var d = LoadDaily();
        var wAgg = LoadWeeklyAggregate();
        var wAll = LoadWeeklyAll();
        Log($"STORED USER id={u?.id} user={u?.username} age={u?.age} weight={u?.weight} height={u?.height} body_fat%={u?.body_fat_percentage}");
        Log($"STORED DAILY id={d?.id} date={d?.date} cal={d?.calories} P={d?.protein} C={d?.carbs} F={d?.fats} Fib={d?.fiber} Water={d?.water}");
        Log($"STORED WEEKLY[0] id={wAgg?.id} date={wAgg?.date} cal={wAgg?.calories} P={wAgg?.protein} C={wAgg?.carbs} F={wAgg?.fats} Fib={wAgg?.fiber} Water={wAgg?.water}");
        for (int i = 0; i < wAll.Length; i++)
        {
            var wi = wAll[i];
            Log($"WEEKLY[{i}] id={wi.id} date={wi.date} cal={wi.calories} P={wi.protein} C={wi.carbs} F={wi.fats} Fib={wi.fiber} Water={wi.water}");
        }
    }

    public static void ClearAllPrefs()
    {
        // User
        PlayerPrefs.DeleteKey(K_User_Id);
        PlayerPrefs.DeleteKey(K_User_Username);
        PlayerPrefs.DeleteKey(K_User_Age);
        PlayerPrefs.DeleteKey(K_User_Weight);
        PlayerPrefs.DeleteKey(K_User_Height);
        PlayerPrefs.DeleteKey(K_User_BFP);
        PlayerPrefs.DeleteKey(K_User_Gender);
        PlayerPrefs.DeleteKey(K_User_Goals);
        PlayerPrefs.DeleteKey(K_User_Allergies);
        PlayerPrefs.DeleteKey(K_User_Conditions);
        PlayerPrefs.DeleteKey(K_User_Notes);
        PlayerPrefs.DeleteKey(K_User_CreatedAt);
        PlayerPrefs.DeleteKey(K_User_UpdatedAt);

        // Daily
        PlayerPrefs.DeleteKey(K_Daily_Date);
        PlayerPrefs.DeleteKey(K_Daily_Cal);
        PlayerPrefs.DeleteKey(K_Daily_Prot);
        PlayerPrefs.DeleteKey(K_Daily_Carbs);
        PlayerPrefs.DeleteKey(K_Daily_Fats);
        PlayerPrefs.DeleteKey(K_Daily_Fiber);
        PlayerPrefs.DeleteKey(K_Daily_Water);
        PlayerPrefs.DeleteKey(K_Daily_Id);
        PlayerPrefs.DeleteKey(K_Daily_UserId);
        PlayerPrefs.DeleteKey(K_Daily_CreatedAt);
        PlayerPrefs.DeleteKey(K_Daily_UpdatedAt);

        // Weekly
        PlayerPrefs.DeleteKey(K_Weekly_Date);
        PlayerPrefs.DeleteKey(K_Weekly_Cal);
        PlayerPrefs.DeleteKey(K_Weekly_Prot);
        PlayerPrefs.DeleteKey(K_Weekly_Carbs);
        PlayerPrefs.DeleteKey(K_Weekly_Fats);
        PlayerPrefs.DeleteKey(K_Weekly_Fiber);
        PlayerPrefs.DeleteKey(K_Weekly_Water);
        PlayerPrefs.DeleteKey(K_Weekly_Id);
        PlayerPrefs.DeleteKey(K_Weekly_UserId);
        PlayerPrefs.DeleteKey(K_Weekly_CreatedAt);
        PlayerPrefs.DeleteKey(K_Weekly_UpdatedAt);

        // Weekly array stored entries
        int existing = PlayerPrefs.GetInt(K_Weekly_Count, 0);
        for (int i = 0; i < existing; i++)
        {
            string baseKey = K_Weekly_Prefix + i + "_";
            PlayerPrefs.DeleteKey(baseKey + "id");
            PlayerPrefs.DeleteKey(baseKey + "date");
            PlayerPrefs.DeleteKey(baseKey + "calories");
            PlayerPrefs.DeleteKey(baseKey + "protein");
            PlayerPrefs.DeleteKey(baseKey + "carbs");
            PlayerPrefs.DeleteKey(baseKey + "fats");
            PlayerPrefs.DeleteKey(baseKey + "fiber");
            PlayerPrefs.DeleteKey(baseKey + "water");
        }
        PlayerPrefs.DeleteKey(K_Weekly_Count);
        PlayerPrefs.DeleteKey(K_Raw_Json);

        PlayerPrefs.Save();
    }

    #endregion
}
