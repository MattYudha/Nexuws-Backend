using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NexusEditor
{
    /// <summary>
    /// Alat bersih-bersih "Missing MonoScript" (warning "The referenced script (Unknown) ... is missing!").
    /// Pakai API Unity resmi (GameObjectUtility) → menangkap SEMUA yang Unity lihat, bukan tebak-tebak teks.
    /// Menu: Nexus/Cleanup. "Report" hanya melaporkan; "Remove" menghapus komponen hilang + menyimpan.
    /// </summary>
    public static class NexusCleanupMissingScripts
    {
        // ---------- SCENE TERBUKA ----------
        [MenuItem("Nexus/Cleanup/Report Missing Scripts (scene terbuka)")]
        public static void ReportOpenScenes()
        {
            var sb = new StringBuilder();
            int total = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    total += Report(root, scene.name, sb);
            }
            Debug.Log(total == 0
                ? "✓ Nexus Cleanup: tidak ada missing script di scene terbuka."
                : $"⚠ Nexus Cleanup: {total} missing script di scene terbuka:\n{sb}");
        }

        [MenuItem("Nexus/Cleanup/Remove Missing Scripts (scene terbuka)")]
        public static void RemoveOpenScenes()
        {
            int removed = 0, objs = 0;
            var sb = new StringBuilder();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                bool dirty = false;
                foreach (var root in scene.GetRootGameObjects())
                {
                    int r = RemoveRecursive(root, scene.name, sb, ref objs);
                    if (r > 0) { removed += r; dirty = true; }
                }
                if (dirty)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            Debug.Log(removed == 0
                ? "✓ Nexus Cleanup: tidak ada yang dihapus (scene bersih)."
                : $"✓ Nexus Cleanup: hapus {removed} missing script dari {objs} objek (scene disimpan):\n{sb}");
        }

        // ---------- SEMUA PREFAB DI Assets ----------
        [MenuItem("Nexus/Cleanup/Report Missing Scripts (semua prefab)")]
        public static void ReportAllPrefabs() => ProcessAllPrefabs(remove: false);

        [MenuItem("Nexus/Cleanup/Remove Missing Scripts (semua prefab)")]
        public static void RemoveAllPrefabs() => ProcessAllPrefabs(remove: true);

        private static void ProcessAllPrefabs(bool remove)
        {
            var sb = new StringBuilder();
            int total = 0, changedFiles = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Photon") || !path.StartsWith("Assets/")) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var tmp = new StringBuilder();
                    int found = remove ? RemoveRecursive(root, path, tmp, ref total)
                                       : Report(root, path, tmp);
                    if (found > 0)
                    {
                        sb.Append(tmp);
                        if (remove) { PrefabUtility.SaveAsPrefabAsset(root, path); changedFiles++; }
                        else total += found;
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            if (remove)
                Debug.Log(changedFiles == 0
                    ? "✓ Nexus Cleanup: tidak ada prefab dengan missing script."
                    : $"✓ Nexus Cleanup: bersihkan {changedFiles} prefab:\n{sb}");
            else
                Debug.Log(total == 0
                    ? "✓ Nexus Cleanup: tidak ada missing script di prefab mana pun."
                    : $"⚠ Nexus Cleanup: missing script di prefab:\n{sb}");
        }

        // ---------- helper ----------
        private static int Report(GameObject go, string ctx, StringBuilder sb)
        {
            int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (n > 0) sb.AppendLine($"  [{ctx}] {PathOf(go)} — {n} missing");
            int total = n;
            foreach (Transform c in go.transform) total += Report(c.gameObject, ctx, sb);
            return total;
        }

        private static int RemoveRecursive(GameObject go, string ctx, StringBuilder sb, ref int objCounter)
        {
            int n = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            int removed = 0;
            if (n > 0)
            {
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                objCounter++;
                sb.AppendLine($"  [{ctx}] {PathOf(go)} — hapus {removed}");
            }
            foreach (Transform c in go.transform) removed += RemoveRecursive(c.gameObject, ctx, sb, ref objCounter);
            return removed;
        }

        private static string PathOf(GameObject go)
        {
            var stack = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) stack.Add(t.name);
            stack.Reverse();
            return string.Join("/", stack);
        }
    }
}
