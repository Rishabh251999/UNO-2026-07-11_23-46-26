using Mirror;
using System.Collections.Generic;
using UnityEngine;

namespace UNO
{
    public class UnoDeck
    {
        private readonly List<UnoCard> _drawPile = new(108);
        private readonly List<UnoCard> _discardPile = new(108);

        public int DrawPileCount => _drawPile.Count;
        public int DiscardPileCount => _discardPile.Count;
        public UnoCard? TopDiscard => _discardPile.Count > 0 ? _discardPile[^1] : null;

        [Server]
        public void BuildDeck()
        {
            _drawPile.Clear();
            _discardPile.Clear();

            byte id = 0;

            CardColor[] colors = { CardColor.Red, CardColor.Green, CardColor.Blue, CardColor.Yellow };

            foreach (var item in colors)
            {
                _drawPile.Add(Make(ref id, item, CardType.Number, 0));

                for (byte i = 1; i <= 9; i++)
                {
                    _drawPile.Add(Make(ref id, item, CardType.Number, i));
                    _drawPile.Add(Make(ref id, item, CardType.Number, i));
                }

                for (byte i = 0; i < 2; i++)
                {
                    _drawPile.Add(Make(ref id, item, CardType.Skip, 20));
                    _drawPile.Add(Make(ref id, item, CardType.Reverse, 20));
                    _drawPile.Add(Make(ref id, item, CardType.DrawTwo, 20));
                }
            }

            for (byte i = 0; i < 4; i++)
            {
                _drawPile.Add(Make(ref id, CardColor.None, CardType.Wild, 50));
                _drawPile.Add(Make(ref id, CardColor.None, CardType.WildDrawFour, 50));
            }

            Debug.Assert(_drawPile.Count == 108,
            $"[UnoDeck] Expected 108 cards, built {_drawPile.Count}");

            Shuffle();
            Debug.Log($"[UnoDeck] Built and shuffled {_drawPile.Count} cards.");
        }

        [Server]
        public void Shuffle()
        {
            int n = _drawPile.Count;
            while (n > 1)
            {
                n--;
                int k = Random.Range(0, n + 1);
                (_drawPile[n], _drawPile[k]) = (_drawPile[k], _drawPile[n]);
            }
        }

        [Server]
        public void ReturnToDraw(UnoCard card) => _drawPile.Add(card);

        [Server]
        public int DrawMultiple(int count, List<UnoCard> hand)
        {
            int drawn = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryDraw(out UnoCard card)) break;
                hand.Add(card);
                drawn++;
            }
            return drawn;
        }

        [Server]
        public bool TryDraw(out UnoCard card)
        {
            if (_drawPile.Count == 0)
                ReshuffleDiscardIntoDraw();

            if (_drawPile.Count == 0)
            {
                card = default;
                Debug.LogWarning("[UnoDeck] Both piles empty — cannot draw.");
                return false;
            }

            card = _drawPile[0];
            _drawPile.RemoveAt(0);
            return true;
        }

        [Server]
        public void Discard(UnoCard card) => _discardPile.Add(card);

        [Server]
        private void ReshuffleDiscardIntoDraw()
        {
            if (_discardPile.Count <= 1) return;

            UnoCard top = _discardPile[^1];
            _discardPile.RemoveAt(_discardPile.Count - 1);

            _drawPile.AddRange(_discardPile);
            _discardPile.Clear();
            _discardPile.Add(top);

            Shuffle();
            Debug.Log($"[UnoDeck] Reshuffled {_drawPile.Count} cards from discard into draw pile.");
        }

        private UnoCard Make(ref byte id, CardColor color, CardType type, byte faceValue) =>
            new(){ Id = id++, Color = color, Type = type, FaceValue = faceValue };
    }
}
