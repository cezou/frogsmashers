using System.Collections;
using System.Collections.Generic;
using FreeLives;
using FrogSmashers.Net.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public enum GameState
{
    Playing,
    RoundFinished,
    JoinScreen
}


public class GameController : MonoBehaviour, ISimTickable
{
    public static bool charactersBounceEachOther = false;
    public static bool weirdBounceTrajectories = false;
    public static bool onlyBounceBeforeRecover = true;
    public static bool allowTeamMode = true;

    public static List<Player> activePlayers = new List<Player>();

    static List<Player> inactivePlayers;

    GameState state;

    public static Color overallWinnerColor = Color.red;

    public static GameState State { get { return instance.state; } }

    public Character characterPrefab;

    public Fly flyPrefab, activeFly;

    float flySpawnDelay;

    public SpriteRenderer offscreenDotPrefab;

    public Canvas scoreCanvas;

    public List<PlayerScoreDisplay> playerScoreDisplays;

    public PlayerScoreDisplay scoreDisplayPrefab;

    static GameController instance;

    public Color[] playerColors;

    List<Color> availableColors;

    public bool isJoinScreen;

    public static string[] levelNames = new string[] { "1BusStop", "2DownSmash", "3Moon", "4FinalFrogstination", "5Skyline", "6Finale" };
    //public static string[] levelNames = new string[] {  "2DownSmash" };
    public JoinCanvas[] joinCanvas;

    float finishDelay = 7.5f;

    public Text joinCountdownText, joinGameModeText;

    public static bool isTeamMode;

    public static bool playersCanDropIn;

    public static bool isShowDown;

    int redTeamScore, blueTeamScore;
    PlayerScoreDisplay redTeamScoreDisplay, blueTeamScoreDisplay;

