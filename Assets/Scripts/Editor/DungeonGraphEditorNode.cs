using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;


namespace DungeonGraph.Editor
{
    public class DungeonGraphEditorNode : Node
    {
        private DungeonGraphNode m_graphNode;
        public DungeonGraphNode Node => m_graphNode;
        private Port m_outputPort;
        private List<Port> m_ports;
        private SerializedProperty m_serializedProperty;

        // private Port m_linkPort;      

        public List<Port> Ports => m_ports;

        // custom containers, these are a style preference
        // private VisualElement m_topPortContainer;
        // private VisualElement m_bottomPortContainer;

        private SerializedObject m_SerializedObject;
        
        private VisualElement m_centerPortContainer;
        private Port m_linkIn, m_linkOut;
        public DungeonGraphEditorNode(DungeonGraphNode node, SerializedObject dungeonGraphObject)
        {
            this.AddToClassList("code-graph-node");

            m_SerializedObject = dungeonGraphObject;
            m_graphNode = node;

            Type typeInfo = node.GetType();
            NodeInfoAttribute info = typeInfo.GetCustomAttribute<NodeInfoAttribute>();

            title = info.title;

            m_ports = new List<Port>();

            string[] depths = info.menuItem.Split('/');
            foreach (string depth in depths)
            {
                this.AddToClassList(depth.ToLower().Replace(' ', '-'));
            }
            this.name = typeInfo.Name;

            inputContainer.style.display = DisplayStyle.None;
            outputContainer.style.display = DisplayStyle.None;

            // Center overlay (matches #center-port-container in your USS)
            m_centerPortContainer = new VisualElement { name = "center-port-container" };
            m_centerPortContainer.style.position = Position.Absolute;
            m_centerPortContainer.style.left = 0; m_centerPortContainer.style.right = 0;
            m_centerPortContainer.style.top  = 0; m_centerPortContainer.style.bottom = 0;
            m_centerPortContainer.pickingMode = PickingMode.Ignore;
            mainContainer.Add(m_centerPortContainer);

            // NEW: a centered, visible "jack" that shows the node connector
            var m_centerJack = new VisualElement { name = "center-jack" };
            m_centerJack.pickingMode = PickingMode.Ignore;     // never block the port
            m_centerPortContainer.Add(m_centerJack);

            // Theme hooks (circle by default; swap to "jack--diamond" to get a diamond)
            m_centerJack.AddToClassList("jack");
            m_centerJack.AddToClassList("jack--circle"); // change to "jack--diamond" if you prefer

            // Add style classes for theming via USS
            this.AddToClassList("dungeon-node");
            if (info != null && !string.IsNullOrEmpty(info.title))
            {
                var slug = info.title.Trim().ToLower().Replace(' ', '-');
                this.AddToClassList($"node-{slug}"); // e.g., node-start, node-basic, node-hub, node-end, node-debug
            }
            else
            {
                var slug = typeInfo.Name.Trim().ToLower().Replace(' ', '-');
                this.AddToClassList($"node-{slug}");
            }

            //CreateLinkPort();
            CreateLinkPorts();

            foreach (FieldInfo property in typeInfo.GetFields())
            {
                if (property.GetCustomAttribute<ExposedPropertyAttribute>() is ExposedPropertyAttribute exposedProperty)
                {
                    //Debug.Log(property.Name);
                    PropertyField field = DrawProperty(property.Name);
                    //field.RegisterValueChangeCallback(OnFieldChangeCallback);
                }
            }


            RefreshPorts();         // Ensure the graph knows about custom-placed ports
            RefreshExpandedState();
        }

        // private void CreateLinkPort()
        // {
        //     // Direction can be Input or Output; just has to be consistent.
        //     m_linkPort = InstantiatePort(
        //         Orientation.Vertical,
        //         Direction.Input,            // same direction for all nodes
        //         Port.Capacity.Multi,        // allow many neighbors
        //         typeof(PortTypes.FlowPort)
        //     );

        //     m_linkPort.portName = "Connections";  // or “Connections”
        //     m_ports.Add(m_linkPort);
        //     m_topPortContainer.Add(m_linkPort);
            
