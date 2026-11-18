using System;
using System.Collections.Generic;
using UnityEngine;

namespace Address.Placement
{
    /// <summary>Hand-authored semantic rules per model (Assets/ADDRESS/Data/profiles.json).</summary>
    [Serializable]
    public class ProfileSemantics
    {
        public string id;
        public string source = "polyhaven";  // polyhaven | blender
        public string group;
        public string support = "Floor";
        public string rotation = "Free";
        public string massClass = "Small";
        public float mass = 1f;
        public float maxTilt = 3f;
        public float minSpacing = 0.05f;
        public float clearanceFront;
        public bool canStack;
        public bool openable;
        public float scaleJitter;
        public float yaw;                    // rotate the source model so its front faces +Z
        public float scale = 1f;             // fix source scale if needed
        public string surfaces = "all";      // all | top | none  - which extracted surfaces may carry children
        public float fallenChance;
        public bool split;                   // source file is a set of several items -> one profile per part
        public bool disabled;                // reference-only model, never spawned
        public Vector3 fallenEuler;
        public float wallMin = 1.2f, wallMax = 1.9f;  // height band (bottom of object) for wall support
        public List<string> styles = new();
        public List<string> moods = new();
        public List<string> rooms = new();
    }

    [Serializable]
    public class ProfileSemanticsFile
    {
        public List<ProfileSemantics> profiles = new();
    }
}
