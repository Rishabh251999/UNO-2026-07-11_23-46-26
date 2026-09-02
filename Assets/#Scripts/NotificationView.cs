using TMPro;
using UnityEngine;

namespace UNO
{
    public class NotificationView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _notificationText1;
        [SerializeField] private TextMeshProUGUI _notificationText2;

        public void Show(string text1, string text2, Color color)
        {
            if (_notificationText1 is not { } || _notificationText2 is not { })
                return;

            gameObject.SetActive(true);

            _notificationText1.SetText(text1);

            _notificationText2.color = color;
            _notificationText2.SetText(text2);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
