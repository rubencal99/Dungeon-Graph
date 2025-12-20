using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// A custom node type that can be created at runtime.
    /// The visual appearance is determined by the associated CustomNodeType.
    /// </summary>
    public class CustomNode : DungeonGraphNode
    {
        [SerializeField]
        private string m_customTypeGuid;

        [SerializeField]
        private string m_customTypeName;

        [SerializeField]
        private Color m_customColor;

        public string customTypeGuid => m_customTypeGuid;
        public string customTypeName => m_customTypeName;
        public Color customColor => m_customColor;

        public CustomNode()
        {
        }

        public void Initialize(CustomNodeType customNodeType)
        {
            if (customNodeType != null)
            {
                m_customTypeGuid = customNodeType.guid;
                m_customTypeName = customNodeType.typeName;
                m_customColor = customNodeType.color;
            }
        }

        public override string OnProcess(DungeonGraphAsset currentGraph)
        {
            Debug.Log($"CUSTOM NODE: {m_customTypeName}");
            return base.OnProcess(currentGraph);
        }
    }
}
