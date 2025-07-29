using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Debug Log", "Debug/Debug Log Console")]
    public class DebugLogNode : DungeonGraphNode
    {
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("Debug log node");
            return base.OnProcess(currentGraph);
        }
    }
}
