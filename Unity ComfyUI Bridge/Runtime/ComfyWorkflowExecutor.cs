using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GLTFast;
using System.Threading.Tasks;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ComfyUIBridge
{
    // Define what kind of output we expect from this specific workflow
    public enum ComfyOutputType
    {
        Image,
        Object3D,
        Audio,
        RenderTexture,
        None
    }

    public class ComfyWorkflowExecutor : MonoBehaviour
    {
        [Header("Main Configuration")]
        [Tooltip("The filename of the JSON file in Assets/StreamingAssets/Workflows/")]
        public string workflowFileName = "my_workflow.json";
        public ComfyOutputType expectedOutput = ComfyOutputType.Image;

        [Header("Node Configuration")]
        [Tooltip("Click + to add inputs (Prompts, Seeds, Images, Dimensions)")]
        public List<ComfyNodeConfig> inputNodes = new List<ComfyNodeConfig>();

        // --- OUTPUT TARGETS ---
        public RawImage resultImage;
        public AudioSource resultAudio;
        public bool autoPlayAudio = true;
        public Transform result3DSpawnPoint;
        public RenderTexture resultRenderTexture;

        public GameObject loadingSpinner;

        // Internal State
        private Dictionary<string, ComfyNode> loadedWorkflow;
        private string currentPromptId;
        private bool isBusy = false;
        private GameObject currentLoadedModel;

        // Internal Queue for "Fire and Forget" execution
        private Queue<System.Action> internalQueue = new Queue<System.Action>();

        private void Start()
        {
            if (loadingSpinner != null) loadingSpinner.SetActive(false);

            if (expectedOutput == ComfyOutputType.Object3D && result3DSpawnPoint == null)
            {
                GameObject go = new GameObject("3D_Spawn_Point");
                result3DSpawnPoint = go.transform;
                result3DSpawnPoint.position = Vector3.zero;
            }

            if (expectedOutput == ComfyOutputType.Image && resultImage != null)
            {
                if (resultImage.GetComponent<AspectRatioFitter>() == null)
                {
                    var fitter = resultImage.gameObject.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                }
            }

            LoadWorkflow();
        }

        public void LoadWorkflow()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "Workflows", workflowFileName);
            if (File.Exists(path))
            {
                loadedWorkflow = JsonConvert.DeserializeObject<Dictionary<string, ComfyNode>>(File.ReadAllText(path));
            }
            else
            {
                Debug.LogError($"Workflow file missing at: {path}");
            }
        }

        // =========================================================
        // QUEUE SYSTEM
        // =========================================================
        public void QueueGeneration(System.Action setupLogic)
        {
            internalQueue.Enqueue(setupLogic);
            if (!isBusy) ProcessInternalQueue();
        }

        private void ProcessInternalQueue()
        {
            if (internalQueue.Count > 0)
            {
                var logic = internalQueue.Dequeue();
                logic?.Invoke();
                ExecuteWithCallback(onComplete: ProcessInternalQueue);
            }
        }

        // =========================================================
        // EXECUTION
        // =========================================================
        public void Execute()
        {
            ExecuteWithCallback(null);
        }

        public void ExecuteWithCallback(System.Action onComplete = null)
        {
            if (isBusy)
            {
                Debug.LogWarning("Workflow is already running.");
                return;
            }

            if (loadedWorkflow == null) LoadWorkflow();
            if (loadedWorkflow == null)
            {
                onComplete?.Invoke();
                return;
            }

            StartCoroutine(ExecutionRoutine(onComplete));
        }

        private IEnumerator ExecutionRoutine(System.Action onComplete)
        {
            isBusy = true;
            if (loadingSpinner != null) loadingSpinner.SetActive(true);

            // 1. UPLOADS
            foreach (var node in inputNodes)
            {
                if (node.type == ComfyNodeType.ImageInput)
                {
                    if (node.inputImage != null && node.nodeID != "-1")
                    {
                        bool uploadDone = false;
                        string tempName = "unity_" + System.Guid.NewGuid().ToString() + ".png";

                        ComfyManager.Instance.UploadImage(node.inputImage, tempName, (serverName) =>
                        {
                            node.uploadedServerFilename = serverName;
                            uploadDone = true;
                        });
                        yield return new WaitUntil(() => uploadDone);

                        if (string.IsNullOrEmpty(node.uploadedServerFilename))
                        {
                            Finish(onComplete);
                            yield break;
                        }
                    }
                }
                else if (node.type == ComfyNodeType.RenderTextureInput)
                {
                    if (node.inputRenderTexture != null && node.nodeID != "-1")
                    {
                        bool uploadDone = false;
                        string tempName = "unity_rt_" + System.Guid.NewGuid().ToString() + ".png";

                        RenderTexture currentRT = RenderTexture.active;
                        RenderTexture.active = node.inputRenderTexture;
                        Texture2D tempTex = new Texture2D(node.inputRenderTexture.width, node.inputRenderTexture.height, TextureFormat.RGB24, false);
                        tempTex.ReadPixels(new Rect(0, 0, node.inputRenderTexture.width, node.inputRenderTexture.height), 0, 0);
                        tempTex.Apply();
                        RenderTexture.active = currentRT;

                        ComfyManager.Instance.UploadImage(tempTex, tempName, (serverName) =>
                        {
                            node.uploadedServerFilename = serverName;
                            uploadDone = true;
                            Destroy(tempTex);
                        });
                        yield return new WaitUntil(() => uploadDone);

                        if (string.IsNullOrEmpty(node.uploadedServerFilename))
                        {
                            Finish(onComplete);
                            yield break;
                        }
                    }
                }
            }

            // 2. APPLY CONFIG
            foreach (var node in inputNodes) node.ApplyToWorkflow(loadedWorkflow);

            // 3. SEND PROMPT
            bool promptSent = false;
            ComfyManager.Instance.QueuePrompt(loadedWorkflow, (id) =>
            {
                currentPromptId = id;
                promptSent = true;
            });

            yield return new WaitUntil(() => promptSent);

            if (string.IsNullOrEmpty(currentPromptId))
            {
                Finish(onComplete);
                yield break;
            }

            // 4. POLL
            StartCoroutine(PollRoutine(onComplete));
        }

        private IEnumerator PollRoutine(System.Action onComplete)
        {
            bool isDone = false;
            while (!isDone)
            {
                yield return new WaitForSeconds(1f);

                string url = $"{ComfyManager.Instance.comfyServerUrl}/history/{currentPromptId}";
                UnityWebRequest req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    if (req.downloadHandler.text != "{}")
                    {
                        isDone = true;
                        HandleResult(req.downloadHandler.text, onComplete);
                    }
                }
                req.Dispose();
            }
        }

        private void HandleResult(string json, System.Action onComplete)
        {
            JObject history = JObject.Parse(json);
            JToken outputData = history[currentPromptId]["outputs"];

            if (outputData == null)
            {
                Debug.LogError("Job finished but no outputs found.");
                Finish(onComplete);
                return;
            }

            // UNIVERSAL SEARCH
            foreach (JProperty node in outputData.Children())
            {
                JObject data = (JObject)node.Value;
                foreach (JProperty property in data.Properties())
                {
                    JToken value = property.Value;

                    if (value.Type == JTokenType.Array && value.HasValues)
                    {
                        if (value[0]["filename"] != null)
                        {
                            var item = value[0];
                            string fname = item["filename"].ToString();
                            string folder = item["subfolder"]?.ToString() ?? "";
                            string type = item["type"]?.ToString() ?? "output";
                            string ext = Path.GetExtension(fname).ToLower();

                            bool isMatch = false;

                            if (expectedOutput == ComfyOutputType.Audio)
                            {
                                if (ext == ".wav" || ext == ".mp3" || ext == ".ogg" || ext == ".aif" || ext == ".aiff" || ext == ".flac") isMatch = true;
                            }
                            else if (expectedOutput == ComfyOutputType.Image || expectedOutput == ComfyOutputType.RenderTexture)
                            {
                                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") isMatch = true;
                            }
                            else if (expectedOutput == ComfyOutputType.Object3D)
                            {
                                if (ext == ".glb" || ext == ".gltf") isMatch = true;
                            }

                            if (!isMatch) continue;

                            Debug.Log($"Found matching output: {fname} ({expectedOutput})");

                            switch (expectedOutput)
                            {
                                case ComfyOutputType.Image:
                                    StartCoroutine(ComfyManager.Instance.DownloadImage(fname, folder, type, (tex) => {
                                        if (resultImage != null)
                                        {
                                            resultImage.texture = tex;
                                            var fitter = resultImage.GetComponent<AspectRatioFitter>();
                                            if (fitter) fitter.aspectRatio = (float)tex.width / tex.height;
                                        }
                                        Finish(onComplete);
                                    }));
                                    break;

                                case ComfyOutputType.RenderTexture:
                                    StartCoroutine(ComfyManager.Instance.DownloadImage(fname, folder, type, (tex) => {
                                        if (resultRenderTexture != null) Graphics.Blit(tex, resultRenderTexture);
                                        Finish(onComplete);
                                    }));
                                    break;

                                case ComfyOutputType.Object3D:
                                    StartCoroutine(ComfyManager.Instance.DownloadFile(fname, folder, type, (path) => Load3DModel(path, onComplete)));
                                    break;

                                case ComfyOutputType.Audio:
                                    if (ext == ".flac")
                                    {
                                        Debug.LogWarning($"[Comfy] FLAC format detected ('{fname}'). Unity cannot play this natively. Downloading to persistent data path instead.");
                                        StartCoroutine(ComfyManager.Instance.DownloadFile(fname, folder, type, (path) => {
                                            Debug.Log($"Audio saved to disk: {path}");
                                            Finish(onComplete);
                                        }));
                                    }
                                    else
                                    {
                                        StartCoroutine(ComfyManager.Instance.DownloadAudio(fname, folder, type, (clip) => {
                                            if (resultAudio != null)
                                            {
                                                resultAudio.clip = clip;
                                                if (autoPlayAudio) resultAudio.Play();
                                            }
                                            Finish(onComplete);
                                        }));
                                    }
                                    break;

                                default:
                                    Finish(onComplete);
                                    break;
                            }
                            return;
                        }
                    }
                }
            }

            Debug.LogWarning($"Finished searching outputs. No file matching type '{expectedOutput}' was found.");
            Finish(onComplete);
        }

        async void Load3DModel(string path, System.Action onComplete)
        {
            if (string.IsNullOrEmpty(path)) { Finish(onComplete); return; }

            if (currentLoadedModel != null) Destroy(currentLoadedModel);

            currentLoadedModel = new GameObject("GeneratedModel");
            currentLoadedModel.transform.position = result3DSpawnPoint.position;
            currentLoadedModel.transform.rotation = result3DSpawnPoint.rotation;

            var gltf = new GltfImport();
            bool success = await gltf.Load("file://" + path);

            if (success) await gltf.InstantiateMainSceneAsync(currentLoadedModel.transform);

            Finish(onComplete);
        }

        void Finish(System.Action onComplete)
        {
            isBusy = false;
            if (loadingSpinner != null) loadingSpinner.SetActive(false);
            Debug.Log("Workflow Execution Complete.");
            onComplete?.Invoke();
        }
    }

    // ... (Editor code) ...
#if UNITY_EDITOR
    [CustomEditor(typeof(ComfyWorkflowExecutor))]
    // FIX: Explicitly use global::UnityEditor.Editor to avoid conflict with namespace ComfyUIBridge.Editor
    public class ComfyWorkflowExecutorEditor : global::UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            ComfyWorkflowExecutor script = (ComfyWorkflowExecutor)target;

            EditorGUILayout.PropertyField(serializedObject.FindProperty("workflowFileName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("expectedOutput"));
            EditorGUILayout.Space(10);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("inputNodes"));
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Outputs", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("loadingSpinner"));

            ComfyOutputType type = script.expectedOutput;
            if (type == ComfyOutputType.Image) EditorGUILayout.PropertyField(serializedObject.FindProperty("resultImage"));
            else if (type == ComfyOutputType.Object3D) EditorGUILayout.PropertyField(serializedObject.FindProperty("result3DSpawnPoint"));
            else if (type == ComfyOutputType.Audio)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("resultAudio"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("autoPlayAudio"));
            }
            else if (type == ComfyOutputType.RenderTexture) EditorGUILayout.PropertyField(serializedObject.FindProperty("resultRenderTexture"));

            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}