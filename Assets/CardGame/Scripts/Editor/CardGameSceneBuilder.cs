#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGame.EditorTools
{
    /// <summary>
    /// One-click setup: Tools > Card Game > Build Demo Scene
    /// Creates sample cards, prefabs and a dungeon-style lane board:
    ///   left:   portrait, mana gems and HP for each player
    ///   middle: 3 lanes (LEFT / MID / RIGHT), each with a SERVANT and a TRAP slot per side
    ///   right:  deck and grave piles for each player
    ///   bottom: your 3-card hand
    /// </summary>
    public static class CardGameSceneBuilder
    {
        const string Root = "Assets/CardGame";
        const string CardsFolder = Root + "/Cards";
        const string PrefabFolder = Root + "/Prefabs";
        const string ScenePath = Root + "/CardGame.unity";

        static readonly Vector2 CardSize = new Vector2(104, 142);
        static readonly Vector2 SlotSize = new Vector2(116, 156);

        // layout (reference resolution 1920 x 1080, origin = screen centre)
        const float HeroX = -560f;
        const float PileX = 300f;
        const float BoardCenterX = -150f;
        static readonly float[] LaneX = { -300f, -130f, 40f };
        const float EnemyTrapY = 435f, EnemyServantY = 270f, MidY = 170f, PlayerServantY = 70f, PlayerTrapY = -95f;

        // dungeon palette
        static readonly Color Bg = Hex("#15131C");
        static readonly Color FramePanel = Hex("#1C1925");
        static readonly Color Gold = Hex("#B8913F");
        static readonly Color GoldDark = Hex("#8A6D2F");
        static readonly Color Parchment = Hex("#F1EFE8");
        static readonly Color Muted = Hex("#8C8299");
        static readonly Color SlotEdge = Hex("#3A3346");
        static readonly Color SlotInner = Hex("#221E2C");
        static readonly Color TrapInner = Hex("#2A1D25");

        static Sprite roundSprite;
        static Sprite circleSprite;

        [MenuItem("Tools/Card Game/Build Demo Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Card Game", "Stop Play mode first, then run Build Demo Scene again.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            circleSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            EnsureFolder(Root);
            EnsureFolder(CardsFolder);
            EnsureFolder(PrefabFolder);

            List<CardData> cards = CreateSampleCards();
            GameObject deckBack = CreateBackPrefab("CardBack", GoldDark, Hex("#3C3489"), "?", Hex("#CECBF6"));
            GameObject graveBack = CreateBackPrefab("GraveCard", Hex("#444441"), Hex("#2C2C2A"), "RIP", Hex("#888780"));
            CardView cardPrefab = CreateCardPrefab(deckBack);
            BuildScene(cards, cardPrefab, deckBack, graveBack);

            Debug.Log("[CardGame] Dungeon board built at " + ScenePath + ". Press Play!");
        }

        // ================================================================ sample cards

        static List<CardData> CreateSampleCards()
        {
            var defs = new (string name, CardType type, int cost, int atk, int hp, string hex, string desc)[]
            {
                ("Skeleton",     CardType.Servant, 1, 1, 2, "#D3D1C7", "Rattles but holds the line."),
                ("Goblin",       CardType.Servant, 1, 2, 1, "#C0DD97", "Quick and fragile."),
                ("Archer",       CardType.Servant, 2, 2, 2, "#9FE1CB", "Steady ranged damage."),
                ("Orc Brute",    CardType.Servant, 3, 3, 4, "#97C459", "Hits hard, takes hits."),
                ("Dark Mage",    CardType.Servant, 3, 4, 2, "#F4C0D1", "Glass cannon."),
                ("Stone Golem",  CardType.Servant, 4, 3, 6, "#FAC775", "A wall that walks."),
                ("Dragon",       CardType.Servant, 5, 6, 5, "#F09595", "Rules the dungeon."),
                ("Spike Pit",    CardType.Trap,    1, 2, 1, "#AFA9EC", "Deals 2 to a servant entering this lane."),
                ("Fire Rune",    CardType.Trap,    2, 3, 1, "#F0997B", "Deals 3 to a servant entering this lane."),
                ("Poison Snare", CardType.Trap,    3, 4, 1, "#5DCAA5", "Deals 4 to a servant entering this lane."),
            };

            var list = new List<CardData>();
            foreach (var d in defs)
            {
                string path = $"{CardsFolder}/{d.name}.asset";
                var card = AssetDatabase.LoadAssetAtPath<CardData>(path);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardData>();
                    AssetDatabase.CreateAsset(card, path);
                }
                card.cardName = d.name;
                card.type = d.type;
                card.cost = d.cost;
                card.attack = d.atk;
                card.health = d.hp;
                card.description = d.desc;
                card.artColor = Hex(d.hex);
                EditorUtility.SetDirty(card);
                list.Add(card);
            }
            AssetDatabase.SaveAssets();
            return list;
        }

        // ================================================================ prefabs

        static GameObject CreateBackPrefab(string name, Color edge, Color inner, string mark, Color markColor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = CardSize;
            Img(go, edge, roundSprite, false);

            RectTransform innerRt = Stretch("Inner", go.transform, 5);
            Img(innerRt.gameObject, inner, roundSprite, false);
            Txt(Stretch("Mark", innerRt, 0), mark, 30, markColor, TextAlignmentOptions.Center);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static CardView CreateCardPrefab(GameObject backPrefab)
        {
            var go = new GameObject("Card", typeof(RectTransform));
            ((RectTransform)go.transform).sizeDelta = CardSize;

            // Invisible click area that never moves (so hover doesn't flicker).
            Img(go, new Color(1, 1, 1, 0), null, true);
            var group = go.AddComponent<CanvasGroup>();
            var view = go.AddComponent<CardView>();

            RectTransform visual = Stretch("Visual", go.transform, 0);

            // selection glow (slightly bigger than the card, behind it)
            RectTransform glow = Stretch("SelectedGlow", visual, -6);
            Img(glow.gameObject, Hex("#EF9F27"), roundSprite, false);
            glow.gameObject.SetActive(false);

            // ---- front
            RectTransform front = Stretch("Front", visual, 0);
            Img(front.gameObject, GoldDark, roundSprite, false);
            RectTransform face = Stretch("Face", front, 3);
            Img(face.gameObject, Hex("#2A2433"), roundSprite, false);

            var nameText = Txt(Rect("Name", face, new Vector2(0.5f, 1f), new Vector2(6, -15), new Vector2(84, 22)), "Name", 14, Parchment, TextAlignmentOptions.Center);
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 9;
            nameText.fontSizeMax = 14;

            RectTransform art = Rect("Art", face, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(86, 58));
            var artImg = Img(art.gameObject, Color.white, roundSprite, false);

            var typeText = Txt(Rect("Type", face, new Vector2(0.5f, 0.5f), new Vector2(0, -29), new Vector2(90, 16)), "SERVANT", 10, Muted, TextAlignmentOptions.Center);

            RectTransform atkBox = Rect("Attack", face, new Vector2(0f, 0f), new Vector2(18, 17), new Vector2(30, 30));
            Img(atkBox.gameObject, Hex("#FAC775"), circleSprite, false);
            var atkText = Txt(Stretch("Value", atkBox, 0), "1", 16, Hex("#412402"), TextAlignmentOptions.Center);

            RectTransform hpBox = Rect("Health", face, new Vector2(1f, 0f), new Vector2(-18, 17), new Vector2(30, 30));
            Img(hpBox.gameObject, Hex("#F09595"), circleSprite, false);
            var hpText = Txt(Stretch("Value", hpBox, 0), "1", 16, Hex("#501313"), TextAlignmentOptions.Center);

            RectTransform costBox = Rect("Cost", front, new Vector2(0f, 1f), new Vector2(5, -5), new Vector2(30, 30));
            Img(costBox.gameObject, Hex("#185FA5"), circleSprite, false);
            var costText = Txt(Stretch("Value", costBox, 0), "1", 16, Color.white, TextAlignmentOptions.Center);

            // ---- back
            GameObject back = (GameObject)PrefabUtility.InstantiatePrefab(backPrefab, visual);
            PrefabUtility.UnpackPrefabInstance(back, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            back.name = "Back";
            var backRt = (RectTransform)back.transform;
            backRt.anchorMin = Vector2.zero;
            backRt.anchorMax = Vector2.one;
            backRt.offsetMin = backRt.offsetMax = Vector2.zero;
            back.SetActive(false);

            view.visual = visual;
            view.front = front.gameObject;
            view.back = back;
            view.selectedGlow = glow.gameObject;
            view.nameText = nameText;
            view.costText = costText;
            view.typeText = typeText;
            view.attackText = atkText;
            view.healthText = hpText;
            view.healthBox = hpBox.gameObject;
            view.artImage = artImg;
            view.canvasGroup = group;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabFolder + "/Card.prefab");
            Object.DestroyImmediate(go);
            return prefab.GetComponent<CardView>();
        }

        // ================================================================ scene

        static void BuildScene(List<CardData> cards, CardView cardPrefab, GameObject deckBack, GameObject graveBack)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Bg; }

            // Canvas
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform c = canvasGo.transform;

            // EventSystem: new Input System module if that package is installed, otherwise the classic one.
            var es = new GameObject("EventSystem", typeof(EventSystem));
            System.Type newInputModule = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputModule != null) es.AddComponent(newInputModule);
            else es.AddComponent<StandaloneInputModule>();

            // Background
            Img(Stretch("Background", c, 0).gameObject, Bg, null, false);

            // Gold-framed board
            RectTransform frame = Rect("BoardFrame", c, Center, new Vector2(BoardCenterX, MidY), new Vector2(1300, 720));
            Img(frame.gameObject, Gold, roundSprite, false);
            Img(Stretch("Inner", frame, 3).gameObject, FramePanel, roundSprite, false);

            // Midline + lane labels
            RectTransform line = Rect("MidLine", c, Center, new Vector2(BoardCenterX, MidY), new Vector2(1270, 3));
            Img(line.gameObject, GoldDark, null, false);
            string[] laneNames = { "LEFT", "MID", "RIGHT" };
            for (int i = 0; i < 3; i++)
            {
                RectTransform pill = Rect("Lane_" + laneNames[i], c, Center, new Vector2(LaneX[i], MidY), new Vector2(80, 24));
                Img(pill.gameObject, Gold, roundSprite, false);
                Txt(Stretch("Text", pill, 0), laneNames[i], 13, Hex("#2C2C2A"), TextAlignmentOptions.Center);
            }

            // ---- slots
            var enemyTraps = new BoardSlot[3];
            var enemyServants = new BoardSlot[3];
            var playerServants = new BoardSlot[3];
            var playerTraps = new BoardSlot[3];
            for (int lane = 0; lane < 3; lane++)
            {
                enemyTraps[lane] = Slot(c, CardType.Trap, lane, false, EnemyTrapY);
                enemyServants[lane] = Slot(c, CardType.Servant, lane, false, EnemyServantY);
                playerServants[lane] = Slot(c, CardType.Servant, lane, true, PlayerServantY);
                playerTraps[lane] = Slot(c, CardType.Trap, lane, true, PlayerTrapY);
            }

            // ---- heroes
            Hero(c, "Enemy", "OPPONENT", true, out RectTransform enemyPortrait, out TMP_Text enemyHp, out ManaBar enemyMana, out TMP_Text enemyHand);
            Hero(c, "Player", "YOU", false, out RectTransform playerPortrait, out TMP_Text playerHp, out ManaBar playerMana, out TMP_Text _);

            // ---- piles
            DeckPile enemyDeck = Pile(c, "EnemyDeck", EnemyTrapY, deckBack, true);
            DeckPile enemyGrave = Pile(c, "EnemyGrave", EnemyServantY, graveBack, false);
            DeckPile playerGrave = Pile(c, "PlayerGrave", PlayerServantY, graveBack, false);
            DeckPile playerDeck = Pile(c, "PlayerDeck", PlayerTrapY, deckBack, true);

            // ---- log + hand
            var log = Txt(Rect("Log", c, Center, new Vector2(BoardCenterX, -235), new Vector2(1100, 40)), "", 22, Parchment, TextAlignmentOptions.Center);

            RectTransform hand = Rect("Hand", c, Center, new Vector2(BoardCenterX, -395), new Vector2(520, 180));
            Img(hand.gameObject, new Color(1, 1, 1, 0.03f), roundSprite, false);
            var layout = hand.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 22;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            Txt(Rect("HandLabel", c, Center, new Vector2(BoardCenterX - 300f, -395), new Vector2(80, 30)), "HAND", 16, Muted, TextAlignmentOptions.Center);

            // ---- buttons
            Button endTurn = MakeButton("EndTurnButton", c, new Vector2(640, MidY), new Vector2(200, 70), "END TURN", Gold, Hex("#2C2C2A"));
            Button restart = MakeButton("RestartButton", c, new Vector2(640, MidY - 85), new Vector2(160, 50), "RESTART", Hex("#3A3346"), Parchment);

            // ---- game manager
            var gm = new GameObject("GameManager").AddComponent<GameManager>();
            gm.cardPool = cards;
            gm.cardPrefab = cardPrefab;
            gm.playerDeck = playerDeck;
            gm.enemyDeck = enemyDeck;
            gm.playerGrave = playerGrave;
            gm.enemyGrave = enemyGrave;
            gm.playerServantSlots = playerServants;
            gm.playerTrapSlots = playerTraps;
            gm.enemyServantSlots = enemyServants;
            gm.enemyTrapSlots = enemyTraps;
            gm.playerHand = hand;
            gm.playerPortrait = playerPortrait;
            gm.enemyPortrait = enemyPortrait;
            gm.playerHealthText = playerHp;
            gm.enemyHealthText = enemyHp;
            gm.playerMana = playerMana;
            gm.enemyMana = enemyMana;
            gm.enemyHandText = enemyHand;
            gm.logText = log;
            gm.endTurnButton = endTurn;
            gm.restartButton = restart;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
        }

        static BoardSlot Slot(Transform parent, CardType kind, int lane, bool playerSide, float y)
        {
            string side = playerSide ? "Player" : "Enemy";
            RectTransform rt = Rect($"{side}{kind}Slot_{lane}", parent, Center, new Vector2(LaneX[lane], y), SlotSize);
            Img(rt.gameObject, SlotEdge, roundSprite, true); // click target
            Img(Stretch("Inner", rt, 2).gameObject, kind == CardType.Trap ? TrapInner : SlotInner, roundSprite, false);
            Txt(Stretch("Label", rt, 0), kind == CardType.Trap ? "TRAP" : "SERVANT", 13,
                kind == CardType.Trap ? Hex("#7A4A55") : Hex("#5F5670"), TextAlignmentOptions.Center);

            RectTransform glow = Stretch("Highlight", rt, 0);
            Img(glow.gameObject, new Color(0.94f, 0.62f, 0.15f, 0.35f), roundSprite, false);
            glow.gameObject.SetActive(false);

            var slot = rt.gameObject.AddComponent<BoardSlot>();
            slot.kind = kind;
            slot.lane = lane;
            slot.isPlayerSide = playerSide;
            slot.highlight = glow.gameObject;
            return slot;
        }

        static void Hero(Transform parent, string side, string title, bool isEnemy,
                         out RectTransform portrait, out TMP_Text hpText, out ManaBar mana, out TMP_Text handText)
        {
            // enemy panel stacks downward from the top, player panel stacks upward from the bottom
            float dir = isEnemy ? -1f : 1f;
            float baseY = isEnemy ? 505f : -165f;

            RectTransform titleRt = Rect(side + "Title", parent, Center, new Vector2(HeroX, baseY), new Vector2(130, 26));
            Img(titleRt.gameObject, Gold, roundSprite, false);
            Txt(Stretch("Text", titleRt, 0), title, 14, Hex("#2C2C2A"), TextAlignmentOptions.Center);

            portrait = Rect(side + "Portrait", parent, Center, new Vector2(HeroX, baseY + dir * 80f), new Vector2(140, 110));
            Img(portrait.gameObject, Gold, roundSprite, false);
            RectTransform pInner = Stretch("Inner", portrait, 3);
            Img(pInner.gameObject, Hex("#100E15"), roundSprite, false);
            RectTransform head = Rect("Head", pInner, new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(40, 40));
            Img(head.gameObject, Hex("#444441"), circleSprite, false);
            RectTransform body = Rect("Body", pInner, new Vector2(0.5f, 0f), new Vector2(0, 18), new Vector2(84, 36));
            Img(body.gameObject, Hex("#444441"), roundSprite, false);

            handText = null;
            if (isEnemy)
                handText = Txt(Rect("HandCount", parent, Center, new Vector2(HeroX + 125f, baseY + dir * 80f), new Vector2(90, 26)), "HAND 0", 14, Muted, TextAlignmentOptions.Left);

            // mana
            RectTransform manaRt = Rect(side + "Mana", parent, Center, new Vector2(HeroX, baseY + dir * 165f), new Vector2(170, 44));
            Img(manaRt.gameObject, SlotEdge, roundSprite, false);
            Txt(Rect("Label", manaRt, new Vector2(0.5f, 1f), new Vector2(0, -8), new Vector2(100, 14)), "MANA", 11, Muted, TextAlignmentOptions.Center);
            mana = manaRt.gameObject.AddComponent<ManaBar>();
            mana.gems = new Image[5];
            for (int i = 0; i < 5; i++)
            {
                RectTransform gem = Rect("Gem" + i, manaRt, new Vector2(0.5f, 0.5f), new Vector2(-56 + i * 28, -6), new Vector2(20, 20));
                mana.gems[i] = Img(gem.gameObject, Hex("#185FA5"), circleSprite, false);
            }

            // HP
            RectTransform hpRt = Rect(side + "HP", parent, Center, new Vector2(HeroX, baseY + dir * 225f), new Vector2(110, 54));
            Img(hpRt.gameObject, Hex("#639922"), roundSprite, false);
            RectTransform hpInner = Stretch("Inner", hpRt, 3);
            Img(hpInner.gameObject, Hex("#173404"), roundSprite, false);
            Txt(Rect("Label", hpInner, new Vector2(0f, 0.5f), new Vector2(20, 0), new Vector2(30, 20)), "HP", 13, Hex("#97C459"), TextAlignmentOptions.Center);
            hpText = Txt(Rect("Value", hpInner, new Vector2(1f, 0.5f), new Vector2(-30, 0), new Vector2(50, 40)), "20", 28, Hex("#EAF3DE"), TextAlignmentOptions.Center);
        }

        static DeckPile Pile(Transform parent, string name, float y, GameObject backPrefab, bool isDeck)
        {
            RectTransform rt = Rect(name, parent, Center, new Vector2(PileX, y), SlotSize);
            Img(rt.gameObject, SlotEdge, roundSprite, true); // click target
            Img(Stretch("Inner", rt, 2).gameObject, SlotInner, roundSprite, false);
            Txt(Stretch("EmptyLabel", rt, 0), isDeck ? "DECK" : "GRAVE", 13, Hex("#5F5670"), TextAlignmentOptions.Center);

            RectTransform glow = Stretch("Highlight", rt, -6);
            Img(glow.gameObject, Hex("#EF9F27"), roundSprite, false);
            glow.gameObject.SetActive(false);
            glow.SetAsFirstSibling();

            RectTransform stack = Rect("Stack", rt, new Vector2(0.5f, 0.5f), new Vector2(-4, -4), CardSize);

            RectTransform labelRt = Rect("Label", rt, new Vector2(1f, 0.5f), new Vector2(60, 0), new Vector2(100, 50));
            var label = Txt(labelRt, name, 15, Muted, TextAlignmentOptions.Left);

            var pile = rt.gameObject.AddComponent<DeckPile>();
            pile.stackRoot = stack;
            pile.cardBackPrefab = backPrefab;
            pile.label = label;
            pile.highlight = glow.gameObject;
            pile.cardsPerBack = isDeck ? 3 : 1;
            pile.stackOffset = 2f;
            return pile;
        }

        static Button MakeButton(string name, Transform parent, Vector2 pos, Vector2 size, string label, Color bg, Color fg)
        {
            RectTransform rt = Rect(name, parent, Center, pos, size);
            var img = Img(rt.gameObject, bg, roundSprite, true);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            Txt(Stretch("Text", rt, 0), label, 22, fg, TextAlignmentOptions.Center);
            return btn;
        }

        // ================================================================ helpers

        static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent, float inset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        static Image Img(GameObject go, Color color, Sprite sprite, bool raycast)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            if (sprite) { img.sprite = sprite; img.type = Image.Type.Sliced; }
            img.raycastTarget = raycast;
            return img;
        }

        static TextMeshProUGUI Txt(RectTransform rt, string text, float size, Color color, TextAlignmentOptions align)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false;
            return t;
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
#endif
