using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace NexusEditor
{
    /// <summary>
    /// Setup Meta UI (UI Toolkit): PanelSettings (+ theme) + GameObject berisi UIDocument & NexusMetaUI.
    /// Menu: Nexus → Setup Meta UI (Toolkit). Lalu Play → layar Login bertema muncul.
    /// </summary>
    public static class NexusUISetup
    {
        private const string Dir = "Assets/QuantumUser/Resources";
        private const string PanelPath = Dir + "/NexusPanelSettings.asset";
        private const string ThemePath = Dir + "/NexusRuntimeTheme.tss";

        [MenuItem("Nexus/Setup Meta UI (Toolkit)")]
        public static void Setup()
        {
            // 1) PanelSettings
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, PanelPath);
            }

            // 2) Theme (TSS) — import default Unity agar kontrol ter-render; USS tema kita ditambah saat runtime.
            var tss = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (tss == null)
            {
                var found = AssetDatabase.FindAssets("t:ThemeStyleSheet");
                if (found.Length > 0) tss = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(found[0]));
            }
            if (tss != null) ps.themeStyleSheet = tss;
            else Debug.LogWarning("NexusUISetup: ThemeStyleSheet belum ter-import. Pastikan NexusRuntimeTheme.tss ada; reimport folder Resources bila perlu.");

            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1280, 720);
            ps.match = 0.5f;
            ps.sortingOrder = 100;
            EditorUtility.SetDirty(ps);
            AssetDatabase.SaveAssets();

            // 2b) Camera (scene kosong → "No cameras rendering"). Beri background gelap bertema.
            if (Object.FindObjectOfType<Camera>() == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.035f, 0.043f, 0.063f);
            }

            // (EventSystem TIDAK dibuat: legacy input → UI Toolkit pakai DefaultEventSystem internal.
            //  NexusMetaUI membuang uGUI EventSystem saat runtime agar input sampai ke panel.)

            // 3) GameObject + UIDocument + NexusMetaUI
            var go = GameObject.Find("NexusMetaUI");
            if (go == null) go = new GameObject("NexusMetaUI");
            var doc = go.GetComponent<UIDocument>();
            if (doc == null) doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            if (go.GetComponent<Nexus.NexusMetaUI>() == null) go.AddComponent<Nexus.NexusMetaUI>();
            EditorUtility.SetDirty(go);
            if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

            Selection.activeGameObject = go;
            Debug.Log("✓ NexusMetaUI siap di scene ini. Tekan Play → layar Login bertema. (Backend harus nyala di " + "http://localhost:5080)");
        }
    }
}
