using UnityEngine;

namespace DungeonGraph
{
    public enum RoomSize
    {
        Small,
        Medium,
        Large
    }

    [NodeInfo("Basic", "Process/Basic", hasFlowInput: true, hasFlowOutput: true)]
    public class BasicNode : DungeonGraphNode
    {
        // This will show as a dropdown in the editor
        [ExposedProperty]
        public RoomSize size = RoomSize.Medium;
        
        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log("Basic NODE");
            return base.OnProcess(currentGraph);
        }
    }
}
