using UnityEditor.Experimental.GraphView;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UIElements;
using System.Reflection;
using System;
using System.Linq;
using PlasticPipe.PlasticProtocol.Messages;

namespace DungeonGraph.Editor
{
    public struct SearchContextElement
    {
        public object target { get; private set; }
        public string title { get; private set; }
        public bool isCreateNewNodeTypeEntry { get; private set; }

        public SearchContextElement(object target, string title, bool isCreateNewNodeType = false)
        {
            this.target = target;
            this.title = title;
            this.isCreateNewNodeTypeEntry = isCreateNewNodeType;
        }
    }

    public class DungeonGraphWindowSearchProvider : ScriptableObject, ISearchWindowProvider
    {
        public DungeonGraphView graph;
        public VisualElement target;
        public static List<SearchContextElement> elements;

        private Vector2 m_contextScreenPosition;

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            m_contextScreenPosition = context.screenMousePosition;

            List<SearchTreeEntry> tree = new List<SearchTreeEntry>();
            tree.Add(new SearchTreeGroupEntry(new GUIContent("Nodes"), 0));

            elements = new List<SearchContextElement>();

            // Add standard node types from attributes
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (type.CustomAttributes.ToList() != null)
                    {
                        var attribute = type.GetCustomAttribute(typeof(NodeInfoAttribute));
                        if (attribute != null)
                        {
                            NodeInfoAttribute att = (NodeInfoAttribute)attribute;
                            var node = Activator.CreateInstance(type);

                            if (string.IsNullOrEmpty(att.menuItem)) { continue; }
                            elements.Add(new SearchContextElement(node, att.menuItem));
                        }
                    }
                }
            }

            // Add custom node types from registry
            var registry = CustomNodeTypeRegistry.GetOrCreateDefault();
            foreach (var customType in registry.customNodeTypes)
            {
                var customNode = new CustomNode();
                customNode.Initialize(customType);
                elements.Add(new SearchContextElement(customNode, $"Custom/{customType.typeName}"));
            }

            // Sort by name
            elements.Sort((entry1, entry2) =>
            {
                string[] splits1 = entry1.title.Split('/');
                string[] splits2 = entry2.title.Split('/');
                for (int i = 0; i < splits1.Length; i++)
                {
                    if (i >= splits2.Length)
                    {
                        return 1;
                    }
                    int value = splits1[i].CompareTo(splits2[i]);
                    if (value != 0)
                    {
                        // Make sure that leaves go before nodes
                        if (splits1.Length != splits2.Length && (i == splits1.Length - 1 || i == splits2.Length - 1))
                            return splits1.Length < splits2.Length ? 1 : -1;
                        return value;
                    }
                }
                return 0;
            });

            List<string> groups = new List<string>();
            foreach (SearchContextElement element in elements)
            {
                string[] entryTitle = element.title.Split('/');
                string groupName = "";
                for (int i = 0; i < entryTitle.Length - 1; i++)
                {
                    groupName += entryTitle[i];
                    if (!groups.Contains(groupName))
                    {
                        tree.Add(new SearchTreeGroupEntry(new GUIContent(entryTitle[i]), i + 1));
                        groups.Add(groupName);
                    }
                    groupName += "/";
                }

                // Debug.Log(entryTitle.Last());
                SearchTreeEntry entry = new SearchTreeEntry(new GUIContent(entryTitle.Last()));
                entry.level = entryTitle.Length;
                entry.userData = new SearchContextElement(element.target, element.title);
                tree.Add(entry);
            }

            // Add separator and "Create New Node Type" button at the bottom
            tree.Add(new SearchTreeEntry(new GUIContent("")) { level = 1 }); // Separator
            SearchTreeEntry createNewEntry = new SearchTreeEntry(new GUIContent("+ Create New Node Type"))
            {
                level = 1,
                userData = new SearchContextElement(null, "CreateNewNodeType", true)
            };
            tree.Add(createNewEntry);

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry SearchTreeEntry, SearchWindowContext context)
        {
            SearchContextElement element = (SearchContextElement)SearchTreeEntry.userData;

            // Check if this is the "Create New Node Type" entry
            if (element.isCreateNewNodeTypeEntry)
            {
                CreateCustomNodeTypeWindow.ShowWindow((typeName, color) =>
                {
                    var registry = CustomNodeTypeRegistry.GetOrCreateDefault();
                    if (registry.AddCustomNodeType(typeName, color))
                    {
                        // Create folders for this node type in all existing floors
                        DungeonFloorManager.CreateNodeFolderInAllFloors(typeName);

                        UnityEditor.EditorUtility.DisplayDialog("Success",
                            $"Custom node type '{typeName}' has been created and added to all dungeon floors!", "OK");
                    }
                    else
                    {
                        UnityEditor.EditorUtility.DisplayDialog("Error",
                            $"Failed to create node type '{typeName}'. A type with this name may already exist.", "OK");
                    }
                });
                return true;
            }

            // Standard node creation
            var windowMousePosition = graph.ChangeCoordinatesTo(graph, context.screenMousePosition - graph.window.position.position);
            var graphMousePosition = graph.contentViewContainer.WorldToLocal(windowMousePosition);

            DungeonGraphNode node = (DungeonGraphNode)element.target;
            node.SetPosition(new Rect(graphMousePosition, new Vector2()));
            graph.Add(node);

            return true;
        }
    }
}
