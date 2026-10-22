// Minimal UnityEngine stand-ins so the Unity-free simulation can be compiled outside Unity (verification only).
using System;
namespace UnityEngine
{
    public class Object { public string name; }
    public class ScriptableObject : Object { }
    public abstract class PropertyAttribute : Attribute { }
    public class SpaceAttribute : PropertyAttribute { public SpaceAttribute() { } public SpaceAttribute(float h) { } }
    public class HideInInspectorAttribute : Attribute { }
    public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string h) { } }
    public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string h) { } }
    public class SerializeField : Attribute { }
    public static class Debug { public static void Log(object o) { } public static void LogWarning(object o) { } public static void LogError(object o) { } }
}
namespace UnityEngine.Scripting { public class PreserveAttribute : Attribute { } }
