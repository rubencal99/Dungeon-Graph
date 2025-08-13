using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Boss", "Rooms/Boss", hasFlowInput: true, hasFlowOutput: false)]
    public class BossNode : DungeonGraphNode
    {
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("BOSS NODE");
            return base.OnProcess(currentGraph);
        }
    }
}
