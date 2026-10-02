using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace UnityVolumeRendering
{
    public sealed partial class VolumeSTCubeQuestSpatialWorkbench
    {
        private void AppendVrKeyboardText(string value)
        {
            prompt += value;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void OpenTextKeyboard()
        {
            if (IsPending(PendingJob.Intent))
            {
                RejectDesktopAction(
                    "Wait for the current Intent request before editing the task.");
                return;
            }
            Debug.Log("[QuestVoice] TYPE pressed. jobRunning=" + IsPending(PendingJob.Analysis));
            input.voiceReviewPending = false;
            input.voiceInputActive = false;
            input.textInputActive = true;
            input.keyboardInputWasVoice = false;
            vrKeyboardOriginalPrompt = prompt;
            input.vrKeyboardVisible = true;
            SetStatus("Point and click the VR keyboard. DONE saves the task.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

        private void VrKeyboardBackspace()
        {
            if (prompt.Length > 0)
                prompt = prompt.Substring(0, prompt.Length - 1);
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void VrKeyboardClear()
        {
            prompt = string.Empty;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
        }

        private void CloseVrKeyboard(bool commit)
        {
            if (!commit)
                prompt = vrKeyboardOriginalPrompt;
            input.vrKeyboardVisible = false;
            input.textInputActive = false;
            input.voiceReviewPending = commit;
            if (commit)
            {
                SlabLabSettings.SetSpatialPrompt(prompt);
                intentConfigured = false;
                intentResolutionError = string.Empty;
                SetStatus("Typed task ready. Apply it or edit again.");
            }
            else
                SetStatus("Text editing cancelled.");
            BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private void ConfirmVoiceInput()
        {
            input.voiceInputActive = false;
            input.textInputActive = false;
            input.voiceReviewPending = false;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            SlabLabSettings.SetSpatialPrompt(prompt);
            SetStatus("Recognized intent text confirmed. Select Resolve & Apply.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }

        private void StartVoiceInput()
        {
            if (IsPending(PendingJob.Intent))
            {
                RejectDesktopAction(
                    "Wait for the current Intent request before editing the task.");
                return;
            }
            // Editing the next analysis task is independent of an existing
            // materialization/digest job.  The previous guard made both VOICE
            // and TYPE appear clickable while silently ignoring the click.
            Debug.Log("[QuestVoice] VOICE pressed. recording=" +
                input.questVoiceRecording + " uploading=" + input.questVoiceUploading +
                " jobRunning=" + IsPending(PendingJob.Analysis));
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (input.questVoiceRecording)
            {
                StopQuestVoiceRecordingAndTranscribe();
                return;
            }
            if (input.questVoiceUploading)
                return;
#endif
            input.voiceReviewPending = false;
            input.voiceInputActive = true;
            input.textInputActive = false;
            input.keyboardInputWasVoice = true;
#if UNITY_EDITOR || SLABLAB_FLAT
            if (VolumeSTCubeQuestBootstrap.IsDesktopPreviewEnabled)
            {
                input.desktopEditingPrompt = true;
                SetStatus("Voice-input desktop fallback: type in the Game view. " +
                    "Enter or Esc moves the transcript to review.");
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                return;
            }
#endif
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
            {
                input.voiceInputActive = false;
                SetStatus("Microphone permission is required for Quest voice input.");
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Microphone);
                if (questVoicePermissionCoroutine != null)
                    StopCoroutine(questVoicePermissionCoroutine);
                questVoicePermissionCoroutine = StartCoroutine(
                    WaitForQuestMicrophonePermission());
                return;
            }
#endif
            OpenQuestSystemVoiceKeyboard();
        }

        private void OpenQuestSystemVoiceKeyboard()
        {
            input.voiceReviewPending = false;
            input.voiceInputActive = true;
            input.textInputActive = false;
            input.keyboardInputWasVoice = true;
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            StartQuestVoiceRecording();
#else
            input.desktopEditingPrompt = true;
            SetStatus("Desktop voice fallback: type the transcript, then press Enter.");
#endif
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
        }

        private void SetPrompt(string value)
        {
            prompt = value;
            SlabLabSettings.SetSpatialPrompt(prompt);
            BuildStage();
        }

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private void StartQuestVoiceRecording()
        {
            try
            {
                string[] devices = Microphone.devices;
                questVoiceDevice = devices != null && devices.Length > 0
                    ? devices[0] : string.Empty;
                questVoiceClip = Microphone.Start(questVoiceDevice, false, 10, 16000);
                if (questVoiceClip == null)
                    throw new InvalidOperationException("Quest microphone did not start.");
                Debug.Log("[QuestVoice] Microphone.Start succeeded. device='" +
                    questVoiceDevice + "' devices=" +
                    (devices == null ? 0 : devices.Length));
                input.questVoiceRecording = true;
                input.questVoiceUploading = false;
                if (questVoiceAutoStopCoroutine != null)
                    StopCoroutine(questVoiceAutoStopCoroutine);
                questVoiceAutoStopCoroutine = StartCoroutine(
                    AutoStopQuestVoiceRecording());
                SetStatus("RECORDING: speak now. Press STOP when finished.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestVoice] Microphone start failed: " + exception);
                input.questVoiceRecording = false;
                input.voiceInputActive = false;
                SetStatus("Microphone could not start: " + exception.Message +
                    ". Use TYPE instead.");
            }
        }
        private IEnumerator AutoStopQuestVoiceRecording()
        {
            yield return new WaitForSecondsRealtime(8.0f);
            questVoiceAutoStopCoroutine = null;
            if (input.questVoiceRecording)
                StopQuestVoiceRecordingAndTranscribe();
        }
        private void StopQuestVoiceRecordingAndTranscribe()
        {
            if (!input.questVoiceRecording || questVoiceClip == null)
            {
                Debug.LogWarning("[QuestVoice] STOP ignored because no recording is active.");
                return;
            }
            int sampleCount = Mathf.Max(1,
                Microphone.GetPosition(questVoiceDevice));
            Debug.Log("[QuestVoice] STOP pressed. capturedFrames=" + sampleCount);
            Microphone.End(questVoiceDevice);
            input.questVoiceRecording = false;
            if (questVoiceAutoStopCoroutine != null)
            {
                StopCoroutine(questVoiceAutoStopCoroutine);
                questVoiceAutoStopCoroutine = null;
            }
            byte[] wav = EncodeWav(questVoiceClip, sampleCount);
            Debug.Log("[QuestVoice] WAV encoded. bytes=" + wav.Length);
            Destroy(questVoiceClip);
            questVoiceClip = null;
            input.questVoiceUploading = true;
            input.voiceInputActive = true;
            SetStatus("TRANSCRIBING: sending the recording to S4D...");
            BuildIntentPanel();
            VolumeSTCubeS4DAnalysisClient voiceClient =
                new VolumeSTCubeS4DAnalysisClient(s4dUrl, 90, 0.5f);
            StartCoroutine(voiceClient.TranscribeAudio(wav,
                OnQuestVoiceTranscribed));
        }
        private void OnQuestVoiceTranscribed(string transcript, string error)
        {
            Debug.Log("[QuestVoice] Transcription completed. transcriptChars=" +
                (transcript == null ? 0 : transcript.Length) + " error='" +
                (error ?? string.Empty) + "'");
            input.questVoiceUploading = false;
            input.voiceInputActive = false;
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                prompt = transcript.Trim();
                input.voiceReviewPending = true;
                intentConfigured = false;
                intentResolutionError = string.Empty;
                SlabLabSettings.SetSpatialPrompt(prompt);
                SetStatus("Voice transcript ready. CONFIRM it or TYPE to edit.");
            }
            else
            {
                input.voiceReviewPending = false;
                SetStatus(string.IsNullOrWhiteSpace(error)
                    ? "No speech was recognized. Try again or use TYPE."
                    : error);
            }
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }
        private static byte[] EncodeWav(AudioClip clip, int sampleFrames)
        {
            int channels = Mathf.Max(1, clip.channels);
            int frames = Mathf.Clamp(sampleFrames, 1, clip.samples);
            float[] samples = new float[frames * channels];
            clip.GetData(samples, 0);
            using (MemoryStream stream = new MemoryStream(44 + samples.Length * 2))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int dataLength = samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);
                for (int index = 0; index < samples.Length; index++)
                    writer.Write((short)Mathf.RoundToInt(
                        Mathf.Clamp(samples[index], -1.0f, 1.0f) * 32767.0f));
                writer.Flush();
                return stream.ToArray();
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private IEnumerator WaitForQuestMicrophonePermission()
        {
            float deadline = Time.realtimeSinceStartup + 12.0f;
            while (Time.realtimeSinceStartup < deadline &&
                !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Microphone))
                yield return null;
            questVoicePermissionCoroutine = null;
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
            {
                SetStatus("Microphone permission was not granted. Use the Quest keyboard or type the intent.");
                input.voiceInputActive = false;
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                yield break;
            }
            OpenQuestSystemVoiceKeyboard();
        }
#endif

        private void UpdateKeyboard()
        {
#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
            UpdateQuestNativeSpeechRecognizer();
#endif
#if UNITY_EDITOR || SLABLAB_FLAT
            // The typed Time-range entry owns the keyboard while it is open, the
            // same way the desktop prompt below does. Additive: the drag path
            // never sets this flag.
            if (input.boundaryRangeEntry)
            {
                bool commitRange = false;
                string rangeTyped = Input.inputString;
                for (int index = 0; index < rangeTyped.Length; index++)
                {
                    char character = rangeTyped[index];
                    if (character == '\b')
                    {
                        if (boundaryRangeEntryText.Length > 0)
                            boundaryRangeEntryText =
                                boundaryRangeEntryText.Substring(0,
                                    boundaryRangeEntryText.Length - 1);
                    }
                    else if (character == '\n' || character == '\r')
                    {
                        commitRange = true;
                    }
                    else if (!char.IsControl(character) &&
                        boundaryRangeEntryText.Length < 12)
                    {
                        boundaryRangeEntryText += character;
                    }
                }
                RefreshBoundaryEntryLabel();
                if (Input.GetKeyDown(KeyCode.Escape))
                {
                    DesktopCancelRangeEntry();
                    return;
                }
                if (commitRange)
                {
                    DesktopApplyRangeEntry();
                    return;
                }
                return;
            }
            if (input.desktopEditingPrompt)
            {
                bool commit = false;
                string typed = Input.inputString;
                for (int index = 0; index < typed.Length; index++)
                {
                    char character = typed[index];
                    if (character == '\b')
                    {
                        if (prompt.Length > 0)
                            prompt = prompt.Substring(0, prompt.Length - 1);
                    }
                    else if (character == '\n' || character == '\r')
                    {
                        commit = true;
                    }
                    else if (!char.IsControl(character))
                    {
                        prompt += character;
                    }
                }
                if (promptText != null)
                    promptText.text = prompt;
                if (intentPromptText != null)
                    intentPromptText.text = prompt;
                if (Input.GetKeyDown(KeyCode.Escape))
                    commit = true;
                if (!commit)
                    return;

                input.desktopEditingPrompt = false;
                input.voiceInputActive = false;
                input.textInputActive = false;
                input.voiceReviewPending = true;
                intentConfigured = false;
                intentResolutionError = string.Empty;
                SetStatus(input.keyboardInputWasVoice
                    ? "Voice transcript ready. Confirm it, record again, or edit by typing."
                    : "Typed task ready. Confirm it, or continue editing.");
                if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                    BuildIntentPanel();
                RefreshIntentSurfaces();
                BuildStage();
                return;
            }
#endif
            if (keyboard == null)
                return;
            prompt = keyboard.text;
            if (promptText != null)
                promptText.text = prompt;
            if (intentPromptText != null)
                intentPromptText.text = prompt;
            if (keyboard.status == TouchScreenKeyboard.Status.Visible)
                return;
            SlabLabSettings.SetSpatialPrompt(prompt);
            keyboard = null;
            input.voiceInputActive = false;
            input.textInputActive = false;
            input.voiceReviewPending = true;
            intentConfigured = false;
            intentResolutionError = string.Empty;
            SetStatus(input.keyboardInputWasVoice
                ? "Quest voice transcript ready. Confirm it, record again, or edit by typing."
                : "Typed task ready. Confirm it, or continue editing.");
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
            BuildStage();
        }

#if UNITY_ANDROID && !UNITY_EDITOR && !SLABLAB_FLAT
        private sealed class QuestSpeechRecognitionListener : AndroidJavaProxy
        {
            private readonly object gate = new object();
            private string transcript = string.Empty;
            private bool final;
            private int error = -1;
            private bool changed;

            public QuestSpeechRecognitionListener()
                : base("android.speech.RecognitionListener") { }

            public void onReadyForSpeech(AndroidJavaObject parameters) { }
            public void onBeginningOfSpeech() { }
            public void onRmsChanged(float rmsdB) { }
            public void onBufferReceived(byte[] buffer) { }
            public void onEndOfSpeech() { }
            public void onEvent(int eventType, AndroidJavaObject parameters) { }

            public void onError(int nextError)
            {
                lock (gate)
                {
                    error = nextError;
                    final = true;
                    changed = true;
                }
            }

            public void onPartialResults(AndroidJavaObject results)
            {
                StoreResults(results, false);
            }

            public void onResults(AndroidJavaObject results)
            {
                StoreResults(results, true);
            }

            private void StoreResults(AndroidJavaObject results, bool isFinal)
            {
                string value = string.Empty;
                try
                {
                    using (AndroidJavaObject matches =
                        results.Call<AndroidJavaObject>("getStringArrayList",
                            "results_recognition"))
                    {
                        if (matches != null && matches.Call<int>("size") > 0)
                            value = matches.Call<string>("get", 0) ?? string.Empty;
                    }
                }
                catch (Exception) { }
                lock (gate)
                {
                    transcript = value;
                    final = isFinal;
                    error = -1;
                    changed = true;
                }
            }

            public bool TryTake(out string nextTranscript,
                out bool isFinal, out int nextError)
            {
                lock (gate)
                {
                    nextTranscript = transcript;
                    isFinal = final;
                    nextError = error;
                    if (!changed)
                        return false;
                    changed = false;
                    return true;
                }
            }
        }
        private void OpenQuestNativeSpeechRecognizer()
        {
            try
            {
                using (AndroidJavaClass unityPlayer =
                    new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>(
                    "currentActivity"))
                using (AndroidJavaClass speechClass =
                    new AndroidJavaClass("android.speech.SpeechRecognizer"))
                {
                    if (!speechClass.CallStatic<bool>("isRecognitionAvailable", activity))
                    {
                        input.voiceInputActive = false;
                        SetStatus("Quest speech service is unavailable. Use TYPE instead.");
                        return;
                    }
                    activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                    {
                        using (AndroidJavaClass callbackUnityPlayer =
                            new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (AndroidJavaObject callbackActivity =
                            callbackUnityPlayer.GetStatic<AndroidJavaObject>(
                                "currentActivity"))
                        using (AndroidJavaClass callbackSpeechClass =
                            new AndroidJavaClass("android.speech.SpeechRecognizer"))
                        {
                            DestroyQuestNativeSpeechRecognizer();
                            questSpeechListener = new QuestSpeechRecognitionListener();
                            questSpeechRecognizer =
                                callbackSpeechClass.CallStatic<AndroidJavaObject>(
                                    "createSpeechRecognizer", callbackActivity);
                            questSpeechRecognizer.Call("setRecognitionListener",
                                questSpeechListener);
                            using (AndroidJavaObject recognizerIntent =
                                new AndroidJavaObject("android.content.Intent",
                                    "android.speech.action.RECOGNIZE_SPEECH"))
                            {
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.LANGUAGE_MODEL", "free_form");
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.PARTIAL_RESULTS", true);
                                recognizerIntent.Call<AndroidJavaObject>("putExtra",
                                    "android.speech.extra.MAX_RESULTS", 3);
                                questSpeechRecognizer.Call("startListening",
                                    recognizerIntent);
                            }
                        }
                    }));
                }
                SetStatus("Listening... Speak the analysis task now.");
            }
            catch (Exception exception)
            {
                input.voiceInputActive = false;
                SetStatus("Quest speech could not start: " + exception.Message +
                    ". Use TYPE instead.");
            }
        }
        private void UpdateQuestNativeSpeechRecognizer()
        {
            if (questSpeechListener == null ||
                !questSpeechListener.TryTake(out string transcript,
                    out bool isFinal, out int error))
                return;
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                prompt = transcript.Trim();
                if (intentPromptText != null)
                    intentPromptText.text = prompt;
            }
            if (!isFinal)
                return;

            input.voiceInputActive = false;
            input.voiceReviewPending = error < 0 && !string.IsNullOrWhiteSpace(prompt);
            intentConfigured = false;
            intentResolutionError = string.Empty;
            if (input.voiceReviewPending)
            {
                SlabLabSettings.SetSpatialPrompt(prompt);
                SetStatus("Voice transcript ready. Confirm, record again, or TYPE to edit.");
            }
            else
                SetStatus(QuestSpeechErrorMessage(error));
            DestroyQuestNativeSpeechRecognizer();
            if (intentCanvas != null && intentCanvas.gameObject.activeSelf)
                BuildIntentPanel();
            RefreshIntentSurfaces();
        }
        private static string QuestSpeechErrorMessage(int error)
        {
            if (error == 7)
                return "No speech was recognized. Move closer and try VOICE again.";
            if (error == 9)
                return "Microphone permission was denied. Enable it or use TYPE.";
            if (error == 1 || error == 2)
                return "Speech service network error. Check Wi-Fi or use TYPE.";
            return "Speech recognition stopped (error " + error + "). Try again or use TYPE.";
        }
        private void DestroyQuestNativeSpeechRecognizer()
        {
            if (questSpeechRecognizer == null)
                return;
            try
            {
                questSpeechRecognizer.Call("cancel");
                questSpeechRecognizer.Call("destroy");
                questSpeechRecognizer.Dispose();
            }
            catch (Exception) { }
            questSpeechRecognizer = null;
            questSpeechListener = null;
        }
#endif
    }
}
