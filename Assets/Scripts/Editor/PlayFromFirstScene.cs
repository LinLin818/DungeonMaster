#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Makes the Play button always start from the first enabled scene in Build Settings
/// (the Menu / opening screen), whichever scene you have open. When you stop playing,
/// the editor goes back to the scene you were editing.
///
/// Toggle it with: Tools > Play From Menu Scene
/// </summary>
[InitializeOnLoad]
public static class PlayFromFirstScene
{
    const string MenuPath = "Tools/Play From Menu Scene";
    const string PrefKey = "DungeonMaster.PlayFromFirstScene";

    static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static PlayFromFirstScene()
    {
        // Build Settings can't be read reliably until the editor has finished loading.
        EditorApplication.delayCall += Apply;
        EditorBuildSettings.sceneListChanged += Apply;
    }

    static void Apply()
    {
        SceneAsset start = null;
        if (Enabled)
        {
            EditorBuildSettingsScene first = EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled);
            if (first != null) start = AssetDatabase.LoadAssetAtPath<SceneAsset>(first.path);
        }
        EditorSceneManager.playModeStartScene = start;
    }

    [MenuItem(MenuPath)]
    static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
#endif
