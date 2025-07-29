using System;
using UnityEngine;

namespace DungeonGraph
{
    public class DungeonGraphObject : MonoBehaviour
    {
        [SerializeField]
        DungeonGraphAsset m_graphAsset;

        private DungeonGraphAsset graphInstance;

        void OnEnable()
        {
            graphInstance = Instantiate(m_graphAsset);
            ExecuteAsset();
        }

        private void ExecuteAsset()
        {
            graphInstance.Init();
            DungeonGraphNode startNode = graphInstance.GetStartNode();

            ProcessAndMoveToNextNode(startNode);
        }

        private void ProcessAndMoveToNextNode(DungeonGraphNode currentNode)
        {
            string nextNodeId = currentNode.OnProcess(graphInstance);
            if (!string.IsNullOrEmpty(nextNodeId))
            {
                DungeonGraphNode node = graphInstance.GetNode(nextNodeId);

                ProcessAndMoveToNextNode(node);
            }
        }
    }
}
