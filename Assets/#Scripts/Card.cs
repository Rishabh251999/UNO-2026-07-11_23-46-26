using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    [RequireComponent(typeof(Image), typeof(Button))]
    [RequireComponent(typeof(LayoutElement))]
    public class Card : MonoBehaviour
    {
        private readonly WaitForSeconds _waitForSeconds0_1 = new(0.1f);

        public UnoCard CardData;

        private Image _cardImage;
        private Button _button;

        private Vector3 _dealTargetLocalPos;
        private RectTransform _rectTransform;
        private LayoutElement _layoutElement;

        public static readonly Dictionary<byte, Sprite> CardSprites = new();

        private void Awake()
        {
            _cardImage = GetComponent<Image>();
            _button = GetComponent<Button>();

            _rectTransform = (RectTransform)transform;

            _layoutElement = GetComponent<LayoutElement>();
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

        public static void PopulateCardSprites()
        {
            CardSprites.Clear();

            // Must match your asset names exactly
            string[] colors = { "Red", "Green", "Blue", "Yellow" };

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
                for (int i = 0; i < 2; i++)
                {
                    CardSprites[id++] = Load($"{color}_Skip");
                    CardSprites[id++] = Load($"{color}_Reverse");
                    CardSprites[id++] = Load($"{color}_Draw");
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


        public void PlayTowards(Transform target, Transform canvas, Action onComplete)
        {
            _button.interactable = false;

            if (target == null)
                return;

            transform.SetParent(canvas, true);
            _rectTransform.sizeDelta = new(150, 217); // no GetComponent call needed anymore

            StartCoroutine(MoveToTarget(target, onComplete));
        }

        private IEnumerator MoveToTarget(Transform target, Action onComplete)
        {
            Vector3 startPos = transform.position;
            float duration = UnoGameController.Instance.PlayMoveDuration; // cache once, avoid repeated singleton+property lookups every frame

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, target.position, t);
                yield return null;
            }

            transform.position = target.position;
            yield return _waitForSeconds0_1;
            onComplete?.Invoke();
        }

        public void CaptureDealTarget()
        {
            _dealTargetLocalPos = _rectTransform.localPosition; // cached field instead of cast
        }

        public void PrepareDeal(Transform origin)
        {
            _button.interactable = false;
            _layoutElement.ignoreLayout = true;

            if (origin is { })
                transform.position = origin.position;
        }

        public void AnimateDeal()
        {
            StartCoroutine(MoveFromOrigin(_dealTargetLocalPos, transform.parent));
        }

        private IEnumerator MoveFromOrigin(Vector3 targetLocalPos, Transform parent)
        {
            Vector3 startPos = transform.position;
            Vector3 targetWorldPos = parent.TransformPoint(targetLocalPos);
            float duration = UnoGameController.Instance.PlayMoveDuration; // cache once

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                transform.position = Vector3.Lerp(startPos, targetWorldPos, t);
                yield return null;
            }

            transform.localPosition = targetLocalPos;
            _layoutElement.ignoreLayout = false;
            _button.interactable = true;
        }
    }
}