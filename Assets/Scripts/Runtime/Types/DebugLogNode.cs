using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Debug Log", "Debug/Debug Log Console")]
    public class DebugLogNode : DungeonGraphNode
    {
        [ExposedProperty()]
        public string logMessage;
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log(logMessage);
            return base.OnProcess(currentGraph);
        }
    }
}
