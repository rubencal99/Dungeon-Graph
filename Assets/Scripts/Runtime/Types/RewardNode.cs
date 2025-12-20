using UnityEngine;

namespace DungeonGraph
{
    [NodeInfo("Reward", "Rooms/Reward", hasFlowInput: true, hasFlowOutput: true)]
    public class RewardNode : DungeonGraphNode
    {
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("REWARD NODE");
            return base.OnProcess(currentGraph);
        }
    }
}
