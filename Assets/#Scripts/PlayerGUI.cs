using Mirror;
using TMPro;
using UnityEngine;

namespace UNO
{
    public class PlayerGUI : MonoBehaviour
    {
        private readonly Color32 ReadyColor = new(76, 175, 80, 255);   // #4CAF50
        private readonly Color32 NotReadyColor = new(239, 108, 108, 255);

        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private GameObject _roomOwnerGameObject;

        [ClientCallback]
        public void SetPlayerInfo(PlayerRoomInfo info)
        {
            _playerName.text = $"{info.playerName}";
            _playerName.color = info.isReady ? ReadyColor : NotReadyColor;

            _roomOwnerGameObject.SetActive(info.isOwner);
        }
    }
}