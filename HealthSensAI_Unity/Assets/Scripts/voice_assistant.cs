using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using OpenAI;
using Meta.WitAi.TTS.Utilities;

namespace Samples.Whisper
{
    public class voice_assistant : MonoBehaviour
    {
        [Header("Start / Loop")]
        [SerializeField] private bool autoStartOnPlay = true;
        [SerializeField] private bool autoLoop = true;
        [SerializeField] private float listenDelayAfterTTS = 0.8f;

        [Header("Optional Button")]
        [SerializeField] private Button startTalkingButton;

        [Header("Meta TTS")]
        [SerializeField] private TTSSpeaker ttsSpeaker;

        [Header("Recording")]
        [SerializeField] private int recordDurationSec = 6;
        [SerializeField] private int sampleRate = 44100;
        [SerializeField] private string recordedFileName = "voice_input.wav";

        [Header("Silence End Detection")]
        [Tooltip("If enabled, stop recording early when user is silent to reduce latency.")]
        [SerializeField] private bool endOnSilence = true;
        [Tooltip("Amplitude below this is treated as silence (0..1). Adjust if mic is noisy.")]
        [Range(0f, 0.1f)][SerializeField] private float silenceThreshold = 0.012f;
        [Tooltip("Seconds of continuous silence required to auto-stop.")]
        [Range(0.1f, 2f)][SerializeField] private float requiredSilenceSeconds = 0.8f;
        [Tooltip("Polling interval while listening (ms)")]
        [SerializeField] private int pollIntervalMs = 60;

        [Header("Microphone")]
        [SerializeField] private string preferredMicName = "Microphone Array (Realtek(R) Audio)";

        [Header("OpenAI")]
        [SerializeField] private string openAIApiKey = "";
        [SerializeField] private string sttModel = "whisper-1";
        [SerializeField] private string sttLanguage = "en";
        [SerializeField] private string chatModel = "gpt-4o";

        [Header("Persona / Safety")]
        [TextArea(3, 8)]
        [SerializeField] private string systemPrompt = "";

        [Header("TTS Chunking")]
        [SerializeField] private int ttsChunkSizeChars = 800;

        private OpenAIApi openai;
        private readonly List<ChatMessage> history = new();
        private bool busy;
        private string cachedSystemPrompt; // rebuilt when profile data updates

        private void OnEnable()
        {
            ApiPlayerDataFetcher.ProfileUpdated += OnProfileUpdated;
            RebuildCachedPrompt();
        }

        private void OnDisable()
        {
            ApiPlayerDataFetcher.ProfileUpdated -= OnProfileUpdated;
        }

        private void Awake()
        {
            var key = string.IsNullOrWhiteSpace(openAIApiKey)
                ? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                : openAIApiKey;
            openai = new OpenAIApi(key);

            if (!ttsSpeaker)
#if UNITY_2023_1_OR_NEWER
                ttsSpeaker = FindFirstObjectByType<TTSSpeaker>(FindObjectsInactive.Exclude);
#else
                ttsSpeaker = FindObjectOfType<TTSSpeaker>();
#endif
        }

        private async void Start()
        {
            if (!ttsSpeaker) { Debug.LogError("[Aiden] TTSSpeaker not found in scene."); return; }
            if (startTalkingButton) startTalkingButton.onClick.AddListener(() => _ = StartOneTurn());
            await Task.Delay(100);
            if (autoStartOnPlay && !ttsSpeaker.IsSpeaking) _ = StartOneTurn();
        }

        private async Task StartOneTurn()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            Debug.LogError("[Aiden] Microphone not supported on WebGL.");
            return;
#endif
            if (busy || openai == null || !ttsSpeaker) { if (openai == null) Debug.LogError("[Aiden] OpenAI not initialized."); return; }
            busy = true;