        //     // Flip to Output only while dragging from this port
        //     m_linkPort.RegisterCallback<MouseDownEvent>(_ => m_linkPort.direction = Direction.Output);
        //     m_linkPort.RegisterCallback<MouseUpEvent>(_   => m_linkPort.direction = Direction.Input);
        // }

        // call this from the ctor instead of CreateLinkPort();
        private void CreateLinkPorts()
        {
            // We'll attach to the jack we created in the ctor
            var jack = m_centerPortContainer.Q<VisualElement>("center-jack");

            // OUTPUT — visible jack is for starting drags (HORIZONTAL for tangent connections)
            m_linkOut = InstantiatePort(
                Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(PortTypes.FlowPort));
            m_linkOut.portName = string.Empty;
            m_ports.Add(m_linkOut);
            jack.Add(m_linkOut);

            // INPUT — invisible drop target (enabled during drag by the GraphView) (HORIZONTAL for tangent connections)
            m_linkIn = InstantiatePort(
                Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(PortTypes.FlowPort));
            m_linkIn.portName = string.Empty;
            m_ports.Add(m_linkIn);
            jack.Add(m_linkIn);

            // Hide the tiny built-in connector visuals; the jack is the visible target
            var inCap  = m_linkIn.Q<VisualElement>("connector");
            var outCap = m_linkOut.Q<VisualElement>("connector");
            if (inCap  != null) { inCap.style.opacity = 0; inCap.style.width = 12; inCap.style.height = 12; }
            if (outCap != null) { outCap.style.opacity = 0; outCap.style.width = 12; outCap.style.height = 12; }

            // Fill the jack so the whole shape is clickable as the port
            m_linkOut.style.position = Position.Absolute;
            m_linkOut.style.left = m_linkOut.style.top = m_linkOut.style.right = m_linkOut.style.bottom = 0;

            m_linkIn.style.position = Position.Absolute;
            m_linkIn.style.left = m_linkIn.style.top = m_linkIn.style.right = m_linkIn.style.bottom = 0;

            // ✅ Pickability rules (flipped): only the Output is grabbable by default
            m_linkOut.pickingMode = PickingMode.Position;   // start drags here
            m_linkIn.pickingMode  = PickingMode.Ignore;     // drop target; GV will enable during drag          
        }

        private void FetchSerializedProperty()
        {
            // Get the nodes
            SerializedProperty nodes = m_SerializedObject.FindProperty("m_nodes");
            if (nodes.isArray)
            {
                int size = nodes.arraySize;
                for (int i = 0; i < size; i++)
                {
                    var element = nodes.GetArrayElementAtIndex(i);
                    var elementId = element.FindPropertyRelative("m_guid");
                    if (elementId.stringValue == m_graphNode.id)
                    {
                        m_serializedProperty = element;
                    }
                }
            }
        }

        private PropertyField DrawProperty(string propertyName)
        {
            if (m_serializedProperty == null)
            {
                FetchSerializedProperty();

            }
            SerializedProperty prop = m_serializedProperty.FindPropertyRelative(propertyName);
            PropertyField field = new PropertyField(prop);
            field.bindingPath = prop.propertyPath;
            extensionContainer.Add(field);
            return field;
        }

        // private void CreateFlowInputPort()
        // {
        //     Port m_inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(PortTypes.FlowPort));
        //     m_inputPort.portName = "Input";
        //     m_inputPort.tooltip = "Flow input";
        //     m_ports.Add(m_inputPort);
        //     //inputContainer.Add(m_inputPort);
        //     m_topPortContainer.Add(m_inputPort);
        // }

        // private void CreateFlowOutputPort()
        // {
        //     m_outputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(PortTypes.FlowPort));
        //     m_outputPort.portName = "Out";
        //     m_outputPort.tooltip = "Flow output";
        //     m_ports.Add(m_outputPort);
        //     //outputContainer.Add(m_outputPort);
        //     m_bottomPortContainer.Add(m_outputPort);
        // }

        public void SavePosition()
        {
            m_graphNode.SetPosition(GetPosition());
        }
    }
}
