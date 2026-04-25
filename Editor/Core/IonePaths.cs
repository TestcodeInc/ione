using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ione.Core
{
    // Project paths cached on load so background threads don't have to
    // touch Application.dataPath.
    [InitializeOnLoad]
    public static class IonePaths
    {
        public static readonly string DataPath;
        public static readonly string ProjectRoot;
        public static readonly string LibraryDir;

        static IonePaths()
        {
            DataPath = Path.GetFullPath(Application.dataPath);
            ProjectRoot = Path.GetFullPath(Path.Combine(DataPath, ".."));
            LibraryDir = Path.Combine(ProjectRoot, "Library");
        }
    }
}
