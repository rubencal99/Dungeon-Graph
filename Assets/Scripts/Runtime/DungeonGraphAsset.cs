using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.MemoryProfiler;
using UnityEngine;

namespace DungeonGraph
{
    [CreateAssetMenu(menuName = "Dungeon Graph/New Graph")]
    public class DungeonGraphAsset : ScriptableObject
    {
        [SerializeReference]
        private List<DungeonGraphNode> m_nodes;
        [SerializeField]
        private List<DungeonGraphConnection> m_connections;

        public List<DungeonGraphNode> Nodes => m_nodes;
        public List<DungeonGraphConnection> Connections => m_connections;

        private Dictionary<string, DungeonGraphNode> m_NodeDictionary;
        public DungeonGraphAsset()
        {
            m_nodes = new List<DungeonGraphNode>();
            m_connections = new List<DungeonGraphConnection>();
            //m_NodeDictionary = new Dictionary<string, DungeonGraphNode>();
        }

        public void Init()
        {
            m_NodeDictionary = new Dictionary<string, DungeonGraphNode>();
            foreach (DungeonGraphNode node in Nodes)
            {
                m_NodeDictionary.Add(node.id, node);
            }
        }

        // Returns start node
        // Multiple start nodes may result in unexpected behavior
        public DungeonGraphNode GetStartNode()
        {
            StartNode[] startNodes = Nodes.OfType<StartNode>().ToArray();
            if (startNodes.Length == 0)
            {
                Debug.LogError("There is no start node in this graph");
                return null;
            }
            return startNodes[0];
        }

        public DungeonGraphNode GetNode(string nextNodeId)
        {
            if (m_NodeDictionary.TryGetValue(nextNodeId, out DungeonGraphNode node))
            {
                return node;
            }
            return null;
        }

        public DungeonGraphNode GetNodeFromOutput(string outputNodeId, int index)
        {
            foreach (DungeonGraphConnection connection in m_connections)
            {
                if (connection.outputPort.nodeId == outputNodeId && connection.outputPort.portIndex == index)
                {
                    string nodeId = connection.inputPort.nodeId;
                    DungeonGraphNode inputNode = m_NodeDictionary[nodeId];
                    return inputNode;
                }
            }
            return null;
        }
    }
}

