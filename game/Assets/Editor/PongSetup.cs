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

        public const string MapPath = "Assets/QuantumUser/Resources/PongMap.asset";

        /// <summary>
        /// Batchmode-friendly variant used by CI: creates the Quantum scene + baked Map, adds the Pong components and
        /// wires the RuntimeConfig (Map, SimulationConfig, SystemsConfig) so no manual editor step is needed.
        /// </summary>
        [MenuItem("Pong/Create Quantum Scene (with Map)")]
        public static void CreateQuantumScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));
            EnsureQuantumGlobalAssets();

            // The Photon App ID is not committed; CI passes it in (GitHub variable PHOTON_APP_ID).
            var appId = Environment.GetEnvironmentVariable("PHOTON_APP_ID");
            if (!string.IsNullOrWhiteSpace(appId))
            {
                var photon = Quantum.PhotonServerSettings.Global;
                photon.AppSettings.AppIdQuantum = appId.Trim();
                EditorUtility.SetDirty(photon);
                AssetDatabase.SaveAssets();
            }
            else
                Debug.LogWarning("PHOTON_APP_ID not set: the APK is built without a Photon App ID and cannot connect.");

            Quantum.Editor.QuantumEditorMenuCreateScene.CreateNewQuantumScene(ScenePath, MapPath, true, true, false);
            var scene = EditorSceneManager.GetActiveScene();

            // The debug runner would start a local game on launch; Pong starts its own networked session instead.
            var debugRunner = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumRunnerLocalDebug>();
            if (debugRunner == null)
                throw new InvalidOperationException("QuantumRunnerLocalDebug missing from the generated Quantum scene.");
            var runtimeConfig = debugRunner.RuntimeConfig;
            UnityEngine.Object.DestroyImmediate(debugRunner.gameObject);
            var debugInput = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumDebugInput>();
            if (debugInput != null) UnityEngine.Object.DestroyImmediate(debugInput.gameObject);

            // The Quantum Hub normally creates the default SimulationConfig; without one the session shuts down at start.
            if (!runtimeConfig.SimulationConfig.IsValid)
            {
                var simulationConfig = ScriptableObject.CreateInstance<Quantum.SimulationConfig>();
                AssetDatabase.CreateAsset(simulationConfig, "Assets/QuantumUser/Resources/PongSimulationConfig.asset");
                AssetDatabase.SaveAssets();
                Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
                runtimeConfig.SimulationConfig = simulationConfig;
            }

            // PongSystem must be registered, otherwise the simulation never runs the game rules.
            var defaultSystems = Quantum.QuantumUnityDB.GetGlobalAsset(runtimeConfig.SystemsConfig) as Quantum.SystemsConfig;
            var systems = ScriptableObject.CreateInstance<Quantum.SystemsConfig>();
            if (defaultSystems != null)
                systems.Entries = defaultSystems.Entries
                    .Select(e => JsonUtility.FromJson<Quantum.SystemsConfig.SystemEntry>(JsonUtility.ToJson(e))).ToList();
            else
                Debug.LogWarning("Default SystemsConfig not found; only PongSystem will be registered.");
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.PongSystem)))
                systems.AddSystem<Quantum.PongSystem>();
            AssetDatabase.CreateAsset(systems, "Assets/QuantumUser/Resources/PongSystemsConfig.asset");
            AssetDatabase.SaveAssets();
            Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
            runtimeConfig.SystemsConfig = systems;
            Debug.Log("SystemsConfig: " + string.Join(", ", systems.Entries.Select(e => e.SystemType.Value?.Name)));

            var go = new GameObject("Pong");
            var game = go.AddComponent<Pong.PongGame>();
            go.AddComponent<Pong.PongView>();
            go.AddComponent<Pong.PongInput>();
            game.RuntimeConfig = runtimeConfig;

            // Default backend the APK talks to (GitHub variable PONG_BACKEND_URL); players can still edit it on the login screen.
            var backendUrl = Environment.GetEnvironmentVariable("PONG_BACKEND_URL");
            if (!string.IsNullOrWhiteSpace(backendUrl))
                game.BackendUrl = backendUrl.Trim().TrimEnd('/');
            else
                Debug.LogWarning("PONG_BACKEND_URL not set: the APK defaults to " + game.BackendUrl + " (emulator host).");

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + ScenePath + " with Quantum Map " + MapPath);
        }

        /// <summary>Creates the global config assets the Quantum Hub would normally create (UnityDB, DefaultConfigs, ...).</summary>
        private static void EnsureQuantumGlobalAssets()
        {
            var ensure = typeof(Quantum.Editor.QuantumGlobalScriptableObjectUtils).GetMethod("EnsureAssetExists");
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch (System.Reflection.ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } })
                .Where(t => !t.IsAbstract && !t.ContainsGenericParameters
                            && t.GetCustomAttributes(typeof(Quantum.QuantumGlobalScriptableObjectAttribute), true).Length > 0)
                .OrderBy(t => t.FullName);
            foreach (var type in types)
            {
                try { ensure.MakeGenericMethod(type).Invoke(null, null); }
                catch (Exception e) { Debug.LogWarning("Could not create global asset " + type.FullName + ": " + (e.InnerException ?? e).Message); }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

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
