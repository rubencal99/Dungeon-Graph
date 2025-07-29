using System;
using UnityEngine;

namespace DungeonGraph
{
    public class NodeInfoAttribute : Attribute
    {
        private string m_nodeTitle;
        private string m_menuItem;
        private bool m_hasFlowInput;
        private bool m_hasFlowOutput;

        public string title => m_nodeTitle;
        public string menuItem => m_menuItem;
        public bool hasFlowInput => m_hasFlowInput;
        public bool hasFlowOutput => m_hasFlowOutput;

        public NodeInfoAttribute(string title, string menuItem = "", bool hasFlowInput = true, bool hasFlowOutput = true)
        {
            m_nodeTitle = title;
            m_menuItem = menuItem;
            m_hasFlowInput = hasFlowInput;
            m_hasFlowOutput = hasFlowOutput;
        }
    }
}
