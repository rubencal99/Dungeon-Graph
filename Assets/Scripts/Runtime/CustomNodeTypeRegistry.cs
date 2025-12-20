using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// ScriptableObject that stores all custom node type definitions.
    /// Persists between sessions and can be saved to disk.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon Graph/Custom Node Type Registry")]
    public class CustomNodeTypeRegistry : ScriptableObject
    {
        [SerializeField]
        private List<CustomNodeType> m_customNodeTypes = new List<CustomNodeType>();

        public List<CustomNodeType> customNodeTypes => m_customNodeTypes;

        /// <summary>
        /// Adds a new custom node type if the name doesn't already exist.
        /// </summary>
        /// <returns>True if added successfully, false if name already exists</returns>
        public bool AddCustomNodeType(string typeName, Color color)
        {
            if (HasNodeType(typeName))
            {
                return false;
            }

            var newType = new CustomNodeType(typeName, color);
            m_customNodeTypes.Add(newType);
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        /// <summary>
        /// Checks if a node type with the given name already exists.
        /// </summary>
        public bool HasNodeType(string typeName)
        {
            return m_customNodeTypes.Any(t => t.typeName.Equals(typeName, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets a custom node type by name.
        /// </summary>
        public CustomNodeType GetNodeType(string typeName)
        {
            return m_customNodeTypes.FirstOrDefault(t => t.typeName.Equals(typeName, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Removes a custom node type by name.
        /// </summary>
        public bool RemoveNodeType(string typeName)
        {
            var nodeType = GetNodeType(typeName);
            if (nodeType != null)
            {
                m_customNodeTypes.Remove(nodeType);
                UnityEditor.EditorUtility.SetDirty(this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets or creates the default registry instance from project resources.
        /// </summary>
        public static CustomNodeTypeRegistry GetOrCreateDefault()
        {
            const string registryPath = "Assets/Scripts/Runtime/DefaultCustomNodeTypeRegistry.asset";

            var registry = UnityEditor.AssetDatabase.LoadAssetAtPath<CustomNodeTypeRegistry>(registryPath);
            if (registry == null)
            {
                registry = CreateInstance<CustomNodeTypeRegistry>();
                UnityEditor.AssetDatabase.CreateAsset(registry, registryPath);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            return registry;
        }
    }
}
