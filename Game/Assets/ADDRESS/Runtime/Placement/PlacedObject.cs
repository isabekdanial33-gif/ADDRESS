using UnityEngine;

namespace Address.Placement
{
    /// <summary>Marker on every placed instance: which profile, who supports it, and whether it is an intentional anomaly.</summary>
    [DisallowMultipleComponent]
    public class PlacedObject : MonoBehaviour
    {
        public PlacementProfile profile;
        public PlacedObject supportParent;     // object it stands on (null = architecture)
        public int surfaceIndex = -1;
        public bool intentionalAnomaly;        // auditor ignores support/tilt rules for these
        public ObjectState state = ObjectState.Normal;
        public string placedBy;                // which rule/scenario step placed it (for failure reports)
        public int uid;
    }
}