            try
            {
                while (ttsSpeaker.IsSpeaking) await Task.Delay(100);

                if (Microphone.devices == null || Microphone.devices.Length == 0) throw new Exception("No microphone found.");
                string mic = ChooseMicDevice();
                if (string.IsNullOrEmpty(mic)) throw new Exception("No suitable microphone found.");

                var safeCopy = await RecordWithOptionalSilenceEnd(mic);

                byte[] wavBytes = SaveWav.Save(recordedFileName, safeCopy);
                if (wavBytes == null || wavBytes.Length == 0) throw new Exception("Failed to encode WAV.");

                var sttReq = new CreateAudioTranscriptionsRequest
                {
                    FileData = new FileData { Data = wavBytes, Name = "audio.wav" },
                    Model = sttModel,
                    Language = sttLanguage
                };
                var sttRes = await openai.CreateAudioTranscription(sttReq);
                string userText = sttRes.Text?.Trim();
                if (string.IsNullOrEmpty(userText)) { if (autoLoop) { busy = false; _ = KickOffListeningAfterDelay(listenDelayAfterTTS); } return; }

                EnsureSystemPersona();
                history.Add(new ChatMessage { Role = "user", Content = userText });

                var chatReq = new CreateChatCompletionRequest
                {
                    Model = chatModel,
                    Messages = history,
                    Temperature = 0.6f
                };

                var chatRes = await openai.CreateChatCompletion(chatReq);
                if (chatRes.Choices == null || chatRes.Choices.Count == 0) { if (autoLoop) { busy = false; _ = KickOffListeningAfterDelay(listenDelayAfterTTS); } return; }

                var assistantMsg = chatRes.Choices[0].Message;
                string full = assistantMsg.Content?.Trim() ?? "";
                // Personalized greeting for first assistant reply
                bool firstAssistant = !history.Exists(h => h.Role == "assistant");
                if (firstAssistant)
                {
                    var profileUser = ApiPlayerDataFetcher.LoadUser();
                    var uname = profileUser?.username;
                    if (!string.IsNullOrEmpty(uname))
                    {
                        var lower = full.ToLowerInvariant();
                        if (!(lower.StartsWith("hi ") || lower.StartsWith("hello ") || lower.StartsWith("hey ")))
                        {
                            full = $"Hi {uname}, " + full.TrimStart();
                        }
                    }
                }
                history.Add(new ChatMessage { Role = "assistant", Content = full });

                string[] chunks = ChunkForTTS(full, Mathf.Max(200, ttsChunkSizeChars));
                await ttsSpeaker.SpeakQueuedTask(chunks);

                if (autoLoop)
                {
                    busy = false;
                    await KickOffListeningAfterDelay(listenDelayAfterTTS);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[Aiden] " + ex.Message);
            }
            finally
            {
                busy = false;
            }
        }

        private async Task KickOffListeningAfterDelay(float delay)
        {
            if (delay > 0) await Task.Delay(Mathf.RoundToInt(delay * 1000f));
            _ = StartOneTurn();
        }

        private void EnsureSystemPersona()
        {
            if (string.IsNullOrEmpty(cachedSystemPrompt))
                RebuildCachedPrompt();

            if (history.Count > 0 && history[0].Role == "system")
            {
                var m = history[0];
                m.Content = cachedSystemPrompt;
                history[0] = m; // reassign after modification (struct safety)
            }
            else
                history.Insert(0, new ChatMessage { Role = "system", Content = cachedSystemPrompt });
        }

