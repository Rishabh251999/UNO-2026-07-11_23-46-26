using Mirror;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    [RequireComponent(typeof(Image), typeof(Button))]
    public class Card : MonoBehaviour
    {
        private Image _cardImage;
        private Button _button;

        public UnoCard CardData;

        public static readonly Dictionary<byte, Sprite> CardSprites = new();

        private void Awake()
        {
            _cardImage = GetComponent<Image>();
            _button = GetComponent<Button>();
        }

        private void Start()
        {
            _button.onClick.AddListener(OnCardClicked);
        }

        private void OnDestroy()
        {
            _button.onClick.RemoveListener(OnCardClicked);
        }

        private void OnCardClicked()
        {
            Debug.Log($"[CardView] Card clicked: {CardData}");

            if (NetworkClient.localPlayer is not { } localPlayer ||
                !localPlayer.TryGetComponent<UnoPlayerController>(out var playerController))
                return;

            playerController.TryPlayCard(this);
        }

        public void SetInteractable(bool canPlay)
        {
            _button.interactable = canPlay;
        }

        /// <summary>
        /// Loads all card sprites from Resources/Cards/ matching your asset names.
        /// Call once from GameManager.OnStartClient()
        /// </summary>
        public static void PopulateCardSprites()
        {
            CardSprites.Clear();

            // Must match your asset names exactly
            string[] colors = { "Red", "Green", "Blue", "Yellow" };
            string[] actions = { "Skip", "Reverse", "Draw" };

            byte id = 0;

            foreach (var color in colors)
            {
                // One zero
                CardSprites[id++] = Load($"{color}_0");

                // Two each of 1-9
                for (int n = 1; n <= 9; n++)
                {
                    var s = Load($"{color}_{n}");
                    CardSprites[id++] = s;
                    CardSprites[id++] = s;
                }

                // Two each of Draw, Reverse, Skip
                foreach (var action in actions)
                {
                    var s = Load($"{color}_{action}");
                    CardSprites[id++] = s;
                    CardSprites[id++] = s;
                }
            }

            // Four Wild, four Wild Draw Four
            var wild = Load("Wild");
            var wildFour = Load("WildDrawFour");
            for (int i = 0; i < 4; i++) CardSprites[id++] = wild;
            for (int i = 0; i < 4; i++) CardSprites[id++] = wildFour;

            Debug.Log($"[CardView] Loaded {CardSprites.Count} sprites.");
        }

        private static Sprite Load(string name)
        {
            var s = Resources.Load<Sprite>($"Cards/{name}");
            if (s == null) Debug.LogWarning($"[CardView] Sprite not found: Cards/{name}");
            return s;
        }

        public void Setup(UnoCard card)
        {
            CardData = card;

            if (CardSprites.TryGetValue(card.Id, out Sprite sprite))
                _cardImage.sprite = sprite;
            else
                Debug.LogWarning($"[CardView] No sprite for card Id {card.Id} ({card})");
        }
    }
}
