#nullable enable

using System;

namespace OpenTween.Controls
{
    public class TabSelectingEventArgs : EventArgs
    {
        public string TabName { get; }

        public bool Cancel { get; set; }

        public TabSelectingEventArgs(string tabName)
            => this.TabName = tabName;
    }

    public class TabDeselectedEventArgs : EventArgs
    {
        public string? PreviousTabName { get; }

        public TabDeselectedEventArgs(string? previousTabName)
            => this.PreviousTabName = previousTabName;
    }
}
