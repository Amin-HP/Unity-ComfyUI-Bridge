using ComfyUIBridge;
using UnityEngine;

public class ComfyRuntimeTester : MonoBehaviour
{
    public ComfyWorkflowExecutor executor;

    // IDs of the nodes we want to change at runtime
    // Make sure these match what you typed in the Inspector!
    public string promptNodeID = "6"; 
    public string samplerNodeID = "3";

    void Update()
    {
        // Example: Press 'Space' to change prompt and generate
        if (Input.GetKeyDown(KeyCode.Space))
        {
            GenerateRandomCar();
        }
    }

    public void GenerateRandomCar()
    {
        if (executor == null) return;

        // 1. FIND THE PROMPT NODE
        // We look through the list in the Executor to find the config object with ID "6"
        var promptNode = executor.inputNodes.Find(n => n.nodeID == promptNodeID);
        
        if (promptNode != null)
        {
            // Pick a random color
            string[] colors = { "Red", "Blue", "Green", "Golden", "Cyberpunk" };
            string randomColor = colors[Random.Range(0, colors.Length)];
            
            // Change the text value at runtime
            promptNode.textValue = $"A futuristic {randomColor} sports car, cinematic lighting, 8k";
            Debug.Log($"Changing Prompt to: {promptNode.textValue}");
        }
        else
        {
            Debug.LogError($"Could not find Node ID {promptNodeID} in the Executor list.");
        }

        // 2. FIND THE SAMPLER NODE (To randomize seed)
        var samplerNode = executor.inputNodes.Find(n => n.nodeID == samplerNodeID);
        
        if (samplerNode != null)
        {
            // Ensure randomization is on, or set a specific manual seed
            samplerNode.randomizeSeed = true; 
        }

        // 3. EXECUTE
        // The executor will now grab these new values and send them to ComfyUI
        executor.Execute();
    }
}