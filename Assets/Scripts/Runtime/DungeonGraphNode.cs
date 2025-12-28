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
        [SerializeField]
        private float m_spawnChance = 100f;

        public string typeName;

        public string id => m_guid;
        public Rect position => m_position;
        public float spawnChance
        {
            get => m_spawnChance;
            set => m_spawnChance = Mathf.Clamp(value, 0f, 100f);
        }

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
