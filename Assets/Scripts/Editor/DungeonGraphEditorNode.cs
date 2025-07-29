using System;
using System.Collections.Generic;
using System.Reflection;
using PlasticPipe.PlasticProtocol.Client.Proxies;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngineInternal;

namespace DungeonGraph.Editor
{
    public class DungeonGraphEditorNode : Node
    {
        private DungeonGraphNode m_graphNode;
        public DungeonGraphNode Node => m_graphNode;
        private Port m_outputPort;
        private List<Port> m_ports;

        public List<Port> Ports => m_ports;
        public DungeonGraphEditorNode(DungeonGraphNode node)
        {
            this.AddToClassList("code-graph-node");

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

            // We do this so that output is alwways index 0
            if (info.hasFlowOutput)
            {
                CreateFlowOutputPort();
            }
            if (info.hasFlowInput)
            {
                CreateFlowInputPort();
            }
            
        }

        private void CreateFlowInputPort()
        {
            Port m_inputPort = InstantiatePort(Orientation.Vertical, Direction.Input, Port.Capacity.Single, typeof(PortTypes.FlowPort));
            m_inputPort.portName = "Input";
            m_inputPort.tooltip = "Flow input";
            m_ports.Add(m_inputPort);
            inputContainer.Add(m_inputPort);
        }

        private void CreateFlowOutputPort()
        {
            m_outputPort = InstantiatePort(Orientation.Vertical, Direction.Output, Port.Capacity.Single, typeof(PortTypes.FlowPort));
            m_outputPort.portName = "Out";
            m_outputPort.tooltip = "Flow output";
            m_ports.Add(m_outputPort);
            outputContainer.Add(m_outputPort);
        }

        public void SavePosition()
        {
            m_graphNode.SetPosition(GetPosition());
        }
    }
}
