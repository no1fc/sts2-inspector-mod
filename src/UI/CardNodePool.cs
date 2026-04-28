using System.Collections.Generic;
using Godot;

namespace TestMode1.UI
{
    public class CardNodePool
    {
        private readonly List<HoverLabel> _available = new();
        private readonly List<HoverLabel> _inUse     = new();
        private readonly Control          _storage;
        private readonly ImageTooltip     _tooltip;

        public CardNodePool(Node parent, int poolSize, ImageTooltip tooltip)
        {
            _tooltip = tooltip;
            _storage = new Control { Visible = false };
            parent.AddChild(_storage);

            for (int i = 0; i < poolSize; i++)
            {
                var lbl = new HoverLabel();
                _storage.AddChild(lbl);
                _available.Add(lbl);
            }
        }

        public HoverLabel Acquire(string displayText, string imageKey,
                                  ImageCache.ItemType itemType, Node target,
                                  string description = "", string stats = "")
        {
            HoverLabel node;
            if (_available.Count > 0)
            {
                node = _available[^1];
                _available.RemoveAt(_available.Count - 1);
            }
            else
            {
                node = new HoverLabel();
                _storage.AddChild(node);
            }

            node.Reparent(target);
            node.Configure(displayText, imageKey, itemType, _tooltip, description, stats);
            node.Visible = true;
            _inUse.Add(node);
            return node;
        }

        public void ReleaseAll()
        {
            foreach (var n in _inUse)
            {
                n.Reparent(_storage);
                n.Visible = false;
                n.Text    = "";
                _available.Add(n);
            }
            _inUse.Clear();
        }
    }
}
