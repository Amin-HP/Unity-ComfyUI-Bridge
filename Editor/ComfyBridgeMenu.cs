using UnityEngine;
using UnityEditor;
using ComfyUIBridge; 

namespace ComfyUIBridge.Editor
{
    public class ComfyBridgeMenu : UnityEditor.Editor
    {
        [MenuItem("Tools/ComfyUI Bridge/Quick Setup")]
        public static void SetupScene()
        {
            // 1. Find existing components to determine the target GameObject
#if UNITY_2023_1_OR_NEWER
            ComfyWorkflowExecutor executor = Object.FindFirstObjectByType<ComfyWorkflowExecutor>();
            ComfyManager manager = Object.FindFirstObjectByType<ComfyManager>();
#else
            ComfyWorkflowExecutor executor = Object.FindObjectOfType<ComfyWorkflowExecutor>();
            ComfyManager manager = Object.FindObjectOfType<ComfyManager>();
#endif

            GameObject bridgeGO = null;

            // Priority 1: Use existing Executor object
            if (executor != null)
            {
                bridgeGO = executor.gameObject;
            }
            // Priority 2: Use existing Manager object
            else if (manager != null)
            {
                bridgeGO = manager.gameObject;
            }
            // Priority 3: Create new Bridge object
            else
            {
                bridgeGO = new GameObject("ComfyBridge");
                Undo.RegisterCreatedObjectUndo(bridgeGO, "Create ComfyBridge");
            }

            // 2. Ensure both scripts are on this GameObject (Merging logic)

            // Check/Add Manager
            if (bridgeGO.GetComponent<ComfyManager>() == null)
            {
                // Only add if we didn't find a manager elsewhere (to avoid duplicate singletons)
                if (manager == null)
                {
                    bridgeGO.AddComponent<ComfyManager>();
                    Debug.Log("[ComfyUI Bridge] Added ComfyManager to " + bridgeGO.name);
                }
                else if (manager.gameObject != bridgeGO)
                {
                    Debug.LogWarning($"[ComfyUI Bridge] ComfyManager already exists on '{manager.gameObject.name}'. It was not merged to avoid breaking references.");
                }
            }

            // Check/Add Executor
            if (bridgeGO.GetComponent<ComfyWorkflowExecutor>() == null)
            {
                executor = bridgeGO.AddComponent<ComfyWorkflowExecutor>();
                Debug.Log("[ComfyUI Bridge] Added ComfyWorkflowExecutor to " + bridgeGO.name);
            }

            // 3. Finalize
            Selection.activeGameObject = bridgeGO;
            Debug.Log($"[ComfyUI Bridge] Setup Complete. Main Object: {bridgeGO.name}");
        }
    }
}