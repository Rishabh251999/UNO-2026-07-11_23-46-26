using Mirror;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UNO
{
    public class LobbyManager : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private RoomGUI _roomPrefab;

        [SerializeField] private Button _createButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private ToggleGroup _toggleGroup;

        [SerializeField] private Transform _matchList;


        private void Start()
        {
            _createButton.onClick.AddListener(RequestCreateRoom);
            _joinButton.onClick.AddListener(RequestJoinRoom);
        }

        private void OnDestroy()
        {
            _createButton.onClick.RemoveListener(RequestCreateRoom);
            _joinButton.onClick.RemoveListener(RequestJoinRoom);
        }

        [ClientCallback]
        public void UpdateRoomList(IReadOnlyDictionary<Guid, RoomInfo> openRooms)
        {
            // Clear existing room list UI
            foreach (Transform child in _matchList.transform)
                Destroy(child.gameObject);

            // Create UI elements for each room
            foreach (var roomInfo in openRooms.Values)
            {
                var roomUIElement = Instantiate(_roomPrefab, _matchList.transform);
                roomUIElement.transform.SetParent(_matchList.transform, false);
                roomUIElement.SetRoomInfo(roomInfo);

                if (roomUIElement.TryGetComponent<Toggle>(out var toggle))
                {
                    toggle.group = _toggleGroup;

                    if (roomInfo.roomCode == _gameManager.selectedRoom)
                        toggle.isOn = true;
                }
            }
        }

        /// <summary>
        /// Called from Create Button
        /// </summary>
        [ClientCallback]
        public void RequestCreateRoom()
        {
            NetworkClient.Send(new ServerRoomMessage { serverRoomOperation = ServerRoomOperation.Create });
        }

        /// <summary>
        /// Called from Join Button
        /// </summary>
        [ClientCallback]
        public void RequestJoinRoom()
        {
            if (_gameManager.selectedRoom == Guid.Empty)
            {
                Debug.LogWarning("No room selected");
                return;
            }

            NetworkClient.Send(new ServerRoomMessage
            {
                serverRoomOperation = ServerRoomOperation.Join,
                roomCode = _gameManager.selectedRoom
            });
        }
    }
}