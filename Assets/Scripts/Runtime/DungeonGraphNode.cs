using System;
using UnityEngine;

namespace DungeonGraph
{
    [System.Serializable]
    public class DungeonGraphNode
    {
        [SerializeField]
        private string m_guid;
        [SerializeField]
        private Rect m_position;

        public string typeName;

        public string id => m_guid;
        public Rect position => m_position;

        public DungeonGraphNode()
        {
            NewGUID();
        }

        private void NewGUID()
        {
            m_guid = Guid.NewGuid().ToString();
        }

        public void SetPosition(Rect position)
        {
            m_position = position;
        }

        public virtual string OnProcess(DungeonGraphAsset currentGraph)
        {
            DungeonGraphNode nextNodeInFlow = currentGraph.GetNodeFromOutput(m_guid, 0);
            if (nextNodeInFlow != null)
            {
                return nextNodeInFlow.id;
            }

            return string.Empty;
        }
        
    }
}
