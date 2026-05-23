using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FrogSmashers.UI
{
    public class MainMenuController : MonoBehaviour
    {
        public Button localGameButton;
        public Button createLobbyButton;
        public Button openLobbiesButton;
        public Button rankedQueueButton;
        public Button settingsButton;

        public GameObject comingSoonPanel;
        public Text comingSoonText;
        public Button comingSoonBackButton;

        GameObject lastSelectedBeforePanel;

        Button[] MenuButtons => new[]
        {
            localGameButton, createLobbyButton, openLobbiesButton,
            rankedQueueButton, settingsButton
        };

        void Start()
        {
            if (comingSoonPanel != null)
                comingSoonPanel.SetActive(false);

            if (EventSystem.current != null && localGameButton != null)
                EventSystem.current.SetSelectedGameObject(localGameButton.gameObject);
        }

        public void OnLocalGame()    { SceneManager.LoadScene("JoinScreen"); }
        public void OnCreateLobby()  { ShowComingSoon("Create Lobby"); }
        public void OnOpenLobbies()  { ShowComingSoon("Open Lobbies"); }
        public void OnRankedQueue()  { ShowComingSoon("Ranked Queue"); }
        public void OnSettings()     { ShowComingSoon("Settings"); }

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
