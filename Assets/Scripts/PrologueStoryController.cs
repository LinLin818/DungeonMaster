using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class PrologueStoryController : MonoBehaviour
{
    public enum Chapter
    {
        Home,
        BurgerShop,
        Guild,
        Dungeon
    }

    struct DialogueLine
    {
        public readonly string Speaker;
        public readonly string Text;

        public DialogueLine(string speaker, string text)
        {
            Speaker = speaker;
            Text = text;
        }
    }

    [Header("Chapter")]
    [SerializeField] Chapter chapter;
    [SerializeField] string nextSceneName;

    [Header("Scene References")]
    [SerializeField] SideScrollerPlayerController player;
    [SerializeField] SceneFadeTransition fadeTransition;
    [SerializeField] GameObject dialoguePanel;
    [SerializeField] TMP_Text speakerText;
    [SerializeField] TMP_Text bodyText;
    [SerializeField] TMP_Text objectiveText;

    readonly List<DialogueLine> activeLines = new();
    int lineIndex;
    bool dialogueOpen;
    bool chapterComplete;
    bool conversationPlayed;
    bool completeAfterDialogue;
    bool loadAfterDialogue;
    Button continueButton;

    void Start()
    {
        continueButton = dialoguePanel.GetComponentInChildren<Button>(true);
        SetDialogueContentsVisible(false);

        switch (chapter)
        {
            case Chapter.Home:
                objectiveText.text = "GOAL: FIND SOMETHING TO EAT";
                completeAfterDialogue = true;
                BeginDialogue(new[]
                {
                    new DialogueLine("YOU", "I should get some food I'm so hungry."),
                    new DialogueLine("YOU", "The shop is to the right. Press A and D to move. W to jump.")
                });
                break;
            case Chapter.BurgerShop:
                objectiveText.text = "GOAL: ASK THE WORKER FOR A BURGER";
                break;
            case Chapter.Guild:
                objectiveText.text = "GOAL: APPLY FOR WORK AT THE GUILD";
                break;
            case Chapter.Dungeon:
                objectiveText.text = "GOAL: ENTER YOUR FIRST DUNGEON";
                break;
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!dialogueOpen || keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            AdvanceDialogue();
    }

    public bool BeginChapterConversation()
    {
        if (conversationPlayed || dialogueOpen)
            return false;

        conversationPlayed = true;
        completeAfterDialogue = chapter != Chapter.Dungeon;
        loadAfterDialogue = chapter == Chapter.Dungeon;

        switch (chapter)
        {
            case Chapter.BurgerShop:
                BeginDialogue(new[]
                {
                    new DialogueLine("YOU", "Can I have a burger please. "),
                    new DialogueLine("BURGER WORKER", "Five gold."),
                    new DialogueLine("YOU", "FIVE? I only have 2 copper, could you spot me?"),
                    new DialogueLine("BURGER WORKER", "No."),
                    new DialogueLine("YOU", "How could I let it get to this. I need to find a job. ")
                });
                return true;
            case Chapter.Guild:
                BeginDialogue(new[]
                {
                    new DialogueLine("GUILD CLERK", "Looking for work?"),
                    new DialogueLine("YOU", "I need a job. Any job."),
                    new DialogueLine("GUILD CLERK", "Register as a Dungeon Master. Clear a floor and you'll be paid."),
                    new DialogueLine("YOU", "Sign me up!"),
                    new DialogueLine("GUILD CLERK", "Welcome to the guild. The dungeon is to the right."),
                });
                return true;
            case Chapter.Dungeon:
                BeginDialogue(new[]
                {
                    new DialogueLine("YOU", "Time to make something of myself. "),
                    new DialogueLine("YOU", "And get a burger.")
                });
                return true;
            default:
                return false;
        }
    }

    public void TryLeaveChapter()
    {
        if (dialogueOpen)
            return;

        if (chapterComplete)
        {
            fadeTransition.LoadScene(nextSceneName);
            return;
        }

        string reminder = chapter == Chapter.BurgerShop
            ? "I should ask the worker about that burger first."
            : "I should talk to the guild clerk first.";
        BeginDialogue(new[] { new DialogueLine("YOU", reminder) });
    }

    public void AdvanceDialogue()
    {
        if (!dialogueOpen)
            return;

        lineIndex++;
        if (lineIndex < activeLines.Count)
        {
            ShowCurrentLine();
            return;
        }

        dialogueOpen = false;
        speakerText.text = string.Empty;
        bodyText.text = string.Empty;
        SetDialogueContentsVisible(false);
        player.SetMovementEnabled(true);

        if (completeAfterDialogue)
        {
            chapterComplete = true;
            completeAfterDialogue = false;
            objectiveText.text = chapter switch
            {
                Chapter.Home => "GOAL: GO EAST TO THE BURGER SHOP",
                Chapter.BurgerShop => "GOAL: GO EAST TO THE GUILD",
                Chapter.Guild => "GOAL: GO EAST TO THE DUNGEON",
                _ => objectiveText.text
            };
        }

        if (loadAfterDialogue)
        {
            loadAfterDialogue = false;
            fadeTransition.LoadScene(nextSceneName);
        }
    }

    void BeginDialogue(IEnumerable<DialogueLine> lines)
    {
        activeLines.Clear();
        activeLines.AddRange(lines);
        lineIndex = 0;
        dialogueOpen = activeLines.Count > 0;
        SetDialogueContentsVisible(dialogueOpen);
        player.SetMovementEnabled(!dialogueOpen);
        if (dialogueOpen)
            ShowCurrentLine();
    }

    void SetDialogueContentsVisible(bool visible)
    {
        dialoguePanel.SetActive(true);
        speakerText.gameObject.SetActive(visible);
        bodyText.gameObject.SetActive(visible);
        if (continueButton != null)
            continueButton.gameObject.SetActive(visible);
    }

    void ShowCurrentLine()
    {
        DialogueLine line = activeLines[lineIndex];
        speakerText.text = line.Speaker;
        bodyText.text = line.Text;
    }
}
