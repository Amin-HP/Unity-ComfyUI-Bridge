using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Newtonsoft.Json.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ComfyUIBridge
{
    // 1. Define the Dropdown Types
    public enum ComfyNodeType
    {
        TextPrompt,
        KSampler,
        EmptyLatentImage,
        EmptyLatentAudio,
        ImageInput,
        RenderTextureInput
    }

    // 2. The Unified Configuration Class
    [System.Serializable]
    public class ComfyNodeConfig
    {
        [Tooltip("Select what kind of node this is.")]
        public ComfyNodeType type;

        [Tooltip("The ID of the Node in the JSON file.")]
        [Delayed]
        public string nodeID = "-1";

        // --- ATTRIBUTES ---

        [TextArea(2, 5)]
        public string textValue;

        public bool randomizeSeed = true;
        public long manualSeed = 12345;
        [Min(1)] public int steps = 20;
        [Min(0)] public float cfg = 8.0f;
        [Range(0f, 1f)] public float denoise = 1.0f;

        // Empty Latent Image Attributes
        public int width = 512;
        public int height = 512;

        // Empty Audio Attributes
        [Tooltip("Duration in seconds for the empty audio latent.")]
        public float audioSeconds = 5.0f;

        public Texture2D inputImage;
        public RenderTexture inputRenderTexture;

        [HideInInspector] public string uploadedServerFilename;

        // 3. The Logic to Apply Values
        public void ApplyToWorkflow(Dictionary<string, ComfyNode> workflow)
        {
            if (string.IsNullOrEmpty(nodeID) || nodeID == "-1") return;

            if (!workflow.ContainsKey(nodeID))
            {
                Debug.LogWarning($"[ComfyNodes] Node ID '{nodeID}' not found in JSON.");
                return;
            }

            ComfyNode targetNode = workflow[nodeID];
            var inputs = targetNode.inputs;

            // --- VALIDATION ---
            string jsonClass = targetNode.class_type.ToLower();
            bool typeMismatch = false;

            switch (type)
            {
                case ComfyNodeType.KSampler:
                    if (!jsonClass.Contains("sampler")) typeMismatch = true;
                    break;
                case ComfyNodeType.TextPrompt:
                    if (!jsonClass.Contains("text") && !jsonClass.Contains("string")) typeMismatch = true;
                    break;
                case ComfyNodeType.ImageInput:
                case ComfyNodeType.RenderTextureInput:
                    if (!jsonClass.Contains("image") && !jsonClass.Contains("load")) typeMismatch = true;
                    break;
                case ComfyNodeType.EmptyLatentImage:
                    if (!jsonClass.Contains("image") && !jsonClass.Contains("empty")) typeMismatch = true;
                    break;
                case ComfyNodeType.EmptyLatentAudio:
                    if (!jsonClass.Contains("audio") && !jsonClass.Contains("empty")) typeMismatch = true;
                    break;
            }

            if (typeMismatch)
            {
                Debug.LogWarning($"[ComfyNodes] Potential Type Mismatch! You configured Node {nodeID} as '{type}', but the JSON node is type '{targetNode.class_type}'.");
            }

            // --- APPLICATION ---
            Debug.Log($"[ComfyNodes] Applying {type} to Node {nodeID}...");

            switch (type)
            {
                case ComfyNodeType.TextPrompt:
                    if (inputs.ContainsKey("text")) inputs["text"] = textValue;
                    else if (inputs.ContainsKey("text_g")) inputs["text_g"] = textValue;
                    else if (inputs.ContainsKey("string")) inputs["string"] = textValue;
                    break;

                case ComfyNodeType.KSampler:
                    long seedToSend = randomizeSeed ? (long)Random.Range(1, 2147483647) : manualSeed;
                    if (randomizeSeed) manualSeed = seedToSend;

                    if (inputs.ContainsKey("seed")) inputs["seed"] = seedToSend;
                    else if (inputs.ContainsKey("noise_seed")) inputs["noise_seed"] = seedToSend;

                    int safeSteps = Mathf.Max(1, steps);
                    if (inputs.ContainsKey("steps")) inputs["steps"] = safeSteps;

                    if (inputs.ContainsKey("cfg")) inputs["cfg"] = (float)cfg;
                    if (inputs.ContainsKey("denoise")) inputs["denoise"] = (float)denoise;
                    break;

                case ComfyNodeType.EmptyLatentImage:
                    if (inputs.ContainsKey("width")) inputs["width"] = (int)width;
                    if (inputs.ContainsKey("height")) inputs["height"] = (int)height;
                    if (inputs.ContainsKey("batch_size")) inputs["batch_size"] = 1;
                    break;

                case ComfyNodeType.EmptyLatentAudio:
                    if (inputs.ContainsKey("seconds")) inputs["seconds"] = audioSeconds;
                    break;

                case ComfyNodeType.ImageInput:
                case ComfyNodeType.RenderTextureInput:
                    if (!string.IsNullOrEmpty(uploadedServerFilename))
                        inputs["image"] = uploadedServerFilename;
                    else
                        Debug.LogWarning($"[ComfyNodes] Image/RT Node {nodeID} skipped (no upload filename).");
                    break;
            }
        }
    }

    // =========================================================
    // 4. CUSTOM EDITOR UI
    // =========================================================
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(ComfyNodeConfig))]
    public class ComfyNodeConfigDrawer : PropertyDrawer
    {
        float lh = EditorGUIUtility.singleLineHeight;
        float pad = 2;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeProp = property.FindPropertyRelative("type");
            var idProp = property.FindPropertyRelative("nodeID");

            // --- HEADER ---
            float currentX = position.x;
            float headerY = position.y;

            // 1. Type Dropdown
            Rect typeRect = new Rect(currentX, headerY, position.width * 0.5f, lh);
            ComfyNodeType currentType = (ComfyNodeType)typeProp.enumValueIndex;
            EditorGUI.PropertyField(typeRect, typeProp, GUIContent.none);
            currentX += position.width * 0.5f + 5;

            // 2. ID Label
            Rect idLabelRect = new Rect(currentX, headerY, 20, lh);
            EditorGUI.LabelField(idLabelRect, "ID");
            currentX += 20;

            // 3. ID Field
            float buttonWidth = 25f;
            float idWidth = position.width - (currentX - position.x) - buttonWidth - 2;
            Rect idRect = new Rect(currentX, headerY, idWidth, lh);

            EditorGUI.PropertyField(idRect, idProp, GUIContent.none);
            currentX += idWidth + 2;

            // 4. Load Defaults Button
            Rect btnRect = new Rect(currentX, headerY, buttonWidth, lh);
            var loadIcon = EditorGUIUtility.IconContent("Refresh");
            loadIcon.tooltip = "Load Attributes from JSON";

            if (GUI.Button(btnRect, loadIcon))
            {
                if (idProp.stringValue != "-1" && !string.IsNullOrEmpty(idProp.stringValue))
                {
                    LoadDefaultsFromJSON(property, idProp.stringValue);
                }
            }

            // --- ATTRIBUTES ---
            float y = position.y + lh + pad;

            void Draw(string propName)
            {
                var p = property.FindPropertyRelative(propName);
                if (p != null)
                {
                    float height = EditorGUI.GetPropertyHeight(p);
                    EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), p, true);
                    y += height + pad;
                }
            }

            EditorGUI.indentLevel++;

            switch (currentType)
            {
                case ComfyNodeType.TextPrompt:
                    var textProp = property.FindPropertyRelative("textValue");
                    float textH = EditorGUI.GetPropertyHeight(textProp);
                    Rect labelRect = new Rect(position.x, y, position.width, lh);
                    EditorGUI.LabelField(labelRect, "Text Value");
                    y += lh;
                    Rect textAreaRect = new Rect(position.x, y, position.width, textH);
                    EditorGUI.PropertyField(textAreaRect, textProp, GUIContent.none);
                    y += textH + pad;
                    break;

                case ComfyNodeType.KSampler:
                    // Fix Defaults
                    var stepProp = property.FindPropertyRelative("steps");
                    if (stepProp.intValue <= 0) stepProp.intValue = 20;
                    var cfgProp = property.FindPropertyRelative("cfg");
                    if (cfgProp.floatValue <= 0) cfgProp.floatValue = 8.0f;
                    var seedProp = property.FindPropertyRelative("manualSeed");
                    if (seedProp.longValue == 0) seedProp.longValue = 12345;

                    Draw("randomizeSeed");
                    if (!property.FindPropertyRelative("randomizeSeed").boolValue)
                        Draw("manualSeed");
                    Draw("steps");
                    Draw("cfg");
                    Draw("denoise");
                    break;

                case ComfyNodeType.EmptyLatentImage:
                    // Fix Defaults
                    var widthProp = property.FindPropertyRelative("width");
                    if (widthProp.intValue <= 0) widthProp.intValue = 512;
                    var heightProp = property.FindPropertyRelative("height");
                    if (heightProp.intValue <= 0) heightProp.intValue = 512;

                    Draw("width");
                    Draw("height");
                    break;

                case ComfyNodeType.EmptyLatentAudio:
                    Draw("audioSeconds");
                    break;

                case ComfyNodeType.ImageInput:
                    Draw("inputImage");
                    break;

                case ComfyNodeType.RenderTextureInput:
                    Draw("inputRenderTexture");
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private void LoadDefaultsFromJSON(SerializedProperty property, string nodeID)
        {
            var targetObject = property.serializedObject.targetObject;
            var executorType = targetObject.GetType();
            var fileNameField = executorType.GetField("workflowFileName");
            if (fileNameField == null) return;

            string fileName = (string)fileNameField.GetValue(targetObject);
            if (string.IsNullOrEmpty(fileName)) return;

            string path = Path.Combine(Application.streamingAssetsPath, "Workflows", fileName);
            if (!File.Exists(path)) return;

            try
            {
                string json = File.ReadAllText(path);
                JObject root = JObject.Parse(json);

                if (!root.ContainsKey(nodeID))
                {
                    Debug.LogWarning($"[ComfyNodes] Node ID {nodeID} not found in JSON.");
                    return;
                }

                string nodeClass = root[nodeID]["class_type"]?.ToString().ToLower() ?? "";
                var typeProp = property.FindPropertyRelative("type");
                ComfyNodeType type = (ComfyNodeType)typeProp.enumValueIndex;

                bool mismatch = false;
                if (type == ComfyNodeType.KSampler && !nodeClass.Contains("sampler")) mismatch = true;
                if (type == ComfyNodeType.TextPrompt && !nodeClass.Contains("text") && !nodeClass.Contains("string")) mismatch = true;

                if (mismatch) Debug.LogWarning($"[ComfyNodes] Warning: Node {nodeID} is type '{nodeClass}', but you selected '{type}'. Values might not map correctly.");

                // FIX: Cast inputs to JObject so we can use ContainsKey
                JObject inputs = (JObject)root[nodeID]["inputs"];

                switch (type)
                {
                    case ComfyNodeType.TextPrompt:
                        string txt = (string)(inputs["text"] ?? inputs["text_g"] ?? inputs["string"]);
                        if (txt != null) property.FindPropertyRelative("textValue").stringValue = txt;
                        break;

                    case ComfyNodeType.KSampler:
                        if (inputs["seed"] != null) property.FindPropertyRelative("manualSeed").longValue = (long)inputs["seed"];
                        else if (inputs["noise_seed"] != null) property.FindPropertyRelative("manualSeed").longValue = (long)inputs["noise_seed"];

                        if (inputs["steps"] != null) property.FindPropertyRelative("steps").intValue = Mathf.Max(1, (int)inputs["steps"]);
                        if (inputs["cfg"] != null) property.FindPropertyRelative("cfg").floatValue = (float)inputs["cfg"];
                        if (inputs["denoise"] != null) property.FindPropertyRelative("denoise").floatValue = (float)inputs["denoise"];
                        break;

                    case ComfyNodeType.EmptyLatentImage:
                        if (inputs["width"] != null) property.FindPropertyRelative("width").intValue = (int)inputs["width"];
                        // FIX: Now we can safely check ContainsKey because 'inputs' is a JObject
                        if (inputs.ContainsKey("height") && inputs["height"] != null)
                            property.FindPropertyRelative("height").intValue = (int)inputs["height"];
                        break;

                    case ComfyNodeType.EmptyLatentAudio:
                        if (inputs["seconds"] != null) property.FindPropertyRelative("audioSeconds").floatValue = (float)inputs["seconds"];
                        break;
                }
                property.serializedObject.ApplyModifiedProperties();
                Debug.Log($"[ComfyNodes] Loaded defaults for Node {nodeID}.");
            }
            catch (System.Exception e) { Debug.LogError($"[ComfyNodes] Parse Error: {e.Message}"); }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var typeProp = property.FindPropertyRelative("type");
            ComfyNodeType currentType = (ComfyNodeType)typeProp.enumValueIndex;

            float h = EditorGUIUtility.singleLineHeight + 2;

            float AddHeight(string propName)
            {
                return EditorGUI.GetPropertyHeight(property.FindPropertyRelative(propName)) + 2;
            }

            switch (currentType)
            {
                case ComfyNodeType.TextPrompt:
                    h += EditorGUIUtility.singleLineHeight;
                    h += AddHeight("textValue");
                    break;
                case ComfyNodeType.KSampler:
                    h += AddHeight("randomizeSeed");
                    if (!property.FindPropertyRelative("randomizeSeed").boolValue)
                        h += AddHeight("manualSeed");
                    h += AddHeight("steps");
                    h += AddHeight("cfg");
                    h += AddHeight("denoise");
                    break;
                case ComfyNodeType.EmptyLatentImage:
                    h += AddHeight("width");
                    h += AddHeight("height");
                    break;
                case ComfyNodeType.EmptyLatentAudio:
                    h += AddHeight("audioSeconds");
                    break;
                case ComfyNodeType.ImageInput:
                    h += AddHeight("inputImage");
                    break;
                case ComfyNodeType.RenderTextureInput:
                    h += AddHeight("inputRenderTexture");
                    break;
            }

            return h + 5;
        }
    }
#endif
}