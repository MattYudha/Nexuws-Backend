using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PongEditor
{
    /// <summary>One-time editor helper that creates the bootstrap scene the CI build expects.</summary>
    public static class PongSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Pong/Create Main Scene")]
        public static void CreateMainScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Pong");
            foreach (var typeName in new[] { "Pong.PongGame", "Pong.PongView", "Pong.PongInput" })
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(typeName, throwOnError: false))
                    .FirstOrDefault(t => t != null);
                if (type == null)
                    throw new InvalidOperationException(typeName + " not found. Import the Quantum SDK and let scripts compile first.");
                go.AddComponent(type);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Selection.activeGameObject = go;
            Debug.Log("Created " + ScenePath + ". Now assign the Quantum Map on PongGame > Runtime Config, save, and commit the scene and all .meta files.");
        }
    }
}
