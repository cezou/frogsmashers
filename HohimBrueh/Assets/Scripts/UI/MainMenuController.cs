using System.Collections;
using System.Collections.Generic;
using FrogSmashers.Net;
using FrogSmashers.Net.Transport;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    public class MainMenuController : MonoBehaviour
    {
        public Button localGameButton;
        public Button createLobbyButton;
        public Button rankedQueueButton;
        public Button settingsButton;

        public GameObject comingSoonPanel;
        public Text comingSoonText;
        public Button comingSoonBackButton;

        public SettingsPanelController settingsPanel;

        public Text lobbyListStatus;
        public Button[] lobbyEntryButtons;
        public Text statusText;

        readonly List<LobbyEntry> entries = new List<LobbyEntry>();
        GameObject lastSelectedBeforePanel;
        bool busy;

        Button[] MenuButtons => new[]
        {
            localGameButton, createLobbyButton,
            rankedQueueButton, settingsButton
        };

        void Start()
        {
            if (comingSoonPanel != null)
                comingSoonPanel.SetActive(false);
            SetStatus("");

            if (lobbyEntryButtons != null)
            {
                for (int i = 0; i < lobbyEntryButtons.Length; i++)
                {
                    int index = i;
                    lobbyEntryButtons[i].onClick.AddListener(
                        () => OnJoinEntry(index));
                    lobbyEntryButtons[i].gameObject.SetActive(false);
                }
            }

            if (EventSystem.current != null && localGameButton != null)
                EventSystem.current.SetSelectedGameObject(localGameButton.gameObject);

            InvokeRepeating(nameof(RefreshLobbies), 0.5f, 4f);

            string cmd = System.Environment.CommandLine;
            if (cmd.Contains("-shotSettings")
                || cmd.Contains("-shotControls"))
            {
                OnSettings();
                if (cmd.Contains("-shotControls")
                    && settingsPanel != null)
                    settingsPanel.ShowControls();
            }
        }

        void Update()
        {
            bool back =
                (Keyboard.current != null
                    && Keyboard.current.escapeKey.wasPressedThisFrame)
                || (Gamepad.current != null
                    && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (back && comingSoonPanel != null
                && comingSoonPanel.activeSelf)
            {
                HideComingSoon();
            }
        }

        public void OnLocalGame()    { SceneManager.LoadScene("JoinScreen"); }
        public void OnRankedQueue()  { ShowComingSoon("Ranked Queue"); }

        public void OnSettings()
        {
            if (settingsPanel == null)
            {
                ShowComingSoon("Settings");
                return;
            }
            foreach (var b in MenuButtons)
                if (b != null) b.interactable = false;
            settingsPanel.Open(settingsButton.gameObject, () =>
            {
                foreach (var b in MenuButtons)
                    if (b != null) b.interactable = true;
            });
        }

        public async void OnCreateLobby()
        {
            if (busy)
                return;
            busy = true;
            SetStatus("CREATING  LOBBY . . .");
            try
            {
                await NetSession.CreateAsync(4, discoverable: true);
                OnlineMatch.HostStartLobby();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MainMenu] Create lobby failed: {e}");
                SetStatus("ERROR :  COULD  NOT  CREATE  LOBBY");
                busy = false;
            }
        }

        async void OnJoinEntry(int index)
        {
            if (busy || index >= entries.Count)
                return;
            busy = true;
            var entry = entries[index];
            SetStatus($"JOINING  {entry.Name} . . .");
            try
            {
                await NetSession.JoinByCodeAsync(entry.RelayCode);
                OnlineMatch.JoinAsClient();
                SetStatus("WAITING  FOR  THE  HOST . . .");
                StartCoroutine(JoinWatchdog());
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MainMenu] Join failed: {e}");
                SetStatus("ERROR :  COULD  NOT  JOIN");
                busy = false;
            }
        }

        /// <summary>
        /// Bails out of a join that never connects (the lobby was a dead
        /// entry whose host is gone): without this the client waits on
        /// the host forever. Tears down through the normal leave path so
        /// the menu becomes responsive again instead of hanging.
        /// </summary>
        IEnumerator JoinWatchdog()
        {
            float timeLeft = 10f;
            var manager = NetworkManager.Singleton;
            while (timeLeft > 0f && manager != null
                && !manager.IsConnectedClient)
            {
                timeLeft -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (manager == null || manager.IsConnectedClient)
                yield break;
            Debug.LogWarning(
                "[MainMenu] Join timed out, host unreachable");
            SetStatus("HOST  UNREACHABLE");
            OnlineMatch.LeaveLocal();
        }

        async void RefreshLobbies()
        {
            if (busy)
                return;
            List<LobbyEntry> found;
            try
            {
                found = await LobbyDiscovery.QueryAsync();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MainMenu] Lobby query failed: {e}");
                return;
            }
            if (this == null || busy)
                return;
            entries.Clear();
            entries.AddRange(found);
            for (int i = 0; i < lobbyEntryButtons.Length; i++)
            {
                bool used = i < entries.Count;
                lobbyEntryButtons[i].gameObject.SetActive(used);
                if (!used)
                    continue;
                var label = lobbyEntryButtons[i]
                    .GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = $"{entries[i].Name}  "
                        + $"{entries[i].Players}/{entries[i].MaxPlayers}";
                }
            }
            if (lobbyListStatus != null)
            {
                lobbyListStatus.text = entries.Count == 0
                    ? "NO  LOBBIES  YET" : "";
            }
        }

        void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        public void ShowComingSoon(string feature)
        {
            if (comingSoonPanel == null) return;

            if (EventSystem.current != null)
                lastSelectedBeforePanel = EventSystem.current.currentSelectedGameObject;

            foreach (var b in MenuButtons)
                if (b != null) b.interactable = false;

            if (comingSoonText != null)
                comingSoonText.text = feature.ToUpper() + " — COMING SOON";

            comingSoonPanel.SetActive(true);

            if (EventSystem.current != null && comingSoonBackButton != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
                EventSystem.current.SetSelectedGameObject(comingSoonBackButton.gameObject);
            }
        }

        public void HideComingSoon()
        {
            if (comingSoonPanel == null) return;
            comingSoonPanel.SetActive(false);

            foreach (var b in MenuButtons)
                if (b != null) b.interactable = true;

            if (EventSystem.current != null && lastSelectedBeforePanel != null)
                EventSystem.current.SetSelectedGameObject(lastSelectedBeforePanel);
        }
    }
}
