using System.Collections.Generic;
using Newtonsoft.Json;

namespace ComfyUIBridge
{
    // The root object sent to the /prompt endpoint
    [System.Serializable]
    public class ComfyPromptRequest
    {
        public string client_id;
        public Dictionary<string, ComfyNode> prompt;
    }

    // Represents a single node in the ComfyUI graph
    [System.Serializable]
    public class ComfyNode
    {
        public Dictionary<string, object> inputs;
        public string class_type;
    }

    // Used to parse websocket messages
    [System.Serializable]
    public class ComfySocketMessage
    {
        public string type;
        public object data;
    }

    // Represents info about the final image/file
    [System.Serializable]
    public class ComfyGeneratedImage
    {
        public string filename;
        public string subfolder;
        public string type;
    }
}