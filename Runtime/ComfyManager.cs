using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;

namespace ComfyUIBridge
{
    public class ComfyManager : MonoBehaviour
    {
        public static ComfyManager Instance;

        [Header("Configuration")]
        public string comfyServerUrl = "http://127.0.0.1:8188";

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // ================== 1. UPLOAD IMAGE ==================
        public void UploadImage(Texture2D sourceTexture, string tempFileName, System.Action<string> onUploadComplete)
        {
            StartCoroutine(UploadImageRoutine(sourceTexture, tempFileName, onUploadComplete));
        }

        private IEnumerator UploadImageRoutine(Texture2D sourceTexture, string tempFileName, System.Action<string> onUploadComplete)
        {
            string url = comfyServerUrl + "/upload/image";

            // Decompress if needed
            Texture2D readableTexture = DecompressTexture(sourceTexture);
            byte[] imageBytes = readableTexture.EncodeToPNG();

            if (readableTexture != sourceTexture) Destroy(readableTexture);

            List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
            formData.Add(new MultipartFormFileSection("image", imageBytes, tempFileName, "image/png"));

            UnityWebRequest request = UnityWebRequest.Post(url, formData);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Upload Error: {request.error} | Response: {request.downloadHandler.text}");
                onUploadComplete?.Invoke(null);
            }
            else
            {
                JObject response = JObject.Parse(request.downloadHandler.text);
                string serverFilename = response["name"].ToString();
                onUploadComplete?.Invoke(serverFilename);
            }
            request.Dispose();
        }

        private Texture2D DecompressTexture(Texture2D source)
        {
            RenderTexture renderTex = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, renderTex);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;
            Texture2D readableText = new Texture2D(source.width, source.height);
            readableText.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            readableText.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);
            return readableText;
        }


        // ================== 2. QUEUE PROMPT ==================
        public void QueuePrompt(Dictionary<string, ComfyNode> workflowGraph, System.Action<string> onPromptIdReceived)
        {
            StartCoroutine(SendPromptRoutine(workflowGraph, onPromptIdReceived));
        }

        private IEnumerator SendPromptRoutine(Dictionary<string, ComfyNode> workflowGraph, System.Action<string> onPromptIdReceived)
        {
            string url = comfyServerUrl + "/prompt";
            ComfyPromptRequest req = new ComfyPromptRequest();
            req.prompt = workflowGraph;
            req.client_id = System.Guid.NewGuid().ToString();

            string jsonToSend = JsonConvert.SerializeObject(req);
            var request = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonToSend);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var responseJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(request.downloadHandler.text);
                if (responseJson.ContainsKey("prompt_id"))
                    onPromptIdReceived?.Invoke(responseJson["prompt_id"].ToString());
            }
            else
            {
                // Print the actual error message from ComfyUI
                Debug.LogError($"Comfy Error ({request.responseCode}): {request.error}");
                Debug.LogError($"Server Response: {request.downloadHandler.text}");
            }
            request.Dispose();
        }

        // ================== 3. DOWNLOAD HELPERS ==================
        public IEnumerator DownloadImage(string filename, string subfolder, string type, System.Action<Texture2D> onImageDownloaded)
        {
            string url = $"{comfyServerUrl}/view?filename={filename}&subfolder={subfolder}&type={type}";
            UnityWebRequest www = UnityWebRequestTexture.GetTexture(url);
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
                onImageDownloaded?.Invoke(((DownloadHandlerTexture)www.downloadHandler).texture);
            else
                Debug.LogError(www.error);
            www.Dispose();
        }

        public IEnumerator DownloadFile(string filename, string subfolder, string type, System.Action<string> onFileSaved)
        {
            string url = $"{comfyServerUrl}/view?filename={filename}&subfolder={subfolder}&type={type}";
            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                string localPath = System.IO.Path.Combine(Application.persistentDataPath, filename);
                System.IO.File.WriteAllBytes(localPath, www.downloadHandler.data);
                onFileSaved?.Invoke(localPath);
            }
            else
                Debug.LogError(www.error);
            www.Dispose();
        }

        public IEnumerator DownloadAudio(string filename, string subfolder, string type, System.Action<AudioClip> onAudioDownloaded)
        {
            string url = $"{comfyServerUrl}/view?filename={filename}&subfolder={subfolder}&type={type}";

            // Determine AudioType manually for robustness
            AudioType audioType = AudioType.UNKNOWN;
            if (filename.ToLower().EndsWith(".wav")) audioType = AudioType.WAV;
            else if (filename.ToLower().EndsWith(".mp3")) audioType = AudioType.MPEG;
            else if (filename.ToLower().EndsWith(".ogg")) audioType = AudioType.OGGVORBIS;

            UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType);
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                onAudioDownloaded?.Invoke(clip);
            }
            else
            {
                Debug.LogError("Audio Download Error: " + www.error);
            }
            www.Dispose();
        }
    }
}