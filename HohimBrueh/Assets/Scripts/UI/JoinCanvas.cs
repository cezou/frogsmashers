using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FreeLives;
public class JoinCanvas : MonoBehaviour
{
    public enum State
    {
        Join,
        ChooseColor,
        Ready
    }

    public Transform effectPos;
    public Canvas joinPromptCanvas;
    public Canvas chooseColorCanvas;
    public Canvas backPromptCanvas;
    public GameObject teamChangeColorObject;
    public Text changeColorText;
    public GameObject[] changeModeObjects;
    public GameObject[] selectionBackObjects;
    public GameObject[] confirmObjects;

    public Text[] texts;

    public Player assignedPlayer;

    public State state;

    Color color;

    public Image frogImage;

    // Use this for initialization
    void Start()
    {

    }
    float delay = 1f;
    InputState input = new InputState();
    float colorLerpAmount;
    bool wasSelect;
    bool IsHostSlot()
    {
        var canvases = GameController.GetJoinCanvases();
        return canvases != null && canvases.Length > 0 && canvases[0] == this;
    }

    /// <summary>
    /// SELECT toggles team mode (host slot only, while selecting),
    /// matching the online lobby: gamepad select button or keyboard
    /// Tab. Polled directly so the InputState wire format stays
    /// untouched.
    /// </summary>
    bool SelectHeld()
    {
        switch (assignedPlayer.inputDevice)
        {
            case FreeLives.InputReader.Device.Keyboard1:
                var kb = UnityEngine.InputSystem.Keyboard.current;
                return kb != null && kb.tabKey.isPressed;
            default:
                int idx = (int)assignedPlayer.inputDevice
                    - (int)FreeLives.InputReader.Device.Gamepad1;
                var pads = UnityEngine.InputSystem.Gamepad.all;
                return idx >= 0 && idx < pads.Count
                    && pads[idx].selectButton.isPressed;
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (FrogSmashers.Net.OnlineMatch.InLobby)
            return;
        foreach (var text in texts)
        {
            text.color = Color.Lerp(Color.white, Color.black, Mathf.PingPong(Time.time * 3f, 1f));
        }

        bool isHostSlot = IsHostSlot();
        if (changeModeObjects != null)
        {
            foreach (var obj in changeModeObjects)
            {
                if (obj != null && obj.activeSelf != isHostSlot)
                    obj.SetActive(isHostSlot);
            }
        }
        bool select = assignedPlayer != null && state == State.ChooseColor && SelectHeld();
        bool selectPressed = select && !wasSelect;
        wasSelect = select;

        if (state == State.ChooseColor)
        {

            FreeLives.InputReader.GetInput(assignedPlayer.inputDevice, input);

            if (isHostSlot && selectPressed)
            {
                GameController.ToggleTeamMode();
                return;
            }


            if (GameController.isTeamMode)
            {
                if (input.bButton && !input.wasBButton)
                {
                    if (assignedPlayer.team == Team.Red)
                        assignedPlayer.team = Team.Blue;
                    else assignedPlayer.team = Team.Red;


                    colorLerpAmount = Random.Range(0f, 0.7f);

                    if (assignedPlayer.team == Team.Red)
                    {
                        color = assignedPlayer.color = Color.Lerp(Color.red, Color.white, colorLerpAmount);
                    }
                    else
                    {
                        color = assignedPlayer.color = Color.Lerp(Color.blue, Color.white, colorLerpAmount);
                    }

                    frogImage.color = color;
                    EffectsController.CreateSpawnEffects(effectPos.position, color);
                    SoundController.PlaySoundEffect("CharacterSpawn", 0.3f, effectPos.position);
                }
                if (input.left)
                    colorLerpAmount = Mathf.MoveTowards(colorLerpAmount, 0f, Time.deltaTime * 0.5f);
                else if (input.right)
                    colorLerpAmount = Mathf.MoveTowards(colorLerpAmount, 0.7f, Time.deltaTime * 0.5f);
                color = assignedPlayer.color = frogImage.color = Color.Lerp(assignedPlayer.team == Team.Red ? Color.red : Color.blue, Color.white, colorLerpAmount);


            }
            else
            {

                if (input.bButton && !input.wasBButton)
                {
                    var newColor = GameController.GetAvailableColor();
                    GameController.ReturnColor(color);
                    color = assignedPlayer.color = newColor;
                    frogImage.color = color;
                    EffectsController.CreateSpawnEffects(effectPos.position, color);
                    SoundController.PlaySoundEffect("CharacterSpawn", 0.3f, effectPos.position);
                }
            }
            if (input.yButton && !input.wasYButton)
            {
                UnAssignPlayer();
            }
            else if (input.xButton && !input.wasXButton)
            {
                ConfirmSelection();
            }
        }
        else if (state == State.Ready)
        {
            FreeLives.InputReader.GetInput(assignedPlayer.inputDevice, input);
            if (input.yButton && !input.wasYButton)
            {
                UnAssignPlayer();
            }
        }
    }

    /// <summary>Locks in the current color/team: spawns the movable
    /// frog and switches to the Ready hints (X press, or the
    /// screenshot harness).</summary>
    public void ConfirmSelection()
    {
        assignedPlayer.color = color;
        GameController.SpawnCharacterJoinScreen(assignedPlayer);
        state = State.Ready;
        chooseColorCanvas.enabled = false;
        backPromptCanvas.enabled = true;
    }

    /// <summary>
    /// Online-lobby variant of the selection hints: joining is
    /// automatic there and Y does nothing while selecting, so the
    /// BACK line is hidden and CONFIRM plus CHANGE MODE move up to
    /// fill its slot. Local lobbies keep BACK (players join with X
    /// and need a way out). Prefab instances are fresh per scene
    /// load, so no revert is needed.
    /// </summary>
    public void ApplyOnlineSelectionLayout()
    {
        if (selectionBackObjects != null)
        {
            foreach (var obj in selectionBackObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }
        MoveLine(confirmObjects, -1.74f, -3.24f);
        if (changeModeObjects != null && changeModeObjects.Length > 0
            && changeModeObjects[0] != null)
        {
            var line = changeModeObjects[0].transform;
            MoveTo(line.Find("Text"), -3.42f);
            MoveTo(line.Find("Icon"), -4.92f);
        }
    }

    static void MoveLine(GameObject[] objects, float textY, float iconY)
    {
        if (objects == null)
            return;
        foreach (var obj in objects)
        {
            if (obj == null)
                continue;
            bool isText = obj.GetComponent<Text>() != null;
            MoveTo(obj.transform, isText ? textY : iconY);
        }
    }

    static void MoveTo(Transform target, float y)
    {
        var rt = target as RectTransform;
        if (rt == null)
            return;
        var pos = rt.anchoredPosition;
        pos.y = y;
        rt.anchoredPosition = pos;
    }

    public void RefreshForMode()
    {
        if (assignedPlayer == null) return;

        if (GameController.isTeamMode)
        {
            // Switching FFA -> Team: release the pool color the player was using.
            GameController.ReturnColor(color);

            teamChangeColorObject.SetActive(true);
            changeColorText.text = "CHANGE TEAM";

            assignedPlayer.team = Random.value < 0.5f ? Team.Red : Team.Blue;
            colorLerpAmount = Random.Range(0f, 0.7f);
            color = assignedPlayer.color = Color.Lerp(
                assignedPlayer.team == Team.Red ? Color.red : Color.blue,
                Color.white,
                colorLerpAmount);
            frogImage.color = color;
        }
        else
        {
            // Switching Team -> FFA: team color wasn't from the pool, nothing to return.
            teamChangeColorObject.SetActive(false);
            changeColorText.text = "CHANGE COLOR";

            color = GameController.GetAvailableColor();
            assignedPlayer.color = color;
            frogImage.color = color;
        }
    }

    public void AssignPlayer(Player player)
    {
        assignedPlayer = player;
        state = State.ChooseColor;
        joinPromptCanvas.enabled = false;
        chooseColorCanvas.enabled = true;

        if (GameController.isTeamMode)
        {
            teamChangeColorObject.SetActive(true);
            changeColorText.text = "CHANGE TEAM";



            player.team = Random.value < 0.5f ? Team.Red : Team.Blue;
            if (assignedPlayer.team == Team.Red)
            {
                color = assignedPlayer.color = Color.Lerp(Color.red, Color.white, Random.Range(0f, 0.7f));
            }
            else
            {
                color = assignedPlayer.color = Color.Lerp(Color.blue, Color.white, Random.Range(0f, 0.7f));
            }
            frogImage.color = color;
        }
        else
        {
            teamChangeColorObject.SetActive(false);
            changeColorText.text = "CHANGE COLOR";

            color = GameController.GetAvailableColor();
            player.color = color;
            frogImage.color = color;
        }
        FreeLives.InputReader.GetInput(assignedPlayer.inputDevice, input);
        EffectsController.CreateSpawnEffects(effectPos.position, color);
        SoundController.PlaySoundEffect("CharacterSpawn", 0.3f, effectPos.position);
    }

    public bool HasAssignedPlayer()
    {
        return assignedPlayer != null;
    }

    void UnAssignPlayer()
    {
        GameController.ReturnPlayer(assignedPlayer);
        GameController.ReturnColor(color);
        if (assignedPlayer.character != null)
        {
            Destroy(assignedPlayer.character.gameObject);
        }
        state = State.Join;
        joinPromptCanvas.enabled = true;
        chooseColorCanvas.enabled = false;
        backPromptCanvas.enabled = false;
        assignedPlayer = null;

    }
}