    void Awake()
    {
        //Camera.main.aspect = 16f / 9f;
        if (isJoinScreen && FrogSmashers.Net.OnlineMatch.InLobby)
        {
            isShowDown = false;
            if (joinCanvas != null)
            {
                foreach (var jc in joinCanvas)
                {
                    if (jc != null)
                        jc.gameObject.SetActive(false);
                }
            }
        }
        else if (isJoinScreen)
        {
            isShowDown = false;
            state = GameState.JoinScreen;
            finishDelay = 5f;
            SetupForJoinScreen();

            inactivePlayers = null;
            activePlayers.Clear();
            levelNo = 0;
            playersCanDropIn = true;

        }
        playerScoreDisplays = new List<PlayerScoreDisplay>();
        instance = this;
        if (!DeterminismHarness.Active && !FrogSmashers.Net.OnlineMatch.Active)
        {
            DeterministicRng.Match.Reseed(
                (ulong)System.Environment.TickCount);
        }
        if (inactivePlayers == null)
        {
            inactivePlayers = new List<Player>();
            Player p;
            p = new Player(FreeLives.InputReader.Device.Gamepad1, playerColors[0], 0);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad2, playerColors[1], 1);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad3, playerColors[2], 2);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Gamepad4, playerColors[3], 3);
            inactivePlayers.Add(p);
            p = new Player(FreeLives.InputReader.Device.Keyboard1, playerColors[4], 4);
            inactivePlayers.Add(p);

        }
        if (!isJoinScreen || FrogSmashers.Net.OnlineMatch.InLobby)
        {
            int i = 0;
            if (isTeamMode)
            {
                foreach (var player in activePlayers)
                {
                    player.score = 0;
                    player.spawnDelay = 0.5f + 0.2f * i;
                    i++;
                }


                var psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                psd.color = Color.red;
                psd.text.color = Color.red;
                redTeamScoreDisplay = psd;
                foreach (var p in activePlayers)
                    if (p.team == Team.Red)
                        psd.player = p;

                playerScoreDisplays.Add(psd);

                psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                psd.color = Color.blue;
                psd.text.color = Color.blue;
                blueTeamScoreDisplay = psd;
                foreach (var p in activePlayers)
                    if (p.team == Team.Blue)
                        psd.player = p;
                playerScoreDisplays.Add(psd);

            }
            else
            {
                foreach (var player in activePlayers)
                {
                    player.score = 0;
                    player.spawnDelay = 0.5f + 0.2f * i;
                    i++;
                    var psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
                    psd.player = player;
                    psd.color = player.color;
                    psd.text.color = player.color;
                    playerScoreDisplays.Add(psd);

                }
            }
        }

        if (isShowDown)
            foreach (var psd in playerScoreDisplays)
            {
                psd.gameObject.SetActive(false);
            }

        InputReader.GetInput(combinedInput);
    }

    internal static void SetupPlayersForShowdown()
    {
        List<Player> winningPlayers = GetLeadingPlayers();
        activePlayers.Clear();
        foreach (var p in winningPlayers)
            activePlayers.Add(p);
        playersCanDropIn = false;

    }

    public static JoinCanvas[] GetJoinCanvases()
    {
        return instance != null ? instance.joinCanvas : null;
    }

    /// <summary>
    /// Device kinds that could still join the local lobby: one entry
    /// per connected-pad brand whose slot is free, plus the keyboard
    /// while its slot is free. Drives the JOIN prompt glyphs.
    /// </summary>
    public static void GetAvailableJoinKinds(
        List<FrogSmashers.Settings.ControlDeviceKind> result)
    {
        result.Clear();
        if (inactivePlayers == null)
            return;
        var pads = UnityEngine.InputSystem.Gamepad.all;
        foreach (var player in inactivePlayers)
        {
            if (player.inputDevice == FreeLives.InputReader.Device.Keyboard1)
            {
                if (UnityEngine.InputSystem.Keyboard.current != null)
                    result.Add(FrogSmashers.Settings.ControlDeviceKind.Keyboard1);
                continue;
            }
            int idx = (int)player.inputDevice
                - (int)FreeLives.InputReader.Device.Gamepad1;
            if (idx < 0 || idx >= pads.Count)
                continue;
            var kind = FrogSmashers.Settings.ControlBindingService.KindOf(pads[idx]);
            if (!result.Contains(kind))
                result.Add(kind);
        }
    }

    /// <summary>Screenshot/test hook: joins the pooled player of a
    /// device slot onto a join canvas, as if they pressed JOIN.</summary>
    public static bool DebugAssignSlot(int canvasIndex, FreeLives.InputReader.Device device)
    {
        if (instance == null || instance.joinCanvas == null
            || canvasIndex < 0 || canvasIndex >= instance.joinCanvas.Length
            || instance.joinCanvas[canvasIndex].HasAssignedPlayer())
            return false;
        var player = inactivePlayers?.Find(p => p.inputDevice == device);
        if (player == null)
            return false;
        inactivePlayers.Remove(player);
        instance.joinCanvas[canvasIndex].AssignPlayer(player);
        return true;
    }

    /// <summary>Bottom-band mode label of the join screen; also
    /// driven by the online lobby (OnlineLobbyOverlay).</summary>
    public static void SetModeText(bool teamMode)
    {
        if (instance != null)
            instance.ApplyModeText(teamMode);
    }

    /// <summary>Instance path: Awake calls this before `instance`
    /// is set.</summary>
    void ApplyModeText(bool teamMode)
    {
        if (joinGameModeText != null)
            joinGameModeText.text = teamMode ? "TEAM" : "FREE  FOR  ALL";
    }

    public static void ToggleTeamMode()
    {
        if (!allowTeamMode || instance == null) return;
        isTeamMode = !isTeamMode;
        SetModeText(isTeamMode);
        if (instance.joinCanvas != null)
        {
            foreach (var jc in instance.joinCanvas)
            {
                if (jc != null && jc.HasAssignedPlayer())
                    jc.RefreshForMode();
            }
        }
    }

    void Start()
    {
        float aspect = ((float)Screen.width / Screen.height);
        float screenWidth = 18f * aspect;
        float adust = 32f / screenWidth;
        Camera.main.orthographicSize = adust * 18f;
    }

    FreeLives.InputState input = new InputState();
    FreeLives.InputState combinedInput = new InputState();

    /// <summary>Match flow ticks before characters and flies.</summary>
    public int SimOrder
    {
        get { return 0; }
    }

    void OnEnable()
    {
        SimulationDriver.Register(this);
    }

    void OnDisable()
    {
        SimulationDriver.Unregister(this);
    }

    /// <summary>Advances match-flow state by one fixed simulation step.</summary>
    public void SimTick(float dt)
    {
        if (state == GameState.Playing)
            TickPlaying(dt);
        else if (state == GameState.RoundFinished)
            TickRoundFinished(dt);
    }

    void TickPlaying(float dt)
    {
        if (activeFly == null && !isShowDown)
        {
            if (flySpawnDelay > 0f)
            {
                flySpawnDelay -= dt;
                if (flySpawnDelay <= 0f)
                    activeFly = SpawnFly(Terrain.GetFlySpawnPoint());
            }
            else
            {
                flySpawnDelay = DeterministicRng.Match.Range(15f, 45f);
            }
        }

        for (int i = 0; i < activePlayers.Count; i++)
        {
            if (activePlayers[i].character == null)
            {
                activePlayers[i].spawnDelay -= dt;
                if (activePlayers[i].spawnDelay < 0f)
                {
                    SpawnCharacter(activePlayers[i]);
                }
            }
        }
    }

    Fly pooledFly;

    /// <summary>Current fly instance (may be inactive while eaten).</summary>
    public static Fly ActiveFlyInstance
    {
        get { return instance != null ? instance.activeFly : null; }
    }

    /// <summary>
    /// Retires a dead fly into the pool at the killing tick, so the
    /// respawn timer starts at a tick-deterministic point instead of
    /// waiting for Unity's deferred Destroy, and rollback can revive it.
    /// </summary>
    public static void PoolFly(Fly fly)
    {
        if (instance == null || fly == null)
            return;
        if (instance.activeFly == fly)
            instance.activeFly = null;
        fly.gameObject.SetActive(false);
        if (instance.pooledFly == null)
            instance.pooledFly = fly;
        else if (instance.pooledFly != fly)
            Destroy(fly.gameObject);
    }

    Fly SpawnFly(Vector3 point)
    {
        Fly fly;
        if (pooledFly != null)
        {
            fly = pooledFly;
            pooledFly = null;
            fly.transform.position = point;
            fly.gameObject.SetActive(true);
        }
        else
        {
            fly = Instantiate(flyPrefab, point, Quaternion.identity);
        }
        fly.SpawnInit();
        return fly;
    }

    /// <summary>Captures the complete sim state into a snapshot.</summary>
    public static void SaveTo(MatchSnapshot snap)
    {
        snap.Tick = SimClock.CurrentTick;
        snap.RngState = DeterministicRng.Match.State;
        snap.GameState = (int)instance.state;
        snap.FlySpawnDelay = instance.flySpawnDelay;
        snap.FinishDelay = instance.finishDelay;
        snap.WinningPlayerIndex = instance.winningPlayer != null
            ? activePlayers.IndexOf(instance.winningPlayer) : -1;
        snap.RedTeamScore = instance.redTeamScore;
        snap.BlueTeamScore = instance.blueTeamScore;
        snap.HasFly = instance.activeFly != null;
        if (snap.HasFly)
            instance.activeFly.SaveTo(ref snap.Fly);
        snap.PlayerCount = Mathf.Min(
            activePlayers.Count, MatchSnapshot.MaxPlayers);
        for (int i = 0; i < snap.PlayerCount; i++)
        {
            var p = activePlayers[i];
            snap.Players[i].Score = p.score;
            snap.Players[i].RoundWins = p.roundWins;
            snap.Players[i].SpawnDelay = p.spawnDelay;
            snap.Players[i].HasCharacter = p.character != null;
            if (p.character != null)
                p.character.SaveTo(ref snap.Players[i].Character);
        }
    }

    /// <summary>
    /// Players stripped by a restore whose snapshot predates their
    /// join; the online membership applier re-adds them on resim.
    /// </summary>
    public static readonly List<Player> StrippedPlayers =
        new List<Player>();

    /// <summary>
    /// Restores the complete sim state from a snapshot. Two passes:
    /// first ensure fly and character instances exist (reviving pooled
    /// ones), then restore fields so cross-references resolve. Players
    /// added after the snapshot's tick are stripped (dynamic join).
    /// </summary>
    public static bool RestoreFrom(MatchSnapshot snap)
    {
        if (instance == null || !snap.Valid
            || snap.PlayerCount > activePlayers.Count)
        {
            Debug.LogError("[GameController] Snapshot restore refused");
            return false;
        }

        while (activePlayers.Count > snap.PlayerCount)
        {
            var stripped = activePlayers[activePlayers.Count - 1];
            if (stripped.character != null)
            {
                stripped.character.gameObject.SetActive(false);
                stripped.pooledCharacter = stripped.character;
                stripped.character = null;
            }
            activePlayers.RemoveAt(activePlayers.Count - 1);
            StrippedPlayers.Add(stripped);
        }

        if (snap.HasFly && instance.activeFly == null)
        {
            instance.activeFly = instance.pooledFly != null
                ? instance.pooledFly
                : Instantiate(instance.flyPrefab);
            instance.pooledFly = null;
            instance.activeFly.gameObject.SetActive(true);
        }
        else if (!snap.HasFly && instance.activeFly != null)
        {
            PoolFly(instance.activeFly);
        }

        for (int i = 0; i < snap.PlayerCount; i++)
        {
            var p = activePlayers[i];
            if (snap.Players[i].HasCharacter && p.character == null)
            {
                var ch = p.pooledCharacter != null
                    ? p.pooledCharacter
                    : Instantiate(instance.characterPrefab);
                p.pooledCharacter = null;
                ch.player = p;
                ch.gameObject.SetActive(true);
                p.character = ch;
            }
            else if (!snap.Players[i].HasCharacter && p.character != null)
            {
                p.pooledCharacter = p.character;
                p.character.gameObject.SetActive(false);
                p.character = null;
            }
        }

        instance.state = (GameState)snap.GameState;
        instance.flySpawnDelay = snap.FlySpawnDelay;
        instance.finishDelay = snap.FinishDelay;
        instance.winningPlayer = snap.WinningPlayerIndex >= 0
            && snap.WinningPlayerIndex < activePlayers.Count
            ? activePlayers[snap.WinningPlayerIndex] : null;
        instance.redTeamScore = snap.RedTeamScore;
        instance.blueTeamScore = snap.BlueTeamScore;
        if (snap.HasFly)
            instance.activeFly.RestoreFrom(in snap.Fly);
        for (int i = 0; i < snap.PlayerCount; i++)
        {
            var p = activePlayers[i];
            p.score = snap.Players[i].Score;
            p.roundWins = snap.Players[i].RoundWins;
            p.spawnDelay = snap.Players[i].SpawnDelay;
            if (p.character != null)
                p.character.RestoreFrom(in snap.Players[i].Character);
        }
        DeterministicRng.Match.State = snap.RngState;
        SimClock.SetTick(snap.Tick);
        Physics2D.SyncTransforms();
        return true;
    }

    /// <summary>Mixes the whole match sim state into a hash.</summary>
    public static uint HashSimState(uint h)
    {
        if (instance == null)
            return h;
        h = StateHash.Mix(h, (int)instance.state);
        h = StateHash.Mix(h, instance.flySpawnDelay);
        h = StateHash.Mix(h, instance.finishDelay);
        h = StateHash.Mix(h, instance.winningPlayer != null
            ? activePlayers.IndexOf(instance.winningPlayer) : -1);
        h = StateHash.Mix(h, instance.redTeamScore);
        h = StateHash.Mix(h, instance.blueTeamScore);
        h = StateHash.Mix(h, instance.activeFly != null);
        if (instance.activeFly != null)
            h = instance.activeFly.HashSimState(h);
        for (int i = 0; i < activePlayers.Count; i++)
        {
            var p = activePlayers[i];
            h = StateHash.Mix(h, p.score);
            h = StateHash.Mix(h, p.roundWins);
            h = StateHash.Mix(h, p.spawnDelay);
            h = StateHash.Mix(h, p.character != null);
            if (p.character != null)
                h = p.character.HashSimState(h);
        }
        return h;
    }

    /// <summary>
    /// Plays the round-win sting and labels once the RoundFinished
    /// state is confirmed (online) or immediately (offline). Gating on
    /// a confirmed tick stops a mispredicted KO from flashing a false
    /// victory that the rollback later reverts.
    /// </summary>
    void PresentRoundWinIfReady()
    {
        if (state != GameState.RoundFinished)
        {
            roundWinPresented = false;
            return;
        }
        if (roundWinPresented)
            return;
        if (FrogSmashers.Net.OnlineMatch.Active
            && FrogSmashers.Net.Rollback.RollbackManager.Active != null)
        {
            if (SimulationDriver.IsResimulating)
                return;
            if (FrogSmashers.Net.OnlineMatch.SafeTick() < roundFinishedTick)
                return;
        }
        roundWinPresented = true;
        SoundController.PlaySoundEffect("VictorySting", 0.5f);
        SoundController.StopMusic();
        if (winningPlayer != null)
        {
            var display = GetPlayerScoreDisplay(winningPlayer);
            if (display != null)
                display.TemorarilyDisplay("WINNER ! ! !", 5f);
            if (winningPlayer.character != null)
                winningPlayer.character.GetComponent<ScorePlum>()
                    .ShowText("WIN!", 5f);
        }
    }

    void TickRoundFinished(float dt)
    {
        for (int i = 0; i < activePlayers.Count; i++)
        {
            if (activePlayers[i].character == null && winningPlayer == activePlayers[i])
            {
                activePlayers[i].spawnDelay -= dt;
                if (activePlayers[i].spawnDelay < 0f)
                {
                    SpawnCharacter(activePlayers[i]);
                }
            }
        }

        finishDelay -= dt;
        if (finishDelay < 0f)
            FinishRound();
    }

    void Update()
    {
        bool backToMenu =
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        if (backToMenu && !FrogSmashers.Net.OnlineMatch.Active)
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            return;
        }

        PresentRoundWinIfReady();

        if (state == GameState.JoinScreen)
        {

            for (int i = inactivePlayers.Count - 1; i >= 0; i--)
            {
                InputReader.GetInput(inactivePlayers[i].inputDevice, input);

                if (input.xButton)
                {
                    for (int j = 0; j < joinCanvas.Length; j++)
                    {
                        if (!joinCanvas[j].HasAssignedPlayer())
                        {
                            joinCanvas[j].AssignPlayer(inactivePlayers[i]);
                            inactivePlayers.RemoveAt(i);
                            j = joinCanvas.Length;
                        }
                    }
                }
            }


            bool playersAreReady = CheckReadyPlayers();

            if (playersAreReady)
            {
                finishDelay -= Time.deltaTime;
                joinCountdownText.text = ((int)(finishDelay) + 1).ToString();
                if (finishDelay <= 0f)
                    FinishRound();
            }
            else
            {
                joinCountdownText.text = "";
                finishDelay = 5f;
            }


        }
        else if (state == GameState.Playing)
        {
            for (int i = 0; i < activePlayers.Count; i++)
            {
                if (activePlayers[i].character != null && activePlayers[i].character.transform.position.y > Terrain.ScreenTop)
                {


                    var spr = activePlayers[i].offscreenDot;
                    if (spr == null)
                    {
                        activePlayers[i].offscreenDot = spr = Instantiate(offscreenDotPrefab);
                        spr.color = activePlayers[i].color;
                    }
                    spr.enabled = true;
                    spr.transform.position = new Vector3(activePlayers[i].character.transform.position.x, Terrain.ScreenTop, -6f);


                }
                else
                {
                    var spr = activePlayers[i].offscreenDot;
                    if (spr != null)
                    {
                        spr.enabled = false;
                    }
                }




            }

            ArrangeScoreboards();

            if (!isShowDown && playersCanDropIn)
            {
                for (int i = inactivePlayers.Count - 1; i >= 0; i--)
                {
                    InputReader.GetInput(inactivePlayers[i].inputDevice, input);

                    if (input.xButton)
                    {
                        print(inactivePlayers[i].color + ", " + inactivePlayers[i].inputDevice);
                        inactivePlayers[i].color = playerColors[Random.Range(0, playerColors.Length)];
                        AddPlayer(inactivePlayers[i]);


                        inactivePlayers.RemoveAt(i);
                    }
                }

                if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
                {
                    SpawnCharacter(null);
                }
            }

        }
        else if (state == GameState.RoundFinished)
        {
            ArrangeScoreboards();
        }

        if (Keyboard.current != null && Keyboard.current.f6Key.wasPressedThisFrame)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
        {
            showGui = !showGui;
        }
    }

    void SetupForJoinScreen()
    {
        availableColors = new List<Color>();
        availableColors.AddRange(playerColors);

        ApplyModeText(isTeamMode);
    }

    bool CheckReadyPlayers()
    {
        int readyPlayers = 0;
        if (GameController.isTeamMode)
        {
            bool redTeamHasPlayer = false, blueTeamHasPlayer = false;
            for (int i = 0; i < joinCanvas.Length; i++)
            {
                if (joinCanvas[i].HasAssignedPlayer())
                {
                    if (joinCanvas[i].state == JoinCanvas.State.Ready)
                    {
                        readyPlayers++;
                        if (joinCanvas[i].assignedPlayer.team == Team.Blue)
                            blueTeamHasPlayer = true;
                        else if (joinCanvas[i].assignedPlayer.team == Team.Red)
                            redTeamHasPlayer = true;
                    }
                    else
                    {
                        readyPlayers = -100;
                    }
                }
            }

            return redTeamHasPlayer && blueTeamHasPlayer && readyPlayers >= 2;
        }
        else
        {
            for (int i = 0; i < joinCanvas.Length; i++)
            {
                if (joinCanvas[i].HasAssignedPlayer())
                {
                    if (joinCanvas[i].state == JoinCanvas.State.Ready)
                        readyPlayers++;
                    else
                    {
                        readyPlayers = -100;
                    }
                }
            }
            return readyPlayers >= 2;
        }
    }

    void ArrangeScoreboards()
    {
        for (int i = 0; i < playerScoreDisplays.Count; i++)
        {
            var p = playerScoreDisplays[i].transform.localPosition;
            //print(p);
            p.y = Mathf.Lerp(p.y, i * -2f, Time.deltaTime * 3f);
            p.z = 0f;
            //print(p);
            playerScoreDisplays[i].transform.localPosition = p;


        }
    }

    public static int levelNo;
    void FinishRound()
    {
        if (FrogSmashers.Net.OnlineMatch.Active)
        {
            FrogSmashers.Net.OnlineMatch.OnRoundFinished();
            return;
        }
        if (state == GameState.JoinScreen)
        {
            levelNo = 0;
            foreach (var jc in joinCanvas)
            {
                if (jc.HasAssignedPlayer())
                    activePlayers.Add(jc.assignedPlayer);

            }
            SceneManager.LoadScene(levelNames[0]);
        }
        else
        {
            levelNo++;
            //if (levelNo >= 5)
            {
                SceneManager.LoadScene("ScoreScreen");
            }
        }
    }

    void AddPlayer(Player player)
    {
        activePlayers.Add(player);

        var psd = Instantiate(scoreDisplayPrefab, scoreCanvas.transform) as PlayerScoreDisplay;
        psd.player = player;
        psd.color = player.color;
        psd.text.color = player.color;
        playerScoreDisplays.Add(psd);

    }


    void SpawnCharacter(Player player)
    {
        var point = player != null
            && FrogSmashers.Net.OnlineMatch.InLobby
            ? Terrain.GetSpawnPoint(player.sortPriority)
            : Terrain.GetSpawnPoint();
        Character ch;
        if (player != null && player.pooledCharacter != null)
        {
            ch = player.pooledCharacter;
            player.pooledCharacter = null;
            ch.ResetForSpawn(point);
            ch.gameObject.SetActive(true);
        }
        else
        {
            ch = Instantiate(characterPrefab, point, Quaternion.identity) as Character;
        }
        if (player != null)
        {
            ch.player = player;
            player.character = ch;
            player.spawnDelay = 1f;

            SimFx.Spawn(player.sortPriority, () => EffectsController.CreateSpawnEffects(point + Vector3.up, player.color));
            SimFx.Play(player.sortPriority, "CharacterSpawn", 0.4f, point);
        }
    }

    public static void SpawnCharacterJoinScreen(Player player)
    {
        for (int i = 0; i < instance.joinCanvas.Length; i++)
        {
            if (instance.joinCanvas[i].assignedPlayer == player)
            {
                var ch = Instantiate(instance.characterPrefab, Terrain.GetSpawnPoint(i), Quaternion.identity) as Character;
                ch.player = player;
                player.character = ch;
            }
        }
    }

    Player winningPlayer;
    uint roundFinishedTick;
    bool roundWinPresented;

    public static Player lastWinningPlayer { get; private set; }
    public static bool HasInstance { get { return instance != null; } }

    public static Player GetWinningPlayer()
    {
        if (State == GameState.RoundFinished)
        {
            return instance.winningPlayer;
        }
        return null;
    }

    void SortScoreboard()
    {
        if (GameController.isTeamMode)
        {
            redTeamScoreDisplay.player.score = redTeamScore;
            blueTeamScoreDisplay.player.score = blueTeamScore;
        }
        instance.playerScoreDisplays.Sort((x, y) => (y.player.score * 100 + y.player.sortPriority) - (x.player.score * 100 + x.player.sortPriority));
    }


    internal static void RegisterKill(Player gotPoint, Player gotKilled, int hits)
    {
        if (FrogSmashers.Net.OnlineMatch.InLobby)
            return;

        if (State == GameState.RoundFinished)
            return;

        if (isShowDown)
        {
            bool wonRound = false;
            if (activePlayers.Contains(gotKilled))
            {
                activePlayers.Remove(gotKilled);
                if (gotKilled.offscreenDot != null)
                    GameObject.Destroy(gotKilled.offscreenDot);
            }
            if (activePlayers.Count == 1)
            {
                wonRound = true;
                var winner = activePlayers[0];
                instance.winningPlayer = winner;
                lastWinningPlayer = winner;
            }
            else if (isTeamMode)
            {
                bool winnersContainRed = false;
                bool winnersContainBlue = false;
                foreach (var player in activePlayers)
                {
                    if (player.team == Team.Red)
                        winnersContainRed = true;
                    else winnersContainBlue = true;
                }

                if (!winnersContainBlue || !winnersContainRed)
                {
                    wonRound = true;
                    var winner = activePlayers[0];
                    instance.winningPlayer = winner;
                    lastWinningPlayer = winner;
                }

            }

            if (wonRound)
            {
                instance.state = GameState.RoundFinished;
                instance.roundFinishedTick = SimClock.CurrentTick;
                instance.roundWinPresented = false;
            }
            return;
        }

        if (gotPoint != null)
        {
            if (hits <= 0)
                hits = 1;

            if (GameController.isTeamMode)
            {
                if (gotPoint.team == Team.Blue)
                    instance.blueTeamScore += hits;
                else
                    instance.redTeamScore += hits;
            }
            else
            {
                gotPoint.score += hits;
            }

            bool wonRound = false;
            if (GameController.isTeamMode)
            {
                wonRound = ((gotPoint.team == Team.Red && instance.redTeamScore >= 10) || (gotPoint.team == Team.Blue && instance.blueTeamScore >= 10));
            }
            else
            {
                if (activePlayers.Count == 2)
                    wonRound = gotPoint.score >= 5;
                else
                    wonRound = gotPoint.score >= 10;
            }
            if (wonRound)
            {
                instance.state = GameState.RoundFinished;
                instance.winningPlayer = gotPoint;
                lastWinningPlayer = gotPoint;
                instance.roundFinishedTick = SimClock.CurrentTick;
                instance.roundWinPresented = false;
            }
            else if (!SimulationDriver.IsResimulating)
            {
                var display = instance.GetPlayerScoreDisplay(gotPoint);
                if (display != null)
                    display.TemorarilyDisplay("+" + hits.ToString());
                if (gotPoint.character != null)
                    gotPoint.character.GetComponent<ScorePlum>()
                        .ShowText("+" + hits.ToString());
            }




        }
        else if (gotKilled != null)
        {

            //if (isTeamMode)
            //{
            //    if (gotKilled.team == Team.Blue)
            //        instance.blueTeamScore--;
            //    else
            //        instance.redTeamScore--;
            //}
            //else
            //{
            //    gotKilled.score--;
            //}
            //instance.GetPlayerScoreDisplay(gotKilled).TemorarilyDisplay("-" + 1);
        }

        instance.SortScoreboard();


    }

    PlayerScoreDisplay GetPlayerScoreDisplay(Player player)
    {
        if (isTeamMode)
        {
            if (player.team == Team.Red)
                return redTeamScoreDisplay;
            else
                return blueTeamScoreDisplay;
        }
        else
        {
            for (int i = 0; i < playerScoreDisplays.Count; i++)
            {
                if (playerScoreDisplays[i].player == player)
                    return playerScoreDisplays[i];
            }
        }
        return null;
    }

    bool showGui;

    public void OnGUI()
    {
        if (showGui)
        {
            GUILayout.BeginArea(new Rect(0, 0, 400, 400));
            charactersBounceEachOther = GUILayout.Toggle(charactersBounceEachOther, "Characters Bounce Each Other");
            weirdBounceTrajectories = GUILayout.Toggle(weirdBounceTrajectories, "Weird Bounce Trajectories");
            onlyBounceBeforeRecover = GUILayout.Toggle(onlyBounceBeforeRecover, "Only Bounce Before Recover");
            GUILayout.EndArea();
        }
    }

    public static Color GetAvailableColor()
    {
        int i = UnityEngine.Random.Range(0, instance.availableColors.Count);
        var col = instance.availableColors[i];
        instance.availableColors.RemoveAt(i);
        return col;
    }

    public static void ReturnColor(Color color)
    {
        instance.availableColors.Add(color);
    }

    public static void ReturnPlayer(Player player)
    {
        activePlayers.Remove(player);
        inactivePlayers.Add(player);
    }


    public static List<Player> GetLeadingPlayers()
    {
        int topScore = -1;
        List<Player> tiedPlayers = new List<Player>();
        foreach (var player in activePlayers)
        {
            if (player.roundWins == topScore)
            {
                tiedPlayers.Add(player);
            }
            else if (player.roundWins > topScore)
            {
                topScore = player.roundWins;
                tiedPlayers.Clear();
                tiedPlayers.Add(player);
            }
        }

        return tiedPlayers;
    }

    public static bool AreAnyPlayersTiedForVictory()
    {
        int topScore = -1;
        List<Player> tiedPlayers = new List<Player>();
        foreach (var player in activePlayers)
        {
            if (player.roundWins == topScore)
            {
                tiedPlayers.Add(player);
            }
            else if (player.roundWins > topScore)
            {
                topScore = player.roundWins;
                tiedPlayers.Clear();
                tiedPlayers.Add(player);
            }
        }
        if (GameController.isTeamMode)
        {
            bool redIsTied = false, blueIsTied = false;
            foreach (var p in tiedPlayers)
            {
                if (p.team == Team.Red)
                    redIsTied = true;
                if (p.team == Team.Blue)
                    blueIsTied = true;
            }
            return redIsTied && blueIsTied;
        }
        else
            return tiedPlayers.Count > 1;

    }

}
