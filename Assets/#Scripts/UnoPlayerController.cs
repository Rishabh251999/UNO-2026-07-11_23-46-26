using Mirror;
using System;
using UnityEngine;

namespace UNO
{
    public class UnoPlayerController : NetworkBehaviour
    {
        #region PlayCard
        public void TryPlayCard(Card card)
        {
            if (!isLocalPlayer)
                return;

            if (UnoGameController.Instance is not { } instance)
                return;

            if (!instance.IsMyTurn())
                return;

            if (card.CardData.Type is CardType.Wild or CardType.WildDrawFour)
            {
                card.SetInteractable(false); // prevent double-clicks while choosing
                instance.ShowColorPicker(chosenColor => SendPlay(card, instance, chosenColor));
                return;
            }

            SendPlay(card, instance, CardColor.None);
        }

        private void SendPlay(Card card, UnoGameController instance, CardColor chosenColor)
        {
            NetworkClient.Send(new ServerDeckMessage
            {
                serverDeckOperation = ServerDeckOperation.PlayCard,
                Card = card.CardData,
                chosenWildColor = chosenColor
            });

            card.PlayTowards(instance.CardTargetTransform, instance._canvas.transform, () => Destroy(card.gameObject));
        }

        #endregion
    }
}