using System.Collections;
using System.Collections.Generic;
using ComfyUIBridge;
using UnityEngine;

public class ComfyMultiRunExample : MonoBehaviour
{
    [Header("References")]
    public ComfyWorkflowExecutor executor;

    [Header("Configuration")]
    [Tooltip("The ID of the Text Node in your executor's list (e.g. '6')")]
    public string promptNodeID = "6";

    [Tooltip("The ID of the Sampler/Seed Node (e.g. '3')")]
    public string samplerNodeID = "3";

    [Header("Data to Process")]
    public List<string> promptsToRun = new List<string>()
    {
        "A red sports car, cinematic lighting",
        "A blue futuristic tank, sci-fi style",
        "A green alien landscape, 8k",
        "A golden robot portrait, intricate details",
        "A cyberpunk city street at night, neon lights"
    };

    void Update()
    {
        // Press Space to start the batch job
        if (Input.GetKeyDown(KeyCode.Space))
        {
            RunBatch();
        }
    }

    public void RunBatch()
    {
        if (executor == null)
        {
            Debug.LogError("Executor is not assigned!");
            return;
        }

        Debug.Log($"Queueing {promptsToRun.Count} jobs...");

        // Loop through our list of strings
        foreach (string promptText in promptsToRun)
        {
            // IMPORTANT: We must capture the string in a local variable 
            // for the lambda function to work correctly in a loop.
            string p = promptText;

            // Add to the Executor's internal queue
            executor.QueueGeneration(() =>
            {
                ApplySettings(p);
            });
        }
    }

    // This function runs right before the specific generation starts
    void ApplySettings(string text)
    {
        Debug.Log($"[Batch Processing] Setting prompt to: {text}");

        // 1. Find the Prompt Config Object in the Inspector List
        var promptConfig = executor.inputNodes.Find(n => n.nodeID == promptNodeID);
        if (promptConfig != null)
        {
            promptConfig.textValue = text;
        }
        else
        {
            Debug.LogWarning($"Could not find node config with ID {promptNodeID}");
        }

        // 2. Randomize Seed so we don't get identical noise patterns
        var samplerConfig = executor.inputNodes.Find(n => n.nodeID == samplerNodeID);
        if (samplerConfig != null)
        {
            samplerConfig.randomizeSeed = true;
        }
    }
}