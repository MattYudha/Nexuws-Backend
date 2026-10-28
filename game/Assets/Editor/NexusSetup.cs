using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace NexusEditor
{
    /// <summary>
    /// SPIKE: buat scene Nexus LOCAL PLAY (tanpa Photon/backend). Memakai QuantumRunnerLocalDebug
    /// bawaan agar simulasi jalan lokal saat Play. Mirip PongSetup, tapi runner debug DIPERTAHANKAN
    /// dan SystemsConfig diisi NxCombatSystem + NxHitSystem.
    /// </summary>
    public static class NexusSetup
    {
        public const string ScenePath = "Assets/Scenes/NexusLocal.unity";
        public const string MapPath = "Assets/QuantumUser/Resources/NexusMap.asset";

        [MenuItem("Nexus/Create Local Spike Scene")]
        public static void CreateLocalScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));
            // EnsureQuantumGlobalAssets hanya perlu pada proyek yang belum di-setup Quantum Hub.
            // Pada first-import, sebuah postprocessor bisa throw "Failed to load QuantumEditorSettings".
            // Settings kini sudah ada → lewati bila ada; dan jadikan non-fatal agar setup tetap tuntas.
            if (AssetDatabase.FindAssets("t:QuantumEditorSettings").Length == 0)
            {
                try { EnsureQuantumGlobalAssets(); }
                catch (Exception e)
                {
                    Debug.LogWarning("EnsureQuantumGlobalAssets diabaikan (first-import ordering): " + (e.InnerException ?? e).Message
                        + "\nJalankan menu ini sekali lagi bila perlu.");
                }
            }

            EnsureAlwaysIncludedShaders("Unlit/Color", "Standard");
            // Clean slate: buka scene kosong + hapus scene lama agar MENIMPA (bukan bikin "NexusLocal 1.unity")
            // dan SystemsConfig selalu di-regenerasi lengkap.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (System.IO.File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
            Quantum.Editor.QuantumEditorMenuCreateScene.CreateNewQuantumScene(ScenePath, MapPath, true, true, false);
            var scene = EditorSceneManager.GetActiveScene();

            var debugRunner = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumRunnerLocalDebug>();
            if (debugRunner == null)
                throw new InvalidOperationException("QuantumRunnerLocalDebug missing from the generated Quantum scene.");
            var runtimeConfig = debugRunner.RuntimeConfig;

            // Hapus input debug default agar input datang dari NexusInput (CallbackPollInput).
            var debugInput = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumDebugInput>();
            if (debugInput != null) UnityEngine.Object.DestroyImmediate(debugInput);

            // SimulationConfig (seperti PongSetup).
            if (!runtimeConfig.SimulationConfig.IsValid)
            {
                var simulationConfig = ScriptableObject.CreateInstance<Quantum.SimulationConfig>();
                AssetDatabase.CreateAsset(simulationConfig, "Assets/QuantumUser/Resources/NexusSimulationConfig.asset");
                AssetDatabase.SaveAssets();
                Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
                runtimeConfig.SimulationConfig = simulationConfig;
            }

            // SystemsConfig: daftarkan sistem Nexus (clone default + tambah Nx systems).
            var defaultSystems = Quantum.QuantumUnityDB.GetGlobalAsset(runtimeConfig.SystemsConfig) as Quantum.SystemsConfig;
            var systems = ScriptableObject.CreateInstance<Quantum.SystemsConfig>();
            if (defaultSystems != null)
                systems.Entries = defaultSystems.Entries
                    .Select(e => JsonUtility.FromJson<Quantum.SystemsConfig.SystemEntry>(JsonUtility.ToJson(e))).ToList();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxCombatSystem)))
                systems.AddSystem<Quantum.NxCombatSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxHitSystem)))
                systems.AddSystem<Quantum.NxHitSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxProjectileSystem)))
                systems.AddSystem<Quantum.NxProjectileSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxSeparationSystem)))
                systems.AddSystem<Quantum.NxSeparationSystem>();
            AssetDatabase.CreateAsset(systems, "Assets/QuantumUser/Resources/NexusSystemsConfig.asset");
            AssetDatabase.SaveAssets();
            Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
            runtimeConfig.SystemsConfig = systems;

            // PENTING: debug runner menambah pemain lokal dari array LocalPlayers (OnGameStarted).
            // Scene yang baru di-generate meninggalkannya null → tidak ada pemain, cuma dummy & tak terkontrol.
            // Isi 1 pemain lokal agar kapsul "kamu" (team 0) selalu muncul & bisa digerakkan.
            debugRunner.LocalPlayers = new Quantum.RuntimePlayer[]
            {
                new Quantum.RuntimePlayer { PlayerNickname = "P1" },
            };

            EditorUtility.SetDirty(debugRunner);
            Debug.Log("Nexus SystemsConfig: " + string.Join(", ", systems.Entries.Select(e => e.SystemType.Value?.Name))
                + " | LocalPlayers=" + debugRunner.LocalPlayers.Length);

            // View + input.
            var go = new GameObject("Nexus");
            go.AddComponent<Nexus.NexusView>();
            go.AddComponent<Nexus.NexusInput>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + ScenePath + " — tekan Play untuk uji lokal (WASD + Space).");
        }

        [MenuItem("Nexus/Create Online Scene")]
        public static void CreateOnlineScene()
        {
            const string onlineScenePath = "Assets/Scenes/NexusOnline.unity";
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory(Path.GetDirectoryName(MapPath));
            if (AssetDatabase.FindAssets("t:QuantumEditorSettings").Length == 0)
            { try { EnsureQuantumGlobalAssets(); } catch (Exception e) { Debug.LogWarning(e.Message); } }

            EnsureAlwaysIncludedShaders("Unlit/Color", "Standard");
            // Clean slate: buka scene kosong + hapus scene lama agar MENIMPA (bukan bikin "NexusOnline 1.unity")
            // dan SystemsConfig online selalu di-regenerasi lengkap (NxProjectile + NxSeparation ikut).
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (System.IO.File.Exists(onlineScenePath)) AssetDatabase.DeleteAsset(onlineScenePath);
            Quantum.Editor.QuantumEditorMenuCreateScene.CreateNewQuantumScene(onlineScenePath, MapPath, true, true, false);
            var scene = EditorSceneManager.GetActiveScene();

            var debugRunner = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumRunnerLocalDebug>();
            if (debugRunner == null) throw new InvalidOperationException("QuantumRunnerLocalDebug missing.");
            var runtimeConfig = debugRunner.RuntimeConfig;
            // Networked: sesi dimulai oleh NexusGame → buang runner debug + input debug.
            UnityEngine.Object.DestroyImmediate(debugRunner.gameObject);
            var debugInput = UnityEngine.Object.FindAnyObjectByType<Quantum.QuantumDebugInput>();
            if (debugInput != null) UnityEngine.Object.DestroyImmediate(debugInput.gameObject);

            if (!runtimeConfig.SimulationConfig.IsValid)
            {
                var sim = ScriptableObject.CreateInstance<Quantum.SimulationConfig>();
                AssetDatabase.CreateAsset(sim, "Assets/QuantumUser/Resources/NexusSimulationConfig.asset");
                AssetDatabase.SaveAssets();
                Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
                runtimeConfig.SimulationConfig = sim;
            }

            var defaultSystems = Quantum.QuantumUnityDB.GetGlobalAsset(runtimeConfig.SystemsConfig) as Quantum.SystemsConfig;
            var systems = ScriptableObject.CreateInstance<Quantum.SystemsConfig>();
            if (defaultSystems != null)
                systems.Entries = defaultSystems.Entries
                    .Select(e => JsonUtility.FromJson<Quantum.SystemsConfig.SystemEntry>(JsonUtility.ToJson(e))).ToList();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxCombatSystem))) systems.AddSystem<Quantum.NxCombatSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxHitSystem))) systems.AddSystem<Quantum.NxHitSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxProjectileSystem))) systems.AddSystem<Quantum.NxProjectileSystem>();
            if (!systems.Entries.Any(e => e.SystemType.Value == typeof(Quantum.NxSeparationSystem))) systems.AddSystem<Quantum.NxSeparationSystem>();
            AssetDatabase.CreateAsset(systems, "Assets/QuantumUser/Resources/NexusSystemsConfigOnline.asset");
            AssetDatabase.SaveAssets();
            Quantum.Editor.QuantumUnityDBUtilities.RefreshGlobalDB();
            runtimeConfig.SystemsConfig = systems;

            var go = new GameObject("Nexus");
            var game = go.AddComponent<Nexus.NexusGame>();
            go.AddComponent<Nexus.NexusView>();
            go.AddComponent<Nexus.NexusInput>();
            game.RuntimeConfig = runtimeConfig;

            // Lobby UITK baru (Tahap 3) — berdampingan; login IMGUI + matchmaking lama tetap jalan.
            // Matikan via NexusGame.UseNewLobby = false di Inspector untuk kembali ke lobby IMGUI lama.
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/QuantumUser/Resources/NexusPanelSettings.asset");
            var lobbyAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/NexusUI/LobbyMain.uxml");
            if (ps != null && lobbyAsset != null)
            {
                var uiGo = new GameObject("NexusLobbyUI");
                var doc = uiGo.AddComponent<UIDocument>();
                doc.panelSettings = ps; doc.visualTreeAsset = lobbyAsset;
                var ctl = uiGo.AddComponent<Nexus.NexusLobbyController>();
                ctl.game = game; ctl.document = doc;
                game.UseNewLobby = true;

                // Halaman Profil UITK (UIDocument sendiri, sortingOrder di atas lobby).
                var profAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/NexusUI/ProfilePage.uxml");
                if (profAsset != null)
                {
                    var pGo = new GameObject("NexusProfileUI");
                    var pdoc = pGo.AddComponent<UIDocument>();
                    pdoc.panelSettings = ps; pdoc.visualTreeAsset = profAsset; pdoc.sortingOrder = 1;
                    var pctl = pGo.AddComponent<Nexus.ProfileController>();
                    pctl.game = game; pctl.document = pdoc;
                    game.UseNewProfile = true;
                }
            }
            else Debug.LogWarning("Lobby UITK dilewati — generate sprite + pastikan NexusPanelSettings & LobbyMain.uxml ada.");

            // PENTING: pin region agar editor DAN build selalu di region sama (cegah timeout matchmaking).
            var photon = Quantum.PhotonServerSettings.Global;
            photon.AppSettings.FixedRegion = "asia"; // Singapura (terdekat Indonesia). Ganti bila perlu.
            var appId = Environment.GetEnvironmentVariable("PHOTON_APP_ID");
            if (!string.IsNullOrWhiteSpace(appId)) photon.AppSettings.AppIdQuantum = appId.Trim();
            EditorUtility.SetDirty(photon);
            AssetDatabase.SaveAssets();

            EditorSceneManager.SaveScene(scene, onlineScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(onlineScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Created " + onlineScenePath + " — pastikan App Id Quantum terisi di PhotonServerSettings, lalu Play + build untuk 2 instance.");
        }

        /// <summary>Pastikan shader render dipakai di build (hindari magenta di standalone).</summary>
        private static void EnsureAlwaysIncludedShaders(params string[] names)
        {
            var gsObj = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gsObj == null || gsObj.Length == 0) return;
            var so = new SerializedObject(gsObj[0]);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < arr.arraySize; i++)
                    if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                if (present) continue;
                arr.InsertArrayElementAtIndex(arr.arraySize);
                arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            }
            so.ApplyModifiedProperties();
        }

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
    }
}
