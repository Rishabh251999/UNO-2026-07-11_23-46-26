using Mirror;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    public class RoomGUI : MonoBehaviour
    {
        [ReadOnly, SerializeField] internal GameManager _gameManager;

        [SerializeField] private Image _roomImage;
        [SerializeField] private TextMeshProUGUI _roomCodeText;
        [SerializeField] private TextMeshProUGUI _playerCountText;

        private Guid _roomCode;

        private void Awake()
        {
            _gameManager = FindAnyObjectByType<GameManager>();
        }

        [ClientCallback]
        public void OnRoomClicked(bool isOn)
        {
            var roomCode = isOn ? _roomCode : Guid.Empty;

            _gameManager.SelectRoom(roomCode);
            _roomImage.color = isOn ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 1f, 1f, 0.2f);
        }

        [ClientCallback]
        public Guid GetRoomCode() => _roomCode;

        [ClientCallback]
        public void SetRoomInfo(RoomInfo roomInfo)
        {
            _roomCode = roomInfo.roomCode;
            _roomCodeText.text = $"{_roomCode.ToString()[..6]}".ToUpper();
            _playerCountText.text = $"{roomInfo.playerCount} / {roomInfo.maxPlayers}";
        }
    }
}