        private async Task<AudioClip> RecordWithOptionalSilenceEnd(string mic)
        {
            var micClip = Microphone.Start(mic, false, recordDurationSec, sampleRate);
            float startGuard = 0f;
            while (Microphone.GetPosition(mic) <= 0 && startGuard < 2f) { startGuard += Time.deltaTime; await Task.Yield(); }
            if (!micClip) throw new Exception("Microphone clip is null.");

            if (!endOnSilence)
            {
                await Task.Delay(recordDurationSec * 1000);
                return FinalizeFullClip(micClip, mic);
            }

            int channels = micClip.channels;
            int maxSamples = sampleRate * recordDurationSec * channels;
            var collected = new List<float>(maxSamples);
            int lastReadPos = 0;
            float silenceTime = 0f;
            float elapsed = 0f;
            while (elapsed < recordDurationSec)
            {
                await Task.Delay(pollIntervalMs);
                elapsed += pollIntervalMs / 1000f;
                int pos = Microphone.GetPosition(mic);
                if (pos < 0) break; // device lost
                int samplesAvailable = pos * channels - lastReadPos;
                if (samplesAvailable > 0)
                {
                    var buffer = new float[samplesAvailable];
                    micClip.GetData(buffer, lastReadPos / channels); // position in frames
                    collected.AddRange(buffer);
                    lastReadPos += samplesAvailable;

                    // Analyze recent chunk amplitude
                    float peak = 0f;
                    for (int i = buffer.Length - 1; i >= 0; i--)
                    {
                        float a = Mathf.Abs(buffer[i]);
                        if (a > peak) peak = a;
                        if (peak >= silenceThreshold) break;
                    }
                    if (peak < silenceThreshold) silenceTime += pollIntervalMs / 1000f; else silenceTime = 0f;
                    if (silenceTime >= requiredSilenceSeconds && collected.Count > sampleRate * channels * 0.8f) // ensure at least some speech
                        break;
                }
            }
            Microphone.End(mic);

            // Trim trailing silence (simple tail scan)
            int tail = collected.Count - 1;
            int minKeep = Mathf.Min(collected.Count, (int)(sampleRate * channels * 0.3f));
            while (tail > minKeep)
            {
                if (Mathf.Abs(collected[tail]) > silenceThreshold * 0.5f) break;
                tail--;
            }
            int finalCount = Mathf.Max(minKeep, tail + 1);
            var finalSamples = collected.GetRange(0, finalCount).ToArray();
            var clip = AudioClip.Create("mic_trimmed", finalCount / channels, channels, sampleRate, false);
            clip.SetData(finalSamples, 0);
            return clip;
        }

        private AudioClip FinalizeFullClip(AudioClip micClip, string mic)
        {
            int channels = micClip.channels;
            var samples = new float[micClip.samples * channels];
            micClip.GetData(samples, 0);
            var safeCopy = AudioClip.Create("mic_copy", micClip.samples, channels, micClip.frequency, false);
            safeCopy.SetData(samples, 0);
            Microphone.End(mic);
            return safeCopy;
        }

        private void OnProfileUpdated()
        {
            RebuildCachedPrompt();
        }

        private void RebuildCachedPrompt()
        {
            var user = ApiPlayerDataFetcher.LoadUser();
            var daily = ApiPlayerDataFetcher.LoadDaily();
            var weekly = ApiPlayerDataFetcher.LoadWeeklyAggregate();
            cachedSystemPrompt = VoiceAssistantPromptBuilder.BuildPrompt(user, daily, weekly);
        }

        private string ChooseMicDevice()
        {
            var devs = Microphone.devices;
            if (devs == null || devs.Length == 0) return null;

            if (!string.IsNullOrWhiteSpace(preferredMicName))
            {
                foreach (var d in devs)
                    if (d.IndexOf(preferredMicName, StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;
            }

            string[] avoid = { "virtual", "speaker", "speakers", "output" };
            foreach (var d in devs)
            {
                var lower = d.ToLowerInvariant();
                bool bad = false; foreach (var a in avoid) if (lower.Contains(a)) { bad = true; break; }
                if (!bad) return d;
            }
            return devs[0];
        }

        private static readonly Regex SentenceBoundary =
            new Regex(@"(?<=[\.!\?])\s+", RegexOptions.Compiled);

        private string ProtectAbbreviations(string text)
        {
            return text
                .Replace("e.g.", "§EG§")
                .Replace("i.e.", "§IE§")
                .Replace("z.B.", "§ZB§")
                .Replace("z. B.", "§ZB§");
        }

        private string RestoreAbbreviations(string text)
        {
            return text
                .Replace("§EG§", "e.g.")
                .Replace("§IE§", "i.e.")
                .Replace("§ZB§", "z.B.");
        }

        private string[] ChunkForTTS(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text)) return new[] { "" };
            string protectedText = ProtectAbbreviations(text.Trim());
            var sentences = SentenceBoundary.Split(protectedText);

            var chunks = new List<string>();
            var sb = new StringBuilder();

            foreach (var raw in sentences)
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;

                if (sb.Length > 0 && sb.Length + 1 + s.Length > maxChars)
                {
                    chunks.Add(RestoreAbbreviations(sb.ToString().Trim()));
                    sb.Clear();
                }

                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s);
            }

            if (sb.Length > 0) chunks.Add(RestoreAbbreviations(sb.ToString().Trim()));
            if (chunks.Count == 0) chunks.Add(RestoreAbbreviations(protectedText));
            return chunks.ToArray();
        }
    }
}
