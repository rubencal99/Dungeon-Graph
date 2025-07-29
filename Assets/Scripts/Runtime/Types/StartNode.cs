using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Start", "Process/Start", hasFlowInput: false, hasFlowOutput: true)]
    public class StartNode : DungeonGraphNode
    {
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("START NODE");
            return base.OnProcess(currentGraph);
        }
    }
}
