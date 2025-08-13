using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Hub", "Process/Hub", hasFlowInput: true, hasFlowOutput: true)]
    public class HubNode : DungeonGraphNode
    {
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("Hub NODE");
            return base.OnProcess(currentGraph);
        }
    }
}
