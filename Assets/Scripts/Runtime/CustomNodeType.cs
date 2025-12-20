using System;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// Defines a custom node type with a name and color.
    /// These can be created at runtime from the graph editor.
    /// </summary>
    [Serializable]
    public class CustomNodeType
    {
        [SerializeField]
        private string m_typeName;

        [SerializeField]
        private Color m_color;

        [SerializeField]
        private string m_guid;

        public string typeName => m_typeName;
        public Color color => m_color;
        public string guid => m_guid;

        public CustomNodeType(string typeName, Color color)
        {
            m_typeName = typeName;
            m_color = color;
            m_guid = Guid.NewGuid().ToString();
        }
    }
}
