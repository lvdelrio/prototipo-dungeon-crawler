using System.Collections.Generic;
using UnityEngine;

namespace DungeonGen
{
    [CreateAssetMenu(fileName = "EventTable", menuName = "Dungeon/Event Table")]
    public class EventTableAsset : ScriptableObject
    {
        public List<EventEntry> entries = new List<EventEntry>();
    }
}
