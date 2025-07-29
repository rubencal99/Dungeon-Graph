using log4net.Appender;
using UnityEngine;

namespace DungeonGraph
{
    [System.Serializable]
    public struct DungeonGraphConnection
    {
        public DungeonGraphConnectionPort inputPort;
        public DungeonGraphConnectionPort outputPort;

        public DungeonGraphConnection(DungeonGraphConnectionPort input, DungeonGraphConnectionPort output)
        {
            inputPort = input;
            outputPort = output;
        }

        public DungeonGraphConnection(string inputPortId, int inputIndex, string outputPortId, int outputPortIndex)
        {
            inputPort = new DungeonGraphConnectionPort(inputPortId, inputIndex);
            outputPort = new DungeonGraphConnectionPort(outputPortId, outputPortIndex);
        }
    }

    [System.Serializable]
    public struct DungeonGraphConnectionPort
    {
        public string nodeId;
        public int portIndex;

        public DungeonGraphConnectionPort(string id, int index)
        {
            this.nodeId = id;
            this.portIndex = index;
        }
    }
}
