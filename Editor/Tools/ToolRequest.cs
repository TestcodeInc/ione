using System;

namespace Ione.Tools
{
    // Flat field bag for tool-call args. JsonUtility needs a single
    // serializable class, so every tool's parameters live together here.
    [Serializable]
    public class ToolRequest
    {
        public string action;

        public string sceneObjectName;
        public string sceneObjectPath;
        public string assetPath;
        public string componentType;

        public string name;
        public string oldName;
        public string newName;
        public string path;
        public string newPath;
        public string prefabPath;
        public string menuPath;
        public string parentName;

        public string primitive;

        public float[] position;
        public float[] rotation;
        public float[] scale;
        public bool worldSpace;

        public string property;
        public float valueNumber;
        public bool valueBool;
        public string valueString;
        public float[] valueArray;
        public string valueRef;

        public string sceneName;
        public bool additive;

        public bool destroySceneObject;

        public string tag;
        public string layer;

        public string[] selection;

        public string content;

        public string shaderName;
        public string propertyName;

        public bool play;
        public bool unpack;
        public bool selfOnly;

        public string logLevel;
        public int logLimit;
        public long logSinceSeq;
        public int logWaitMs;

        public int captureWidth;
        public int captureHeight;
        public string captureSource;

        public string imageBase64;

        // Image generation.
        public string prompt;     // natural-language description
        public string imageSize;  // "1024x1024" | "1536x1024" | "1024x1536" | "auto"

        public string parameterType;
        public string stateName;
        public string fromState;
        public string toState;
        public string motionPath;
        public bool hasExitTime;
        public float transitionDuration;
        public string controllerPath;
        public string clipPath;
        public string propertyPath;
        public string targetType;
        public float duration;
        public bool loop;
    }
}
