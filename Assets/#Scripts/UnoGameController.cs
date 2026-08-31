using Mirror;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    [RequireComponent(typeof(NetworkMatch))]
    public class UnoGameController : NetworkBehaviour
    {
        private readonly Color32 _redColor = new(234, 50, 60, 255);
        private readonly Color32 _blueColor = new(0, 152, 220, 255);
        private readonly Color32 _yellowColor = new(255, 200, 37, 255);
        private readonly Color32 _greenColor = new(51, 152, 75, 255);
        private readonly WaitForSeconds _waitForSeconds0_12 = new(0.12f);

        public static UnoGameController Instance { get; private set; }

        [Header("Script References")]
        [SerializeField] private Card _cardPrefab;

        [Space(5)]

        [Header("GUI References")]
        [SerializeField] private GamePlayerGUI _playerGUIPrefab;
        [Space(2.5f)]
        [SerializeField] private Button _quitButton;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _passTurnButton;
        [SerializeField] private Button _redColorButton;
        [SerializeField] private Button _blueColorButton;
        [SerializeField] private Button _greenColorButton;
        [SerializeField] private Button _yellowColorButton;


        [Space(2.5f)]
        [SerializeField] private Image _topDiscardImage;
        [SerializeField] private Image _turnTimerRingImage;
        [Space(2.5f)]
        [SerializeField] private TextMeshProUGUI _countdownText;
        [SerializeField] private TextMeshProUGUI _currentColorText;
        [SerializeField] private TextMeshProUGUI _drawPileCountText;
        [SerializeField] private TextMeshProUGUI _currentPlayerNameText;
        private Button _cardDrawButton;
        private CanvasGroup _canvasGroup;

        [Space(5f)]

        [Header("Gameobject References")]
        public GameObject _canvas;
        [SerializeField] private GameObject _hand;
        [SerializeField] private GameObject _pausePanel;
        [SerializeField] private GameObject _countdownPanel;
        [SerializeField] private GameObject _colorPickerPanel;
        [SerializeField] private GameObject _currentColorPanel;
        [SerializeField] private GameObject _cardDrawGameobject;

        [Space(5f)]

        [Header("Transform References")]
        [SerializeField] private Transform _cardTargetTransform;
        private Transform _handContainer;
        public Transform CardTargetTransform => _cardTargetTransform;

        [SerializeField] private int _cardPerPlayer;
        [SerializeField] private float _turnTimeLimit = 15f;
        [SerializeField] private float _playMoveDuration = 0.3f;
        [SerializeField] private float _countdownStepDuration = 1f;
        public float PlayMoveDuration => _playMoveDuration;    

        private readonly List<uint> _turnOrder = new();
        private readonly List<GamePlayerGUI> _playerGUIs = new();
        private readonly Dictionary<uint, int> _netIdToGuiIndex = new(); // netId -> GUI list index
        private readonly Dictionary<uint, PlayerEntry> _serverPlayers = new(); 
        private readonly Dictionary<uint, List<UnoCard>> _serverHands = new();

        private readonly SyncDictionary<uint, PlayerGameInfo> _playerData = new();

        private UnoDeck _deck;

        [SyncVar(hook = nameof(OnCurrentPlayerChanged))]
        private uint _currentPlayerNetId;

        [SyncVar(hook = nameof(OnTopDiscardChanged))]
        private UnoCard _syncedTopDiscard;

        [SyncVar(hook = nameof(OnTurnStartTimeChanged))]
        private double _turnStartTime;

        private Action<CardColor> _onColorChosen;

        private Coroutine _turnTimerCoroutine;

        private int _turnIndex = 0;
        private int _turnDirection = 1;

        private bool _isTimerRunningLocally;
        private bool _awaitingDrawnCardDecision;

        private void Awake()
        {
            _playerData.OnChange += OnPlayerDataChanged;
        }

        private void OnDestroy()
        {
            _playerData.OnChange -= OnPlayerDataChanged;

            if (_turnTimerCoroutine is { })
                StopCoroutine(_turnTimerCoroutine);
        }

        private void Update()
        {
            if (!_isTimerRunningLocally || _turnTimerRingImage == null) return;

            var elapsed = NetworkTime.time - _turnStartTime;
            var normalized = _turnTimeLimit > 0f ? 1f - (float)(elapsed / _turnTimeLimit) : 0f;
            _turnTimerRingImage.fillAmount = Mathf.Clamp01(normalized);

            if (normalized <= 0f)
                _isTimerRunningLocally = false;
        }

        public override void OnStartClient()
        {
            Instance = this;

            _canvasGroup = _canvas.GetComponent<CanvasGroup>();
            _cardDrawButton = _cardDrawGameobject.GetComponent<Button>();

            _handContainer = _hand.transform;

            _canvasGroup.alpha = 1f;
            _canvasGroup.interactable = true;
            _canvasGroup.blocksRaycasts = true;

            _quitButton.onClick.AddListener(OnClickQuitButton);
            _pauseButton.onClick.AddListener(() => OpenAndClosePausePanel(true));
            _resumeButton.onClick.AddListener(() => OpenAndClosePausePanel(false));

            _cardDrawButton.onClick.AddListener(OnDrawButtonClicked);
            _passTurnButton.onClick.AddListener(OnPassButtonClicked);
            _passTurnButton.gameObject.SetActive(false);

            _redColorButton.onClick.AddListener(OnRedColorClicked);
            _yellowColorButton.onClick.AddListener(OnYellowColorClicked);
            _greenColorButton.onClick.AddListener(OnGreenColorClicked);
            _blueColorButton.onClick.AddListener(OnBlueColorClicked);
            _colorPickerPanel.SetActive(false);
        }

        public override void OnStopClient()
        {
            _cardDrawButton.onClick.RemoveListener(OnDrawButtonClicked);
            _passTurnButton.onClick.RemoveListener(OnPassButtonClicked);

            _redColorButton.onClick.RemoveListener(OnRedColorClicked);
            _yellowColorButton.onClick.RemoveListener(OnYellowColorClicked);
            _greenColorButton.onClick.RemoveListener(OnGreenColorClicked);
            _blueColorButton.onClick.RemoveListener(OnBlueColorClicked);

            _quitButton.onClick.RemoveListener(OnClickQuitButton);
            _pauseButton.onClick.RemoveListener(() => OpenAndClosePausePanel(true));
            _resumeButton.onClick.RemoveListener(() => OpenAndClosePausePanel(false));

            Instance = null;
        }

        private Color ToUnityColor(CardColor color) => color switch
        {
            CardColor.Red => _redColor,
            CardColor.Yellow => _yellowColor,
            CardColor.Green => _greenColor,
            CardColor.Blue => _blueColor,
            _ => Color.white
        };

        private void OnClickQuitButton()
        {
            NetworkClient.Send(new ServerDeckMessage()
            {
                serverDeckOperation = ServerDeckOperation.QuitMatch
            });
        }

        private void OpenAndClosePausePanel(bool value)
        {
            _pausePanel.SetActive(value);
        }


        [Client]
        private void OnDrawButtonClicked()
        {
            if (!IsMyTurn()) return;

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.DrawCard
            });
        }

        [Client]
        private void OnPassButtonClicked()
        {
            if (!_awaitingDrawnCardDecision)
                return;

            _awaitingDrawnCardDecision = false;

            _passTurnButton.gameObject.SetActive(false);

            RefreshHandInteractability(false);

            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.PassTurn
            });
        }

        // ── Server: Setup ─────────────────────────────────────────────────────

        [Server]
        public void AddPlayer(NetworkConnectionToClient conn, PlayerRoomInfo info)
        {
            uint netId = conn.identity.netId;

            _serverPlayers[netId] = new PlayerEntry { Conn = conn, RoomInfo = info };
            _serverHands[netId] = new List<UnoCard>();
            _turnOrder.Add(netId);
            _playerData[netId] = new PlayerGameInfo
            {
                connectionId = netId,
                cardCount = 0,
                playerName = info.playerName,
                isOwner = info.isOwner
            };
        }

        [Server]
        public void StartGame(UnoDeck deck)
        {
            _deck = deck;
            FlipFirstCard();
            StartCoroutine(StartGameSequence());
        }

        [Server]
        private IEnumerator StartGameSequence()
        {
            yield return null; // let SyncDictionary flush first

            RpcPlayStartCountdown();
            DealCards(_cardPerPlayer); // cards can be dealt while the countdown animation plays

            float countdownDuration = _countdownStepDuration * 4;

            // Client applies an extra 0.5s startDelay before the deal animation begins
            // (see ShowDealtCards(..., applyStartDelay: true) in GameManager.OnClientDeckMessage),
            // plus the staggered per-card reveal (0.12s apart) and the final card's move animation.
            const float dealStartDelay = 0.5f;
            float dealAnimationDuration = dealStartDelay + (_cardPerPlayer - 1) * 0.12f + _playMoveDuration;

            // Wait for whichever finishes last so the turn timer never starts
            // before either the countdown or the deal animation has completed.
            float waitDuration = Mathf.Max(countdownDuration, dealAnimationDuration);
            yield return new WaitForSeconds(waitDuration);

            SetNextTurn(_turnOrder[0]);
        }

        [Server]
        private void DealCards(int count)
        {
            foreach (var (netId, entry) in _serverPlayers)
            {
                List<UnoCard> hand = new();

                _deck.DrawMultiple(count, hand);

                _serverHands[netId].AddRange(hand);

                // Struct must be fully reassigned to trigger SyncDictionary sync
                var data = _playerData[netId];
                data.cardCount = hand.Count;
                _playerData[netId] = data;

                entry.Conn.Send(new ClientDeckMessage
                {
                    clientDeckOperation = ClientDeckOperation.CardDealt,
                    Cards = hand.ToArray(),
                    DrawPileCount = _deck.DrawPileCount
                });
            }
        }

        [Server]
        private void FlipFirstCard()
        {
            UnoCard firstCard;

            // Keep drawing until we get a non-power card
            do
            {
                if (!_deck.TryDraw(out firstCard)) return;

                if (firstCard.Type != CardType.Number)
                {
                    // Put it back and reshuffle before trying again
                    _deck.ReturnToDraw(firstCard);
                    _deck.Shuffle();
                }
                else
                {
                    break;
                }
            } while (true);

            _deck.Discard(firstCard);
            SetTopDiscard(firstCard, _deck.DrawPileCount + 1);
        }

        [Server]
        private void SetNextTurn(uint netID)
        {
            _currentPlayerNetId = netID;
            Debug.Log($"[Turn] Now: {JsonUtility.ToJson(_playerData[netID])} (netId {netID})");

            RestartTurnTimer();
        }

        [Server]
        private void RestartTurnTimer()
        {
            if (_turnTimerCoroutine is { })
                StopCoroutine(_turnTimerCoroutine);

            _turnStartTime = NetworkTime.time;
            _turnTimerCoroutine = StartCoroutine(TurnTimerRoutine(_currentPlayerNetId));
        }

        [Server]
        private IEnumerator TurnTimerRoutine(uint netIdForThisTurn)
        {
            yield return new WaitForSeconds(_turnTimeLimit);

            if (_currentPlayerNetId != netIdForThisTurn) yield break;

            Debug.Log($"[Turn] Time expired for netId {netIdForThisTurn}. Forcing pass.");
            AdvanceTurn();
        }

        [Server]
        public void AdvanceTurn()
        {
            _turnIndex = (_turnIndex + _turnDirection + _turnOrder.Count) % _turnOrder.Count;
            SetNextTurn(_turnOrder[_turnIndex]);
        }

        /// <summary>Server: reverse turn direction (Reverse card).</summary>
        [Server]
        public void ReverseTurnDirection()
        {
            _turnDirection *= -1;
        }

        /// <summary>Server: skip next player (Skip card).</summary>
        [Server]
        public void SkipNextPlayer()
        {
            _turnIndex = (_turnIndex + _turnDirection + _turnOrder.Count) % _turnOrder.Count;
        }

        /// <summary>Server: check if this connection is allowed to act.</summary>
        [Server]
        public bool IsCurrentPlayer(NetworkConnectionToClient conn)
        {
            return conn.identity.netId == _currentPlayerNetId;
        }

        [Server]
        private uint GetNextPlayerNetId()
        {
            int nextIndex = (_turnIndex + _turnDirection + _turnOrder.Count) % _turnOrder.Count;
            return _turnOrder[nextIndex];
        }

        [Server]
        private void ForcePlayerDraw(uint targetNetId, int count)
        {
            if (!_serverPlayers.TryGetValue(targetNetId, out var entry))
            {
                Debug.LogWarning($"[Server] ForcePlayerDraw: no connection found for netId {targetNetId}.");
                return;
            }

            var drawn = new List<UnoCard>();
            _deck.DrawMultiple(count, drawn);

            _serverHands[targetNetId].AddRange(drawn);

            var data = _playerData[targetNetId];
            data.cardCount += drawn.Count;
            _playerData[targetNetId] = data;

            entry.Conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardDrawn,
                Cards = drawn.ToArray(),
                DrawPileCount = _deck.DrawPileCount,
                CanPlayDrawnCard = false
            });
        }

        [Server]
        public void UpdatePlayerCardCount(NetworkConnectionToClient conn, int newCount)
        {
            uint netId = conn.identity.netId;
            if (!_playerData.ContainsKey(netId)) return;

            var data = _playerData[netId];
            data.cardCount = newCount;
            _playerData[netId] = data;
        }

        public void HandlePlayerQuit(NetworkConnectionToClient conn)
        {
            var netId = conn.identity.netId;

            if (!_serverPlayers.ContainsKey(netId))
                return;

            var wasCurrentPlayer = _currentPlayerNetId == netId;

            _serverPlayers.Remove(netId);
            _serverHands.Remove(netId);   
            _turnOrder.Remove(netId);
            _playerData.Remove(netId);

            if (wasCurrentPlayer && _turnOrder.Count > 0)
            {
                _turnIndex %= _turnOrder.Count;
                _currentPlayerNetId = _turnOrder[_turnIndex];
                _turnStartTime = NetworkTime.time;
            }

            // Tell the quitting client to go back to the lobby (still connected!)
            conn.Send(new ClientRoomMessage
            {
                clientRoomOperation = ClientRoomOperation.Left
            });

            // Remove their player object from the match without disconnecting them
            if (conn.identity != null)
            {
                NetworkServer.RemovePlayerForConnection(conn, RemovePlayerOptions.Destroy);
            }

            foreach (var entry in _serverPlayers.Values)
            {
                entry.Conn.Send(new ClientDeckMessage
                {
                    clientDeckOperation = ClientDeckOperation.PlayerQuit
                });
            }

            // If only one (or zero) players remain, you may want to end the match here
            if (_turnOrder.Count <= 1)
            {
                // TODO: end match / declare remaining player winner
            }
        }

        // ── Client ────────────────────────────────────────────────────────────

        [Client]
        public void ShowDealtCards(UnoCard[] cards, bool applyStartDelay = false)
        {
            StartCoroutine(ShowDealtCardsStaggered(cards, applyStartDelay ? 0.5f : 0f));
        }

        private IEnumerator ShowDealtCardsStaggered(UnoCard[] cards, float startDelay)
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            var views = new List<Card>();

            foreach (var card in cards)
            {
                var cardView = Instantiate(_cardPrefab, _handContainer);
                cardView.Setup(card);
                views.Add(cardView);
            }

            // ONE rebuild with all cards still fully layout-controlled,
            // so every card captures its correct, final grid slot.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_handContainer);

            foreach (var cardView in views)
                cardView.CaptureDealTarget();

            // ONLY NOW do we start excluding cards from layout + snapping to origin.
            foreach (var cardView in views)
                cardView.PrepareDeal(_cardDrawGameobject.transform);

            foreach (var cardView in views)
            {
                cardView.AnimateDeal();
                yield return _waitForSeconds0_12;
            }

            // Cards may have finished spawning after the turn was already assigned
            // (e.g. during the start countdown), so re-sync interactability now.
            RefreshHandInteractability(IsMyTurn());
        }

        [Client]
        public bool IsMyTurn() => NetworkClient.localPlayer is { netId: var netId } && _currentPlayerNetId == netId;

        [Client]
        public void ShowColorPicker(Action<CardColor> onColorChosen)
        {
            _onColorChosen = onColorChosen;
            _colorPickerPanel.SetActive(true);
        }

        [Client]
        private void OnRedColorClicked() => HandleColorPicked(CardColor.Red);

        [Client]
        private void OnYellowColorClicked() => HandleColorPicked(CardColor.Yellow);

        [Client]
        private void OnGreenColorClicked() => HandleColorPicked(CardColor.Green);

        [Client]
        private void OnBlueColorClicked() => HandleColorPicked(CardColor.Blue);

        [Client]
        private void HandleColorPicked(CardColor color)
        {
            _colorPickerPanel.SetActive(false);

            var callback = _onColorChosen;
            _onColorChosen = null;
            callback?.Invoke(color);
        }

        [Server]
        public void HandlePlayerCard(NetworkConnectionToClient conn, UnoCard card, CardColor chosenWildColor)
        {
            var netId = conn.identity.netId;

            if (!IsLegalPlay(card))
            {
                Debug.LogWarning($"[Server] Rejected illegal play from netId {netId}: {card}");

                conn.Send(new ClientDeckMessage
                {
                    clientDeckOperation = ClientDeckOperation.Error,
                    errorMessage = $"Illegal play: {card} does not match the current discard/stack requirement."
                });

                return;
            }

            if (card.Type is CardType.Wild or CardType.WildDrawFour)
            {
                if (chosenWildColor is CardColor.None)
                {
                    Debug.LogWarning($"[Server] Rejected wild play from netId {netId}: no color chosen.");

                    conn.Send(new ClientDeckMessage
                    {
                        clientDeckOperation = ClientDeckOperation.Error,
                        errorMessage = "You must choose a color for the Wild card."
                    });

                    return;
                }

                card.Color = chosenWildColor;
            }

            if (!TryRemoveFromHand(netId, card))
            {
                Debug.LogWarning($"[Server] Rejected play from netId {netId}: card {card} not found in tracked hand.");

                conn.Send(new ClientDeckMessage
                {
                    clientDeckOperation = ClientDeckOperation.Error,
                    errorMessage = $"Illegal play: {card} is not in your hand."
                });

                return;
            }

            _deck.Discard(card);

            SetTopDiscard(card, _deck.DrawPileCount + 1);

            var data = _playerData[netId];
            data.cardCount -= 1;
            _playerData[netId] = data;

            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardPlayed,
                TopDiscardCard = card,
                DrawPileCount = _deck.DrawPileCount
            });

            switch (card.Type)
            {
                case CardType.Reverse:
                    ReverseTurnDirection();

                    if (_turnOrder.Count == 2)
                        SkipNextPlayer();

                    AdvanceTurn();
                    break;

                case CardType.Skip:
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                case CardType.DrawTwo:
                    ForcePlayerDraw(GetNextPlayerNetId(), 2);
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                case CardType.WildDrawFour:
                    ForcePlayerDraw(GetNextPlayerNetId(), 4);
                    SkipNextPlayer();
                    AdvanceTurn();
                    break;

                default: // Number, Wild
                    AdvanceTurn();
                    break;
            }
        }

        [Server]
        private bool IsLegalPlay(UnoCard card)
        {
            return IsPlayableAgainstTop(card);
        }

        [Server]
        private bool TryRemoveFromHand(uint netId, UnoCard card)
        {
            if (!_serverHands.TryGetValue(netId, out var hand))
                return false;

            int index = hand.FindIndex(c => c.Id == card.Id);
            if (index < 0) return false;

            hand.RemoveAt(index);
            return true;
        }

        [Server]
        public void HandleDrawCard(NetworkConnectionToClient conn)
        {
            var netId = conn.identity.netId;

            if (!_deck.TryDraw(out UnoCard drawnCard))
            {
                Debug.LogWarning("[Server] Draw pile empty.");
                return;
            }

            _serverHands[netId].Add(drawnCard);

            var data = _playerData[netId];
            data.cardCount++;
            _playerData[netId] = data;

            var canPlay = IsPlayableAgainstTop(drawnCard);

            conn.Send(new ClientDeckMessage
            {
                clientDeckOperation = ClientDeckOperation.CardDrawn,
                Cards = new[] { drawnCard },
                DrawPileCount = _deck.DrawPileCount,
                CanPlayDrawnCard = canPlay,
            });

            if (!canPlay)
                AdvanceTurn();

            Debug.Log($"[Server] {data} drew a card. Hand size: {data.cardCount}");
        }

        [Server]
        public void HandlePassTurn(NetworkConnectionToClient conn)
        {
            if (!IsCurrentPlayer(conn)) return;
            Debug.Log("[Server] Player passed after drawing.");
            AdvanceTurn();
        }

        [Client]
        public void OnDrawnCardReceived(bool canPlay, UnoCard drawnCard)
        {
            if(!canPlay)
            {
                RefreshHandInteractability(false);
                return;
            }

            _awaitingDrawnCardDecision = true;
            _passTurnButton.gameObject.SetActive(true);

            foreach (Transform item in _handContainer)
            {
                if (!item.TryGetComponent<Card>(out var card))
                    continue;

                var isDrawnCard = card.CardData.Id == drawnCard.Id;
                var isPlayable = IsValidPlay(card.CardData);
                card.SetInteractable(isDrawnCard && isPlayable);
            }
        }

        [Server]
        private bool IsPlayableAgainstTop(UnoCard drawnCard)
        {
            if(_deck.TopDiscard is not { } topCard)
                return true;

            return drawnCard.Type switch
            {
                CardType.Wild or CardType.WildDrawFour => true,

                _ when drawnCard.Color == topCard.Color => true,
                _ when drawnCard.Type == topCard.Type => true,
                CardType.Number when topCard.Type is CardType.Number
                                 && drawnCard.FaceValue == topCard.FaceValue => true,

                _ => false
            };
        }


        [ClientRpc]
        private void RpcShowTopDiscard(UnoCard card, int drawCount)
        {
            _syncedTopDiscard = card;

            if (Card.CardSprites.TryGetValue(card.Id, out var sprite))
            {
                _topDiscardImage.color = new(1, 1, 1, 1);
                _topDiscardImage.sprite = sprite;
            }

            var isWildCard = card.Type is CardType.Wild or CardType.WildDrawFour;

            _currentColorPanel.SetActive(isWildCard);

            if (isWildCard)
            {
                _currentColorText.color = ToUnityColor(card.Color);
                _currentColorText.SetText($"{card.Color}");
            }

            _drawPileCountText.text = $"{drawCount} left";
        }

        [ClientRpc]
        private void RpcPlayStartCountdown()
        {
            StartCoroutine(StartCountdownRoutine());
        }

        [Client]
        private IEnumerator StartCountdownRoutine()
        {
            if (_countdownPanel != null)
                _countdownPanel.SetActive(true);

            string[] steps = { "3", "2", "1", "GO!" };
            foreach (var step in steps)
            {
                yield return StartCoroutine(PlayCountdownStep(step));
            }

            if (_countdownPanel != null)
                _countdownPanel.SetActive(false);
        }

        [Client]
        private IEnumerator PlayCountdownStep(string text)
        {
            if (_countdownText == null) yield break;

            const float startScale = 1.6f;
            const float endScale = 1f;

            _countdownText.text = text;

            var rect = _countdownText.rectTransform;
            rect.localScale = Vector3.one * startScale;

            float elapsed = 0f;
            while (elapsed < _countdownStepDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _countdownStepDuration);

                float scale = Mathf.Lerp(startScale, endScale, 1f - (1f - t) * (1f - t));
                rect.localScale = Vector3.one * scale;

                yield return null;
            }

            rect.localScale = Vector3.one * endScale;
        }

        private void OnCurrentPlayerChanged(uint oldNetId, uint newNetId)
        {
            _awaitingDrawnCardDecision = false;
            _passTurnButton.gameObject.SetActive(false);

            if (NetworkClient.localPlayer is not { netId: var selfNetId }) return;

            if (newNetId == selfNetId)
            {
                _currentPlayerNameText.SetText($"you");

                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
                RefreshHandInteractability(true);
            }
            else
            {
                if (_playerData.TryGetValue(newNetId, out var currentPlayerInfo))
                    _currentPlayerNameText.SetText(currentPlayerInfo.playerName);

                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = true; // ← true always, so canvas still catches clicks
                RefreshHandInteractability(false);
            }
        }

        private void OnTopDiscardChanged(UnoCard oldCard, UnoCard newCard)
        {
            _syncedTopDiscard = newCard;

            var isWildCard = newCard.Type is CardType.Wild or CardType.WildDrawFour;

            _currentColorPanel.SetActive(isWildCard);

            if (isWildCard)
            {
                _currentColorText.color = ToUnityColor(newCard.Color);
                _currentColorText.SetText($"{newCard.Color}");
            }

            if (NetworkClient.localPlayer is { netId: var selfNetId } && _currentPlayerNetId == selfNetId)
                RefreshHandInteractability(true);
        }

        private void OnTurnStartTimeChanged(double oldValue, double newValue)
        {
            var isMyTurn = NetworkClient.localPlayer is { netId: var selfNetId } && _currentPlayerNetId == selfNetId;

            _isTimerRunningLocally = isMyTurn;

            if (_turnTimerRingImage != null)
                _turnTimerRingImage.fillAmount = 1f;
        }

        [Server]
        private void SetTopDiscard(UnoCard card, int drawCount)
        {
            _syncedTopDiscard = card;      // SyncVar, always consistent for late/joining clients too
            RpcShowTopDiscard(card, drawCount); // keep RPC only for the visual/GUI update
        }

        /// <summary>
        /// Fires automatically on every client when any _playerData entry changes.
        /// </summary>
        private void OnPlayerDataChanged(SyncIDictionary<uint, PlayerGameInfo>.Operation op, uint netId, PlayerGameInfo data)
        {
            if (!_netIdToGuiIndex.TryGetValue(netId, out int index))
                return; // self or panel not built yet

            if(_playerData.TryGetValue(netId, out var updatedData))
                _playerGUIs[index].UpdateCardCount(updatedData.cardCount);
        }

        public void RefreshHandInteractability(bool isMyTurn)
        {
            foreach (Transform item in _handContainer)
            {
                if (!item.TryGetComponent<Card>(out var card))
                    continue;

                card.SetInteractable(isMyTurn && IsValidPlay(card.CardData));
            }
        }

        private bool IsValidPlay(UnoCard card)
        {
            if (card.Type is CardType.Wild ||
                card.Type is CardType.WildDrawFour)
                return true;

            if (card.Color == _syncedTopDiscard.Color)
                return true;

            if (card.Type is CardType.Number &&
                _syncedTopDiscard.Type is CardType.Number)
            {
                return card.FaceValue == _syncedTopDiscard.FaceValue;
            }

            return card.Type == _syncedTopDiscard.Type;
        }
    }

    public class PlayerEntry
    {
        public NetworkConnectionToClient Conn;
        public PlayerRoomInfo RoomInfo;
    }
}