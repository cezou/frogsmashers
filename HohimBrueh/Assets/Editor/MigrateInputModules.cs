using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public static class MigrateInputModules
{
    [MenuItem("Tools/FrogSmashers/Migrate EventSystem to InputSystem UI Module")]
    public static void Migrate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Stop Play mode first",
                "Exit Play mode before running the migration (scenes cannot be saved during Play).",
                "OK");
            return;
        }

        if (EditorSceneManager.GetActiveScene().isDirty)
        {
            if (!EditorUtility.DisplayDialog(
                "Save current scene?",
                "Current scene has unsaved changes. Save before running migration?",
                "Save & Continue", "Cancel"))
                return;
            EditorSceneManager.SaveOpenScenes();
        }

        var sceneGuids = AssetDatabase.FindAssets("t:Scene");
        int totalMigrated = 0;
        int scenesChanged = 0;

        foreach (var guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/Scenes/")) continue;
            if (path.Contains("/Rejects/")) continue;

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var sims = Object.FindObjectsByType<StandaloneInputModule>(FindObjectsSortMode.None);
            int converted = 0;
            foreach (var sim in sims)
            {
                var go = sim.gameObject;
                Object.DestroyImmediate(sim, true);
                if (go.GetComponent<InputSystemUIInputModule>() == null)
                    go.AddComponent<InputSystemUIInputModule>();
                converted++;
            }
            if (converted > 0)
            {
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[MigrateInputModules] {path}: replaced {converted} StandaloneInputModule(s)");
                totalMigrated += converted;
                scenesChanged++;
            }
        }

        Debug.Log($"[MigrateInputModules] Done. Scenes modified: {scenesChanged}, components replaced: {totalMigrated}");
        EditorUtility.DisplayDialog(
            "Migration complete",
            $"Scenes modified: {scenesChanged}\nComponents replaced: {totalMigrated}",
            "OK");
    }
}
