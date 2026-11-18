#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CardGame.EditorTools
{
    /// <summary>
    /// Turns your card sprites into CardData assets.
    ///
    /// Expected sprite names:  Card_&lt;Element&gt;_&lt;Name&gt;_Base   and   Card_&lt;Element&gt;_&lt;Name&gt;_Art
    ///   e.g. Card_Fire_BurningSkeleton_Base + Card_Fire_BurningSkeleton_Art
    ///        -> card "Burning Skeleton", element "Fire", with frame + art filled in.
    ///
    /// How to use:
    ///   1. In the Project window select one or more folders (or the sprites themselves).
    ///   2. Tools > Card Game > Create Cards From Selected Sprites
    ///   3. Cards are saved in Assets/CardGame/Cards/&lt;Element&gt;/ and added to the GameManager
    ///      in the open scene. Running it again only updates the sprites; your stats are kept.
    /// </summary>
    public static class CardSpriteImporter
    {
        const string CardsRoot = "Assets/CardGame/Cards";
        // Lenient: allows "_" or spaces between parts, and the "_0" Unity adds to sliced sprites.
        static readonly Regex NamePattern = new Regex(@"^\s*Card[_\s-]+([^_\s-]+)[_\s-]+(.+?)[_\s-]+(Base|Art)(?:[_\s-]*\d+)?\s*$", RegexOptions.IgnoreCase);

        class Pair
        {
            public string element;
            public string id;
            public Sprite baseSprite;
            public Sprite art;
            public bool isTrap; // sprite lives in a ".../Traps/" folder
        }

        [MenuItem("Tools/Card Game/Create Cards From Selected Sprites")]
        public static void CreateFromSelection()
        {
            List<Sprite> sprites = CollectSelectedSprites();
            if (sprites.Count == 0)
            {
                EditorUtility.DisplayDialog("Card Game",
                    "Select a folder with your card sprites (or the sprites themselves) in the Project window first.\n\n" +
                    "Names should look like Card_Fire_BurningSkeleton_Base and Card_Fire_BurningSkeleton_Art, " +
                    "and the images must be imported as Texture Type: Sprite (2D and UI).", "OK");
                return;
            }

            // ---- pair Base + Art sprites by element and name
            var pairs = new Dictionary<string, Pair>();
            var skipped = new List<string>();
            foreach (Sprite s in sprites)
            {
                Match m = NamePattern.Match(s.name);
                if (!m.Success) { skipped.Add(s.name); continue; }

                string element = m.Groups[1].Value;
                string id = m.Groups[2].Value.Trim('_', ' ', '-');
                string key = element + "/" + id;
                if (!pairs.TryGetValue(key, out Pair p)) pairs[key] = p = new Pair { element = element, id = id };
                if (AssetDatabase.GetAssetPath(s).Replace('\\', '/').IndexOf("/Traps/", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    p.isTrap = true;

                if (m.Groups[3].Value.ToLowerInvariant() == "base") p.baseSprite = s;
                else p.art = s;
            }

            if (pairs.Count == 0)
            {
                string examples = string.Join("\n", skipped.Take(8));
                EditorUtility.DisplayDialog("Card Game",
                    $"Found {sprites.Count} sprite(s), but none matched Card_<Element>_<Name>_Base / _Art.\n\n" +
                    $"Sprite names found:\n{examples}", "OK");
                Debug.Log("[CardGame] Sprites found but not matched:\n" + string.Join("\n", skipped));
                return;
            }

            // ---- create or update the CardData assets
            var created = new List<CardData>();
            int newCount = 0;
            foreach (Pair p in pairs.Values.OrderBy(x => x.element).ThenBy(x => x.id))
            {
                string folder = $"{CardsRoot}/{p.element}";
                EnsureFolder(folder);
                string path = $"{folder}/{p.id}.asset";

                var card = AssetDatabase.LoadAssetAtPath<CardData>(path);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardData>();
                    card.cardName = SplitCamelCase(p.id);
                    card.type = p.isTrap ? CardType.Trap : CardType.Servant;
                    card.cost = 2;
                    card.attack = 2;
                    card.health = 2;
                    card.artColor = Color.white;
                    AssetDatabase.CreateAsset(card, path);
                    newCount++;
                }

                card.element = p.element;
                if (p.baseSprite) card.baseSprite = p.baseSprite;
                if (p.art) card.artwork = p.art;
                EditorUtility.SetDirty(card);
                created.Add(card);
            }
            AssetDatabase.SaveAssets();

            // ---- add them to the GameManager in the open scene
            string poolNote = "Open the CardGame scene and run this again to add them to the deck.";
            GameManager gm = Object.FindAnyObjectByType<GameManager>();
            if (gm != null)
            {
                int choice = EditorUtility.DisplayDialogComplex("Card Game",
                    $"Made {created.Count} cards ({newCount} new).\n\nAdd them to the GameManager's Card Pool?",
                    "Replace pool", "Don't change", "Add to pool");

                if (choice != 1)
                {
                    Undo.RecordObject(gm, "Update card pool");
                    if (choice == 0) gm.cardPool.Clear();
                    foreach (CardData c in created)
                        if (!gm.cardPool.Contains(c)) gm.cardPool.Add(c);
                    EditorUtility.SetDirty(gm);
                    EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
                    poolNote = $"Card Pool now has {gm.cardPool.Count} cards. Save the scene (Cmd+S).";
                }
                else poolNote = "Card Pool was not changed.";
            }

            string skippedNote = skipped.Count > 0 ? $"\n\nSkipped {skipped.Count} sprite(s) with other names, e.g. {skipped[0]}." : "";
            EditorUtility.DisplayDialog("Card Game",
                $"Cards saved in {CardsRoot}/<Element>/.\n{poolNote}\n\n" +
                "New cards start with Cost 2 / Attack 2 / Health 2 (Servants, or Traps if they came from a Traps folder). Select each card to set its real stats." +
                skippedNote, "OK");

            if (created.Count > 0)
            {
                Selection.activeObject = created[0];
                EditorGUIUtility.PingObject(created[0]);
            }
        }

        static List<Sprite> CollectSelectedSprites()
        {
            var paths = new HashSet<string>();
            foreach (string guid in Selection.assetGUIDs)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string g in AssetDatabase.FindAssets("t:Sprite", new[] { path }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(g));
                }
                else paths.Add(path);
            }

            var sprites = new List<Sprite>();
            foreach (string path in paths)
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Sprite s && !sprites.Contains(s)) sprites.Add(s);
            return sprites;
        }

        static string SplitCamelCase(string s)
        {
            // "BurningSkeleton" -> "Burning Skeleton", "Magma_Brute" -> "Magma Brute"
            s = s.Replace('_', ' ').Replace('-', ' ');
            return Regex.Replace(s, "(?<=[a-z0-9])(?=[A-Z])", " ").Trim();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
#endif