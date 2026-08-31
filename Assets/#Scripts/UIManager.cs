using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UNO
{
    public enum ScreenType
    {
        Lobby,
        Room,
        Game,
    }

    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        public event Action<ScreenType> OnScreenShown;

        [Serializable]
        private struct PanelEntry
        {
            public ScreenType ScreenType;
            public CanvasGroup CanvasGroup;
        }

        public ScreenType ScreenType { get; private set; }

        [SerializeField] private List<PanelEntry> _panels;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public void SetState(ScreenType state)
        {
            ScreenType = state;

            foreach (var entry in _panels)
                SetVisible(entry.CanvasGroup, entry.ScreenType == state);

            OnScreenShown?.Invoke(state);
        }

        private void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
                return;

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }
    }
}