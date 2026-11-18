#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CardGame.EditorTools
{
    /// <summary>
    /// Tools > Card Game > Apply Field Sprites
    ///
    /// Puts Field_Board.png behind the CardGame scene and lines everything up with the art:
    ///   - slots, piles and portraits become invisible click areas sitting on the slots drawn in the board
    ///   - HP uses the Counter_HP sheet (0..20), mana uses the Counter_Mana sheet ("MANA x/5")
    ///   - the old drawn frame, midline, lane labels and titles are removed (they are in the art now)
    ///
    /// The board is 960x540 and is shown at 2x on the 1920x1080 canvas, so every position below
    /// is in board pixels (top-left origin), measured from the PNG.
    /// Safe to run again.
    /// </summary>
    public static class FieldBoardSetup
    {
        const string BoardPath = "Assets/Art/Sprites/Field/Field_Board.png";
        const string HpSheetPath = "Assets/Art/Sprites/UI/Counters/Counter_HP_Sheet.png";
        const string ManaSheetPath = "Assets/Art/Sprites/UI/Counters/Counter_Mana_Sheet.png";

        const float BoardW = 960f, BoardH = 540f, Scale = 2f;

        // ---- measured from Field_Board.png (x, y = top-left corner in board pixels)
        static readonly int[] LaneX = { 391, 457, 523 };             // slot left edge per lane (incl. 1px border)
        const int PileX = 677;
        const int SlotW = 46, SlotH = 66;
        const int EnemyTrapY = 114, EnemyServantY = 184, PlayerServantY = 290, PlayerTrapY = 360;

        static readonly RectInt EnemyPortrait = new RectInt(230, 129, 57, 49);
        static readonly RectInt PlayerPortrait = new RectInt(230, 361, 57, 49);
        static readonly RectInt EnemyManaPanel = new RectInt(215, 185, 87, 23);
        static readonly RectInt PlayerManaPanel = new RectInt(215, 333, 87, 23);
        static readonly RectInt EnemyHpBox = new RectInt(229, 215, 58, 30);
        static readonly RectInt PlayerHpBox = new RectInt(229, 295, 58, 30);
        static readonly RectInt EnemyHpDigits = new RectInt(245, 229, 28, 11);
        static readonly RectInt PlayerHpDigits = new RectInt(245, 309, 28, 11);
        static readonly RectInt EnemyHandCount = new RectInt(293, 148, 60, 12);

        static readonly Color MarginColor = new Color32(30, 26, 34, 255); // board's outer background
        static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

        [MenuItem("Tools/Card Game/Apply Field Sprites")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Card Game", "Stop Play mode first, then run Apply Field Sprites again.", "OK");
                return;
            }

            GameManager gm = Object.FindAnyObjectByType<GameManager>();
            if (gm == null || gm.playerHand == null)
            {
                EditorUtility.DisplayDialog("Card Game", "Open the CardGame scene first (Assets/Scenes/CardGame.unity).", "OK");
                return;
            }

            Sprite board = LoadSprites(BoardPath).FirstOrDefault();
            Sprite[] hp = Enumerable.Range(0, 21).Select(i => Find(HpSheetPath, "Counter_HP_" + i)).ToArray();
            Sprite[] mana = Enumerable.Range(0, 6).Select(i => Find(ManaSheetPath, "Counter_Mana_" + i)).ToArray();
            var missing = new System.Collections.Generic.List<string>();
            if (board == null) missing.Add(BoardPath);
            if (hp.Any(s => s == null)) missing.Add($"{HpSheetPath}  ({hp.Count(s => s != null)}/21 sprites found)");
            if (mana.Any(s => s == null)) missing.Add($"{ManaSheetPath}  ({mana.Count(s => s != null)}/6 panels found)");
            if (missing.Count > 0)
            {
                EditorUtility.DisplayDialog("Card Game",
                    "Couldn't load these as sprites:\n\n" + string.Join("\n", missing) + "\n\n" +
                    "Right-click each one in the Project window > Reimport, then run this again. " +
                    "If it still fails, check the Console for import errors.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Apply Field Sprites");
            int undoGroup = Undo.GetCurrentGroup();

            Canvas canvas = gm.playerHand.GetComponentInParent<Canvas>().rootCanvas;
            Transform c = canvas.transform;

            // Always keep the whole 1920x1080 board on screen, whatever the window shape.
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler)
            {
                Undo.RecordObject(scaler, "Apply Field Sprites");
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(BoardW * Scale, BoardH * Scale);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }

            Camera cam = Camera.main;
            if (cam) { Undo.RecordObject(cam, "Apply Field Sprites"); cam.backgroundColor = MarginColor; }

            // ---- background + board art
            Transform bg = c.Find("Background");
            if (bg && bg.TryGetComponent(out Image bgImg)) { Undo.RecordObject(bgImg, "Apply Field Sprites"); bgImg.color = MarginColor; }

            Transform boardT = c.Find("FieldBoard");
            if (boardT == null)
            {
                var go = new GameObject("FieldBoard", typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Apply Field Sprites");
                boardT = go.transform;
                boardT.SetParent(c, false);
            }
            boardT.SetSiblingIndex(bg ? bg.GetSiblingIndex() + 1 : 0);
            var boardRt = (RectTransform)boardT;
            boardRt.anchorMin = boardRt.anchorMax = boardRt.pivot = new Vector2(0.5f, 0.5f);
            boardRt.anchoredPosition = Vector2.zero;
            boardRt.sizeDelta = new Vector2(BoardW * Scale, BoardH * Scale);
            var boardImg = boardT.GetComponent<Image>();
            boardImg.sprite = board;
            boardImg.color = Color.white;
            boardImg.type = Image.Type.Simple;
            boardImg.preserveAspect = false;
            boardImg.raycastTarget = false;

            // ---- things now drawn in the art
            foreach (string n in new[] { "BoardFrame", "MidLine", "Lane_LEFT", "Lane_MID", "Lane_RIGHT", "EnemyTitle", "PlayerTitle" })
                DestroyIfFound(c.Find(n));

            // ---- slots
            for (int lane = 0; lane < GameManager.Lanes; lane++)
            {
                SetupSlot(gm.enemyTrapSlots[lane], LaneX[lane], EnemyTrapY);
                SetupSlot(gm.enemyServantSlots[lane], LaneX[lane], EnemyServantY);
                SetupSlot(gm.playerServantSlots[lane], LaneX[lane], PlayerServantY);
                SetupSlot(gm.playerTrapSlots[lane], LaneX[lane], PlayerTrapY);
            }

            // ---- piles
            SetupPile(gm.enemyDeck, EnemyTrapY);
            SetupPile(gm.enemyGrave, EnemyServantY);
            SetupPile(gm.playerGrave, PlayerServantY);
            SetupPile(gm.playerDeck, PlayerTrapY);

            // ---- heroes
            SetupPortrait(gm.enemyPortrait, EnemyPortrait);
            SetupPortrait(gm.playerPortrait, PlayerPortrait);

            Undo.RecordObject(gm, "Apply Field Sprites");
            gm.enemyHealthCounter = SetupHp(c.Find("EnemyHP"), EnemyHpBox, EnemyHpDigits, hp);
            gm.playerHealthCounter = SetupHp(c.Find("PlayerHP"), PlayerHpBox, PlayerHpDigits, hp);
            if (gm.enemyHealthCounter) gm.enemyHealthText = null;   // old text objects were removed
            if (gm.playerHealthCounter) gm.playerHealthText = null;

            SetupMana(gm.enemyMana, EnemyManaPanel, mana);
            SetupMana(gm.playerMana, PlayerManaPanel, mana);

            if (gm.enemyHandText) PlaceOnBoard((RectTransform)gm.enemyHandText.transform, EnemyHandCount);

            // ---- off-board UI: log in the top margin, hand in the bottom margin, buttons in the empty right side
            Place(gm.logText ? (RectTransform)gm.logText.transform : null, new Vector2(0f, 434f), new Vector2(1100f, 40f));
            Place(gm.playerHand, new Vector2(0f, -434f), new Vector2(660f, 180f)); // 5 cards
            Place(c.Find("HandLabel") as RectTransform, new Vector2(-400f, -434f), new Vector2(80f, 30f));
            Place(gm.endTurnButton ? (RectTransform)gm.endTurnButton.transform : null, new Vector2(790f, 50f), new Vector2(200f, 70f));
            Place(gm.restartButton ? (RectTransform)gm.restartButton.transform : null, new Vector2(790f, -45f), new Vector2(160f, 50f));

            EditorUtility.SetDirty(gm);
            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(gm.gameObject.scene);
            EditorSceneManager.SaveScene(gm.gameObject.scene);

            Debug.Log("[CardGame] Field sprites applied and scene saved.");
            EditorUtility.DisplayDialog("Card Game", "Field board, HP and mana sprites applied. The scene has been saved.\n\nPress Play to try it.", "OK");
        }

        // ================================================================ pieces

        static void SetupSlot(BoardSlot slot, int x, int y)
        {
            if (slot == null) return;
            var rt = (RectTransform)slot.transform;
            PlaceOnBoard(rt, new RectInt(x, y, SlotW, SlotH));
            MakeInvisibleClickArea(slot.gameObject);
            DestroyIfFound(rt.Find("Inner"));
            DestroyIfFound(rt.Find("Label"));

            Undo.RecordObject(slot, "Apply Field Sprites");
            // A card is 142 tall; the slot's inside is 64 board px = 128.
            slot.cardScale = (SlotH - 2) * Scale / 142f;
            EditorUtility.SetDirty(slot);
        }

        static void SetupPile(DeckPile pile, int y)
        {
            if (pile == null) return;
            var rt = (RectTransform)pile.transform;
            PlaceOnBoard(rt, new RectInt(PileX, y, SlotW, SlotH));
            MakeInvisibleClickArea(pile.gameObject);
            DestroyIfFound(rt.Find("Inner"));
            DestroyIfFound(rt.Find("EmptyLabel"));

            if (pile.stackRoot)
            {
                Undo.RecordObject(pile.stackRoot, "Apply Field Sprites");
                pile.stackRoot.localScale = Vector3.one * ((SlotH - 2) * Scale / 142f);
                pile.stackRoot.anchoredPosition = new Vector2(-4f, -4f);
            }
        }

        static void SetupPortrait(RectTransform portrait, RectInt rect)
        {
            if (portrait == null) return;
            PlaceOnBoard(portrait, rect);
            if (portrait.TryGetComponent(out Image img)) { Undo.RecordObject(img, "Apply Field Sprites"); img.color = Clear; img.raycastTarget = false; }
            DestroyChildren(portrait, null);
        }

        static SpriteCounter SetupHp(Transform root, RectInt box, RectInt digits, Sprite[] frames)
        {
            if (root == null) return null;
            var rt = (RectTransform)root;
            PlaceOnBoard(rt, box);
            if (root.TryGetComponent(out Image img)) { Undo.RecordObject(img, "Apply Field Sprites"); img.color = Clear; img.raycastTarget = false; }
            DestroyChildren(root, "Digits");

            Transform digitsT = root.Find("Digits");
            if (digitsT == null)
            {
                var go = new GameObject("Digits", typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(go, "Apply Field Sprites");
                digitsT = go.transform;
                digitsT.SetParent(root, false);
            }
            var drt = (RectTransform)digitsT;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
            drt.anchoredPosition = BoardCenter(digits) - BoardCenter(box);
            drt.sizeDelta = new Vector2(digits.width, digits.height) * Scale;
            var dImg = digitsT.GetComponent<Image>();
            dImg.sprite = frames[frames.Length - 1];
            dImg.type = Image.Type.Simple;
            dImg.color = Color.white;
            dImg.raycastTarget = false;

            SpriteCounter counter = root.GetComponent<SpriteCounter>();
            if (counter == null) counter = Undo.AddComponent<SpriteCounter>(root.gameObject);
            counter.image = dImg;
            counter.frames = frames;
            counter.fallbackText = null;
            EditorUtility.SetDirty(counter);
            return counter;
        }

        static void SetupMana(ManaBar bar, RectInt rect, Sprite[] panels)
        {
            if (bar == null) return;
            var rt = (RectTransform)bar.transform;
            PlaceOnBoard(rt, rect);
            DestroyChildren(rt, null); // "MANA" label + gems are part of the panel sprite

            Image img = bar.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(bar.gameObject);
            Undo.RecordObject(img, "Apply Field Sprites");
            img.sprite = panels[panels.Length - 1];
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.preserveAspect = false;
            img.raycastTarget = false;

            Undo.RecordObject(bar, "Apply Field Sprites");
            bar.panel = img;
            bar.panelSprites = panels;
            bar.gems = new Image[0];
            EditorUtility.SetDirty(bar);
        }

        // ================================================================ helpers

        static Vector2 BoardCenter(RectInt r) =>
            new Vector2((r.x + r.width * 0.5f - BoardW * 0.5f) * Scale, (BoardH * 0.5f - (r.y + r.height * 0.5f)) * Scale);

        static void PlaceOnBoard(RectTransform rt, RectInt r) =>
            Place(rt, BoardCenter(r), new Vector2(r.width, r.height) * Scale);

        static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            if (rt == null) return;
            Undo.RecordObject(rt, "Apply Field Sprites");
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void MakeInvisibleClickArea(GameObject go)
        {
            Image img = go.GetComponent<Image>();
            if (img == null) img = Undo.AddComponent<Image>(go);
            Undo.RecordObject(img, "Apply Field Sprites");
            img.color = Clear;          // invisible, but still catches clicks
            img.raycastTarget = true;
        }

        static void DestroyChildren(Transform t, string keep)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Transform child = t.GetChild(i);
                if (keep != null && child.name == keep) continue;
                Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        static void DestroyIfFound(Transform t)
        {
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }

        static Sprite[] LoadSprites(string path)
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            if (sprites.Length == 0)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
            }
            return sprites;
        }

        static Sprite Find(string path, string name) => LoadSprites(path).FirstOrDefault(s => s.name == name);
    }
}
#endif
