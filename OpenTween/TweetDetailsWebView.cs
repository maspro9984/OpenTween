// OpenTween - Client of Twitter
// Copyright (c) 2026 OpenTween contributors
// All rights reserved.
//
// This file is part of OpenTween.
//
// This program is free software; you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation; either version 3 of the License, or (at your option)
// any later version.
//
// This program is distributed in the hope that it will be useful, but
// WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
// or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along
// with this program. If not, see <http://www.gnu.org/licenses/>, or write to
// the Free Software Foundation, Inc., 51 Franklin Street - Fifth Floor,
// Boston, MA 02110-1301, USA.

#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace OpenTween
{
    /// <summary>
    /// 発言詳細欄の本文を表示する WebView2 コントロール
    /// </summary>
    /// <remarks>
    /// IE の WebBrowser コントロール (mshtml) は UI スレッド上で独自のメッセージループを回すため、
    /// 他のアプリケーションが差し込むフックとの組み合わせで UI スレッドが応答しなくなることがある。
    /// WebView2 は描画を別プロセスで行うため、この問題が起きない。
    /// リンクのクリック・マウスオーバー・選択文字列はページ内のスクリプトからメッセージで受け取る。
    /// </remarks>
    public class TweetDetailsWebView : WebView2
    {
        private const string BridgeScript = """
            (() => {
              'use strict';
              const post = message => window.chrome.webview.postMessage(message);
              const findLink = target => {
                const element = target instanceof Element ? target : target && target.parentElement;
                return element ? element.closest('a[href]') : null;
              };

              let hoverLink = null;
              const setHoverLink = link => {
                if (link === hoverLink) return;
                hoverLink = link;
                post({ type: 'hover', href: link ? link.getAttribute('href') : '' });
              };
              document.addEventListener('mouseover', e => setHoverLink(findLink(e.target)), true);
              document.addEventListener('mouseout', e => { if (!e.relatedTarget) setHoverLink(null); }, true);

              const onClick = e => {
                const link = findLink(e.target);
                if (!link) return;
                e.preventDefault();
                if (e.button !== 0 && e.button !== 1) return;
                post({ type: 'click', href: link.getAttribute('href'), ctrl: e.ctrlKey });
              };
              document.addEventListener('click', onClick, true);
              document.addEventListener('auxclick', onClick, true);

              document.addEventListener('selectionchange', () => post({ type: 'selection', text: String(document.getSelection()) }));

              window.chrome.webview.addEventListener('message', e => {
                const message = e.data;
                const ratio = window.devicePixelRatio || 1;
                switch (message.type) {
                  case 'scroll':
                    window.scrollBy(0, message.deltaPx / ratio);
                    break;
                  case 'page': {
                    const delta = window.innerHeight - message.lineHeightPx / ratio;
                    window.scrollBy(0, message.forward ? delta : -delta);
                    break;
                  }
                  case 'selectAll':
                    document.execCommand('selectAll');
                    break;
                }
              });
            })();
            """;

        private const string ContentSecurityPolicyMeta =
            """<meta http-equiv="Content-Security-Policy" content="script-src 'none'; object-src 'none'">""";

        private static readonly Regex AnchorRegex =
            new(@"<a\s(?<attrs>[^>]*)>(?<inner>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex AttributeRegex =
            new(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')");

        private static readonly Regex TagRegex = new("<[^>]*>");

        private bool initializationStarted;
        private bool browserReady;
        private TweetDetailsLink[]? links;

        /// <summary>表示中の HTML</summary>
        public string DocumentHtml { get; private set; } = "";

        /// <summary>マウスカーソルが乗っているリンクの URL (リンク上にない場合は空文字列)</summary>
        public string StatusText { get; private set; } = "";

        /// <summary>選択中の文字列 (選択されていない場合は null)</summary>
        public string? SelectedText { get; private set; }

        /// <summary>右クリック時に表示するメニュー</summary>
        /// <remarks>WebView2 は <see cref="Control.ContextMenuStrip"/> を使用できないため代わりに使用する</remarks>
        [DefaultValue(null)]
        public ContextMenuStrip? BrowserContextMenu { get; set; }

        /// <summary><see cref="StatusText"/> が変化した時に発生するイベント</summary>
        public event EventHandler? StatusTextChanged;

        /// <summary>本文中のリンクがクリックされた時に発生するイベント</summary>
        public event EventHandler<TweetDetailsLinkClickedEventArgs>? LinkClicked;

        public TweetDetailsWebView()
            => this.AllowExternalDrop = false;

        /// <summary>
        /// 指定された HTML を表示します
        /// </summary>
        public void NavigateToHtml(string html)
        {
            this.DocumentHtml = html;
            this.links = null;
            this.SelectedText = null;
            this.SetStatusText("");

            if (this.browserReady)
                this.CoreWebView2.NavigateToString(InsertContentSecurityPolicy(html));
        }

        /// <summary>表示中の HTML に含まれるリンクを返します</summary>
        public TweetDetailsLink[] GetLinks()
            => this.links ??= ParseLinks(this.DocumentHtml);

        /// <summary>指定したピクセル数だけ縦にスクロールします</summary>
        public void ScrollByPixels(int deltaPx)
            => this.PostToPage($$"""{"type":"scroll","deltaPx":{{deltaPx}}}""");

        /// <summary>表示領域の高さから 1 行分を引いた量だけ縦にスクロールします</summary>
        public void ScrollByPage(bool forward, int lineHeightPx)
            => this.PostToPage($$"""{"type":"page","forward":{{(forward ? "true" : "false")}},"lineHeightPx":{{lineHeightPx}}}""");

        /// <summary>本文をすべて選択します</summary>
        public void SelectAll()
            => this.PostToPage("""{"type":"selectAll"}""");

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (!this.initializationStarted && !this.DesignMode)
            {
                this.initializationStarted = true;
                _ = this.InitializeBrowserAsync();
            }
        }

        private async Task InitializeBrowserAsync()
        {
            try
            {
                var environment = await WebView2EnvironmentProvider.GetEnvironmentAsync();
                if (this.IsDisposed)
                    return;

                await this.EnsureCoreWebView2Async(environment);
                if (this.IsDisposed)
                    return;

                var coreWebView = this.CoreWebView2;
                var settings = coreWebView.Settings;
                settings.AreDevToolsEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = false;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.IsPinchZoomEnabled = false;
                settings.IsSwipeNavigationEnabled = false;
                settings.IsBuiltInErrorPageEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;

                coreWebView.ContextMenuRequested += this.CoreWebView2_ContextMenuRequested;
                coreWebView.NavigationStarting += this.CoreWebView2_NavigationStarting;
                coreWebView.NewWindowRequested += this.CoreWebView2_NewWindowRequested;
                coreWebView.WebMessageReceived += this.CoreWebView2_WebMessageReceived;

                await coreWebView.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript);
                if (this.IsDisposed)
                    return;

                this.browserReady = true;
                coreWebView.NavigateToString(InsertContentSecurityPolicy(this.DocumentHtml));
            }
            catch (Exception ex)
            {
                MyCommon.TraceOut(ex, "TweetDetailsWebView の初期化に失敗");
                this.ShowInitializationError(ex);
            }
        }

        private void ShowInitializationError(Exception ex)
        {
            if (this.IsDisposed)
                return;

            var errorLabel = new Label
            {
                Text = $"WebView2 初期化失敗: {ex.GetType().Name}: {ex.Message}",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Red,
                BackColor = SystemColors.Control,
            };
            this.Controls.Add(errorLabel);
        }

        private void PostToPage(string json)
        {
            if (this.browserReady)
                this.CoreWebView2.PostWebMessageAsJson(json);
        }

        private void SetStatusText(string statusText)
        {
            if (this.StatusText == statusText)
                return;

            this.StatusText = statusText;
            this.StatusTextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RaiseLinkClicked(string url, bool ctrlKey)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                this.LinkClicked?.Invoke(this, new(uri, ctrlKey));
        }

        private void CoreWebView2_ContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            // 既定のメニューの代わりに ContextMenuStrip を表示する
            e.Handled = true;

            var target = e.ContextMenuTarget;
            this.SelectedText = target.HasSelection && !MyCommon.IsNullOrEmpty(target.SelectionText) ? target.SelectionText : null;

            var contextMenu = this.BrowserContextMenu;
            if (contextMenu != null)
            {
                var position = Cursor.Position;
                this.BeginInvoke(() => contextMenu.Show(position));
            }
        }

        private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            // NavigateToString による表示のみ許可し、それ以外の遷移はアプリ側で開く
            var uri = e.Uri;
            if (uri.StartsWith("data:", StringComparison.Ordinal) || uri == "about:blank")
                return;

            e.Cancel = true;

            // 解決できない URL のリンクは about:blank#blocked への遷移になるため開かない
            if (e.IsUserInitiated && !uri.StartsWith("about:", StringComparison.Ordinal))
                this.RaiseLinkClicked(uri, MyCommon.IsKeyDown(Keys.Control));
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            if (e.IsUserInitiated && !e.Uri.StartsWith("about:", StringComparison.Ordinal))
                this.RaiseLinkClicked(e.Uri, MyCommon.IsKeyDown(Keys.Control));
        }

        private void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            BridgeMessage message;
            try
            {
                message = MyCommon.CreateDataFromJson<BridgeMessage>(e.WebMessageAsJson);
            }
            catch (SerializationException)
            {
                return;
            }

            switch (message.Type)
            {
                case "hover":
                    this.SetStatusText(ResolveHref(message.Href ?? ""));
                    break;
                case "click":
                    if (!MyCommon.IsNullOrEmpty(message.Href))
                        this.RaiseLinkClicked(ResolveHref(message.Href), message.Ctrl);
                    break;
                case "selection":
                    this.SelectedText = MyCommon.IsNullOrEmpty(message.Text) ? null : message.Text;
                    break;
            }
        }

        /// <summary>
        /// href 属性の値を絶対 URL に変換します
        /// </summary>
        /// <remarks>
        /// スキーム省略の URL (//opentween/status/... 等) は、IE の WebBrowser で about:blank を基準に解決した場合と同じく about: スキームとして扱う
        /// </remarks>
        internal static string ResolveHref(string href)
        {
            href = href.Trim();
            return href.StartsWith("//", StringComparison.Ordinal) ? "about:" + href : href;
        }

        /// <summary>
        /// スクリプトの実行を禁止する Content-Security-Policy を HTML に挿入します
        /// </summary>
        internal static string InsertContentSecurityPolicy(string html)
        {
            const string headTag = "<head>";

            var pos = html.IndexOf(headTag, StringComparison.OrdinalIgnoreCase);
            if (pos == -1)
                return $"<head>{ContentSecurityPolicyMeta}</head>{html}";

            return html.Insert(pos + headTag.Length, ContentSecurityPolicyMeta);
        }

        /// <summary>
        /// HTML に含まれる a 要素を抽出します
        /// </summary>
        /// <remarks>
        /// 発言詳細欄の HTML は OpenTween 自身が生成したもので、a 要素はネストされていない前提
        /// </remarks>
        internal static TweetDetailsLink[] ParseLinks(string html)
        {
            var links = new List<TweetDetailsLink>();

            foreach (Match anchorMatch in AnchorRegex.Matches(html))
            {
                var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match attrMatch in AttributeRegex.Matches(anchorMatch.Groups["attrs"].Value))
                {
                    var name = attrMatch.Groups["name"].Value;
                    if (!attrs.ContainsKey(name))
                        attrs[name] = WebUtility.HtmlDecode(attrMatch.Groups["value"].Value);
                }

                if (!attrs.TryGetValue("href", out var href))
                    continue;

                attrs.TryGetValue("title", out var title);
                attrs.TryGetValue("class", out var className);

                var text = WebUtility.HtmlDecode(TagRegex.Replace(anchorMatch.Groups["inner"].Value, ""));

                links.Add(new(ResolveHref(href), title ?? "", text, className ?? ""));
            }

            return links.ToArray();
        }

        [DataContract]
        internal sealed class BridgeMessage
        {
            [DataMember(Name = "type")]
            public string Type { get; set; } = "";

            [DataMember(Name = "href", IsRequired = false)]
            public string? Href { get; set; }

            [DataMember(Name = "text", IsRequired = false)]
            public string? Text { get; set; }

            [DataMember(Name = "ctrl", IsRequired = false)]
            public bool Ctrl { get; set; }
        }
    }

    /// <summary>発言詳細欄の本文に含まれるリンク</summary>
    /// <param name="Href">リンク先の URL</param>
    /// <param name="Title">title 属性の値 (短縮 URL の展開後の URL 等)</param>
    /// <param name="Text">リンクのテキスト</param>
    /// <param name="ClassName">class 属性の値</param>
    public record TweetDetailsLink(string Href, string Title, string Text, string ClassName);

    public class TweetDetailsLinkClickedEventArgs : EventArgs
    {
        /// <summary>クリックされたリンクの URL</summary>
        public Uri Uri { get; }

        /// <summary>Ctrl キーを押しながらクリックされたか</summary>
        public bool CtrlKey { get; }

        public TweetDetailsLinkClickedEventArgs(Uri uri, bool ctrlKey)
        {
            this.Uri = uri;
            this.CtrlKey = ctrlKey;
        }
    }
}
