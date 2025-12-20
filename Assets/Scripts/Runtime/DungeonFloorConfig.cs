using System;
using UnityEngine;

namespace DungeonGraph
{
    /// <summary>
    /// Represents a dungeon floor configuration with its folder path and display name.
    /// </summary>
    [Serializable]
    public class DungeonFloorConfig
    {
        [SerializeField]
        private string m_floorName;

        [SerializeField]
        private string m_folderPath;

        public string floorName => m_floorName;
        public string folderPath => m_folderPath;

        public DungeonFloorConfig(string floorName, string folderPath)
        {
            m_floorName = floorName;
            m_folderPath = folderPath;
        }

        public override string ToString()
        {
            return m_floorName;
        }
    }
}
